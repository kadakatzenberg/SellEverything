using Dalamud.Plugin.Services;
using SellEverything.Models;
using System.Diagnostics;

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
    private readonly HashSet<ItemQualityKey> skippedForSession = [];
    private readonly HashSet<InventoryStackKey> skippedStacksForSession = [];
    private readonly List<AutomationActivity> activity = [];

    private DateTimeOffset stateStartedAt;
    private DateTimeOffset lastListingConfirmAttemptAt;
    private DateTimeOffset lastVendorAttemptAt;
    private int currentIndex;
    private int currentRetainerIndex;
    private int sessionRetainerLimit;
    private int listingConfirmAttempts;
    private int vendorAttempts;
    private AutomationState pausedFromState = AutomationState.Idle;
    private bool queueNeedsReview;
    private int queueConfigurationFingerprint;

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
    public int CurrentRetainerNumber => this.currentRetainerIndex + 1;
    public int SessionRetainerLimit => this.sessionRetainerLimit;
    public IReadOnlyList<AutomationActivity> Activity => this.activity;
    public string LastFault { get; private set; } = string.Empty;
    public bool NeedsReview => this.queueNeedsReview;
    public bool LocksConfiguration => this.IsRunning || this.State == AutomationState.Paused;
    public int ProcessedCount => this.Queue.Count(entry => entry.State is QueueEntryState.Completed or QueueEntryState.Skipped or QueueEntryState.Failed);
    public float ProgressFraction => this.Queue.Count == 0 ? 0f : Math.Clamp((float)this.ProcessedCount / this.Queue.Count, 0f, 1f);
    public TimeSpan CurrentStateElapsed => this.stateStartedAt == default
        ? TimeSpan.Zero
        : DateTimeOffset.UtcNow - this.stateStartedAt;

    public bool IsRunning => this.State is not AutomationState.Idle
        and not AutomationState.ReadyForReview
        and not AutomationState.Paused
        and not AutomationState.Completed
        and not AutomationState.Faulted;

    public SellQueueEntry? Current => this.currentIndex >= 0 && this.currentIndex < this.Queue.Count
        ? this.Queue[this.currentIndex]
        : null;

    public void BuildQueue()
    {
        if (this.LocksConfiguration)
            return;

        this.skippedForSession.Clear();
        this.skippedStacksForSession.Clear();
        this.Queue.Clear();
        this.Queue.AddRange(this.scanner.Scan(this.configuration)
            .Select(candidate => new SellQueueEntry { Candidate = candidate }));
        this.currentIndex = FindNextPendingIndex();
        this.queueConfigurationFingerprint = ComputeConfigurationFingerprint();
        this.queueNeedsReview = this.configuration.RequireReviewBeforeRun && this.Queue.Count > 0;
        this.State = AutomationState.ReadyForReview;
        this.stateStartedAt = DateTimeOffset.UtcNow;
        this.stepTimer.Restart();
        this.Status = this.queueNeedsReview
            ? $"Review and approve {this.Queue.Count} eligible stacks before starting."
            : $"Ready with {this.Queue.Count} eligible stacks.";
        this.LastFault = string.Empty;
        RecordActivity(this.Status, AutomationActivityKind.Info);
    }

    public void ApproveQueue()
    {
        if (this.IsRunning || !this.queueNeedsReview)
            return;

        if (this.queueConfigurationFingerprint != ComputeConfigurationFingerprint())
        {
            BuildQueue();
            this.Status = "Settings changed after the scan. Review the rebuilt queue before approval.";
            RecordActivity(this.Status, AutomationActivityKind.Warning);
            return;
        }

        this.queueNeedsReview = false;
        this.Status = $"Approved {this.Queue.Count(entry => entry.State == QueueEntryState.Pending)} pending queue entries.";
        this.stateStartedAt = DateTimeOffset.UtcNow;
        this.stepTimer.Restart();
        RecordActivity(this.Status, AutomationActivityKind.Success);
    }

    public void Start()
    {
        if (this.IsRunning || this.State == AutomationState.Paused)
            return;

        if (this.Queue.Count > 0 && this.queueConfigurationFingerprint != ComputeConfigurationFingerprint())
        {
            BuildQueue();
            if (this.queueNeedsReview)
            {
                this.Status = "Settings changed after the previous scan. Review and approve the rebuilt queue.";
                RecordActivity(this.Status, AutomationActivityKind.Warning);
                return;
            }
        }

        if (!this.Queue.Any(entry => entry.State == QueueEntryState.Pending))
            BuildQueue();

        if (this.queueNeedsReview)
        {
            this.State = AutomationState.ReadyForReview;
            this.Status = "Queue review is required. Approve the queue before starting live actions.";
            RecordActivity(this.Status, AutomationActivityKind.Warning);
            return;
        }

        this.currentIndex = FindNextPendingIndex();
        if (this.Current is null)
        {
            this.Status = "No eligible inventory stacks found.";
            return;
        }

        this.currentRetainerIndex = 0;
        this.listingConfirmAttempts = 0;
        this.vendorAttempts = 0;
        this.LastFault = string.Empty;
        var detectedRetainers = this.retainerUi.RetainerCount;
        this.sessionRetainerLimit = detectedRetainers > 0
            ? Math.Min(Math.Max(1, this.configuration.RetainersPerSession), detectedRetainers)
            : Math.Max(1, this.configuration.RetainersPerSession);

        if (this.configuration.DryRun)
        {
            this.Transition(AutomationState.OpeningSellWindow, "Dry run: validating queue without game actions.");
            return;
        }

        if (this.configuration.AutomateRetainers)
        {
            if (!this.retainerUi.IsRetainerListOpen)
            {
                this.pausedFromState = AutomationState.SelectingRetainer;
                this.State = AutomationState.Paused;
                this.Status = "Open the retainer list, then press Resume.";
                this.chat.Print("[Sell Everything] Open the retainer list, then press Resume.");
                return;
            }

            this.Transition(
                AutomationState.SelectingRetainer,
                $"Selecting retainer {this.CurrentRetainerNumber}/{this.sessionRetainerLimit}.");
            return;
        }

        this.Transition(AutomationState.OpeningSellWindow, "Opening the first inventory item.");
    }

    public void Pause(string reason = "Paused by user.")
    {
        if (!this.IsRunning)
            return;

        this.pausedFromState = this.State;
        this.marketPrices.Cancel();
        this.retainerUi.DisarmComparePrices();
        this.retainerUi.DisarmExpectedYesNo();
        this.State = AutomationState.Paused;
        this.stateStartedAt = DateTimeOffset.UtcNow;
        this.stepTimer.Restart();
        this.Status = reason;
        RecordActivity(reason, AutomationActivityKind.Warning);
    }

    public void Resume()
    {
        if (this.State != AutomationState.Paused)
            return;

        RecordActivity($"Resume requested from {this.pausedFromState}.", AutomationActivityKind.Info);

        if (this.Current is null)
        {
            this.State = AutomationState.ReadyForReview;
            this.Status = "No current entry remains. Scan inventory before starting again.";
            return;
        }

        if (this.configuration.DryRun)
        {
            this.Transition(AutomationState.OpeningSellWindow, "Resuming dry run.");
            return;
        }

        if (this.pausedFromState is AutomationState.WaitingForListingConfirmation
            or AutomationState.WaitingForVendorConfirmation
            or AutomationState.WaitingForTransaction)
        {
            if (CurrentInventoryChanged())
            {
                CompleteCurrentTransaction();
                return;
            }

            var resumedState = this.pausedFromState switch
            {
                AutomationState.WaitingForListingConfirmation when this.retainerUi.IsRetainerSellOpen
                    => AutomationState.WaitingForListingConfirmation,
                AutomationState.WaitingForVendorConfirmation
                    => AutomationState.WaitingForVendorConfirmation,
                _ => AutomationState.WaitingForTransaction,
            };

            if (this.retainerUi.IsSelectYesNoOpen && this.configuration.AutoConfirmExpectedDialogs)
            {
                var kind = this.Current.Action == SellAction.RetainerVendor
                    ? ExpectedDialogKind.RetainerVendorSale
                    : ExpectedDialogKind.MarketListing;
                this.retainerUi.ArmExpectedYesNo(kind, "resumed transaction confirmation");
            }

            this.Transition(resumedState, "Resuming transaction verification.");
            return;
        }

        if (this.configuration.AutomateRetainers && this.retainerUi.IsRetainerListOpen)
        {
            this.Transition(
                AutomationState.SelectingRetainer,
                $"Selecting retainer {this.CurrentRetainerNumber}/{this.sessionRetainerLimit}.");
            return;
        }

        // Any paused market-price request is restarted from a clean item window
        // because the old packet generation was intentionally cancelled.
        this.retainerUi.CloseMarketResults();
        this.retainerUi.CancelSellWindow();
        this.Current.State = QueueEntryState.Pending;
        this.Transition(AutomationState.OpeningSellWindow, "Restarting the current item after pause.");
    }

    public void Stop()
    {
        this.marketPrices.Cancel();
        this.retainerUi.DisarmComparePrices();
        this.retainerUi.DisarmExpectedYesNo();

        if (this.Current is not null &&
            this.Current.State is not QueueEntryState.Completed
                and not QueueEntryState.Skipped
                and not QueueEntryState.Failed)
        {
            ResetEntryForRetry(this.Current, "Stopped before completion; returned to pending.");
        }

        this.pausedFromState = AutomationState.Idle;
        this.State = AutomationState.Idle;
        this.stateStartedAt = DateTimeOffset.UtcNow;
        this.stepTimer.Restart();
        this.Status = "Stopped.";
        RecordActivity(this.Status, AutomationActivityKind.Warning);
    }

    public void RetryFailed()
    {
        if (this.LocksConfiguration)
            return;

        this.marketPrices.Cancel();
        this.retainerUi.DisarmComparePrices();
        this.retainerUi.DisarmExpectedYesNo();
        this.retainerUi.CloseMarketResults();
        this.retainerUi.CancelSellWindow();

        var reset = 0;
        foreach (var entry in this.Queue.Where(entry => entry.State == QueueEntryState.Failed))
        {
            ResetEntryForRetry(entry, string.Empty);
            reset++;
        }

        if (reset == 0)
            return;

        this.currentIndex = FindNextPendingIndex();
        this.queueNeedsReview = this.configuration.RequireReviewBeforeRun;
        this.State = AutomationState.ReadyForReview;
        this.stateStartedAt = DateTimeOffset.UtcNow;
        this.stepTimer.Restart();
        this.Status = $"Reset {reset} failed entr{(reset == 1 ? "y" : "ies")} for retry.";
        this.LastFault = string.Empty;
        RecordActivity(this.Status, AutomationActivityKind.Info);
    }

    public void Update()
    {
        // Lifecycle-triggered Compare Prices work and confirmation-arm expiry
        // are pumped every frame. Yes/No acceptance remains bound to the
        // automation state handlers below.
        this.retainerUi.Pump();

        if (!this.IsRunning)
            return;

        if (this.stepTimer.ElapsedMilliseconds < Math.Clamp(this.configuration.ActionDelayMilliseconds, 300, 5000))
            return;

        this.stepTimer.Restart();

        try
        {
            switch (this.State)
            {
                case AutomationState.SelectingRetainer:
                    SelectRetainer();
                    break;
                case AutomationState.WaitingForRetainerMenu:
                    WaitForRetainerMenu();
                    break;
                case AutomationState.SelectingMarketMenu:
                    SelectMarketMenu();
                    break;
                case AutomationState.WaitingForRetainerReady:
                    WaitForRetainerReady();
                    break;
                case AutomationState.OpeningSellWindow:
                    OpenCurrentItem();
                    break;
                case AutomationState.WaitingForSellWindow:
                    WaitForSellWindow();
                    break;
                case AutomationState.WaitingForMarketPrice:
                    WaitForMarketPrice();
                    break;
                case AutomationState.WaitingForMarketResultsClose:
                    WaitForMarketResultsClose();
                    break;
                case AutomationState.ExecutingDecision:
                    ExecuteDecision();
                    break;
                case AutomationState.WaitingForListingConfirmation:
                    WaitForListingConfirmation();
                    break;
                case AutomationState.WaitingToVendor:
                    WaitToVendor();
                    break;
                case AutomationState.WaitingForVendorConfirmation:
                    WaitForVendorConfirmation();
                    break;
                case AutomationState.WaitingForTransaction:
                    WaitForTransaction();
                    break;
                case AutomationState.WaitingForUiClose:
                    WaitForUiClose();
                    break;
                case AutomationState.RefreshingInventory:
                    RefreshInventory();
                    break;
                case AutomationState.ClosingRetainer:
                    CloseCurrentRetainer();
                    break;
                case AutomationState.WaitingForRetainerList:
                    WaitForRetainerList();
                    break;
            }
        }
        catch (Exception exception)
        {
            Fault(exception);
        }
    }

    private void SelectRetainer()
    {
        if (!this.retainerUi.IsRetainerListOpen)
        {
            if (this.ElapsedInState > UiTimeout)
                throw new TimeoutException("The retainer list is not open.");
            return;
        }

        var detectedCount = this.retainerUi.RetainerCount;
        if (detectedCount > 0 && this.currentRetainerIndex >= detectedCount)
        {
            CompleteSession("All available retainers were processed.");
            return;
        }

        if (!this.retainerUi.SelectRetainer(this.currentRetainerIndex))
            throw new InvalidOperationException($"Could not select retainer {this.CurrentRetainerNumber}.");

        this.Transition(
            AutomationState.WaitingForRetainerMenu,
            $"Waiting for retainer {this.CurrentRetainerNumber} menu.");
    }

    private void WaitForRetainerMenu()
    {
        if (this.retainerUi.IsSelectStringOpen)
        {
            this.Transition(AutomationState.SelectingMarketMenu, "Selecting market selling.");
            return;
        }

        if (this.retainerUi.IsSelectOkOpen)
        {
            this.retainerUi.DismissExpectedOk();
            MoveToNextRetainer("Retainer could not be opened.");
            return;
        }

        if (this.ElapsedInState > UiTimeout)
            MoveToNextRetainer("Retainer menu did not open.");
    }

    private void SelectMarketMenu()
    {
        if (!this.retainerUi.IsSelectStringOpen)
        {
            if (this.ElapsedInState > UiTimeout)
                throw new TimeoutException("The retainer menu closed before market selling was selected.");
            return;
        }

        if (!this.retainerUi.SelectMarketSellingMenu(Math.Max(0, this.configuration.MarketMenuOptionIndex)))
            throw new InvalidOperationException("Could not select Sell items in your inventory on the market.");

        this.Transition(AutomationState.WaitingForRetainerReady, "Waiting for retainer market inventory.");
    }

    private void WaitForRetainerReady()
    {
        if (!this.retainerUi.IsSelectStringOpen &&
            !this.retainerUi.IsRetainerListOpen &&
            this.retainerUi.IsRetainerContextActive)
        {
            this.Transition(AutomationState.OpeningSellWindow, "Opening the first eligible item.");
            return;
        }

        if (this.retainerUi.IsSelectOkOpen)
        {
            this.retainerUi.DismissExpectedOk();
            MoveToNextRetainer("Market selling is unavailable for this retainer.");
            return;
        }

        if (this.ElapsedInState > UiTimeout)
            throw new TimeoutException("The retainer market inventory did not become ready.");
    }

    private void OpenCurrentItem()
    {
        if (this.Current is null)
        {
            if (this.configuration.DryRun)
            {
                CompleteSession("Dry run completed.");
                return;
            }

            this.Transition(AutomationState.ClosingRetainer, "No eligible inventory remains.");
            return;
        }

        this.Current.State = QueueEntryState.OpeningSellWindow;
        this.listingConfirmAttempts = 0;
        this.vendorAttempts = 0;

        if (this.configuration.DryRun)
        {
            this.Current.Action = SellAction.Skip;
            this.Current.State = QueueEntryState.Skipped;
            this.Current.Note = $"Dry run: {this.Current.Candidate.QualityLabel} quality verified; no game action sent.";
            AdvanceToNextPending();
            return;
        }

        if (!this.scanner.TryValidate(this.configuration, this.Current.Candidate, out var live))
        {
            this.Current.Action = SellAction.Skip;
            this.Current.State = QueueEntryState.Skipped;
            this.Current.Note = "Inventory slot changed before processing. Re-scanning.";
            this.Transition(AutomationState.RefreshingInventory, this.Current.Note);
            return;
        }

        this.Current.Candidate = live;

        // Begin collecting before RetainerSell is opened, then arm the native
        // Compare Prices event. RetainerUi dispatches it from PostSetup, which
        // is the same timing pattern used by Marketbuddy.
        this.marketPrices.Cancel();
        this.marketPrices.Begin(live.ItemId, live.IsHq);
        this.retainerUi.ArmComparePrices(live.IsHq, live.ItemName);

        if (!this.retainerUi.OpenPutUpForSale(live.InventoryType, live.Slot))
        {
            this.marketPrices.Cancel();
            this.retainerUi.DisarmComparePrices();
            MoveToNextRetainer("Current retainer cannot accept another listing.");
            return;
        }

        this.Transition(
            AutomationState.WaitingForSellWindow,
            $"Opening {live.ItemName} ({live.QualityLabel}).");
    }

    private void WaitForSellWindow()
    {
        if (this.retainerUi.IsRetainerSellOpen)
        {
            if (!string.IsNullOrEmpty(this.retainerUi.ComparePricesValidationFailure))
            {
                this.marketPrices.Cancel();
                var failure = this.retainerUi.ComparePricesValidationFailure;
                this.retainerUi.CancelSellWindow();
                this.retainerUi.DisarmComparePrices();
                throw new InvalidOperationException(failure);
            }

            if (!this.retainerUi.IsRetainerContextActive)
            {
                if (this.ElapsedInState > UiTimeout)
                {
                    this.marketPrices.Cancel();
                    this.retainerUi.DisarmComparePrices();
                    throw new TimeoutException("RetainerSell opened without an active retainer context.");
                }

                this.Status = "Waiting for the active retainer context.";
                return;
            }

            if (!this.retainerUi.ComparePricesDispatched && !this.retainerUi.TryDispatchComparePrices())
            {
                if (this.ElapsedInState > UiTimeout)
                {
                    this.marketPrices.Cancel();
                    this.retainerUi.DisarmComparePrices();
                    throw new TimeoutException("Compare Prices did not dispatch after RetainerSell setup.");
                }

                this.Status = $"Waiting for Compare Prices on {this.Current?.Candidate.ItemName}.";
                return;
            }

            if (this.Current is not null)
                this.Current.State = QueueEntryState.RequestingPrice;

            this.Transition(
                AutomationState.WaitingForMarketPrice,
                $"Waiting for {this.Current?.Candidate.QualityLabel}-only market listings.");
            return;
        }

        if (this.retainerUi.IsSelectOkOpen)
        {
            this.marketPrices.Cancel();
            this.retainerUi.DisarmComparePrices();
            this.retainerUi.DismissExpectedOk();
            MoveToNextRetainer("Current retainer has no usable selling slot.");
            return;
        }

        if (this.ElapsedInState > TimeSpan.FromMilliseconds(Math.Min(this.configuration.UiTimeoutMilliseconds, 8000)))
        {
            this.marketPrices.Cancel();
            this.retainerUi.DisarmComparePrices();
            MoveToNextRetainer("Put Up for Sale did not open; treating this retainer as full.");
        }
    }

    private void WaitForMarketPrice()
    {
        if (this.Current is null)
            throw new InvalidOperationException("No active sale entry.");

        if (this.marketPrices.TryConsumeSettled(TimeSpan.FromMilliseconds(700), out var result))
        {

            if (result.ItemId != this.Current.Candidate.ItemId || result.IsHq != this.Current.Candidate.IsHq)
            {
                throw new InvalidOperationException(
                    $"Rejected mismatched market response. Expected {this.Current.Candidate.ItemId} {this.Current.Candidate.QualityLabel}.");
            }

            this.Current.MarketRequestId = result.RequestId;
            this.Current.HqListingsSeen = result.HqListings;
            this.Current.NqListingsSeen = result.NqListings;
            this.Current.OwnListingsIgnored = result.OwnListingsIgnored;
            this.Current.LowestMatchingPrice = result.LowestPrice;
            this.Current.State = QueueEntryState.PriceReceived;
            Decide(result);

            if (!this.retainerUi.CloseMarketResults())
                throw new InvalidOperationException("Could not close the market results window.");

            this.Transition(AutomationState.WaitingForMarketResultsClose, this.Current.Note);
            return;
        }

        if (!this.marketPrices.Active &&
            !this.retainerUi.IsMarketResultsOpen &&
            this.retainerUi.ComparePricesDispatched &&
            this.retainerUi.ComparePricesDispatchAttempts < 3 &&
            this.retainerUi.LastComparePricesDispatchAt is DateTimeOffset lastDispatch &&
            DateTimeOffset.UtcNow - lastDispatch > TimeSpan.FromSeconds(2.5))
        {
            if (this.retainerUi.RetryComparePrices())
            {
                this.Status = $"Retrying Compare Prices ({this.retainerUi.ComparePricesDispatchAttempts}/3) for {this.Current.Candidate.ItemName}.";
                RecordActivity(this.Status, AutomationActivityKind.Warning);
            }
        }

        if (this.ElapsedInState > MarketTimeout)
        {
            this.marketPrices.Cancel();
            this.retainerUi.DisarmComparePrices();
            MarkCurrentSkipped("No valid market response before timeout.");
            this.retainerUi.CloseMarketResults();
            this.retainerUi.CancelSellWindow();
            this.Transition(AutomationState.WaitingForUiClose, this.Current.Note);
        }
    }

    private void Decide(MarketPriceResult result)
    {
        if (this.Current is null)
            return;

        var requestedQuality = this.Current.Candidate.QualityLabel;
        var oppositeCount = this.Current.Candidate.IsHq ? result.NqListings : result.HqListings;
        var matchingCount = this.Current.Candidate.IsHq ? result.HqListings : result.NqListings;
        var ownIgnored = result.OwnListingsIgnored;
        var ownSuffix = ownIgnored > 0 ? $" Own listings ignored {ownIgnored}." : string.Empty;

        if (result.LowestPrice is null)
        {
            this.skippedForSession.Add(new ItemQualityKey(result.ItemId, result.IsHq));
            this.Current.Action = SellAction.Skip;
            this.Current.Note =
                $"{requestedQuality}: no matching listings. Matching {matchingCount}, opposite-quality ignored {oppositeCount}.{ownSuffix}";
            return;
        }

        if (result.LowestPrice.Value < this.configuration.MarketFloor)
        {
            if (this.Current.Candidate.IsPartialStack)
            {
                this.skippedStacksForSession.Add(ToInventoryStackKey(this.Current.Candidate));
                this.Current.Action = SellAction.Skip;
                this.Current.Note =
                    $"{requestedQuality}: lowest {result.LowestPrice.Value:N0}, but {this.Current.Candidate.ProtectedQuantity:N0} must remain. " +
                    "Partial-stack retainer vending is not dispatched because it could sell the protected remainder.";
                return;
            }

            if (this.Current.Candidate.NpcSellPrice == 0)
            {
                this.skippedForSession.Add(new ItemQualityKey(result.ItemId, result.IsHq));
                this.Current.Action = SellAction.Skip;
                this.Current.Note =
                    $"{requestedQuality}: lowest {result.LowestPrice.Value:N0}, but the item has no retainer-vendor value.";
                return;
            }

            this.Current.Action = SellAction.RetainerVendor;
            this.Current.Note =
                $"{requestedQuality}: lowest {result.LowestPrice.Value:N0}; opposite-quality ignored {oppositeCount}.{ownSuffix} Retainer sale.";
            return;
        }

        this.Current.Action = SellAction.MarketList;
        var undercut = this.configuration.UndercutAmount;
        this.Current.ListingPrice = result.LowestPrice.Value > undercut
            ? result.LowestPrice.Value - undercut
            : 1u;
        this.Current.Note =
            $"{requestedQuality}: lowest {result.LowestPrice.Value:N0}; opposite-quality ignored {oppositeCount}.{ownSuffix} " +
            $"List {this.Current.Candidate.SellQuantity:N0} at {this.Current.ListingPrice.Value:N0}.";
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
            throw new InvalidOperationException("No active sale entry.");

        if (!this.scanner.TryValidate(this.configuration, this.Current.Candidate, out var live))
            throw new InvalidOperationException("The inventory slot changed before the final sale action.");

        this.Current.Candidate = live;
        this.Current.State = QueueEntryState.Executing;

        switch (this.Current.Action)
        {
            case SellAction.MarketList:
                if (this.Current.ListingPrice is null)
                    throw new InvalidOperationException("No listing price was calculated.");

                // Arm before dispatch so a confirmation addon created
                // synchronously cannot appear outside the state-bound window.
                if (this.configuration.AutoConfirmExpectedDialogs)
                    this.retainerUi.ArmExpectedYesNo(ExpectedDialogKind.MarketListing, "market listing");

                // As with Compare Prices, the confirm control may not be ready on
                // the first frame after the results window closes. Keep retrying.
                if (!TrySubmitListing(this.Current.ListingPrice.Value, this.Current.Candidate.SellQuantity))
                {
                    this.retainerUi.DisarmExpectedYesNo();
                    if (this.ElapsedInState > UiTimeout)
                        throw new TimeoutException("The listing Confirm event did not dispatch.");

                    this.Status = $"Waiting to confirm {this.Current.Candidate.ItemName} at {this.Current.ListingPrice.Value:N0} gil.";
                    return;
                }

                this.Transition(AutomationState.WaitingForListingConfirmation, this.Current.Note);
                return;

            case SellAction.RetainerVendor:
                if (!this.retainerUi.CancelSellWindow())
                    throw new InvalidOperationException("Could not close the market listing window before retainer sale.");
                this.Transition(AutomationState.WaitingToVendor, "Closing market window before retainer sale.");
                return;

            case SellAction.Skip:
                this.retainerUi.CancelSellWindow();
                this.Transition(AutomationState.WaitingForUiClose, this.Current.Note);
                return;

            default:
                throw new InvalidOperationException("No sale decision was produced.");
        }
    }

    private void WaitForListingConfirmation()
    {
        if (this.retainerUi.IsSelectYesNoOpen)
        {
            if (!this.configuration.AutoConfirmExpectedDialogs)
            {
                Pause("Listing confirmation is waiting for manual approval.");
                return;
            }

            if (!this.retainerUi.ConfirmExpectedYesNo(ExpectedDialogKind.MarketListing))
                throw new InvalidOperationException("Could not confirm the market listing.");

            this.Transition(AutomationState.WaitingForTransaction, "Listing confirmed; verifying inventory change.");
            return;
        }

        if (CurrentInventoryChanged())
        {
            this.retainerUi.DisarmExpectedYesNo();
            CompleteCurrentTransaction();
            return;
        }

        if (!this.retainerUi.IsRetainerSellOpen && this.ElapsedInState > TimeSpan.FromMilliseconds(700))
        {
            this.retainerUi.DisarmExpectedYesNo();
            this.Transition(AutomationState.WaitingForTransaction, "Listing submitted; verifying inventory change.");
            return;
        }

        if (this.retainerUi.IsRetainerSellOpen &&
            this.Current?.ListingPrice is uint listingPrice &&
            this.listingConfirmAttempts < 3 &&
            this.ElapsedInState > TimeSpan.FromSeconds(2.5) &&
            DateTimeOffset.UtcNow - this.lastListingConfirmAttemptAt > TimeSpan.FromSeconds(2))
        {
            if (this.configuration.AutoConfirmExpectedDialogs)
                this.retainerUi.ArmExpectedYesNo(ExpectedDialogKind.MarketListing, "market listing retry");

            if (TrySubmitListing(listingPrice, this.Current.Candidate.SellQuantity))
            {
                this.Status = $"Retrying listing confirmation ({this.listingConfirmAttempts}/3).";
                RecordActivity(this.Status, AutomationActivityKind.Warning);
            }
            else
            {
                this.retainerUi.DisarmExpectedYesNo();
            }
        }

        if (this.ElapsedInState > UiTimeout)
        {
            this.retainerUi.DisarmExpectedYesNo();
            throw new TimeoutException("The listing confirmation did not complete.");
        }
    }

    private void WaitToVendor()
    {
        if (this.Current is null)
            throw new InvalidOperationException("No active sale entry.");

        if (this.retainerUi.IsRetainerSellOpen)
        {
            if (this.ElapsedInState > TimeSpan.FromSeconds(8))
                throw new TimeoutException("The market listing window did not close before retainer sale.");
            return;
        }

        if (!this.scanner.TryValidate(this.configuration, this.Current.Candidate, out var live))
            throw new InvalidOperationException("The inventory slot changed before Have Retainer Sell Items.");

        this.Current.Candidate = live;

        if (this.configuration.AutoConfirmExpectedDialogs)
            this.retainerUi.ArmExpectedYesNo(ExpectedDialogKind.RetainerVendorSale, "Have Retainer Sell Items");

        if (!TrySubmitVendorSale(live))
        {
            this.retainerUi.DisarmExpectedYesNo();
            throw new InvalidOperationException("Could not invoke Have Retainer Sell Items.");
        }

        this.Transition(AutomationState.WaitingForVendorConfirmation, "Waiting for retainer-sale confirmation.");
    }

    private void WaitForVendorConfirmation()
    {
        if (this.retainerUi.IsSelectYesNoOpen)
        {
            if (!this.configuration.AutoConfirmExpectedDialogs)
            {
                Pause("Retainer-sale confirmation is waiting for manual approval.");
                return;
            }

            if (!this.retainerUi.ConfirmExpectedYesNo(ExpectedDialogKind.RetainerVendorSale))
                throw new InvalidOperationException("Could not confirm the retainer sale.");

            this.Transition(AutomationState.WaitingForTransaction, "Retainer sale confirmed; verifying inventory change.");
            return;
        }

        if (CurrentInventoryChanged())
        {
            this.retainerUi.DisarmExpectedYesNo();
            CompleteCurrentTransaction();
            return;
        }

        if (this.Current is not null &&
            this.vendorAttempts < 3 &&
            this.ElapsedInState > TimeSpan.FromSeconds(2.5) &&
            DateTimeOffset.UtcNow - this.lastVendorAttemptAt > TimeSpan.FromSeconds(2) &&
            this.scanner.TryValidate(this.configuration, this.Current.Candidate, out var retryLive))
        {
            if (this.configuration.AutoConfirmExpectedDialogs)
                this.retainerUi.ArmExpectedYesNo(ExpectedDialogKind.RetainerVendorSale, "Have Retainer Sell Items retry");

            if (TrySubmitVendorSale(retryLive))
            {
                this.Status = $"Retrying retainer-sale confirmation ({this.vendorAttempts}/3).";
                RecordActivity(this.Status, AutomationActivityKind.Warning);
            }
            else
            {
                this.retainerUi.DisarmExpectedYesNo();
            }
        }

        if (this.ElapsedInState > UiTimeout)
        {
            this.retainerUi.DisarmExpectedYesNo();
            throw new TimeoutException("The retainer-sale confirmation did not complete.");
        }
    }

    private void WaitForTransaction()
    {
        if (this.retainerUi.IsSelectYesNoOpen)
        {
            if (!this.configuration.AutoConfirmExpectedDialogs)
            {
                Pause("A transaction confirmation is waiting for manual approval.");
                return;
            }

            var kind = this.Current?.Action == SellAction.RetainerVendor
                ? ExpectedDialogKind.RetainerVendorSale
                : ExpectedDialogKind.MarketListing;
            if (!this.retainerUi.ConfirmExpectedYesNo(kind))
                Pause("A confirmation dialog appeared outside the active confirmation arm. Review it manually, then resume.");
            return;
        }

        if (CurrentInventoryChanged())
        {
            this.retainerUi.DisarmExpectedYesNo();
            CompleteCurrentTransaction();
            return;
        }

        if (this.ElapsedInState > UiTimeout)
        {
            this.retainerUi.DisarmExpectedYesNo();
            throw new TimeoutException("The game did not confirm an inventory change after the transaction.");
        }
    }

    private void WaitForUiClose()
    {
        if (this.retainerUi.IsRetainerSellOpen ||
            this.retainerUi.IsMarketResultsOpen ||
            this.retainerUi.IsSelectYesNoOpen)
        {
            if (this.ElapsedInState > TimeSpan.FromSeconds(10))
                throw new TimeoutException("The item sale or confirmation windows did not close.");
            return;
        }

        if (this.Current?.State == QueueEntryState.Completed)
        {
            this.Transition(AutomationState.RefreshingInventory, "Transaction windows closed; re-scanning inventory.");
            return;
        }

        if (this.Current is not null)
            this.Current.State = QueueEntryState.Skipped;

        AdvanceToNextPending();
    }

    private void RefreshInventory()
    {
        var completed = this.Queue
            .Where(entry => entry.State is QueueEntryState.Completed or QueueEntryState.Skipped or QueueEntryState.Failed)
            .ToList();

        var pending = this.scanner.Scan(this.configuration)
            .Where(candidate => !this.skippedForSession.Contains(new ItemQualityKey(candidate.ItemId, candidate.IsHq)))
            .Where(candidate => !this.skippedStacksForSession.Contains(ToInventoryStackKey(candidate)))
            .Select(candidate => new SellQueueEntry { Candidate = candidate })
            .ToList();

        this.Queue.Clear();
        this.Queue.AddRange(completed);
        this.Queue.AddRange(pending);
        this.currentIndex = FindNextPendingIndex();

        if (this.Current is null)
        {
            if (this.configuration.AutomateRetainers)
                this.Transition(AutomationState.ClosingRetainer, "No eligible inventory remains; closing retainer.");
            else
                CompleteSession("No eligible inventory remains.");
            return;
        }

        this.Transition(
            AutomationState.OpeningSellWindow,
            $"Next: {this.Current.Candidate.ItemName} ({this.Current.Candidate.QualityLabel}).");
    }

    private void CloseCurrentRetainer()
    {
        this.marketPrices.Cancel();
        this.retainerUi.DisarmComparePrices();
        this.retainerUi.DisarmExpectedYesNo();
        this.retainerUi.CloseMarketResults();
        this.retainerUi.CancelSellWindow();

        if (!this.retainerUi.CloseCurrentRetainer())
            throw new InvalidOperationException("Could not close the current retainer.");

        this.Transition(AutomationState.WaitingForRetainerList, "Waiting for the retainer list.");
    }

    private void WaitForRetainerList()
    {
        if (this.retainerUi.IsRetainerListOpen)
        {
            this.currentRetainerIndex++;

            if (this.Current is null)
            {
                CompleteSession("No eligible inventory remains.");
                return;
            }

            if (this.currentRetainerIndex >= this.sessionRetainerLimit)
            {
                CompleteSession("All selected retainers were processed.");
                return;
            }

            this.Transition(
                AutomationState.SelectingRetainer,
                $"Selecting retainer {this.CurrentRetainerNumber}/{this.sessionRetainerLimit}.");
            return;
        }

        if (this.ElapsedInState > UiTimeout)
        {
            Pause("The retainer list did not return. Reopen it and press Resume.");
        }
    }

    private bool CurrentInventoryChanged()
    {
        if (this.Current is null)
            return true;

        if (!this.scanner.TryValidate(this.configuration, this.Current.Candidate, out var live))
            return true;

        return live.Quantity != this.Current.Candidate.Quantity;
    }

    private void CompleteCurrentTransaction()
    {
        if (this.Current is not null)
            this.Current.State = QueueEntryState.Completed;

        this.Transition(AutomationState.WaitingForUiClose, "Transaction completed; waiting for sale windows to close.");
    }

    private void AdvanceToNextPending()
    {
        var nextStart = this.currentIndex + 1;
        for (var i = Math.Max(0, nextStart); i < this.Queue.Count; i++)
        {
            var entry = this.Queue[i];
            var itemKey = new ItemQualityKey(entry.Candidate.ItemId, entry.Candidate.IsHq);
            var stackKey = ToInventoryStackKey(entry.Candidate);
            if (entry.State == QueueEntryState.Pending &&
                (this.skippedForSession.Contains(itemKey) || this.skippedStacksForSession.Contains(stackKey)))
            {
                entry.Action = SellAction.Skip;
                entry.State = QueueEntryState.Skipped;
                entry.Note = "Skipped because this item-quality or inventory stack already reached a terminal skip decision in the current session.";
            }
        }

        this.currentIndex = FindNextPendingIndex(nextStart);
        if (this.Current is null)
        {
            if (this.configuration.DryRun)
            {
                CompleteSession("Dry run completed.");
                return;
            }

            if (this.configuration.AutomateRetainers)
                this.Transition(AutomationState.ClosingRetainer, "No remaining queue entries.");
            else
                CompleteSession("Queue completed.");
            return;
        }

        this.Transition(
            AutomationState.OpeningSellWindow,
            $"Next: {this.Current.Candidate.ItemName} ({this.Current.Candidate.QualityLabel}).");
    }

    private void MoveToNextRetainer(string reason)
    {
        this.log.Information("Retainer {RetainerNumber}: {Reason}", this.CurrentRetainerNumber, reason);

        if (this.Current is not null &&
            this.Current.State is not QueueEntryState.Completed
                and not QueueEntryState.Skipped
                and not QueueEntryState.Failed)
        {
            ResetEntryForRetry(this.Current, $"Deferred to the next retainer: {reason}");
        }

        if (!this.configuration.AutomateRetainers)
        {
            Pause(reason);
            return;
        }

        if (!this.retainerUi.IsRetainerContextActive)
        {
            this.Transition(AutomationState.WaitingForRetainerList, $"{reason} Waiting for the retainer list.");
            return;
        }

        this.Transition(AutomationState.ClosingRetainer, reason);
    }

    private void MarkCurrentSkipped(string note)
    {
        if (this.Current is null)
            return;

        this.Current.Action = SellAction.Skip;
        this.Current.State = QueueEntryState.Skipped;
        this.Current.Note = note;
    }

    private int FindNextPendingIndex(int start = 0)
    {
        for (var i = Math.Max(0, start); i < this.Queue.Count; i++)
        {
            if (this.Queue[i].State == QueueEntryState.Pending)
                return i;
        }

        return -1;
    }

    private void CompleteSession(string reason)
    {
        this.marketPrices.Cancel();
        this.retainerUi.DisarmComparePrices();
        this.retainerUi.DisarmExpectedYesNo();
        this.State = AutomationState.Completed;
        this.stateStartedAt = DateTimeOffset.UtcNow;
        this.stepTimer.Restart();
        var remaining = this.Queue.Count(entry => entry.State is not QueueEntryState.Completed
            and not QueueEntryState.Skipped
            and not QueueEntryState.Failed);
        this.Status = remaining > 0 ? $"{reason} {remaining} entries remain." : reason;
        RecordActivity(this.Status, AutomationActivityKind.Success);
        this.chat.Print($"[Sell Everything] {this.Status}");
    }

    private void Fault(Exception exception)
    {
        this.log.Error(exception, "Sell Everything automation failed in {State}.", this.State);
        this.marketPrices.Cancel();
        this.retainerUi.DisarmComparePrices();
        this.retainerUi.DisarmExpectedYesNo();

        if (this.Current is not null)
        {
            this.Current.State = QueueEntryState.Failed;
            this.Current.Note = exception.Message;
            this.Status = $"Stopped on {this.Current.Candidate.ItemName}: {exception.Message}";
        }
        else
        {
            this.Status = $"Stopped: {exception.Message}";
        }

        this.State = AutomationState.Faulted;
        this.stateStartedAt = DateTimeOffset.UtcNow;
        this.stepTimer.Restart();
        this.LastFault = exception.Message;
        RecordActivity(this.Status, AutomationActivityKind.Error);
        this.chat.PrintError($"[Sell Everything] {this.Status}");
    }

    private void Transition(AutomationState state, string status)
    {
        this.State = state;
        this.Status = status;
        this.stateStartedAt = DateTimeOffset.UtcNow;
        this.stepTimer.Restart();
        RecordActivity(status, state == AutomationState.Completed ? AutomationActivityKind.Success : AutomationActivityKind.Info);
    }

    private bool TrySubmitListing(uint price, int quantity)
    {
        if (!this.retainerUi.SetPriceAndConfirm(price, quantity))
            return false;

        this.lastListingConfirmAttemptAt = DateTimeOffset.UtcNow;
        this.listingConfirmAttempts++;
        return true;
    }

    private bool TrySubmitVendorSale(SellCandidate candidate)
    {
        if (!this.retainerUi.SellToRetainer(candidate.InventoryType, candidate.Slot))
            return false;

        this.lastVendorAttemptAt = DateTimeOffset.UtcNow;
        this.vendorAttempts++;
        return true;
    }

    private static void ResetEntryForRetry(SellQueueEntry entry, string note)
    {
        entry.State = QueueEntryState.Pending;
        entry.Action = SellAction.Unknown;
        entry.LowestMatchingPrice = null;
        entry.ListingPrice = null;
        entry.HqListingsSeen = 0;
        entry.NqListingsSeen = 0;
        entry.OwnListingsIgnored = 0;
        entry.MarketRequestId = null;
        entry.Note = note;
    }

    private void RecordActivity(string message, AutomationActivityKind kind)
    {
        this.activity.Insert(0, new AutomationActivity(DateTimeOffset.UtcNow, this.State, message, kind));
        if (this.activity.Count > 80)
            this.activity.RemoveRange(80, this.activity.Count - 80);
    }

    private int ComputeConfigurationFingerprint()
    {
        var hash = new HashCode();
        hash.Add(this.configuration.MarketFloor);
        hash.Add(this.configuration.UndercutAmount);
        hash.Add(this.configuration.DryRun);
        hash.Add(this.configuration.SkipGear);
        hash.Add(this.configuration.SkipMateriaAttached);
        hash.Add(this.configuration.SkipCollectables);
        hash.Add(this.configuration.AutomateRetainers);
        hash.Add(this.configuration.AutoConfirmExpectedDialogs);
        hash.Add(this.configuration.UndercutOwnRetainers);
        hash.Add(this.configuration.RetainersPerSession);
        hash.Add(this.configuration.MarketMenuOptionIndex);

        foreach (var rule in this.configuration.ProtectedItems
                     .OrderBy(rule => rule.ItemId)
                     .ThenBy(rule => rule.Quality)
                     .ThenBy(rule => rule.KeepQuantity))
        {
            hash.Add(rule.ItemId);
            hash.Add(rule.Quality);
            hash.Add(rule.KeepQuantity);
        }

        return hash.ToHashCode();
    }

    private static InventoryStackKey ToInventoryStackKey(SellCandidate candidate)
        => new(candidate.InventoryType, candidate.Slot, candidate.ItemId, candidate.IsHq);

    private TimeSpan ElapsedInState => DateTimeOffset.UtcNow - this.stateStartedAt;
    private TimeSpan MarketTimeout => TimeSpan.FromMilliseconds(Math.Clamp(this.configuration.MarketTimeoutMilliseconds, 5000, 60000));
    private TimeSpan UiTimeout => TimeSpan.FromMilliseconds(Math.Clamp(this.configuration.UiTimeoutMilliseconds, 5000, 60000));

    private readonly record struct ItemQualityKey(uint ItemId, bool IsHq);
    private readonly record struct InventoryStackKey(
        FFXIVClientStructs.FFXIV.Client.Game.InventoryType InventoryType,
        uint Slot,
        uint ItemId,
        bool IsHq);
}

public enum AutomationState
{
    Idle,
    ReadyForReview,
    SelectingRetainer,
    WaitingForRetainerMenu,
    SelectingMarketMenu,
    WaitingForRetainerReady,
    OpeningSellWindow,
    WaitingForSellWindow,
    WaitingForMarketPrice,
    WaitingForMarketResultsClose,
    ExecutingDecision,
    WaitingForListingConfirmation,
    WaitingToVendor,
    WaitingForVendorConfirmation,
    WaitingForTransaction,
    WaitingForUiClose,
    RefreshingInventory,
    ClosingRetainer,
    WaitingForRetainerList,
    Paused,
    Completed,
    Faulted,
}


public sealed record AutomationActivity(
    DateTimeOffset Timestamp,
    AutomationState State,
    string Message,
    AutomationActivityKind Kind);

public enum AutomationActivityKind
{
    Info,
    Success,
    Warning,
    Error,
}
