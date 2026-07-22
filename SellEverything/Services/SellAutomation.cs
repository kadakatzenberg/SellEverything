using Dalamud.Plugin.Services;
using System.Diagnostics;
using SellEverything.Models;

namespace SellEverything.Services;

public sealed class SellAutomation
{
    private readonly Configuration configuration;
    private readonly InventoryScanner scanner;
    private readonly MarketPriceCollector marketPrices;
    private readonly RetainerUi retainerUi;
    private readonly IChatGui chat;
    private readonly IPluginLog log;
    private readonly Stopwatch stepTimer = Stopwatch.StartNew();
    private DateTimeOffset stateStartedAt;
    private int currentIndex;

    public SellAutomation(
        Configuration configuration,
        InventoryScanner scanner,
        MarketPriceCollector marketPrices,
        RetainerUi retainerUi,
        IChatGui chat,
        IPluginLog log)
    {
        this.configuration = configuration;
        this.scanner = scanner;
        this.marketPrices = marketPrices;
        this.retainerUi = retainerUi;
        this.chat = chat;
        this.log = log;
    }

    public List<SellQueueEntry> Queue { get; } = [];
    public AutomationState State { get; private set; } = AutomationState.Idle;
    public string Status { get; private set; } = "Idle";
    public bool IsRunning => this.State is not AutomationState.Idle and not AutomationState.ReadyForReview and not AutomationState.Paused and not AutomationState.Completed and not AutomationState.Faulted;
    public SellQueueEntry? Current => this.currentIndex >= 0 && this.currentIndex < this.Queue.Count ? this.Queue[this.currentIndex] : null;

    public void BuildQueue()
    {
        if (this.IsRunning)
            return;

        this.Queue.Clear();
        this.Queue.AddRange(this.scanner.Scan(this.configuration).Select(candidate => new SellQueueEntry { Candidate = candidate }));
        this.currentIndex = 0;
        this.State = AutomationState.ReadyForReview;
        this.Status = $"Review {this.Queue.Count} eligible stacks.";
    }

    public void Start()
    {
        if (this.Queue.Count == 0)
            BuildQueue();

        if (this.Queue.Count == 0)
        {
            this.Status = "No eligible inventory stacks found.";
            return;
        }

        this.currentIndex = Math.Clamp(this.currentIndex, 0, this.Queue.Count - 1);
        this.Transition(AutomationState.OpeningSellWindow, "Opening the first item.");
    }

    public void Pause(string reason = "Paused by user.")
    {
        this.marketPrices.Cancel();
        this.State = AutomationState.Paused;
        this.Status = reason;
    }

    public void Resume()
    {
        if (this.State != AutomationState.Paused)
            return;

        this.Transition(AutomationState.OpeningSellWindow, "Resuming queue.");
    }

    public void Stop()
    {
        this.marketPrices.Cancel();
        this.State = AutomationState.Idle;
        this.Status = "Stopped.";
    }

    public void Update()
    {
        if (!this.IsRunning || this.Current is null)
            return;

        if (this.stepTimer.ElapsedMilliseconds < Math.Clamp(this.configuration.ActionDelayMilliseconds, 300, 5000))
            return;

        this.stepTimer.Restart();

        try
        {
            switch (this.State)
            {
                case AutomationState.OpeningSellWindow:
                    this.OpenCurrentItem();
                    break;
                case AutomationState.WaitingForSellWindow:
                    this.WaitForSellWindow();
                    break;
                case AutomationState.RequestingMarketPrice:
                    this.RequestMarketPrice();
                    break;
                case AutomationState.WaitingForMarketPrice:
                    this.WaitForMarketPrice();
                    break;
                case AutomationState.WaitingForMarketResultsClose:
                    this.WaitForMarketResultsClose();
                    break;
                case AutomationState.ExecutingDecision:
                    this.ExecuteDecision();
                    break;
                case AutomationState.WaitingForUiClose:
                    this.WaitForUiClose();
                    break;
            }
        }
        catch (Exception exception)
        {
            this.log.Error(exception, "Sell Everything automation failed.");
            this.Current.State = QueueEntryState.Failed;
            this.Current.Note = exception.Message;
            this.State = AutomationState.Faulted;
            this.Status = $"Stopped on {this.Current.Candidate.ItemName}: {exception.Message}";
        }
    }

