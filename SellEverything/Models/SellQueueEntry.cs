namespace SellEverything.Models;

public sealed class SellQueueEntry
{
    public required SellCandidate Candidate { get; init; }
    public QueueEntryState State { get; set; } = QueueEntryState.Pending;
    public SellAction Action { get; set; } = SellAction.Unknown;
    public uint? LowestMatchingPrice { get; set; }
    public uint? ListingPrice { get; set; }
    public string Note { get; set; } = string.Empty;
}

public enum SellAction
{
    Unknown,
    MarketList,
    RetainerVendor,
    Skip,
}

public enum QueueEntryState
{
    Pending,
    OpeningSellWindow,
    RequestingPrice,
    PriceReceived,
    Executing,
    Completed,
    Skipped,
    Failed,
}
