using FFXIVClientStructs.FFXIV.Client.Game;

namespace SellEverything.Models;

public sealed record SellCandidate(
    InventoryType InventoryType,
    uint Slot,
    uint ItemId,
    string ItemName,
    int Quantity,
    bool IsHq,
    uint NpcSellPrice,
    bool CanBeHq,
    bool IsMarketable)
{
    public string QualityLabel => this.IsHq ? "HQ" : "NQ";
}
