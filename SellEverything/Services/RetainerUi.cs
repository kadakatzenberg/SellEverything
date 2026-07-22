using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace SellEverything.Services;

public sealed unsafe class RetainerUi(IGameGui gameGui, IPluginLog log)
{
    private const ulong PutUpForSaleCallback = 2;
    private const ulong RetainerVendorCallback = 5;

    public bool IsRetainerListOpen => IsAddonVisible("RetainerList");
    public bool IsSelectStringOpen => IsAddonVisible("SelectString");
    public bool IsSelectYesNoOpen => IsAddonVisible("SelectYesno");
    public bool IsSelectOkOpen => IsAddonVisible("SelectOk");
    public bool IsMarketResultsOpen => IsAddonVisible("ItemSearchResult");

    public bool IsRetainerSellOpen
    {
        get
        {
            var addon = gameGui.GetAddonByName<AddonRetainerSell>("RetainerSell");
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

    public bool SelectRetainer(int zeroBasedIndex)
    {
        var count = this.RetainerCount;
        if (zeroBasedIndex < 0 || (count > 0 && zeroBasedIndex >= count))
            return false;

        return FireIntCallback("RetainerList", zeroBasedIndex, "retainer row");
    }

    public bool SelectMarketSellingMenu(int zeroBasedIndex)
        => FireIntCallback("SelectString", zeroBasedIndex, "retainer market menu");

    public bool ConfirmExpectedYesNo()
        => FireIntCallback("SelectYesno", 0, "Yes");

    public bool DismissExpectedOk()
        => FireIntCallback("SelectOk", 0, "OK");

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

    public bool ClickComparePrices()
    {
        var addon = gameGui.GetAddonByName<AddonRetainerSell>("RetainerSell");
        if (addon == null || addon->ComparePrices == null || !addon->ComparePrices->IsEnabled)
            return false;

        return ClickButton(addon->ComparePrices, "Compare Prices");
    }

    public bool CloseMarketResults()
    {
        var addon = gameGui.GetAddonByName<AtkUnitBase>("ItemSearchResult");
        if (addon == null)
            return true;

        addon->Close(true);
        return true;
    }

    public bool SetPriceAndConfirm(uint price)
    {
        var addon = gameGui.GetAddonByName<AddonRetainerSell>("RetainerSell");
        if (addon == null || addon->AskingPrice == null || addon->Confirm == null)
            return false;

        addon->AskingPrice->SetValue((int)Math.Clamp(price, 1, int.MaxValue));
        return ClickButton(addon->Confirm, "Confirm listing");
    }

    public bool CancelSellWindow()
    {
        var addon = gameGui.GetAddonByName<AddonRetainerSell>("RetainerSell");
        if (addon == null)
            return true;
        if (addon->Cancel == null)
            return false;

        return ClickButton(addon->Cancel, "Cancel");
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
            log.Error(exception, "Failed to close the current retainer agent.");
            return false;
        }
    }

    private bool IsAddonVisible(string name)
    {
        var addon = gameGui.GetAddonByName<AtkUnitBase>(name);
        return addon != null && addon->IsVisible;
    }

    private bool FireIntCallback(string addonName, int value, string label)
    {
        var addon = gameGui.GetAddonByName<AtkUnitBase>(addonName);
        if (addon == null || !addon->IsVisible)
            return false;

        try
        {
            return addon->FireCallbackInt(value);
        }
        catch (Exception exception)
        {
            log.Error(exception, "Failed to select {Label} with callback value {Value}.", label, value);
            return false;
        }
    }

    private bool ClickButton(AtkComponentButton* button, string label)
    {
        if (button == null || !button->IsEnabled)
            return false;

        try
        {
            button->ReceiveEvent(AtkEventType.ButtonClick, 0, null, null);
            return true;
        }
        catch (Exception exception)
        {
            log.Error(exception, "Failed to click {Label}.", label);
            return false;
        }
    }
}
