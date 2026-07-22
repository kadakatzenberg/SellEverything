using Dalamud.Configuration;
using Dalamud.Plugin;
using System.Text.Json.Serialization;

namespace SellEverything;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;

    public uint MarketFloor { get; set; } = 100;
    public uint UndercutAmount { get; set; } = 1;
    public int ActionDelayMilliseconds { get; set; } = 900;
    public int MarketTimeoutMilliseconds { get; set; } = 15000;
    public int UiTimeoutMilliseconds { get; set; } = 12000;
    public bool DryRun { get; set; } = true;
    public bool RequireReviewBeforeRun { get; set; } = false;
    public bool SkipGear { get; set; } = true;
    public bool SkipMateriaAttached { get; set; } = true;
    public bool SkipCollectables { get; set; } = true;
    public bool AutomateRetainers { get; set; } = true;
    public bool AutoConfirmExpectedDialogs { get; set; } = true;
    public int RetainersPerSession { get; set; } = 6;

    // Retainer menu order in the current client:
    // 0 entrust/withdraw items, 1 gil, 2 sell inventory items on the market.
    public int MarketMenuOptionIndex { get; set; } = 2;

    public List<ProtectedItemRule> ProtectedItems { get; set; } = [];

    [JsonIgnore]
    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pi) => this.pluginInterface = pi;

    public void Save() => this.pluginInterface?.SavePluginConfig(this);
}

[Serializable]
public sealed class ProtectedItemRule
{
    public uint ItemId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public int KeepQuantity { get; set; } = int.MaxValue;
    public QualityScope Quality { get; set; } = QualityScope.Both;
}

public enum QualityScope
{
    Both,
    NqOnly,
    HqOnly,
}
