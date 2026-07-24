using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Lumina.Excel.Sheets;
using SellEverything.Models;
using SellEverything.Services;

namespace SellEverything.Windows;

public sealed class MainWindow : Window
{
    private readonly Plugin plugin;
    private readonly SellAutomation automation;

    private string itemSearch = string.Empty;
    private string queueSearch = string.Empty;
    private int queueFilter;
    private int protectedQualityIndex;
    private int keepQuantity = 1;
    private bool protectAll = true;

    public MainWindow(Plugin plugin, SellAutomation automation)
        : base("Sell Everything###SellEverythingMain")
    {
        this.plugin = plugin;
        this.automation = automation;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(880, 620),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void Draw()
    {
        UiTheme.PushWindowStyle();
        try
        {
            DrawHeader();
            ImGui.Separator();

            if (ImGui.BeginTabBar("SellEverythingTabs"))
            {
                if (ImGui.BeginTabItem("Overview"))
                {
                    DrawOverview();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Queue"))
                {
                    DrawQueue();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Protected Items"))
                {
                    DrawProtectedItems();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Settings"))
                {
                    DrawSettingsSummary();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
        }
        finally
        {
            UiTheme.PopWindowStyle();
        }
    }

    private void DrawHeader()
    {
        ImGui.TextColored(UiTheme.Accent, "SELL EVERYTHING");
        ImGui.SameLine();
        UiTheme.MutedText("Retainer market automation");

        UiTheme.StatusText(
            this.automation.State,
            $"● {UiTheme.FriendlyState(this.automation.State)}");
        ImGui.SameLine();
        UiTheme.MutedText(this.plugin.Configuration.DryRun ? "DRY RUN" : "LIVE MODE");
    }

    private void DrawOverview()
    {
        UiTheme.SectionTitle(
            "Run control",
            "One place to scan, start, pause, recover, and stop the current session.");

        DrawActionBar();
        ImGui.Spacing();
        DrawModeNotice();
        ImGui.Spacing();
        DrawRunProgress();
        ImGui.Spacing();
        DrawMetrics();
        ImGui.Spacing();
        DrawCurrentItem();
        ImGui.Spacing();
        DrawActivity();
    }

    private void DrawActionBar()
    {
        var buttonHeight = 34f;

        if (this.automation.State == AutomationState.Paused)
        {
            if (UiTheme.PrimaryButton("Resume", new Vector2(120, buttonHeight)))
                this.automation.Resume();
        }
        else if (!this.automation.IsRunning)
        {
            if (UiTheme.PrimaryButton("Start full run", new Vector2(150, buttonHeight)))
                this.automation.Start();
        }
        else
        {
            if (UiTheme.QuietButton("Pause", new Vector2(100, buttonHeight)))
                this.automation.Pause();
        }

        ImGui.SameLine();
        if (UiTheme.QuietButton("Scan inventory", new Vector2(135, buttonHeight)))
            this.automation.BuildQueue();

        if (this.automation.State == AutomationState.Faulted)
        {
            ImGui.SameLine();
            if (UiTheme.QuietButton("Retry failed", new Vector2(120, buttonHeight)))
                this.automation.RetryFailed();
        }

        if (this.automation.State is not AutomationState.Idle and not AutomationState.Completed)
        {
            ImGui.SameLine();
            if (UiTheme.DangerButton("Emergency stop", new Vector2(145, buttonHeight)))
                this.automation.Stop();
        }

        ImGui.SameLine();
        var dryRun = this.plugin.Configuration.DryRun;
        if (ImGui.Checkbox("Dry run", ref dryRun))
        {
            this.plugin.Configuration.DryRun = dryRun;
            this.plugin.Configuration.Save();
        }
    }

    private void DrawModeNotice()
    {
        if (this.plugin.Configuration.DryRun)
        {
            ImGui.TextColored(
                UiTheme.Warning,
                "Dry run is active. The queue and quality checks run, but no game actions are sent.");
        }
        else
        {
            ImGui.TextColored(
                UiTheme.Danger,
                "Live mode is active. Open the retainer list before starting.");
        }
    }

    private void DrawRunProgress()
    {
        UiTheme.SectionTitle("Current run");

        var progress = this.automation.ProgressFraction;
        var overlay = this.automation.Queue.Count == 0
            ? "No queue"
            : $"{this.automation.ProcessedCount}/{this.automation.Queue.Count} processed";
        ImGui.ProgressBar(progress, new Vector2(-1, 18), overlay);

        UiTheme.StatusText(this.automation.State, UiTheme.FriendlyState(this.automation.State));
        ImGui.SameLine();
        ImGui.TextWrapped(this.automation.Status);

        if (this.automation.SessionRetainerLimit > 0)
        {
            UiTheme.MutedText(
                $"Retainer {this.automation.CurrentRetainerNumber}/{this.automation.SessionRetainerLimit}  |  Step {FormatDuration(this.automation.CurrentStateElapsed)}");
        }

        if (!string.IsNullOrWhiteSpace(this.automation.LastFault))
            ImGui.TextColored(UiTheme.Danger, this.automation.LastFault);
    }

    private void DrawMetrics()
    {
        var pending = this.automation.Queue.Count(entry => entry.State == QueueEntryState.Pending);
        var completed = this.automation.Queue.Count(entry => entry.State == QueueEntryState.Completed);
        var skipped = this.automation.Queue.Count(entry => entry.State == QueueEntryState.Skipped);
        var failed = this.automation.Queue.Count(entry => entry.State == QueueEntryState.Failed);

        if (!ImGui.BeginTable("RunMetrics", 4, ImGuiTableFlags.SizingStretchSame))
            return;

        DrawMetricCell("Pending", pending, UiTheme.Muted);
        DrawMetricCell("Completed", completed, UiTheme.Success);
        DrawMetricCell("Skipped", skipped, UiTheme.Warning);
        DrawMetricCell("Failed", failed, UiTheme.Danger);

        ImGui.EndTable();
    }

    private static void DrawMetricCell(string label, int value, Vector4 color)
    {
        ImGui.TableNextColumn();
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(color.X, color.Y, color.Z, 0.28f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(color.X, color.Y, color.Z, 0.36f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(color.X, color.Y, color.Z, 0.36f));
        _ = ImGui.Button($"{value:N0}\n{label}##Metric{label}", new Vector2(-1, 58));
        ImGui.PopStyleColor(3);
    }

    private void DrawCurrentItem()
    {
        UiTheme.SectionTitle("Current item");

        var current = this.automation.Current;
        if (current is null)
        {
            UiTheme.MutedText("No active queue entry.");
            return;
        }

        if (!ImGui.BeginTable("CurrentItemSummary", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
            return;

        DrawKeyValue("Item", current.Candidate.ItemName);
        DrawKeyValue("Quality and quantity", $"{current.Candidate.QualityLabel}  |  {current.Candidate.Quantity:N0}");
        DrawKeyValue("Market", current.LowestMatchingPrice is uint lowest ? $"Lowest matching {lowest:N0} gil" : "Waiting for a matching price");
        DrawKeyValue("Decision", current.Action == SellAction.Unknown ? "Pending" : current.Action.ToString());
        DrawKeyValue("Note", string.IsNullOrWhiteSpace(current.Note) ? "No note" : current.Note);

        ImGui.EndTable();
    }

    private void DrawActivity()
    {
        UiTheme.SectionTitle("Recent activity", "Newest events appear first.");

        if (!ImGui.BeginTable(
                "AutomationActivity",
                3,
                ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp,
                new Vector2(0, 180)))
        {
            return;
        }

        ImGui.TableSetupColumn("Time");
        ImGui.TableSetupColumn("State");
        ImGui.TableSetupColumn("Event");
        ImGui.TableHeadersRow();

        foreach (var entry in this.automation.Activity.Take(12))
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            UiTheme.MutedText(entry.Timestamp.ToLocalTime().ToString("HH:mm:ss"));
            ImGui.TableNextColumn();
            ImGui.TextColored(UiTheme.ActivityColor(entry.Kind), UiTheme.FriendlyState(entry.State));
            ImGui.TableNextColumn();
            ImGui.TextWrapped(entry.Message);
        }

        ImGui.EndTable();
    }

    private void DrawQueue()
    {
        UiTheme.SectionTitle(
            "Sale queue",
            "Search and filter without changing the order used by the automation.");

        ImGui.SetNextItemWidth(340);
        ImGui.InputTextWithHint("##QueueSearch", "Search item or note", ref this.queueSearch, 128);

        ImGui.SameLine();
        DrawQueueFilterButton("All", 0);
        ImGui.SameLine();
        DrawQueueFilterButton("Pending", 1);
        ImGui.SameLine();
        DrawQueueFilterButton("Completed", 2);
        ImGui.SameLine();
        DrawQueueFilterButton("Skipped", 3);
        ImGui.SameLine();
        DrawQueueFilterButton("Failed", 4);

        ImGui.Spacing();

        var filtered = this.automation.Queue.Where(QueueEntryMatchesFilter).ToList();
        UiTheme.MutedText($"Showing {filtered.Count:N0} of {this.automation.Queue.Count:N0} entries");

        if (!ImGui.BeginTable(
                "SellQueue",
                8,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp,
                new Vector2(0, -1)))
        {
            return;
        }

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Item");
        ImGui.TableSetupColumn("Quality");
        ImGui.TableSetupColumn("Qty");
        ImGui.TableSetupColumn("Market");
        ImGui.TableSetupColumn("Decision");
        ImGui.TableSetupColumn("Price");
        ImGui.TableSetupColumn("State");
        ImGui.TableSetupColumn("Note");
        ImGui.TableHeadersRow();

        foreach (var entry in filtered)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(entry.Candidate.ItemName);
            ImGui.TableNextColumn();
            ImGui.TextColored(entry.Candidate.IsHq ? UiTheme.Warning : UiTheme.Muted, entry.Candidate.QualityLabel);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(entry.Candidate.Quantity.ToString("N0"));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"NQ {entry.NqListingsSeen} / HQ {entry.HqListingsSeen}");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(entry.Action == SellAction.Unknown ? "-" : entry.Action.ToString());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(entry.ListingPrice?.ToString("N0") ?? entry.LowestMatchingPrice?.ToString("N0") ?? "-");
            ImGui.TableNextColumn();
            ImGui.TextColored(UiTheme.QueueStateColor(entry.State), entry.State.ToString());
            ImGui.TableNextColumn();
            ImGui.TextWrapped(entry.Note);
        }

        ImGui.EndTable();
    }

    private void DrawProtectedItems()
    {
        UiTheme.SectionTitle(
            "Protected items",
            "Rules are matched by item ID and quality before an item enters the queue.");

        ImGui.SetNextItemWidth(320);
        ImGui.InputTextWithHint("##ItemSearch", "Exact or partial item name", ref this.itemSearch, 128);

        ImGui.SameLine();
        if (ImGui.Checkbox("Protect all", ref this.protectAll) && this.protectAll)
            this.keepQuantity = 1;

        if (!this.protectAll)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(95);
            ImGui.InputInt("Keep", ref this.keepQuantity);
            this.keepQuantity = Math.Max(0, this.keepQuantity);
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(115);
        DrawQualityCombo("##NewProtectedQuality", ref this.protectedQualityIndex);

        ImGui.SameLine();
        if (UiTheme.PrimaryButton("Add rule", new Vector2(95, 0)))
            AddFirstMatch();

        ImGui.Spacing();

        if (!ImGui.BeginTable(
                "ProtectedRules",
                5,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp,
                new Vector2(0, -1)))
        {
            return;
        }

        ImGui.TableSetupColumn("Item");
        ImGui.TableSetupColumn("Item ID");
        ImGui.TableSetupColumn("Quality");
        ImGui.TableSetupColumn("Keep");
        ImGui.TableSetupColumn("Action");
        ImGui.TableHeadersRow();

        for (var i = 0; i < this.plugin.Configuration.ProtectedItems.Count; i++)
        {
            var rule = this.plugin.Configuration.ProtectedItems[i];
            ImGui.PushID(i);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(rule.DisplayName);
            ImGui.TableNextColumn();
            UiTheme.MutedText(rule.ItemId.ToString());
            ImGui.TableNextColumn();

            var qualityIndex = (int)rule.Quality;
            ImGui.SetNextItemWidth(-1);
            if (DrawQualityCombo("##RuleQuality", ref qualityIndex))
            {
                rule.Quality = (QualityScope)Math.Clamp(qualityIndex, 0, 2);
                this.plugin.Configuration.Save();
            }

            ImGui.TableNextColumn();
            var keep = rule.KeepQuantity == int.MaxValue ? -1 : rule.KeepQuantity;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputInt("##RuleKeep", ref keep))
            {
                rule.KeepQuantity = keep < 0 ? int.MaxValue : keep;
                this.plugin.Configuration.Save();
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Use -1 to protect the entire stack.");

            ImGui.TableNextColumn();
            if (UiTheme.DangerButton("Remove", new Vector2(-1, 0)))
            {
                this.plugin.Configuration.ProtectedItems.RemoveAt(i);
                this.plugin.Configuration.Save();
                ImGui.PopID();
                break;
            }

            ImGui.PopID();
        }

        ImGui.EndTable();
    }

    private void DrawSettingsSummary()
    {
        UiTheme.SectionTitle(
            "Settings",
            "Core values are shown here. Open the full settings window to edit timing and safety controls.");

        var config = this.plugin.Configuration;
        if (ImGui.BeginTable("SettingsSummary", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            DrawKeyValue("Market floor", $"{config.MarketFloor:N0} gil");
            DrawKeyValue("Undercut", $"{config.UndercutAmount:N0} gil");
            DrawKeyValue("Retainers per run", config.RetainersPerSession.ToString());
            DrawKeyValue("Action delay", $"{config.ActionDelayMilliseconds:N0} ms");
            DrawKeyValue("Market timeout", $"{config.MarketTimeoutMilliseconds:N0} ms");
            DrawKeyValue("Own-retainer listings", config.UndercutOwnRetainers ? "Included" : "Ignored");
            ImGui.EndTable();
        }

        ImGui.Spacing();
        if (UiTheme.PrimaryButton("Open full settings", new Vector2(180, 34)))
            this.plugin.ToggleConfigUi();
    }

    private void DrawQueueFilterButton(string label, int value)
    {
        if (UiTheme.FilterButton(label, this.queueFilter == value))
            this.queueFilter = value;
    }

    private bool QueueEntryMatchesFilter(SellQueueEntry entry)
    {
        var statusMatches = this.queueFilter switch
        {
            1 => entry.State == QueueEntryState.Pending,
            2 => entry.State == QueueEntryState.Completed,
            3 => entry.State == QueueEntryState.Skipped,
            4 => entry.State == QueueEntryState.Failed,
            _ => true,
        };

        if (!statusMatches)
            return false;

        var query = this.queueSearch.Trim();
        return query.Length == 0 ||
               entry.Candidate.ItemName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               entry.Note.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static bool DrawQualityCombo(string id, ref int selected)
    {
        string[] labels = ["Both", "NQ only", "HQ only"];
        selected = Math.Clamp(selected, 0, labels.Length - 1);
        var changed = false;

        if (ImGui.BeginCombo(id, labels[selected]))
        {
            for (var i = 0; i < labels.Length; i++)
            {
                if (ImGui.Selectable(labels[i], selected == i))
                {
                    selected = i;
                    changed = true;
                }
            }

            ImGui.EndCombo();
        }

        return changed;
    }

    private static void DrawKeyValue(string key, string value)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        UiTheme.MutedText(key);
        ImGui.TableNextColumn();
        ImGui.TextWrapped(value);
    }

    private void AddFirstMatch()
    {
        var query = this.itemSearch.Trim();
        if (query.Length == 0)
            return;

        var item = Plugin.DataManager.GetExcelSheet<Item>()
            .Where(row => row.RowId != 0 && row.Name.ToString().Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(row => row.Name.ToString().Length)
            .FirstOrDefault();

        if (item.RowId == 0)
        {
            Plugin.ChatGui.Print($"[Sell Everything] No item found matching '{query}'.");
            return;
        }

        if (this.plugin.Configuration.ProtectedItems.Any(rule => rule.ItemId == item.RowId))
        {
            Plugin.ChatGui.Print($"[Sell Everything] {item.Name} already has a protection rule.");
            return;
        }

        this.plugin.Configuration.ProtectedItems.Add(new ProtectedItemRule
        {
            ItemId = item.RowId,
            DisplayName = item.Name.ToString(),
            KeepQuantity = this.protectAll ? int.MaxValue : Math.Max(0, this.keepQuantity),
            Quality = (QualityScope)Math.Clamp(this.protectedQualityIndex, 0, 2),
        });
        this.plugin.Configuration.Save();
        this.itemSearch = string.Empty;
    }

    private static string FormatDuration(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
            value = TimeSpan.Zero;

        return value.TotalSeconds < 60
            ? $"{value.TotalSeconds:0}s"
            : $"{(int)value.TotalMinutes}m {value.Seconds}s";
    }
}
