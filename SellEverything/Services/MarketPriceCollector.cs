using Dalamud.Plugin.Services;
using Dalamud.Game.Network.Structures;

namespace SellEverything.Services;

public sealed class MarketPriceCollector : IDisposable
{
    private readonly IMarketBoard marketBoard;
    private readonly IPluginLog log;
    private PendingRequest? pending;

    public MarketPriceCollector(IMarketBoard marketBoard, IPluginLog log)
    {
        this.marketBoard = marketBoard;
        this.log = log;
        this.marketBoard.OfferingsReceived += this.OnOfferingsReceived;
    }

    public uint? LastLowestPrice { get; private set; }
    public bool Waiting => this.pending is not null;
    public bool ResponseReceived { get; private set; }
    public DateTimeOffset? RequestedAt => this.pending?.RequestedAt;

    public void Begin(uint itemId, bool isHq)
    {
        this.LastLowestPrice = null;
        this.ResponseReceived = false;
        this.pending = new PendingRequest(itemId, isHq, DateTimeOffset.UtcNow);
    }

    public void Cancel()
    {
        this.pending = null;
        this.LastLowestPrice = null;
        this.ResponseReceived = false;
    }

    public uint? Consume()
    {
        var value = this.LastLowestPrice;
        this.LastLowestPrice = null;
        this.pending = null;
        this.ResponseReceived = false;
        return value;
    }

    public void Dispose() => this.marketBoard.OfferingsReceived -= this.OnOfferingsReceived;

    private void OnOfferingsReceived(IMarketBoardCurrentOfferings offerings)
    {
        if (this.pending is null)
            return;

        var prices = offerings.ItemListings
            .Where(listing => listing.ItemId == this.pending.ItemId && listing.IsHq == this.pending.IsHq)
            .Select(listing => listing.PricePerUnit)
            .ToArray();

        this.LastLowestPrice = prices.Length == 0 ? null : prices.Min();
        this.ResponseReceived = true;
        this.log.Information(
            "Received {Count} matching market listings for item {ItemId} ({Quality}). Lowest: {Lowest}",
            prices.Length,
            this.pending.ItemId,
            this.pending.IsHq ? "HQ" : "NQ",
            this.LastLowestPrice?.ToString() ?? "none");
    }

    private sealed record PendingRequest(uint ItemId, bool IsHq, DateTimeOffset RequestedAt);
}
