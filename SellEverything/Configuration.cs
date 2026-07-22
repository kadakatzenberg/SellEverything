using Dalamud.Plugin;
using Dalamud.Configuration;
using System.Text.Json.Serialization;

namespace SellEverything;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public uint MarketFloor { get; set; } = 100;
    public uint UndercutAmount { get; set; } = 1;
    public int ActionDelayMilliseconds { get; set; } = 900;
    public int MarketTimeoutMilliseconds { get; set; } = 15000;
    public bool DryRun { get; set; } = true;
    public bool RequireReviewBeforeRun { get; set; } = true;
    public bool SkipGear { get; set; } = true;
    public bool SkipMateriaAttached { get; set; } = true;
    public bool SkipCollectables { get; set; } = true;
    public int RetainersPerSession { get; set; } = 6;
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
