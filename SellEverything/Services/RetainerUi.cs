using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace SellEverything.Services;

/// <summary>
/// Owns all native retainer UI interaction.
///
/// The RetainerSell and ItemSearchResult interaction sequence follows the
/// event-driven pattern used by Marketbuddy: wait for addon setup, send the
/// addon's native event with the exact event parameter and target node, set the
/// numeric input directly, close the result window through its window
/// component, then submit the listing through RetainerSell.
/// </summary>
public sealed unsafe class RetainerUi : IDisposable
{
    private const ulong PutUpForSaleCallback = 2;
    private const ulong RetainerVendorCallback = 5;

    // These parameters are handled by the current RetainerSell addon.
    private const int ComparePricesEventParam = 4;
    private const int ConfirmListingEventParam = 21;
    private const int CloseSearchResultsEventParam = 2;

    // Marketbuddy calls this CHANGE. In current FFXIVClientStructs the numeric
    // value remains 25, so keep the value explicit rather than relying on an
    // enum name that has changed between client-struct revisions.
    private const AtkEventType NativeChangeEvent = (AtkEventType)25;

    private readonly IGameGui gameGui;
    private readonly IAddonLifecycle addonLifecycle;
    private readonly IPluginLog log;

    private nint retainerSellAddress;
    private nint itemSearchResultAddress;
    private nint selectYesNoAddress;

    private bool comparePricesArmed;
    private bool comparePricesDispatched;
    private int comparePricesDispatchAttempts;
    private DateTimeOffset? lastComparePricesDispatchAt;
    private bool expectedYesNoArmed;
    private ExpectedDialogKind expectedDialogKind;
    private DateTimeOffset? expectedYesNoArmedAt;
    private string expectedYesNoPurpose = string.Empty;
    private bool? currentSellWindowIsHq;
    private string currentSellWindowItemName = string.Empty;
    private bool? expectedSellWindowIsHq;
    private string expectedSellWindowItemName = string.Empty;
    private string comparePricesValidationFailure = string.Empty;

    public RetainerUi(IGameGui gameGui, IAddonLifecycle addonLifecycle, IPluginLog log)
    {
        this.gameGui = gameGui;
        this.addonLifecycle = addonLifecycle;
        this.log = log;

        addonLifecycle.RegisterListener(AddonEvent.PostSetup, "RetainerSell", this.OnRetainerSellSetup);
        addonLifecycle.RegisterListener(AddonEvent.PreFinalize, "RetainerSell", this.OnRetainerSellFinalize);
        addonLifecycle.RegisterListener(AddonEvent.PostSetup, "ItemSearchResult", this.OnItemSearchResultSetup);
        addonLifecycle.RegisterListener(AddonEvent.PreFinalize, "ItemSearchResult", this.OnItemSearchResultFinalize);
        addonLifecycle.RegisterListener(AddonEvent.PostSetup, "SelectYesno", this.OnSelectYesNoSetup);
        addonLifecycle.RegisterListener(AddonEvent.PreFinalize, "SelectYesno", this.OnSelectYesNoFinalize);
    }

    public bool IsRetainerListOpen => this.IsAddonVisible("RetainerList");
    public bool IsSelectStringOpen => this.IsAddonVisible("SelectString");
    public bool IsSelectYesNoOpen => this.GetVisibleAddon("SelectYesno", this.selectYesNoAddress) != null;
    public bool IsSelectOkOpen => this.IsAddonVisible("SelectOk");
    public bool IsMarketResultsOpen => this.GetVisibleAddon("ItemSearchResult", this.itemSearchResultAddress) != null;
    public bool ComparePricesDispatched => this.comparePricesDispatched;
    public int ComparePricesDispatchAttempts => this.comparePricesDispatchAttempts;
    public DateTimeOffset? LastComparePricesDispatchAt => this.lastComparePricesDispatchAt;
    public bool? CurrentSellWindowIsHq => this.currentSellWindowIsHq;
    public string CurrentSellWindowItemName => this.currentSellWindowItemName;
    public string ComparePricesValidationFailure => this.comparePricesValidationFailure;
    public string CurrentYesNoPrompt => this.GetCurrentYesNoPrompt();

