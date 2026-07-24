using Dalamud.Game.Network.Structures;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace SellEverything.Services;

/// <summary>
/// Captures one in-game Compare Prices request at a time.
///
/// The request is prepared before RetainerSell opens, but does not accept
/// packets until ItemSearchResult reaches PostSetup. This mirrors Penny
/// Pincher's request gate and prevents a delayed packet from a previous search
/// from becoming the first packet of a new same-item request.
/// </summary>
public sealed unsafe class MarketPriceCollector : IDisposable
{
    private readonly IMarketBoard marketBoard;
    private readonly IPluginLog log;
    private readonly Configuration configuration;
    private PendingRequest? pending;
    private long generation;
    private int? lastCompletedRequestId;

    public MarketPriceCollector(IMarketBoard marketBoard, IPluginLog log, Configuration configuration)
    {
        this.marketBoard = marketBoard;
        this.log = log;
        this.configuration = configuration;
        this.marketBoard.OfferingsReceived += this.OnOfferingsReceived;
    }

    public bool Waiting => this.pending is not null;
    public bool Active => this.pending?.IsActive == true;
    public DateTimeOffset? RequestedAt => this.pending?.RequestedAt;

    public long Begin(uint itemId, bool isHq)
    {
        this.generation++;
        this.pending = new PendingRequest(this.generation, itemId, isHq, DateTimeOffset.UtcNow);
        this.log.Debug(
            "Prepared market request generation {Generation} for item {ItemId} {Quality}; waiting for ItemSearchResult PostSetup.",
            this.generation,
            itemId,
            isHq ? "HQ" : "NQ");
        return this.generation;
    }

    /// <summary>
    /// Called synchronously from the ItemSearchResult PostSetup lifecycle event.
    /// Penny Pincher uses this point to mark the next market response as valid.
    /// </summary>
    public void Activate()
    {
        if (this.pending is null || this.pending.IsActive)
            return;

        this.pending.IsActive = true;
        this.pending.ActivatedAt = DateTimeOffset.UtcNow;
        this.log.Debug(
            "Activated market request generation {Generation} for item {ItemId} {Quality}.",
            this.pending.Generation,
            this.pending.ItemId,
            this.pending.IsHq ? "HQ" : "NQ");
    }

    public void Cancel() => this.pending = null;

    public bool TryConsumeSettled(TimeSpan quietPeriod, out MarketPriceResult result)
    {
        result = null!;
        if (this.pending?.LastPacketAt is not DateTimeOffset lastPacketAt)
            return false;

        if (DateTimeOffset.UtcNow - lastPacketAt < quietPeriod)
            return false;

        var request = this.pending;
        var exactItemListings = request.Listings.Values.ToArray();
        var hqCount = exactItemListings.Count(listing => listing.IsHq);
        var nqCount = exactItemListings.Length - hqCount;
        var matchingPrices = exactItemListings
            .Where(listing => listing.IsHq == request.IsHq)
            .Select(listing => listing.PricePerUnit)
            .ToArray();
        uint? lowest = matchingPrices.Length == 0 ? null : matchingPrices.Min();

        result = new MarketPriceResult(
            request.Generation,
            request.ItemId,
            request.IsHq,
            request.RequestId ?? -1,
            lowest,
            hqCount,
            nqCount,
            request.OwnListingIds.Count);

        if (request.RequestId is int completedId)
            this.lastCompletedRequestId = completedId;

        this.log.Information(
            "Settled market result {RequestId}: item {ItemId} {Quality}, NQ {NqCount}, HQ {HqCount}, own ignored {OwnIgnored}, matching {MatchingCount}, lowest {Lowest}.",
            result.RequestId,
            result.ItemId,
            result.IsHq ? "HQ" : "NQ",
            result.NqListings,
            result.HqListings,
            result.OwnListingsIgnored,
            matchingPrices.Length,
            result.LowestPrice?.ToString() ?? "none");

        this.pending = null;
        return true;
    }

    public void Dispose() => this.marketBoard.OfferingsReceived -= this.OnOfferingsReceived;

