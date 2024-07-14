using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System.Collections.Generic;
using System.Linq;
using xgather.GameData;
using xgather.Windows;

namespace xgather;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "xgather";
    private const string CommandName = "/xgather";

    public WindowSystem WindowSystem = new("xgather");

    private Overlay Overlay { get; init; }

    internal List<Aetheryte> Aetherytes;

    public bool RecordMode { get; set; } = false;

    public Plugin(IDalamudPluginInterface pluginInterface, ICommandManager commandManager)
    {
        Svc.Init(this, pluginInterface);

        Svc.Route.Init();

        Svc.Config.RegisterGameItems();

        Overlay = new Overlay(new RouteBrowser(), new ItemBrowser(Svc.Config.ItemSearchText), new DebugView())
        {
            IsOpen = true
        };

        WindowSystem.AddWindow(Overlay);

        commandManager.AddHandler(
            CommandName,
            new CommandInfo(OnCommand) { HelpMessage = "A useful message to display in /xlhelp" }
        );

        Svc.PluginInterface.UiBuilder.Draw += DrawUI;
        Svc.Framework.Update += Tick;

        Aetherytes = Aetheryte.LoadAetherytes().ToList();
    }

    public void Dispose()
    {
        WindowSystem.RemoveAllWindows();
        Svc.Config.Save();
        IPCHelper.PathStop();
        Svc.CommandManager.RemoveHandler(CommandName);
        Svc.Framework.Update -= Tick;
        Svc.PluginInterface.UiBuilder.Draw -= DrawUI;
    }

    private void OnCommand(string command, string args)
    {
        Overlay.IsOpen = true;
    }

    private void DrawUI()
    {
        WindowSystem.Draw();
    }

    private void Tick(IFramework framework)
    {
        if (RecordMode)
            foreach (var obj in Svc.ObjectTable.Where(x => x.ObjectKind is Dalamud.Game.ClientState.Objects.Enums.ObjectKind.GatheringPoint))
                Svc.Config.RecordPosition(obj);
    }
}