    /// <summary>
    /// Raised synchronously when ItemSearchResult reaches PostSetup. Market
    /// response collection is activated here to mirror Penny Pincher's
    /// new-request gate.
    /// </summary>
    public event Action? MarketResultsOpened;

    public bool IsRetainerContextActive
    {
        get
        {
            var module = ItemOrderModule.Instance();
            return module != null && module->ActiveRetainerId != 0;
        }
    }

    public bool IsRetainerSellOpen
    {
        get
        {
            var addon = this.GetRetainerSell();
            return addon != null && addon->AtkUnitBase.IsVisible;
        }
    }

    public int RetainerCount
    {
        get
        {
            var agent = AgentRetainerList.Instance();
            return agent == null ? 0 : agent->RetainerCount;
        }
    }

    /// <summary>
    /// Runs every framework frame while automation is active. Lifecycle events
    /// identify the exact addon instance; Pump performs actions outside the
    /// lifecycle hook to avoid depending on the automation's configured delay.
    /// </summary>
    public void Pump()
    {
        if (this.comparePricesArmed && !this.comparePricesDispatched)
            _ = this.TryDispatchComparePrices();

        if (this.expectedYesNoArmed &&
            this.expectedYesNoArmedAt is DateTimeOffset armedAt &&
            DateTimeOffset.UtcNow - armedAt > TimeSpan.FromSeconds(8))
        {
            this.log.Warning(
                "Expired expected SelectYesno arm for {Purpose} without confirming a dialog.",
                this.expectedYesNoPurpose);
            this.DisarmExpectedYesNo();
        }
    }

    public bool SelectRetainer(int zeroBasedIndex)
    {
        var count = this.RetainerCount;
        if (zeroBasedIndex < 0 || (count > 0 && zeroBasedIndex >= count))
            return false;

        return this.FireIntCallback("RetainerList", zeroBasedIndex, "retainer row");
    }

    public bool SelectMarketSellingMenu(int zeroBasedIndex)
        => this.FireIntCallback("SelectString", zeroBasedIndex, "retainer market menu");

    public bool DismissExpectedOk()
        => this.FireIntCallback("SelectOk", 0, "OK");

    public bool OpenPutUpForSale(InventoryType inventoryType, uint slot)
    {
        var agent = AgentRetainer.Instance();
        if (agent == null)
            return false;

        agent->HandleCallback(slot, inventoryType, (InventoryContextFlag)0, PutUpForSaleCallback);
        return true;
    }

    public bool SellToRetainer(InventoryType inventoryType, uint slot)
    {
        var agent = AgentRetainer.Instance();
        if (agent == null)
            return false;

        agent->HandleCallback(slot, inventoryType, (InventoryContextFlag)0, RetainerVendorCallback);
        return true;
    }

    /// <summary>
    /// Arms Compare Prices before RetainerSell opens. The lifecycle listener
    /// then dispatches the event as soon as PostSetup has completed, matching
    /// Marketbuddy's reliable timing.
    /// </summary>
    public void ArmComparePrices(bool expectedIsHq, string expectedItemName)
    {
        this.comparePricesArmed = true;
        this.comparePricesDispatched = false;
        this.comparePricesDispatchAttempts = 0;
        this.lastComparePricesDispatchAt = null;
        this.expectedSellWindowIsHq = expectedIsHq;
        this.expectedSellWindowItemName = expectedItemName;
        this.comparePricesValidationFailure = string.Empty;

        // Also covers a RetainerSell addon that was already set up before the
        // arm call, without waiting for the next automation tick.
        _ = this.TryDispatchComparePrices();
    }

    public void DisarmComparePrices()
    {
        this.comparePricesArmed = false;
        this.comparePricesDispatched = false;
        this.comparePricesDispatchAttempts = 0;
        this.lastComparePricesDispatchAt = null;
        this.expectedSellWindowIsHq = null;
        this.expectedSellWindowItemName = string.Empty;
        this.comparePricesValidationFailure = string.Empty;
    }

