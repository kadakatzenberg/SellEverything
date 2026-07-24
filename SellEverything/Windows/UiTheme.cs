using System.Numerics;
using Dalamud.Bindings.ImGui;
using SellEverything.Models;
using SellEverything.Services;

namespace SellEverything.Windows;

internal static class UiTheme
{
    public static readonly Vector4 Accent = new(0.28f, 0.68f, 0.92f, 1.00f);
    public static readonly Vector4 AccentStrong = new(0.18f, 0.55f, 0.82f, 1.00f);
    public static readonly Vector4 Success = new(0.31f, 0.78f, 0.49f, 1.00f);
    public static readonly Vector4 Warning = new(0.95f, 0.69f, 0.27f, 1.00f);
    public static readonly Vector4 Danger = new(0.90f, 0.31f, 0.34f, 1.00f);
    public static readonly Vector4 Muted = new(0.62f, 0.66f, 0.72f, 1.00f);
    public static readonly Vector4 Surface = new(0.18f, 0.20f, 0.24f, 1.00f);
    public static readonly Vector4 SurfaceHover = new(0.23f, 0.26f, 0.31f, 1.00f);

    public static void PushWindowStyle()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5f);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 5f);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(9f, 7f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8f, 5f));
    }

    public static void PopWindowStyle() => ImGui.PopStyleVar(4);

    public static bool PrimaryButton(string label, Vector2 size)
        => ColoredButton(label, size, AccentStrong, Accent, AccentStrong);

    public static bool DangerButton(string label, Vector2 size)
        => ColoredButton(label, size, new Vector4(0.64f, 0.18f, 0.21f, 1f), Danger, new Vector4(0.75f, 0.22f, 0.25f, 1f));

    public static bool QuietButton(string label, Vector2 size)
        => ColoredButton(label, size, Surface, SurfaceHover, SurfaceHover);

    public static bool FilterButton(string label, bool selected)
    {
        var baseColor = selected ? AccentStrong : Surface;
        var hoverColor = selected ? Accent : SurfaceHover;
        return ColoredButton(label, new Vector2(0, 0), baseColor, hoverColor, hoverColor);
    }

    public static void SectionTitle(string title, string? subtitle = null)
    {
        ImGui.TextColored(Accent, title);
        if (!string.IsNullOrWhiteSpace(subtitle))
            ImGui.TextColored(Muted, subtitle);
        ImGui.Spacing();
    }

    public static void MutedText(string text) => ImGui.TextColored(Muted, text);

    public static void StatusText(AutomationState state, string text)
        => ImGui.TextColored(StateColor(state), text);

    public static Vector4 StateColor(AutomationState state)
        => state switch
        {
            AutomationState.Completed => Success,
            AutomationState.Faulted => Danger,
            AutomationState.Paused => Warning,
            AutomationState.Idle or AutomationState.ReadyForReview => Muted,
            _ => Accent,
        };

    public static Vector4 QueueStateColor(QueueEntryState state)
        => state switch
        {
            QueueEntryState.Completed => Success,
            QueueEntryState.Failed => Danger,
            QueueEntryState.Skipped => Warning,
            QueueEntryState.Pending => Muted,
            _ => Accent,
        };

    public static Vector4 ActivityColor(AutomationActivityKind kind)
        => kind switch
        {
            AutomationActivityKind.Success => Success,
            AutomationActivityKind.Warning => Warning,
            AutomationActivityKind.Error => Danger,
            _ => Muted,
        };

    public static string FriendlyState(AutomationState state)
        => state switch
        {
            AutomationState.ReadyForReview => "Ready",
            AutomationState.SelectingRetainer => "Selecting retainer",
            AutomationState.WaitingForRetainerMenu => "Waiting for retainer",
            AutomationState.SelectingMarketMenu => "Opening market inventory",
            AutomationState.WaitingForRetainerReady => "Waiting for inventory",
            AutomationState.OpeningSellWindow => "Opening item",
            AutomationState.WaitingForSellWindow => "Waiting for sale window",
            AutomationState.RequestingMarketPrice => "Requesting prices",
            AutomationState.WaitingForMarketPrice => "Reading market",
            AutomationState.WaitingForMarketResultsClose => "Closing results",
            AutomationState.ExecutingDecision => "Applying decision",
            AutomationState.WaitingForListingConfirmation => "Confirming listing",
            AutomationState.WaitingToVendor => "Preparing retainer sale",
            AutomationState.WaitingForVendorConfirmation => "Confirming retainer sale",
            AutomationState.WaitingForTransaction => "Verifying transaction",
            AutomationState.WaitingForUiClose => "Closing windows",
            AutomationState.RefreshingInventory => "Refreshing inventory",
            AutomationState.ClosingRetainer => "Closing retainer",
            AutomationState.WaitingForRetainerList => "Returning to retainer list",
            _ => state.ToString(),
        };

    private static bool ColoredButton(
        string label,
        Vector2 size,
        Vector4 normal,
        Vector4 hovered,
        Vector4 active)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, normal);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, active);
        var clicked = ImGui.Button(label, size);
        ImGui.PopStyleColor(3);
        return clicked;
    }
}
