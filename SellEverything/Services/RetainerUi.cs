using System.Runtime.InteropServices;
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

    // AddonRetainerSell receives Change events for these controls.
    private const int ComparePricesEventParam = 4;
    private const int ConfirmListingEventParam = 21;
    private const int ChangeEventType = 25;

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
        if (addon == null || !addon->AtkUnitBase.IsVisible || addon->ComparePrices == null || !addon->ComparePrices->IsEnabled)
            return false;

        var target = addon->ComparePrices->AtkComponentBase.OwnerNode;
        if (target == null)
            return false;

        return SendChangeEvent(
            (AtkEventListener*)addon,
            ComparePricesEventParam,
            target,
            "Compare Prices");
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
        if (addon == null || !addon->AtkUnitBase.IsVisible || addon->AskingPrice == null || addon->Confirm == null)
            return false;

        if (!addon->Confirm->IsEnabled)
            return false;

        addon->AskingPrice->SetValue((int)Math.Clamp(price, 1, int.MaxValue));

        return SendChangeEvent(
            (AtkEventListener*)addon,
            ConfirmListingEventParam,
            addon->Confirm,
            "Confirm listing");
    }

    public bool CancelSellWindow()
    {
        var addon = gameGui.GetAddonByName<AddonRetainerSell>("RetainerSell");
        if (addon == null)
            return true;

        try
        {
            ((AtkUnitBase*)addon)->Close(true);
            return true;
        }
        catch (Exception exception)
        {
            log.Error(exception, "Failed to close the RetainerSell window.");
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
            // The return value is the addon's callback result, not proof that the
            // callback was dispatched. Only an exception is treated as failure.
            _ = addon->FireCallbackInt(value);
            return true;
        }
        catch (Exception exception)
        {
            log.Error(exception, "Failed to select {Label} with callback value {Value}.", label, value);
            return false;
        }
    }

    private bool SendChangeEvent(AtkEventListener* listener, int eventParam, void* target, string label)
    {
        if (listener == null || target == null)
            return false;

        IntPtr eventMemory = IntPtr.Zero;
        IntPtr eventDataMemory = IntPtr.Zero;

        try
        {
            eventMemory = Marshal.AllocHGlobal(0x40);
            eventDataMemory = Marshal.AllocHGlobal(0x40);

            for (var i = 0; i < 0x40; i++)
            {
                Marshal.WriteByte(eventMemory, i, 0);
                Marshal.WriteByte(eventDataMemory, i, 0);
            }

            Marshal.WriteIntPtr(eventMemory, 0x08, new IntPtr(target));
            Marshal.WriteIntPtr(eventMemory, 0x10, new IntPtr(listener));

            listener->ReceiveEvent(
                (AtkEventType)ChangeEventType,
                eventParam,
                (AtkEvent*)eventMemory,
                (AtkEventData*)eventDataMemory);

            return true;
        }
        catch (Exception exception)
        {
            log.Error(exception, "Failed to activate {Label} with event parameter {EventParam}.", label, eventParam);
            return false;
        }
        finally
        {
            if (eventMemory != IntPtr.Zero)
                Marshal.FreeHGlobal(eventMemory);
            if (eventDataMemory != IntPtr.Zero)
                Marshal.FreeHGlobal(eventDataMemory);
        }
    }
}
