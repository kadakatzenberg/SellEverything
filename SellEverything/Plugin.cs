using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using SellEverything.Services;
using SellEverything.Windows;

namespace SellEverything;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/selleverything";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static IMarketBoard MarketBoard { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IContextMenu ContextMenu { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private readonly WindowSystem windowSystem = new("SellEverything");
    private readonly MarketPriceCollector marketPrices;
    private readonly RetainerUi retainerUi;
    private readonly SellAutomation automation;
    private readonly InventoryContextMenu inventoryContextMenu;
    private readonly MainWindow mainWindow;
    private readonly ConfigWindow configWindow;

    public Plugin()
    {
        this.Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        this.Configuration.Initialize(PluginInterface);

        var scanner = new InventoryScanner(DataManager, Log);
        this.marketPrices = new MarketPriceCollector(MarketBoard, Log, this.Configuration);
        this.retainerUi = new RetainerUi(GameGui, AddonLifecycle, Log);
        this.retainerUi.MarketResultsOpened += this.marketPrices.Activate;
        this.automation = new SellAutomation(this.Configuration, scanner, this.marketPrices, this.retainerUi, ChatGui, Log);
        this.inventoryContextMenu = new InventoryContextMenu(this, ContextMenu);

        this.mainWindow = new MainWindow(this, this.automation);
        this.configWindow = new ConfigWindow(this);
        this.windowSystem.AddWindow(this.mainWindow);
        this.windowSystem.AddWindow(this.configWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Open Sell Everything. Open the retainer list first, then use start. Subcommands: scan, start, pause, resume, retry, stop, config."
        });

        PluginInterface.UiBuilder.Draw += this.windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += this.ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += this.ToggleConfigUi;
        Framework.Update += this.OnFrameworkUpdate;

        Log.Information("Sell Everything loaded. Dry run: {DryRun}", this.Configuration.DryRun);
    }

    public Configuration Configuration { get; }
    internal bool IsAutomationRunning => this.automation.IsRunning;
    internal bool AreSettingsLocked => this.automation.LocksConfiguration;

    public void Dispose()
    {
        Framework.Update -= this.OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw -= this.windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= this.ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= this.ToggleConfigUi;
        CommandManager.RemoveHandler(CommandName);
        this.inventoryContextMenu.Dispose();
        this.automation.Stop();
        this.retainerUi.MarketResultsOpened -= this.marketPrices.Activate;
        this.retainerUi.Dispose();
        this.marketPrices.Dispose();
        this.windowSystem.RemoveAllWindows();
    }

    public void ToggleMainUi() => this.mainWindow.Toggle();
    public void ToggleConfigUi() => this.configWindow.Toggle();

    private void OnFrameworkUpdate(IFramework framework)
    {
        this.automation.Update();
    }

    private void OnCommand(string command, string arguments)
    {
        switch (arguments.Trim().ToLowerInvariant())
        {
            case "scan":
                this.automation.BuildQueue();
                this.mainWindow.IsOpen = true;
                break;
            case "start":
                this.automation.Start();
                this.mainWindow.IsOpen = true;
                break;
            case "pause":
                this.automation.Pause();
                break;
            case "resume":
                this.automation.Resume();
                break;
            case "retry":
                this.automation.RetryFailed();
                this.mainWindow.IsOpen = true;
                break;
            case "stop":
                this.automation.Stop();
                break;
            case "config":
                this.configWindow.Toggle();
                break;
            default:
                this.mainWindow.Toggle();
                break;
        }
    }
}