    public bool TryDispatchComparePrices()
    {
        if (this.comparePricesDispatched)
            return true;

        if (!this.comparePricesArmed || !string.IsNullOrEmpty(this.comparePricesValidationFailure))
            return false;

        if (!this.IsRetainerContextActive)
            return false;

        var addon = this.GetRetainerSell();
        if (addon == null ||
            !addon->AtkUnitBase.IsReady ||
            !addon->AtkUnitBase.IsVisible ||
            addon->ComparePrices == null)
        {
            return false;
        }

        var target = addon->ComparePrices->AtkComponentBase.OwnerNode;
        if (target == null)
            return false;

        if (!this.SendNativeChange((AtkEventListener*)addon, ComparePricesEventParam, target, "Compare Prices"))
            return false;

        this.comparePricesDispatched = true;
        this.comparePricesDispatchAttempts++;
        this.lastComparePricesDispatchAt = DateTimeOffset.UtcNow;
        return true;
    }

    public bool RetryComparePrices()
    {
        if (!this.comparePricesArmed || this.IsMarketResultsOpen || this.comparePricesDispatchAttempts >= 3)
            return false;

        this.comparePricesDispatched = false;
        return this.TryDispatchComparePrices();
    }

    public bool CloseMarketResults()
    {
        var addon = this.GetItemSearchResult();
        if (addon == null || !addon->IsVisible)
            return true;

        try
        {
            var windowNode = addon->WindowNode;
            var windowComponent = windowNode == null ? null : windowNode->Component;

            // Marketbuddy closes ItemSearchResult through the window component
            // using event parameter 2. This avoids the unreliable generic
            // AtkUnitBase.Close path for this addon.
            if (windowComponent != null &&
                windowComponent->UldManager.NodeListCount > 7 &&
                windowComponent->UldManager.NodeList[7] != null)
            {
                var closeComponent = windowComponent->UldManager.NodeList[7]->GetComponent();
                var closeTarget = closeComponent == null ? null : closeComponent->OwnerNode;
                if (closeTarget != null &&
                    this.SendNativeChange(
                        (AtkEventListener*)windowComponent,
                        CloseSearchResultsEventParam,
                        closeTarget,
                        "Close market results"))
                {
                    return true;
                }
            }

            // Patch-safe fallback if the window node layout changes.
            addon->Close(true);
            return true;
        }
        catch (Exception exception)
        {
            this.log.Error(exception, "Failed to close ItemSearchResult.");
            return false;
        }
    }

    public bool SetPriceAndConfirm(uint price, int quantity)
    {
        var addon = this.GetRetainerSell();
        if (addon == null ||
            !addon->AtkUnitBase.IsVisible ||
            addon->Quantity == null ||
            addon->AskingPrice == null ||
            addon->Confirm == null)
        {
            return false;
        }

        try
        {
            var safeQuantity = Math.Max(1, quantity);
            var safePrice = (int)Math.Min(Math.Max(1u, price), (uint)int.MaxValue);
            addon->Quantity->SetValue(safeQuantity);
            addon->AskingPrice->SetValue(safePrice);

            // The target is the Confirm component itself. This is the exact
            // target used by RetainerSell's native event handler.
            return this.SendNativeChange(
                (AtkEventListener*)addon,
                ConfirmListingEventParam,
                addon->Confirm,
                "Confirm listing");
        }
        catch (Exception exception)
        {
            this.log.Error(exception, "Failed to set the listing quantity, price, or submit RetainerSell.");
            return false;
        }
    }

    public void ArmExpectedYesNo(ExpectedDialogKind kind, string purpose)
    {
        this.expectedDialogKind = kind;
        this.expectedYesNoPurpose = purpose;
        this.expectedYesNoArmed = true;
        this.expectedYesNoArmedAt = DateTimeOffset.UtcNow;
    }

    public void DisarmExpectedYesNo()
    {
        this.expectedDialogKind = ExpectedDialogKind.None;
        this.expectedYesNoArmed = false;
        this.expectedYesNoArmedAt = null;
        this.expectedYesNoPurpose = string.Empty;
    }

    public bool ConfirmExpectedYesNo(ExpectedDialogKind kind)
        => this.TryConfirmExpectedYesNo(kind);

