using Dalamud.Game.Command;
using Dalamud.Game.DutyState;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using UltiSim.Core;
using UltiSim.Core.Map;
using UltiSim.Windows;

namespace UltiSim;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static ISigScanner SigScanner { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider GameInterop { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPartyList PartyList { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IDutyState DutyState { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;

    private const string CommandName = "/ultisim";

    public Configuration Configuration { get; init; }
    internal static Configuration Config { get; private set; } = null!;

    public readonly WindowSystem WindowSystem = new("UltiSim");
    public Game Game { get; }
    // SimObjects need the Game (e.g. SimPlayer reads Game.PlayerInputHooks for
    // stun on death). Mirror the pattern of the other Plugin.* statics.
    internal static Game GameInstance { get; private set; } = null!;
    internal static LogManager LogManager { get; private set; } = null!;
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Config = Configuration;

        LogManager = new LogManager();
        if (Config.EnableEventLogging) LogManager.Open();

        Game = new Game();
        GameInstance = Game;
        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open UltiSim. Subcommands: config, start, reset, leave"
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        Framework.Update += OnFrameworkUpdate;

        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
        ClientState.TerritoryChanged += OnTerritoryChanged;
        DutyState.DutyStarted += OnDutyStarted;
        DutyState.DutyWiped += OnDutyWiped;
        DutyState.DutyCompleted += OnDutyCompleted;

        Log.Information($"===A cool log message from {PluginInterface.Manifest.Name}===");
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        ClientState.TerritoryChanged -= OnTerritoryChanged;
        DutyState.DutyStarted -= OnDutyStarted;
        DutyState.DutyWiped -= OnDutyWiped;
        DutyState.DutyCompleted -= OnDutyCompleted;

        WindowSystem.RemoveAllWindows();

        Game.Dispose();
        LogManager.Dispose();
        ConfigWindow.Dispose();
        MainWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        Game.Tick((float)framework.UpdateDelta.TotalSeconds);
    }

    private void OnTerritoryChanged(uint territory)
    {
        var row = DataManager.GetExcelSheet<TerritoryType>()?.GetRowOrDefault(territory);
        var isInn = row?.TerritoryIntendedUse.RowId == 2; // TerritoryIntendedUse.Inn
        if (!isInn)
        {
            var name = row?.PlaceName.ValueNullable?.Name.ExtractText() ?? string.Empty;
            LogManager.LogEnterInstance(territory, name);
        }

        if (Config.OpenSimMenuOnInn && isInn)
        {
            MainWindow.IsOpen = true;
            return;
        }
        if (Config.OpenSimMenuOnSupportedInstanceSolo && PartyList.Length <= 1)
        {
            foreach (var scenario in Game.Scenarios)
            {
                bool match = scenario.TargetInstance?.TerritoryId == territory;
                if (!match)
                    foreach (var ovr in scenario.OriginOverrides)
                        if (ovr.TerritoryId == territory) { match = true; break; }
                if (match) { MainWindow.IsOpen = true; return; }
            }
        }
    }

    private void OnDutyStarted(IDutyStateEventArgs args)
        => LogManager.LogCombatStart(args.TerritoryType.RowId);

    private void OnDutyWiped(IDutyStateEventArgs args)
        => LogManager.LogCombatEnd(args.TerritoryType.RowId, wipe: true);

    private void OnDutyCompleted(IDutyStateEventArgs args)
        => LogManager.LogCombatEnd(args.TerritoryType.RowId, wipe: false);

    private void OnCommand(string command, string args)
    {
        switch (args.Trim())
        {
            case "config":
                ConfigWindow.Toggle();
                break;
            case "start":
                if (MainWindow.SelectedScenario is { } scenario)
                    Game.RunScenario(scenario);
                break;
            case "reset":
                Game.Reset();
                break;
            case "leave":
                Game.Leave();
                break;
            default:
                MainWindow.Toggle();
                break;
        }
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
}