    private void OpenCurrentItem()
    {
        if (this.Current is null)
            return;

        this.Current.State = QueueEntryState.OpeningSellWindow;

        if (this.configuration.DryRun)
        {
            this.Current.Action = SellAction.Skip;
            this.Current.State = QueueEntryState.Skipped;
            this.Current.Note = "Dry run: market query not sent.";
            this.Advance();
            return;
        }

        if (!this.retainerUi.OpenPutUpForSale(this.Current.Candidate.InventoryType, this.Current.Candidate.Slot))
        {
            this.Pause("The current retainer cannot accept another listing. Open the next retainer and Resume.");
            return;
        }

        this.Transition(AutomationState.WaitingForSellWindow, $"Opening {this.Current.Candidate.ItemName}.");
    }

    private void WaitForSellWindow()
    {
        if (this.retainerUi.IsRetainerSellOpen)
        {
            this.Transition(AutomationState.RequestingMarketPrice, "Requesting in-game market listings.");
            return;
        }

        if (this.ElapsedInState > TimeSpan.FromSeconds(8))
            throw new TimeoutException("The Put Up for Sale window did not open.");
    }

    private void RequestMarketPrice()
    {
        if (this.Current is null)
            return;

        this.marketPrices.Begin(this.Current.Candidate.ItemId, this.Current.Candidate.IsHq);
        this.Current.State = QueueEntryState.RequestingPrice;

        if (!this.retainerUi.ClickComparePrices())
            throw new InvalidOperationException("Could not activate Compare Prices.");

        this.Transition(AutomationState.WaitingForMarketPrice, "Waiting for in-game market response.");
    }

    private void WaitForMarketPrice()
    {
        if (this.Current is null)
            return;

        if (!this.marketPrices.Waiting)
            return;

        if (this.marketPrices.ResponseReceived)
        {
            var lowest = this.marketPrices.Consume();
            this.Current.LowestMatchingPrice = lowest;
            this.Current.State = QueueEntryState.PriceReceived;
            this.Decide(lowest);

            if (!this.retainerUi.CloseMarketResults())
                throw new InvalidOperationException("Could not close the market results window.");

            this.Transition(AutomationState.WaitingForMarketResultsClose, this.Current.Note);
            return;
        }

        if (this.ElapsedInState > TimeSpan.FromMilliseconds(Math.Clamp(this.configuration.MarketTimeoutMilliseconds, 5000, 60000)))
        {
            this.marketPrices.Cancel();
            this.Current.Action = SellAction.Skip;
            this.Current.State = QueueEntryState.Skipped;
            this.Current.Note = "No matching market listing response before timeout.";
            this.retainerUi.CancelSellWindow();
            this.Transition(AutomationState.WaitingForUiClose, this.Current.Note);
        }
    }

    private void Decide(uint? lowest)
    {
        if (this.Current is null)
            return;

        if (lowest is null)
        {
            this.Current.Action = SellAction.Skip;
            this.Current.Note = "No matching HQ/NQ listings. Skipped.";
            return;
        }

        if (lowest.Value < this.configuration.MarketFloor)
        {
            this.Current.Action = SellAction.RetainerVendor;
            this.Current.Note = $"Lowest matching offer is {lowest.Value:N0} gil, below the {this.configuration.MarketFloor:N0}-gil floor. Retainer vendor sale.";
            return;
        }

        this.Current.Action = SellAction.MarketList;
        this.Current.ListingPrice = Math.Max(1, lowest.Value - this.configuration.UndercutAmount);
        this.Current.Note = $"List at {this.Current.ListingPrice.Value:N0} gil each.";
    }