    public bool CancelSellWindow()
    {
        var addon = this.GetRetainerSell();
        if (addon == null)
            return true;

        try
        {
            ((AtkUnitBase*)addon)->Close(true);
            return true;
        }
        catch (Exception exception)
        {
            this.log.Error(exception, "Failed to close the RetainerSell window.");
            return false;
        }
    }

    public bool CloseCurrentRetainer()
    {
        var agent = AgentRetainer.Instance();
        if (agent == null)
            return false;

        try
        {
            agent->Hide();
            return true;
        }
        catch (Exception exception)
        {
            this.log.Error(exception, "Failed to close the current retainer agent.");
            return false;
        }
    }

    public void Dispose()
    {
        this.addonLifecycle.UnregisterListener(this.OnRetainerSellSetup);
        this.addonLifecycle.UnregisterListener(this.OnRetainerSellFinalize);
        this.addonLifecycle.UnregisterListener(this.OnItemSearchResultSetup);
        this.addonLifecycle.UnregisterListener(this.OnItemSearchResultFinalize);
        this.addonLifecycle.UnregisterListener(this.OnSelectYesNoSetup);
        this.addonLifecycle.UnregisterListener(this.OnSelectYesNoFinalize);
    }

    private void OnRetainerSellSetup(AddonEvent _, AddonArgs args)
    {
        this.retainerSellAddress = args.Addon.Address;

        var addon = (AddonRetainerSell*)args.Addon.Address;
        this.currentSellWindowItemName = addon->ItemName == null
            ? string.Empty
            : addon->ItemName->NodeText.ToString();
        this.currentSellWindowIsHq = string.IsNullOrEmpty(this.currentSellWindowItemName)
            ? null
            : this.currentSellWindowItemName.Contains('\uE03C');

        this.log.Debug(
            "RetainerSell opened for {ItemName}; detected quality {Quality}.",
            this.currentSellWindowItemName,
            this.currentSellWindowIsHq is bool isHq ? (isHq ? "HQ" : "NQ") : "unknown");

        if (this.expectedSellWindowIsHq is bool expectedIsHq &&
            this.currentSellWindowIsHq is bool actualIsHq &&
            expectedIsHq != actualIsHq)
        {
            this.comparePricesValidationFailure =
                $"RetainerSell quality mismatch for {this.expectedSellWindowItemName}: expected {(expectedIsHq ? "HQ" : "NQ")}, UI shows {(actualIsHq ? "HQ" : "NQ")}.";
            this.log.Error(this.comparePricesValidationFailure);
            return;
        }

        if (this.comparePricesArmed)
            _ = this.TryDispatchComparePrices();
    }

    private void OnRetainerSellFinalize(AddonEvent _, AddonArgs args)
    {
        if (this.retainerSellAddress == args.Addon.Address)
        {
            this.retainerSellAddress = 0;
            this.currentSellWindowIsHq = null;
            this.currentSellWindowItemName = string.Empty;
        }
    }

    private void OnItemSearchResultSetup(AddonEvent _, AddonArgs args)
    {
        this.itemSearchResultAddress = args.Addon.Address;
        this.comparePricesArmed = false;
        this.MarketResultsOpened?.Invoke();
    }

    private void OnItemSearchResultFinalize(AddonEvent _, AddonArgs args)
    {
        if (this.itemSearchResultAddress == args.Addon.Address)
            this.itemSearchResultAddress = 0;
    }

    private void OnSelectYesNoSetup(AddonEvent _, AddonArgs args)
        => this.selectYesNoAddress = args.Addon.Address;

    private void OnSelectYesNoFinalize(AddonEvent _, AddonArgs args)
    {
        if (this.selectYesNoAddress == args.Addon.Address)
            this.selectYesNoAddress = 0;
    }

