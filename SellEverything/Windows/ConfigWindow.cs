using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace SellEverything.Windows;

public sealed class ConfigWindow : Window
{
    private readonly Plugin plugin;

    public ConfigWindow(Plugin plugin) : base("Sell Everything Settings###SellEverythingConfig")
    {
        this.plugin = plugin;
        this.Size = new Vector2(460, 360);
        this.SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var config = this.plugin.Configuration;

        var floor = (int)config.MarketFloor;
        if (ImGui.InputInt("Retainer-vendor below", ref floor))
        {
            config.MarketFloor = (uint)Math.Max(1, floor);
            config.Save();
        }

        var undercut = (int)config.UndercutAmount;
        if (ImGui.InputInt("Undercut amount", ref undercut))
        {
            config.UndercutAmount = (uint)Math.Max(0, undercut);
            config.Save();
        }

        var delay = config.ActionDelayMilliseconds;
        if (ImGui.InputInt("Action delay (ms)", ref delay))
        {
            config.ActionDelayMilliseconds = Math.Clamp(delay, 300, 5000);
            config.Save();
        }

        var timeout = config.MarketTimeoutMilliseconds;
        if (ImGui.InputInt("Market timeout (ms)", ref timeout))
        {
            config.MarketTimeoutMilliseconds = Math.Clamp(timeout, 5000, 60000);
            config.Save();
        }

        var skipGear = config.SkipGear;
        if (ImGui.Checkbox("Skip equippable gear", ref skipGear))
        {
            config.SkipGear = skipGear;
            config.Save();
        }

        var skipMateria = config.SkipMateriaAttached;
        if (ImGui.Checkbox("Skip items with materia", ref skipMateria))
        {
            config.SkipMateriaAttached = skipMateria;
            config.Save();
        }

        var skipCollectables = config.SkipCollectables;
        if (ImGui.Checkbox("Skip collectables", ref skipCollectables))
        {
            config.SkipCollectables = skipCollectables;
            config.Save();
        }

        ImGui.Separator();
        ImGui.TextWrapped("HQ stacks are compared only to HQ listings. NQ stacks are compared only to NQ listings. No matching listings are skipped. The initial build processes the currently opened retainer and pauses when the game UI no longer permits another listing.");
    }
}