    private void WaitForMarketResultsClose()
    {
        if (this.retainerUi.IsMarketResultsOpen)
        {
            if (this.ElapsedInState > TimeSpan.FromSeconds(8))
                throw new TimeoutException("The market results window did not close.");
            return;
        }

        this.Transition(AutomationState.ExecutingDecision, this.Current?.Note ?? "Executing decision.");
    }

    private void ExecuteDecision()
    {
        if (this.Current is null)
            return;

        this.Current.State = QueueEntryState.Executing;

        switch (this.Current.Action)
        {
            case SellAction.MarketList:
                if (this.Current.ListingPrice is null || !this.retainerUi.SetPriceAndConfirm(this.Current.ListingPrice.Value))
                    throw new InvalidOperationException("Could not set or confirm the listing price.");
                break;

            case SellAction.RetainerVendor:
                if (!this.retainerUi.CancelSellWindow())
                    throw new InvalidOperationException("Could not close the market listing window before retainer sale.");
                this.Transition(AutomationState.WaitingToVendor, "Closing market window before retainer sale.");
                return;

            case SellAction.Skip:
                this.retainerUi.CancelSellWindow();
                break;
        }

        this.Transition(AutomationState.WaitingForUiClose, this.Current.Note);
    }

    private void WaitForUiClose()
    {
        if (this.retainerUi.IsRetainerSellOpen)
        {
            if (this.ElapsedInState > TimeSpan.FromSeconds(10))
                throw new TimeoutException("The retainer sale window did not close.");
            return;
        }

        if (this.Current is not null)
            this.Current.State = this.Current.Action == SellAction.Skip ? QueueEntryState.Skipped : QueueEntryState.Completed;

        this.Advance();
    }

    public void UpdateVendorStep()
    {
        if (this.State != AutomationState.WaitingToVendor || this.Current is null)
            return;

        if (this.stepTimer.ElapsedMilliseconds < Math.Clamp(this.configuration.ActionDelayMilliseconds, 300, 5000))
            return;

        this.stepTimer.Restart();

        try
        {
            if (this.retainerUi.IsRetainerSellOpen)
                return;

            if (!this.retainerUi.SellToRetainer(this.Current.Candidate.InventoryType, this.Current.Candidate.Slot))
                throw new InvalidOperationException("Could not invoke Have Retainer Sell Items.");

            this.Current.State = QueueEntryState.Completed;
            this.Advance();
        }
        catch (Exception exception)
        {
            this.log.Error(exception, "Sell Everything retainer-vendor step failed.");
            this.Current.State = QueueEntryState.Failed;
            this.Current.Note = exception.Message;
            this.State = AutomationState.Faulted;
            this.Status = $"Stopped on {this.Current.Candidate.ItemName}: {exception.Message}";
        }
    }

    private void Advance()
    {
        this.currentIndex++;
        if (this.currentIndex >= this.Queue.Count)
        {
            this.State = AutomationState.Completed;
            this.Status = "Queue completed.";
            this.chat.Print("[Sell Everything] Queue completed.");
            return;
        }

        this.Transition(AutomationState.OpeningSellWindow, $"Next: {this.Current?.Candidate.ItemName}.");
    }

    private void Transition(AutomationState state, string status)
    {
        this.State = state;
        this.Status = status;
        this.stateStartedAt = DateTimeOffset.UtcNow;
        this.stepTimer.Restart();
    }

    private TimeSpan ElapsedInState => DateTimeOffset.UtcNow - this.stateStartedAt;
}

public enum AutomationState
{
    Idle,
    ReadyForReview,
    OpeningSellWindow,
    WaitingForSellWindow,
    RequestingMarketPrice,
    WaitingForMarketPrice,
    WaitingForMarketResultsClose,
    ExecutingDecision,
    WaitingToVendor,
    WaitingForUiClose,
    Paused,
    Completed,
    Faulted,
}
