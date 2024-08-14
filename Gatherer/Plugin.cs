using Dalamud.Game.Command;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
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

    internal Overlay Overlay { get; init; }
    private DebugView DebugView { get; init; }

    internal List<Aetheryte> Aetherytes;

    public bool RecordMode { get; set; } = false;

    public Plugin(IDalamudPluginInterface pluginInterface, ICommandManager commandManager)
    {
        Svc.Init(this, pluginInterface);

        Svc.Route.Init();

        Svc.Config.RegisterGameItems();

        Overlay = new Overlay(new RouteBrowser(), new ItemBrowser(Svc.Config.ItemSearchText)) { IsOpen = Svc.Config.OverlayOpen };
        DebugView = new();

        WindowSystem.AddWindow(Overlay);
        WindowSystem.AddWindow(DebugView);

        commandManager.AddHandler(
            CommandName,
            new CommandInfo(OnCommand) { HelpMessage = "Open it" }
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
        if (args == "")
        {
            DebugView.IsOpen = true;
            return;
        }

        if (args == "items")
        {
            Overlay.IsOpen = true;
            return;
        }

        var it = Svc.ExcelSheet<Lumina.Excel.GeneratedSheets.Item>().FirstOrDefault(it => it.Name.ToString().Contains(args, System.StringComparison.InvariantCultureIgnoreCase));
        if (it == null)
        {
            UiMessage.Error($"No item found for query {args}");
            return;
        }

        foreach (var rte in Svc.Config.GetRoutesForItem(it.RowId))
        {
            var msg = new SeString().Append("Identified ").Append(new UIForegroundPayload(1)).Append(new ItemPayload(it.RowId)).Append(it.Name.ToString()).Append(RawPayload.LinkTerminator).Append(new UIForegroundPayload(0)).Append($" for \"{args}\"");
            UiMessage.Info(msg);
            Svc.Route.Start(rte.Item2);
            DebugView.IsOpen = true;
            return;
        }

        UiMessage.Error($"No routes found for item {args}");
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
