using Dalamud.Game;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Excel;
using xgather.GameData;

namespace xgather;

public class Svc
{
#nullable disable
    public static Plugin Plugin { get; private set; }

    [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; }
    [PluginService] public static ICommandManager CommandManager { get; private set; }
    [PluginService] public static IObjectTable ObjectTable { get; private set; }
    [PluginService] public static IPluginLog Log { get; private set; }
    [PluginService] public static IGameGui GameGui { get; private set; }
    [PluginService] public static IClientState ClientState { get; private set; }
    [PluginService] public static ITargetManager TargetManager { get; private set; }
    [PluginService] public static IFramework Framework { get; private set; }
    [PluginService] public static ICondition Condition { get; private set; }
    [PluginService] public static IDataManager Data { get; private set; }
    [PluginService] public static ITextureProvider TextureProvider { get; private set; }
    [PluginService] public static ISigScanner SigScanner { get; private set; }
    [PluginService] public static IChatGui Chat { get; private set; }
    [PluginService] public static IAddonLifecycle AddonLifecycle { get; private set; }
    [PluginService] public static IToastGui Toast { get; private set; }
    [PluginService] public static IGameInteropProvider Hook { get; private set; }

    public static Configuration Config { get; private set; }
    private static ItemDatabaseManager ItemDBM;
    public static ItemDatabase ItemDB { get; private set; }
#nullable enable

    public static IPlayerCharacter? Player => ClientState.LocalPlayer;

    internal static bool IsInitialized = false;

    public static bool IsDev => PluginInterface.IsDev;

    public static void Init(Plugin plugin, IDalamudPluginInterface pi)
    {
        if (IsInitialized)
            return;
        IsInitialized = true;
        pi.Create<Svc>();

        ItemDBM = new ItemDatabaseManager(pi);

        if (IsDev)
            Data.GameData.Options.PanicOnSheetChecksumMismatch = false;

        ItemDB = IsDev ? ItemDBM.Create() : ItemDBM.OpenOrCreate();

        Config = pi.GetPluginConfig() as Configuration ?? new Configuration();
        Plugin = plugin;
    }

    public static void OnDispose()
    {
        Config.Save();
        ItemDBM.Save(ItemDB);
    }

    public static ExcelSheet<T> ExcelSheet<T>() where T : struct, IExcelRow<T> => Data.Excel.GetSheet<T>();
    public static SubrowExcelSheet<T> SubrowExcelSheet<T>() where T : struct, IExcelSubrow<T> => Data.Excel.GetSubrowSheet<T>();
    public static T? ExcelRowMaybe<T>(uint id) where T : struct, IExcelRow<T> => Data.Excel.GetSheet<T>().TryGetRow(id, out var row) ? row : null;
    public static T ExcelRow<T>(uint id) where T : struct, IExcelRow<T> => ExcelRowMaybe<T>(id) ?? throw new System.Exception($"no {typeof(T)} with id {id}");
}