    private bool TryConfirmExpectedYesNo(ExpectedDialogKind kind)
    {
        if (!this.expectedYesNoArmed ||
            kind == ExpectedDialogKind.None ||
            this.expectedDialogKind != kind)
        {
            return false;
        }

        if (this.expectedYesNoArmedAt is not DateTimeOffset armedAt ||
            DateTimeOffset.UtcNow - armedAt > TimeSpan.FromSeconds(8))
        {
            this.DisarmExpectedYesNo();
            return false;
        }

        var addon = (AddonSelectYesno*)this.GetVisibleAddon("SelectYesno", this.selectYesNoAddress);
        if (addon == null || addon->PromptText == null)
            return false;

        var prompt = addon->PromptText->NodeText.ToString().Trim();
        if (prompt.Length == 0)
        {
            this.log.Warning(
                "Refused to confirm an empty SelectYesno prompt for {Purpose}.",
                this.expectedYesNoPurpose);
            return false;
        }

        var purpose = string.IsNullOrWhiteSpace(this.expectedYesNoPurpose)
            ? kind.ToString()
            : this.expectedYesNoPurpose;

        // Clear the arm before firing because the callback can immediately
        // finalize the addon and re-enter lifecycle listeners.
        this.DisarmExpectedYesNo();

        try
        {
            _ = addon->AtkUnitBase.FireCallbackInt(0);
            this.log.Debug(
                "Confirmed state-bound SelectYesno dialog for {Purpose}: {Prompt}",
                purpose,
                prompt);
            return true;
        }
        catch (Exception exception)
        {
            this.log.Error(exception, "Failed to confirm expected SelectYesno dialog: {Purpose}.", purpose);
            return false;
        }
    }

    private string GetCurrentYesNoPrompt()
    {
        var addon = (AddonSelectYesno*)this.GetVisibleAddon("SelectYesno", this.selectYesNoAddress);
        return addon == null || addon->PromptText == null
            ? string.Empty
            : addon->PromptText->NodeText.ToString();
    }

    private AddonRetainerSell* GetRetainerSell()
    {
        if (this.retainerSellAddress != 0)
            return (AddonRetainerSell*)this.retainerSellAddress;

        return this.gameGui.GetAddonByName<AddonRetainerSell>("RetainerSell");
    }

    private AtkUnitBase* GetItemSearchResult()
    {
        if (this.itemSearchResultAddress != 0)
            return (AtkUnitBase*)this.itemSearchResultAddress;

        return this.gameGui.GetAddonByName<AtkUnitBase>("ItemSearchResult");
    }

    private AtkUnitBase* GetVisibleAddon(string name, nint trackedAddress)
    {
        var addon = trackedAddress != 0
            ? (AtkUnitBase*)trackedAddress
            : this.gameGui.GetAddonByName<AtkUnitBase>(name);

        return addon != null && addon->IsVisible ? addon : null;
    }

    private bool IsAddonVisible(string name)
    {
        var addon = this.gameGui.GetAddonByName<AtkUnitBase>(name);
        return addon != null && addon->IsVisible;
    }

    private bool FireIntCallback(string addonName, int value, string label)
    {
        var addon = this.gameGui.GetAddonByName<AtkUnitBase>(addonName);
        if (addon == null || !addon->IsVisible)
            return false;

        try
        {
            // FireCallbackInt's return value is the addon's own callback result,
            // not proof that dispatch occurred. An exception is the only local
            // failure condition.
            _ = addon->FireCallbackInt(value);
            return true;
        }
        catch (Exception exception)
        {
            this.log.Error(exception, "Failed to select {Label} with callback value {Value}.", label, value);
            return false;
        }
    }

    private bool SendNativeChange(AtkEventListener* listener, int eventParam, void* target, string label)
    {
        if (listener == null || target == null)
            return false;

        try
        {
            AtkEvent nativeEvent = default;
            AtkEventData nativeEventData = default;

            nativeEvent.Target = (AtkEventTarget*)target;
            nativeEvent.Listener = listener;
            nativeEvent.Param = (uint)eventParam;

            listener->ReceiveEvent(
                NativeChangeEvent,
                eventParam,
                &nativeEvent,
                &nativeEventData);

            this.log.Debug("Activated {Label} with native event parameter {EventParam}.", label, eventParam);
            return true;
        }
        catch (Exception exception)
        {
            this.log.Error(exception, "Failed to activate {Label} with event parameter {EventParam}.", label, eventParam);
            return false;
        }
    }
}

public enum ExpectedDialogKind
{
    None,
    MarketListing,
    RetainerVendorSale,
}
