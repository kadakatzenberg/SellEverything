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
    private int queueSortColumn;
    private bool queueSortAscending = true;
    private int activeSection;
    private int protectedQualityIndex;
    private int keepQuantity = 1;
    private bool protectAll = true;

    public MainWindow(Plugin plugin, SellAutomation automation)
        : base("Sell Everything###SellEverythingMain")
    {
        this.plugin = plugin;
        this.automation = automation;
        this.Size = new Vector2(940, 680);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(760, 540),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void Draw()
    {
        UiTheme.PushWindowStyle();
        try
        {
            DrawHeroHeader();
            ImGui.Spacing();

            var available = ImGui.GetContentRegionAvail();

            ImGui.BeginChild("SellEverythingNav", new Vector2(168f, available.Y), false);
            DrawNav();
            ImGui.EndChild();

            ImGui.SameLine();

            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(1f, 1f, 1f, 0.015f));
            ImGui.BeginChild("SellEverythingContent", new Vector2(0, available.Y), true);
            switch (this.activeSection)
            {
                case 1:
                    DrawQueue();
                    break;
                case 2:
                    DrawProtectedItems();
                    break;
                case 3:
                    DrawSettingsSummary();
                    break;
                default:
                    DrawOverview();
                    break;
            }

            ImGui.EndChild();
            ImGui.PopStyleColor();
        }
        finally
        {
            UiTheme.PopWindowStyle();
        }
    }

    private void DrawHeroHeader()
    {
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        const float height = 46f;
        var corner = new Vector2(origin.X + width, origin.Y + height);

        drawList.AddRectFilled(
            origin,
            corner,
            ImGui.GetColorU32(new Vector4(UiTheme.Accent.X, UiTheme.Accent.Y, UiTheme.Accent.Z, 0.10f)),
            6f);
        drawList.AddRectFilled(origin, new Vector2(origin.X + 4f, corner.Y), ImGui.GetColorU32(UiTheme.Accent), 6f);

        ImGui.SetCursorScreenPos(new Vector2(origin.X + 16f, origin.Y + 6f));
        ImGui.TextColored(UiTheme.Accent, "SELL  EVERYTHING");
        ImGui.SetCursorScreenPos(new Vector2(origin.X + 16f, origin.Y + 24f));
        UiTheme.MutedText("Retainer market automation");

        ImGui.SetCursorScreenPos(new Vector2(origin.X, corner.Y + 8f));

        UiTheme.Pill($"● {UiTheme.FriendlyState(this.automation.State)}", UiTheme.StateColor(this.automation.State));
        ImGui.SameLine();
        var dryRun = this.plugin.Configuration.DryRun;
        UiTheme.Pill(dryRun ? "DRY RUN" : "LIVE MODE", dryRun ? UiTheme.Warning : UiTheme.Danger);

        if (this.automation.LocksConfiguration)
        {
            ImGui.SameLine();
            UiTheme.Pill("LOCKED", UiTheme.Muted);
        }

        if (this.automation.State == AutomationState.Faulted)
        {
            ImGui.SameLine();
            UiTheme.Pill("FAULT", UiTheme.Danger);
        }

        ImGui.NewLine();
    }

    private void DrawNav()
    {
        DrawNavItem(0, "Overview");
        DrawNavItem(1, this.automation.Queue.Count > 0 ? $"Queue  ({this.automation.Queue.Count})" : "Queue");
        DrawNavItem(2, "Protected");
        DrawNavItem(3, "Settings");
    }

    private void DrawNavItem(int index, string label)
    {
        if (ImGui.Selectable($"   {label}", this.activeSection == index, ImGuiSelectableFlags.None, new Vector2(0, 32f)))
            this.activeSection = index;
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

        if (this.automation.NeedsReview)
        {
            if (UiTheme.PrimaryButton("Approve queue", new Vector2(140, buttonHeight)))
                this.automation.ApproveQueue();
        }
        else if (this.automation.State == AutomationState.Paused)
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
        ImGui.BeginDisabled(this.automation.LocksConfiguration);
        if (UiTheme.QuietButton("Scan inventory", new Vector2(135, buttonHeight)))
            this.automation.BuildQueue();
        ImGui.EndDisabled();

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
        ImGui.BeginDisabled(this.automation.LocksConfiguration);
        if (ImGui.Checkbox("Dry run", ref dryRun))
        {
            this.plugin.Configuration.DryRun = dryRun;
            this.plugin.Configuration.Save();
        }
        ImGui.EndDisabled();
    }

    private void DrawModeNotice()
    {
        if (this.automation.NeedsReview)
        {
            ImGui.TextColored(
                UiTheme.Warning,
                "Queue review is required. Inspect the Queue tab, then approve it before starting.");
            return;
        }

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
        var pending = this.automation.Queue.Count(entry => entry.State is QueueEntryState.Pending
            or QueueEntryState.OpeningSellWindow
            or QueueEntryState.RequestingPrice
            or QueueEntryState.PriceReceived
            or QueueEntryState.Executing);
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
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(color.X, color.Y, color.Z, 0.14f));
        if (ImGui.BeginChild($"Metric{label}", new Vector2(0, 62), true, ImGuiWindowFlags.NoScrollbar))
        {
            var drawList = ImGui.GetWindowDrawList();
            var origin = ImGui.GetWindowPos();
            var width = ImGui.GetWindowSize().X;
            drawList.AddRectFilled(origin, new Vector2(origin.X + width, origin.Y + 3f), ImGui.GetColorU32(color));

            ImGui.Spacing();
            ImGui.TextColored(color, value.ToString("N0"));
            UiTheme.MutedText(label);
        }

        ImGui.EndChild();
        ImGui.PopStyleColor();
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
        var quantityText = current.Candidate.IsPartialStack
            ? $"{current.Candidate.QualityLabel}  |  sell {current.Candidate.SellQuantity:N0} of {current.Candidate.Quantity:N0}  |  keep {current.Candidate.ProtectedQuantity:N0}"
            : $"{current.Candidate.QualityLabel}  |  {current.Candidate.Quantity:N0}";
        DrawKeyValue("Quality and quantity", quantityText);
        DrawKeyValue("Market", current.LowestMatchingPrice is uint lowest ? $"Lowest matching {lowest:N0} gil" : "Waiting for a matching price");
        DrawKeyValue("Decision", current.Action == SellAction.Unknown ? "Pending" : current.Action.ToString());
        DrawKeyValue("Note", string.IsNullOrWhiteSpace(current.Note) ? "No note" : current.Note);

        ImGui.EndTable();
    }

    private void DrawActivity()
    {
        UiTheme.SectionTitle("Recent activity", "Newest events appear first.");

        if (this.automation.Activity.Count == 0)
        {
            UiTheme.EmptyState("No activity yet.", "Events appear here once a scan or run begins.");
            return;
        }

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
            "Click any column header to sort. Search, filter, and sort change only the view, never the automation order.");

        ImGui.SetNextItemWidth(320);
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

        if (this.automation.Queue.Count == 0)
        {
            UiTheme.EmptyState(
                "The queue is empty.",
                "Open your retainer list, then use Scan inventory on the Overview tab to build it.");
            return;
        }

        var filtered = this.automation.Queue.Where(QueueEntryMatchesFilter).ToList();
        UiTheme.MutedText($"Showing {filtered.Count:N0} of {this.automation.Queue.Count:N0} entries");

        if (!ImGui.BeginTable(
                "SellQueue",
                8,
                ImGuiTableFlags.Sortable | ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp,
                new Vector2(0, -1)))
        {
            return;
        }

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.DefaultSort, 2.6f);
        ImGui.TableSetupColumn("Quality", ImGuiTableColumnFlags.WidthFixed, 64f);
        ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 74f);
        ImGui.TableSetupColumn("Market", ImGuiTableColumnFlags.WidthFixed, 100f);
        ImGui.TableSetupColumn("Decision", ImGuiTableColumnFlags.WidthFixed, 96f);
        ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed, 92f);
        ImGui.TableSetupColumn("State", ImGuiTableColumnFlags.WidthFixed, 104f);
        ImGui.TableSetupColumn("Note", ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoSort, 2.4f);
        ImGui.TableHeadersRow();

        var sortSpecs = ImGui.TableGetSortSpecs();
        if (sortSpecs.SpecsCount > 0)
        {
            var spec = sortSpecs.Specs;
            this.queueSortColumn = spec.ColumnIndex;
            this.queueSortAscending = spec.SortDirection != ImGuiSortDirection.Descending;
        }

        foreach (var entry in SortQueueEntries(filtered))
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(entry.Candidate.ItemName);

            ImGui.TableNextColumn();
            ImGui.TextColored(entry.Candidate.IsHq ? UiTheme.Warning : UiTheme.Muted, entry.Candidate.QualityLabel);

            ImGui.TableNextColumn();
            if (entry.Candidate.IsPartialStack)
            {
                ImGui.TextColored(UiTheme.Accent, $"{entry.Candidate.SellQuantity:N0}/{entry.Candidate.Quantity:N0}");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip($"Selling {entry.Candidate.SellQuantity:N0}, keeping {entry.Candidate.ProtectedQuantity:N0}.");
            }
            else
            {
                ImGui.TextUnformatted(entry.Candidate.Quantity.ToString("N0"));
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"NQ {entry.NqListingsSeen} / HQ {entry.HqListingsSeen}");

            ImGui.TableNextColumn();
            ImGui.TextColored(UiTheme.DecisionColor(entry.Action), UiTheme.DecisionLabel(entry.Action));

            ImGui.TableNextColumn();
            var price = entry.ListingPrice ?? entry.LowestMatchingPrice;
            ImGui.TextUnformatted(price?.ToString("N0") ?? "-");

            ImGui.TableNextColumn();
            ImGui.TextColored(UiTheme.QueueStateColor(entry.State), UiTheme.FriendlyQueueState(entry.State));

            ImGui.TableNextColumn();
            if (string.IsNullOrWhiteSpace(entry.Note))
                UiTheme.MutedText("-");
            else
                ImGui.TextWrapped(entry.Note);
        }

        ImGui.EndTable();
    }

    private List<SellQueueEntry> SortQueueEntries(List<SellQueueEntry> entries)
    {
        IOrderedEnumerable<SellQueueEntry> ordered = this.queueSortColumn switch
        {
            1 => entries.OrderBy(e => e.Candidate.IsHq ? 0 : 1),
            2 => entries.OrderBy(e => e.Candidate.SellQuantity),
            3 => entries.OrderBy(e => e.NqListingsSeen + e.HqListingsSeen),
            4 => entries.OrderBy(e => (int)e.Action),
            5 => entries.OrderBy(e => e.ListingPrice ?? e.LowestMatchingPrice ?? uint.MaxValue),
            6 => entries.OrderBy(e => (int)e.State),
            _ => entries.OrderBy(e => e.Candidate.ItemName, StringComparer.OrdinalIgnoreCase),
        };

        ordered = ordered.ThenBy(e => e.Candidate.ItemName, StringComparer.OrdinalIgnoreCase);
        return this.queueSortAscending ? ordered.ToList() : ((IEnumerable<SellQueueEntry>)ordered).Reverse().ToList();
    }

    private void DrawProtectedItems()
    {
        UiTheme.SectionTitle(
            "Protected items",
            "Rules are matched by item ID and quality before an item enters the queue.");

        if (this.automation.LocksConfiguration)
            ImGui.TextColored(UiTheme.Warning, "Protected-item rules are locked while a run is active or paused.");
        UiTheme.MutedText("Partial keep quantities are applied to market listings. Low-price retainer vending skips partial stacks to preserve the protected remainder.");

        ImGui.BeginDisabled(this.automation.LocksConfiguration);
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

        if (ImGui.BeginTable(
                "ProtectedRules",
                5,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp,
                new Vector2(0, -1)))
        {
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

        ImGui.EndDisabled();
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
            DrawKeyValue("Queue review", config.RequireReviewBeforeRun ? "Required" : "Not required");
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
            1 => entry.State is QueueEntryState.Pending
                or QueueEntryState.OpeningSellWindow
                or QueueEntryState.RequestingPrice
                or QueueEntryState.PriceReceived
                or QueueEntryState.Executing,
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

        var newQuality = (QualityScope)Math.Clamp(this.protectedQualityIndex, 0, 2);
        var overlaps = this.plugin.Configuration.ProtectedItems.Any(rule =>
            rule.ItemId == item.RowId &&
            (rule.Quality == QualityScope.Both ||
             newQuality == QualityScope.Both ||
             rule.Quality == newQuality));
        if (overlaps)
        {
            Plugin.ChatGui.Print($"[Sell Everything] {item.Name} already has an overlapping protection rule.");
            return;
        }

        this.plugin.Configuration.ProtectedItems.Add(new ProtectedItemRule
        {
            ItemId = item.RowId,
            DisplayName = item.Name.ToString(),
            KeepQuantity = this.protectAll ? int.MaxValue : Math.Max(0, this.keepQuantity),
            Quality = newQuality,
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
