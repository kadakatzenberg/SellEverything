using Dalamud.Game.Network.Structures;
using Dalamud.Plugin.Services;

namespace SellEverything.Services;

public sealed class MarketPriceCollector : IDisposable
{
    private readonly IMarketBoard marketBoard;
    private readonly IPluginLog log;
    private PendingRequest? pending;
    private long generation;

    public MarketPriceCollector(IMarketBoard marketBoard, IPluginLog log)
    {
        this.marketBoard = marketBoard;
        this.log = log;
        this.marketBoard.OfferingsReceived += this.OnOfferingsReceived;
    }

    public bool Waiting => this.pending is not null;
    public DateTimeOffset? RequestedAt => this.pending?.RequestedAt;

    public long Begin(uint itemId, bool isHq)
    {
        this.generation++;
        this.pending = new PendingRequest(this.generation, itemId, isHq, DateTimeOffset.UtcNow);
        return this.generation;
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
            nqCount);

        this.log.Information(
            "Settled market result {RequestId}: item {ItemId} {Quality}, NQ {NqCount}, HQ {HqCount}, matching {MatchingCount}, lowest {Lowest}.",
            result.RequestId,
            result.ItemId,
            result.IsHq ? "HQ" : "NQ",
            result.NqListings,
            result.HqListings,
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

        var exactItemListings = offerings.ItemListings
            .Where(listing => listing.ItemId == this.pending.ItemId)
            .ToArray();

        // Stale packets from a previous Compare Prices search must never complete this request.
        if (exactItemListings.Length == 0)
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
            this.pending.Listings[listing.ListingId] = new CapturedListing(
                listing.ListingId,
                listing.IsHq,
                listing.PricePerUnit);
        }

        this.log.Debug(
            "Captured market packet {RequestId} for item {ItemId}; accumulated {Count} exact-item listings.",
            offerings.RequestId,
            this.pending.ItemId,
            this.pending.Listings.Count);
    }

    private sealed class PendingRequest(long generation, uint itemId, bool isHq, DateTimeOffset requestedAt)
    {
        public long Generation { get; } = generation;
        public uint ItemId { get; } = itemId;
        public bool IsHq { get; } = isHq;
        public DateTimeOffset RequestedAt { get; } = requestedAt;
        public int? RequestId { get; set; }
        public DateTimeOffset? LastPacketAt { get; set; }
        public Dictionary<ulong, CapturedListing> Listings { get; } = [];
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
    int NqListings);
