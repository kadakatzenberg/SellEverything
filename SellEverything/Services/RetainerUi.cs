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

    public bool IsMarketResultsOpen
    {
        get
        {
            var addon = gameGui.GetAddonByName<AtkUnitBase>("ItemSearchResult");
            return addon != null && addon->IsVisible;
        }
    }

    public bool IsRetainerSellOpen
    {
        get
        {
            var addon = gameGui.GetAddonByName<AddonRetainerSell>("RetainerSell");
            return addon != null && addon->AtkUnitBase.IsVisible;
        }
    }

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
        return ClickButton(addon->Confirm, "Confirm");
    }

    public bool CancelSellWindow()
    {
        var addon = gameGui.GetAddonByName<AddonRetainerSell>("RetainerSell");
        if (addon == null || addon->Cancel == null)
            return false;

        return ClickButton(addon->Cancel, "Cancel");
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
