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
    private int keepQuantity = int.MaxValue;

    public MainWindow(Plugin plugin, SellAutomation automation)
        : base("Sell Everything###SellEverythingMain")
    {
        this.plugin = plugin;
        this.automation = automation;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(820, 560),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void Draw()
    {
        DrawControls();
        ImGui.Separator();
        DrawStatus();
        ImGui.Separator();
        DrawQueue();
        ImGui.Separator();
        DrawWhitelist();
    }

    private void DrawControls()
    {
        if (ImGui.Button("Scan inventory"))
            this.automation.BuildQueue();

        ImGui.SameLine();
        if (ImGui.Button("Start full retainer run"))
            this.automation.Start();

        ImGui.SameLine();
        if (this.automation.IsRunning)
        {
            if (ImGui.Button("Pause"))
                this.automation.Pause();
        }
        else if (this.automation.State == AutomationState.Paused)
        {
            if (ImGui.Button("Resume"))
                this.automation.Resume();
        }

        ImGui.SameLine();
        if (ImGui.Button("Emergency stop"))
            this.automation.Stop();

        var dryRun = this.plugin.Configuration.DryRun;
        if (ImGui.Checkbox("Dry run", ref dryRun))
        {
            this.plugin.Configuration.DryRun = dryRun;
            this.plugin.Configuration.Save();
        }

        if (dryRun)
        {
            ImGui.TextWrapped("Dry run is ON. Inventory and quality are validated, but no retainer, market, confirmation, or sale actions are sent.");
        }
        else
        {
            ImGui.TextWrapped("LIVE MODE. Open the retainer list before starting. The plugin will select retainers, compare prices, confirm listings or retainer sales, and continue through the configured retainers.");
        }
    }

    private void DrawStatus()
    {
        ImGui.Text($"State: {this.automation.State}");
        if (this.automation.SessionRetainerLimit > 0)
            ImGui.Text($"Retainer: {this.automation.CurrentRetainerNumber}/{this.automation.SessionRetainerLimit}");
        ImGui.TextWrapped(this.automation.Status);

        var completed = this.automation.Queue.Count(entry => entry.State == QueueEntryState.Completed);
        var skipped = this.automation.Queue.Count(entry => entry.State == QueueEntryState.Skipped);
        var failed = this.automation.Queue.Count(entry => entry.State == QueueEntryState.Failed);
        var pending = this.automation.Queue.Count(entry => entry.State == QueueEntryState.Pending);
        ImGui.Text($"Queue: {this.automation.Queue.Count} | Pending: {pending} | Completed: {completed} | Skipped: {skipped} | Failed: {failed}");
    }

    private void DrawQueue()
    {
        if (!ImGui.BeginTable("SellQueue", 10, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY, new Vector2(0, 250)))
            return;

        ImGui.TableSetupColumn("Item");
        ImGui.TableSetupColumn("Quality");
        ImGui.TableSetupColumn("Qty");
        ImGui.TableSetupColumn("NQ seen");
        ImGui.TableSetupColumn("HQ seen");
        ImGui.TableSetupColumn("Lowest matching");
        ImGui.TableSetupColumn("Action");
        ImGui.TableSetupColumn("List price");
        ImGui.TableSetupColumn("State");
        ImGui.TableSetupColumn("Note");
        ImGui.TableHeadersRow();

        foreach (var entry in this.automation.Queue)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.TextUnformatted(entry.Candidate.ItemName);
            ImGui.TableNextColumn(); ImGui.TextUnformatted(entry.Candidate.QualityLabel);
            ImGui.TableNextColumn(); ImGui.Text(entry.Candidate.Quantity.ToString());
            ImGui.TableNextColumn(); ImGui.Text(entry.NqListingsSeen.ToString());
            ImGui.TableNextColumn(); ImGui.Text(entry.HqListingsSeen.ToString());
            ImGui.TableNextColumn(); ImGui.Text(entry.LowestMatchingPrice?.ToString("N0") ?? "-");
            ImGui.TableNextColumn(); ImGui.TextUnformatted(entry.Action.ToString());
            ImGui.TableNextColumn(); ImGui.Text(entry.ListingPrice?.ToString("N0") ?? "-");
            ImGui.TableNextColumn(); ImGui.TextUnformatted(entry.State.ToString());
            ImGui.TableNextColumn(); ImGui.TextUnformatted(entry.Note);
        }

        ImGui.EndTable();
    }

    private void DrawWhitelist()
    {
        ImGui.Text("Protected items");
        ImGui.SetNextItemWidth(320);
        ImGui.InputTextWithHint("##ItemSearch", "Exact or partial item name", ref this.itemSearch, 128);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(110);
        ImGui.InputInt("Keep", ref this.keepQuantity);
        ImGui.SameLine();
        if (ImGui.Button("Add first match"))
            AddFirstMatch();

        for (var i = 0; i < this.plugin.Configuration.ProtectedItems.Count; i++)
        {
            var rule = this.plugin.Configuration.ProtectedItems[i];
            ImGui.PushID(i);
            ImGui.TextUnformatted($"{rule.DisplayName} [{rule.ItemId}] | Keep: {(rule.KeepQuantity == int.MaxValue ? "all" : rule.KeepQuantity)} | {rule.Quality}");
            ImGui.SameLine();
            if (ImGui.SmallButton("Remove"))
            {
                this.plugin.Configuration.ProtectedItems.RemoveAt(i);
                this.plugin.Configuration.Save();
                ImGui.PopID();
                break;
            }
            ImGui.PopID();
        }
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
            return;

        this.plugin.Configuration.ProtectedItems.Add(new ProtectedItemRule
        {
            ItemId = item.RowId,
            DisplayName = item.Name.ToString(),
            KeepQuantity = this.keepQuantity < 0 ? int.MaxValue : this.keepQuantity,
            Quality = QualityScope.Both,
        });
        this.plugin.Configuration.Save();
        this.itemSearch = string.Empty;
    }
}
