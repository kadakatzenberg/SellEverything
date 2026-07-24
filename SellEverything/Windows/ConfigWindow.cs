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
        this.Size = new Vector2(620, 560);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560, 470),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void Draw()
    {
        UiTheme.PushWindowStyle();
        try
        {
            ImGui.TextColored(UiTheme.Accent, "SELL EVERYTHING SETTINGS");
            UiTheme.MutedText("Pricing, automation timing, and safety behavior.");
            ImGui.Separator();

            if (this.plugin.AreSettingsLocked)
                ImGui.TextColored(UiTheme.Warning, "Settings are locked while a run is active or paused.");

            ImGui.BeginDisabled(this.plugin.AreSettingsLocked);
            if (ImGui.BeginTabBar("SellEverythingSettingsTabs"))
            {
                if (ImGui.BeginTabItem("Pricing"))
                {
                    DrawPricing();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Automation"))
                {
                    DrawAutomation();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Safety"))
                {
                    DrawSafety();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
            ImGui.EndDisabled();
        }
        finally
        {
            UiTheme.PopWindowStyle();
        }
    }

    private void DrawPricing()
    {
        var config = this.plugin.Configuration;
        UiTheme.SectionTitle(
            "Pricing rules",
            "Every comparison remains locked to the current stack's exact HQ or NQ quality.");

        if (!ImGui.BeginTable("PricingSettings", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            return;

        var floor = (int)config.MarketFloor;
        DrawIntRow(
            "Retainer-sell below",
            "Matching prices below this value use Have Retainer Sell Items.",
            "##MarketFloor",
            ref floor,
            1,
            int.MaxValue,
            value => config.MarketFloor = (uint)value);

        var undercut = (int)config.UndercutAmount;
        DrawIntRow(
            "Undercut amount",
            "Subtract this many gil from the lowest matching-quality listing.",
            "##UndercutAmount",
            ref undercut,
            0,
            int.MaxValue,
            value => config.UndercutAmount = (uint)value);

        ImGui.EndTable();
    }

    private void DrawAutomation()
    {
        var config = this.plugin.Configuration;
        UiTheme.SectionTitle(
            "Run behavior",
            "Delays pace ordinary state changes. Compare Prices and expected confirmations use lifecycle-driven retries.");

        if (ImGui.BeginTable("AutomationSettings", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            var delay = config.ActionDelayMilliseconds;
            DrawIntRow(
                "Action delay",
                "Delay between ordinary automation steps in milliseconds.",
                "##ActionDelay",
                ref delay,
                300,
                5000,
                value => config.ActionDelayMilliseconds = value);

            var marketTimeout = config.MarketTimeoutMilliseconds;
            DrawIntRow(
                "Market timeout",
                "Maximum wait for a valid settled market response.",
                "##MarketTimeout",
                ref marketTimeout,
                5000,
                60000,
                value => config.MarketTimeoutMilliseconds = value);

            var uiTimeout = config.UiTimeoutMilliseconds;
            DrawIntRow(
                "UI timeout",
                "Maximum wait for a required game window or confirmation.",
                "##UiTimeout",
                ref uiTimeout,
                5000,
                60000,
                value => config.UiTimeoutMilliseconds = value);

            var retainers = config.RetainersPerSession;
            DrawIntRow(
                "Retainers per run",
                "Maximum retainers processed from the open retainer list.",
                "##RetainerCount",
                ref retainers,
                1,
                10,
                value => config.RetainersPerSession = value);

            var marketMenuIndex = config.MarketMenuOptionIndex;
            DrawIntRow(
                "Market-menu index",
                "Current default is 2. Change only when the client menu order differs.",
                "##MarketMenuIndex",
                ref marketMenuIndex,
                0,
                20,
                value => config.MarketMenuOptionIndex = value);

            ImGui.EndTable();
        }

        ImGui.Spacing();
        DrawCheckbox(
            "Automate retainer selection",
            "Select each retainer and open its market inventory automatically.",
            config.AutomateRetainers,
            value => config.AutomateRetainers = value);

        DrawCheckbox(
            "Auto-confirm expected sale dialogs",
            "Only accepts a non-empty dialog while the matching transaction state is active.",
            config.AutoConfirmExpectedDialogs,
            value => config.AutoConfirmExpectedDialogs = value);

        DrawCheckbox(
            "Require queue approval",
            "After each scan, require an explicit approval before live actions can begin.",
            config.RequireReviewBeforeRun,
            value => config.RequireReviewBeforeRun = value);

    }

    private void DrawSafety()
    {
        var config = this.plugin.Configuration;
        UiTheme.SectionTitle(
            "Inventory safeguards",
            "These exclusions are applied before a stack enters the queue and again before irreversible actions.");

        DrawCheckbox(
            "Skip equippable gear",
            "Excludes items with an equipment slot category.",
            config.SkipGear,
            value => config.SkipGear = value);

        DrawCheckbox(
            "Skip items with materia",
            "Excludes any stack with attached materia.",
            config.SkipMateriaAttached,
            value => config.SkipMateriaAttached = value);

        DrawCheckbox(
            "Skip collectables",
            "Excludes collectable inventory items.",
            config.SkipCollectables,
            value => config.SkipCollectables = value);

        DrawCheckbox(
            "Allow undercutting your own retainers",
            "Off by default to prevent your retainers from repeatedly undercutting one another.",
            config.UndercutOwnRetainers,
            value => config.UndercutOwnRetainers = value);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextWrapped(
            "Strict quality matching cannot be disabled. Item ID, request ID, active-retainer context, sell-window quality, and live inventory slot are validated before pricing or selling.");
    }

    private void DrawIntRow(
        string label,
        string description,
        string id,
        ref int value,
        int minimum,
        int maximum,
        Action<int> apply)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(label);
        UiTheme.MutedText(description);
        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputInt(id, ref value))
        {
            value = Math.Clamp(value, minimum, maximum);
            apply(value);
            this.plugin.Configuration.Save();
        }
    }

    private void DrawCheckbox(
        string label,
        string description,
        bool current,
        Action<bool> apply)
    {
        var value = current;
        if (ImGui.Checkbox(label, ref value))
        {
            apply(value);
            this.plugin.Configuration.Save();
        }
        UiTheme.MutedText(description);
        ImGui.Spacing();
    }
}