    private void OnOfferingsReceived(IMarketBoardCurrentOfferings offerings)
    {
        if (this.pending is null)
            return;

        if (!this.pending.IsActive)
        {
            this.log.Debug(
                "Ignoring market packet {RequestId}: ItemSearchResult has not reached PostSetup for generation {Generation}.",
                offerings.RequestId,
                this.pending.Generation);
            return;
        }

        // Penny Pincher explicitly rejects a repeated request ID because the
        // client can re-emit the prior response after a temporary search error.
        if (this.lastCompletedRequestId is int lastId && offerings.RequestId == lastId)
        {
            this.log.Debug(
                "Ignoring repeated completed market request {RequestId} for active generation {Generation}.",
                offerings.RequestId,
                this.pending.Generation);
            return;
        }

        var allListings = offerings.ItemListings.ToArray();
        var exactItemListings = allListings
            .Where(listing => listing.ItemId == this.pending.ItemId)
            .ToArray();

        // An entirely empty packet is a valid no-listings response once the
        // matching ItemSearchResult window has reached PostSetup. A non-empty
        // packet containing only another item is stale and remains rejected.
        if (exactItemListings.Length == 0 && allListings.Length > 0)
        {
            this.log.Debug(
                "Ignoring market packet {RequestId}: it contains no listings for active item {ItemId}.",
                offerings.RequestId,
                this.pending.ItemId);
            return;
        }

        if (this.pending.RequestId is int expectedRequestId && expectedRequestId != offerings.RequestId)
        {
            this.log.Warning(
                "Ignoring market packet {RequestId}; active request is {ExpectedRequestId} for item {ItemId}.",
                offerings.RequestId,
                expectedRequestId,
                this.pending.ItemId);
            return;
        }

        this.pending.RequestId ??= offerings.RequestId;
        this.pending.LastPacketAt = DateTimeOffset.UtcNow;

        foreach (var listing in exactItemListings)
        {
            if (!this.configuration.UndercutOwnRetainers && IsOwnRetainer(listing.RetainerId))
            {
                this.pending.OwnListingIds.Add(listing.ListingId);
                continue;
            }

            this.pending.Listings[listing.ListingId] = new CapturedListing(
                listing.ListingId,
                listing.IsHq,
                listing.PricePerUnit);
        }

        this.log.Debug(
            "Captured market packet {RequestId} for item {ItemId}; accumulated {Count} usable exact-item listings, ignored {OwnIgnored} own listings.",
            offerings.RequestId,
            this.pending.ItemId,
            this.pending.Listings.Count,
            this.pending.OwnListingIds.Count);
    }

    private static bool IsOwnRetainer(ulong retainerId)
    {
        if (retainerId == 0)
            return false;

        var manager = RetainerManager.Instance();
        if (manager == null)
            return false;

        for (uint index = 0; index < manager->GetRetainerCount(); index++)
        {
            var retainer = manager->GetRetainerBySortedIndex(index);
            if (retainer != null && retainer->RetainerId == retainerId)
                return true;
        }

        return false;
    }

    private sealed class PendingRequest(long generation, uint itemId, bool isHq, DateTimeOffset requestedAt)
    {
        public long Generation { get; } = generation;
        public uint ItemId { get; } = itemId;
        public bool IsHq { get; } = isHq;
        public DateTimeOffset RequestedAt { get; } = requestedAt;
        public DateTimeOffset? ActivatedAt { get; set; }
        public bool IsActive { get; set; }
        public int? RequestId { get; set; }
        public DateTimeOffset? LastPacketAt { get; set; }
        public Dictionary<ulong, CapturedListing> Listings { get; } = [];
        public HashSet<ulong> OwnListingIds { get; } = [];
    }

    private sealed record CapturedListing(ulong ListingId, bool IsHq, uint PricePerUnit);
}

public sealed record MarketPriceResult(
    long Generation,
    uint ItemId,
    bool IsHq,
    int RequestId,
    uint? LowestPrice,
    int HqListings,
    int NqListings,
    int OwnListingsIgnored);
