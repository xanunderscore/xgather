using Dalamud.Game;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Excel;

namespace xgather;

public class Svc
{
#nullable disable
    public static Plugin Plugin { get; private set; }
    public static RouteExec Route { get; private set; }

    [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; }
    [PluginService] public static ICommandManager CommandManager { get; private set; }
    [PluginService] public static IObjectTable ObjectTable { get; private set; }
    [PluginService] public static IPluginLog Log { get; private set; }
    [PluginService] public static IClientState ClientState { get; private set; }
    [PluginService] public static ITargetManager TargetManager { get; private set; }
    [PluginService] public static IFramework Framework { get; private set; }
    [PluginService] public static ICondition Condition { get; private set; }
    [PluginService] public static IDataManager Data { get; private set; }
    [PluginService] public static IToastGui Toast { get; private set; }
    [PluginService] public static ITextureProvider TextureProvider { get; private set; }
    [PluginService] public static ISigScanner SigScanner { get; private set; }
    [PluginService] public static IChatGui Chat { get; private set; }
    public static Configuration Config { get; private set; }
#nullable enable

    public static IPlayerCharacter? Player => ClientState.LocalPlayer;

    internal static bool IsInitialized = false;

    public static void Init(Plugin plugin, IDalamudPluginInterface pi)
    {
        if (IsInitialized)
            return;
        IsInitialized = true;
        pi.Create<Svc>();

        Config = pi.GetPluginConfig() as Configuration ?? new Configuration();
        Route = new RouteExec();
        Route.Init();
        Plugin = plugin;
    }

    public static ExcelSheet<T> ExcelSheet<T>() where T : ExcelRow => Data.Excel.GetSheet<T>()!;
    public static T ExcelRow<T>(uint id) where T : ExcelRow => Data.Excel.GetSheet<T>()!.GetRow(id)!;
}
