using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using SellEverything.Models;

namespace SellEverything.Services;

public sealed unsafe class InventoryScanner(IDataManager dataManager, IPluginLog log)
{
    private static readonly InventoryType[] PlayerInventories =
    [
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4,
    ];

    public List<SellCandidate> Scan(Configuration configuration)
    {
        var result = new List<SellCandidate>();
        var manager = InventoryManager.Instance();
        if (manager == null)
            return result;

        foreach (var inventoryType in PlayerInventories)
        {
            var container = manager->GetInventoryContainer(inventoryType);
            if (container == null || !container->IsLoaded)
                continue;

            for (var slotIndex = 0; slotIndex < container->Size; slotIndex++)
            {
                if (TryReadCandidate(configuration, inventoryType, (uint)slotIndex, out var candidate))
                    result.Add(candidate);
            }
        }

        log.Information("Sell Everything scanned {Count} eligible stacks.", result.Count);
        return result;
    }

    public bool TryValidate(Configuration configuration, SellCandidate expected, out SellCandidate live)
    {
        if (!TryReadCandidate(configuration, expected.InventoryType, expected.Slot, out live))
            return false;

        return live.ItemId == expected.ItemId && live.IsHq == expected.IsHq;
    }

    private bool TryReadCandidate(
        Configuration configuration,
        InventoryType inventoryType,
        uint slot,
        out SellCandidate candidate)
    {
        candidate = null!;

        var manager = InventoryManager.Instance();
        if (manager == null)
            return false;

        var container = manager->GetInventoryContainer(inventoryType);
        if (container == null || !container->IsLoaded || slot >= (uint)container->Size)
            return false;

        var inventoryItem = container->GetInventorySlot((int)slot);
        if (inventoryItem == null || inventoryItem->ItemId == 0 || inventoryItem->Quantity <= 0)
            return false;

        var itemSheet = dataManager.GetExcelSheet<Item>();
        if (!itemSheet.TryGetRow(inventoryItem->ItemId, out var itemRow))
            return false;

        var isHq = inventoryItem->IsHighQuality();
        var quantity = inventoryItem->Quantity;

        if (IsProtected(configuration, inventoryItem->ItemId, isHq, quantity))
            return false;

        if (configuration.SkipCollectables && inventoryItem->IsCollectable())
            return false;

        if (configuration.SkipMateriaAttached && inventoryItem->GetMateriaCount() > 0)
            return false;

        if (configuration.SkipGear && itemRow.EquipSlotCategory.RowId != 0)
            return false;

        var marketable = !itemRow.IsUntradable && itemRow.ItemSearchCategory.RowId != 0;
        if (!marketable)
            return false;

        candidate = new SellCandidate(
            inventoryType,
            slot,
            inventoryItem->ItemId,
            itemRow.Name.ToString(),
            quantity,
            isHq,
            itemRow.PriceLow,
            itemRow.CanBeHq,
            marketable);

        return true;
    }

    private static bool IsProtected(Configuration configuration, uint itemId, bool isHq, int quantity)
    {
        foreach (var rule in configuration.ProtectedItems)
        {
            if (rule.ItemId != itemId)
                continue;

            var qualityMatches = rule.Quality switch
            {
                QualityScope.Both => true,
                QualityScope.NqOnly => !isHq,
                QualityScope.HqOnly => isHq,
                _ => true,
            };

            if (!qualityMatches)
                continue;

            if (rule.KeepQuantity == int.MaxValue || quantity <= rule.KeepQuantity)
                return true;
        }

        return false;
    }
}
