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
        this.Size = new Vector2(500, 520);
        this.SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var config = this.plugin.Configuration;

        var floor = (int)config.MarketFloor;
        if (ImGui.InputInt("Retainer-sell below", ref floor))
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

        var marketTimeout = config.MarketTimeoutMilliseconds;
        if (ImGui.InputInt("Market timeout (ms)", ref marketTimeout))
        {
            config.MarketTimeoutMilliseconds = Math.Clamp(marketTimeout, 5000, 60000);
            config.Save();
        }

        var uiTimeout = config.UiTimeoutMilliseconds;
        if (ImGui.InputInt("UI timeout (ms)", ref uiTimeout))
        {
            config.UiTimeoutMilliseconds = Math.Clamp(uiTimeout, 5000, 60000);
            config.Save();
        }

        var retainers = config.RetainersPerSession;
        if (ImGui.InputInt("Retainers per run", ref retainers))
        {
            config.RetainersPerSession = Math.Clamp(retainers, 1, 10);
            config.Save();
        }

        var marketMenuIndex = config.MarketMenuOptionIndex;
        if (ImGui.InputInt("Retainer market-menu index", ref marketMenuIndex))
        {
            config.MarketMenuOptionIndex = Math.Max(0, marketMenuIndex);
            config.Save();
        }

        var automateRetainers = config.AutomateRetainers;
        if (ImGui.Checkbox("Automate retainer selection", ref automateRetainers))
        {
            config.AutomateRetainers = automateRetainers;
            config.Save();
        }

        var autoConfirm = config.AutoConfirmExpectedDialogs;
        if (ImGui.Checkbox("Auto-confirm expected sale dialogs", ref autoConfirm))
        {
            config.AutoConfirmExpectedDialogs = autoConfirm;
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
        ImGui.TextWrapped("Strict quality matching is always enabled. NQ inventory stacks ignore every HQ listing. HQ inventory stacks ignore every NQ listing. Market packets for other items or request IDs are rejected.");
        ImGui.TextWrapped("Default retainer market-menu index is 2. Change it only if the game menu order differs in your client.");
    }
}
