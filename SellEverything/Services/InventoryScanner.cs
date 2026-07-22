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

        var itemSheet = dataManager.GetExcelSheet<Item>();

        foreach (var inventoryType in PlayerInventories)
        {
            var container = manager->GetInventoryContainer(inventoryType);
            if (container == null || !container->IsLoaded)
                continue;

            for (var slotIndex = 0; slotIndex < container->Size; slotIndex++)
            {
                var inventoryItem = container->GetInventorySlot(slotIndex);
                if (inventoryItem == null || inventoryItem->ItemId == 0 || inventoryItem->Quantity <= 0)
                    continue;

                if (!itemSheet.TryGetRow(inventoryItem->ItemId, out var itemRow))
                    continue;

                var isHq = inventoryItem->IsHighQuality();
                var quantity = inventoryItem->Quantity;

                if (IsProtected(configuration, inventoryItem->ItemId, isHq, quantity))
                    continue;

                if (configuration.SkipCollectables && inventoryItem->IsCollectable())
                    continue;

                if (configuration.SkipMateriaAttached && inventoryItem->GetMateriaCount() > 0)
                    continue;

                if (configuration.SkipGear && itemRow.EquipSlotCategory.RowId != 0)
                    continue;

                var marketable = !itemRow.IsUntradable && itemRow.ItemSearchCategory.RowId != 0;
                if (!marketable)
                    continue;

                result.Add(new SellCandidate(
                    inventoryType,
                    (uint)slotIndex,
                    inventoryItem->ItemId,
                    itemRow.Name.ToString(),
                    quantity,
                    isHq,
                    itemRow.PriceLow,
                    itemRow.CanBeHq,
                    marketable));
            }
        }

        log.Information("Sell Everything scanned {Count} eligible stacks.", result.Count);
        return result;
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
