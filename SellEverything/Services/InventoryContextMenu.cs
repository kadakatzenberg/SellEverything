using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace SellEverything.Services;

/// <summary>
/// Adds a right-click "Protect item" / "Unprotect item" entry to inventory
/// context menus so marketable items can be added to or removed from the
/// Sell Everything protected list without opening the main window.
/// </summary>
public sealed class InventoryContextMenu : IDisposable
{
    private const ushort SellEverythingPrefixColor = 541;

    private readonly Plugin plugin;
    private readonly IContextMenu contextMenu;

    public InventoryContextMenu(Plugin plugin, IContextMenu contextMenu)
    {
        this.plugin = plugin;
        this.contextMenu = contextMenu;
        this.contextMenu.OnMenuOpened += this.OnMenuOpened;
    }

    public void Dispose() => this.contextMenu.OnMenuOpened -= this.OnMenuOpened;

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (args.MenuType != ContextMenuType.Inventory)
            return;

        if (args.Target is not MenuTargetInventory inventory || inventory.TargetItem is not { } target)
            return;

        // GameInventoryItem.ItemId carries the quality offset the game applies to
        // inventory slots (HQ +1,000,000, collectible +500,000), while the scanner
        // and the protected list are keyed on the plain Item row id. Strip it, or
        // HQ stacks resolve to a nonexistent row and never get a menu entry.
        var itemId = NormalizeItemId(target.ItemId);
        if (itemId == 0 || !TryResolveMarketableItem(itemId, out var itemName))
            return;

        var isProtected = this.plugin.Configuration.ProtectedItems.Any(rule => rule.ItemId == itemId);

        args.AddMenuItem(new MenuItem
        {
            Name = isProtected ? "Sell Everything: Unprotect item" : "Sell Everything: Protect item",
            PrefixChar = 'S',
            PrefixColor = SellEverythingPrefixColor,
            IsEnabled = !this.plugin.AreSettingsLocked,
            OnClicked = _ => this.ToggleProtection(itemId, itemName),
        });
    }

    private void ToggleProtection(uint itemId, string itemName)
    {
        if (this.plugin.AreSettingsLocked)
        {
            Plugin.ChatGui.Print("[Sell Everything] Protected items are locked while a run is active or paused.");
            return;
        }

        var config = this.plugin.Configuration;
        var existing = config.ProtectedItems.FirstOrDefault(rule => rule.ItemId == itemId);
        if (existing is not null)
        {
            config.ProtectedItems.Remove(existing);
            config.Save();
            Plugin.ChatGui.Print($"[Sell Everything] {itemName} is no longer protected.");
            return;
        }

        config.ProtectedItems.Add(new ProtectedItemRule
        {
            ItemId = itemId,
            DisplayName = itemName,
            KeepQuantity = int.MaxValue,
            Quality = QualityScope.Both,
        });
        config.Save();
        Plugin.ChatGui.Print($"[Sell Everything] {itemName} is now protected and will never be sold.");
    }

    /// <summary>
    /// Converts an inventory slot's item id to the plain Item row id, dropping the
    /// high-quality and collectible offsets. Event items have no market listing and
    /// are reported as 0 so the caller skips them.
    /// </summary>
    private static uint NormalizeItemId(uint rawItemId) => rawItemId switch
    {
        >= 2_000_000 => 0,
        >= 1_000_000 => rawItemId - 1_000_000,
        >= 500_000 => rawItemId - 500_000,
        _ => rawItemId,
    };

    private static bool TryResolveMarketableItem(uint itemId, out string itemName)
    {
        itemName = string.Empty;

        if (Plugin.DataManager.GetExcelSheet<Item>().GetRowOrDefault(itemId) is not { } row)
            return false;

        // Only marketable items (those with a market-board search category) are
        // ever eligible for the queue, so limit the entry to those.
        if (row.ItemSearchCategory.RowId == 0)
            return false;

        itemName = row.Name.ToString();
        return !string.IsNullOrEmpty(itemName);
    }
}
