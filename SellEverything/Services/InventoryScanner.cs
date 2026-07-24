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
        var result = ScanInternal(configuration);
        log.Information("Sell Everything scanned {Count} eligible stacks.", result.Count);
        return result;
    }

    public bool TryValidate(Configuration configuration, SellCandidate expected, out SellCandidate live)
    {
        live = ScanInternal(configuration)
            .FirstOrDefault(candidate =>
                candidate.InventoryType == expected.InventoryType &&
                candidate.Slot == expected.Slot &&
                candidate.ItemId == expected.ItemId &&
                candidate.IsHq == expected.IsHq)!;

        return live is not null;
    }

    private List<SellCandidate> ScanInternal(Configuration configuration)
    {
        var rawStacks = ReadRawStacks(configuration);
        var result = new List<SellCandidate>(rawStacks.Count);
        var remainingProtection = new Dictionary<ItemQualityKey, int>();

        foreach (var stack in rawStacks)
        {
            var key = new ItemQualityKey(stack.ItemId, stack.IsHq);
            if (!remainingProtection.TryGetValue(key, out var keepRemaining))
            {
                keepRemaining = GetProtectedQuantity(configuration, stack.ItemId, stack.IsHq);
                remainingProtection[key] = keepRemaining;
            }

            var protectedForStack = keepRemaining == int.MaxValue
                ? stack.Quantity
                : Math.Min(stack.Quantity, Math.Max(0, keepRemaining));

            if (keepRemaining != int.MaxValue)
                remainingProtection[key] = Math.Max(0, keepRemaining - protectedForStack);

            var sellQuantity = stack.Quantity - protectedForStack;
            if (sellQuantity <= 0)
                continue;

            result.Add(new SellCandidate(
                stack.InventoryType,
                stack.Slot,
                stack.ItemId,
                stack.ItemName,
                stack.Quantity,
                sellQuantity,
                protectedForStack,
                stack.IsHq,
                stack.NpcSellPrice,
                stack.CanBeHq,
                stack.IsMarketable));
        }

        return result;
    }

    private List<RawStack> ReadRawStacks(Configuration configuration)
    {
        var result = new List<RawStack>();
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
                if (inventoryItem == null || inventoryItem->ItemId == 0 || inventoryItem->Quantity == 0)
                    continue;

                if (!itemSheet.TryGetRow(inventoryItem->ItemId, out var itemRow))
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

                result.Add(new RawStack(
                    inventoryType,
                    (uint)slotIndex,
                    inventoryItem->ItemId,
                    itemRow.Name.ToString(),
                    checked((int)inventoryItem->Quantity),
                    inventoryItem->IsHighQuality(),
                    itemRow.PriceLow,
                    itemRow.CanBeHq,
                    marketable));
            }
        }

        return result;
    }

    private static int GetProtectedQuantity(Configuration configuration, uint itemId, bool isHq)
    {
        var keep = 0;

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

            if (rule.KeepQuantity == int.MaxValue)
                return int.MaxValue;

            keep = Math.Max(keep, Math.Max(0, rule.KeepQuantity));
        }

        return keep;
    }

    private readonly record struct ItemQualityKey(uint ItemId, bool IsHq);

    private sealed record RawStack(
        InventoryType InventoryType,
        uint Slot,
        uint ItemId,
        string ItemName,
        int Quantity,
        bool IsHq,
        uint NpcSellPrice,
        bool CanBeHq,
        bool IsMarketable);
}
