using Dalamud.Game.Command;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System.Collections.Generic;
using System.Linq;
using xgather.GameData;
using xgather.UI;
using xgather.UI.Windows;

namespace xgather;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "xgather";
    private const string CommandName = "/xgather";

    public WindowSystem WindowSystem = new("xgather");

    internal MainWindow MainWindow { get; init; }
    private Overlay Overlay { get; init; }

    internal List<Aetheryte> Aetherytes;

    public bool RecordMode { get; set; } = false;

    public Plugin(IDalamudPluginInterface pluginInterface, ICommandManager commandManager)
    {
        Svc.Init(this, pluginInterface);

        Svc.Config.RegisterGameItems();

        MainWindow = new(new Routes(), new ItemSearch(Svc.Config.ItemSearchText), new Lists());
        Overlay = new() { IsOpen = Svc.Config.OverlayOpen };

        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(Overlay);

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
            Overlay.IsOpen = true;
            return;
        }

        if (args == "items")
        {
            MainWindow.IsOpen = true;
            return;
        }

        var git = Svc.ExcelSheet<Lumina.Excel.GeneratedSheets.GatheringItem>().FirstOrDefault(it =>
        Svc.ExcelRow<Lumina.Excel.GeneratedSheets.Item>((uint)it.Item).Name.ToString().Contains(args, System.StringComparison.InvariantCultureIgnoreCase));
        if (git == null)
        {
            Alerts.Error($"No item found for query {args}");
            return;
        }

        var it = Svc.ExcelRow<Lumina.Excel.GeneratedSheets.Item>((uint)git.Item)!;

        foreach (var rte in Svc.Config.GetGatherPointGroupsForItem(it.RowId))
        {
            var msg = new SeString().Append("Identified ").Append(new UIForegroundPayload(1)).Append(new ItemPayload(it.RowId)).Append(it.Name.ToString()).Append(RawPayload.LinkTerminator).Append(new UIForegroundPayload(0)).Append($" for \"{args}\"");
            Alerts.Info(msg);
            Svc.Executor.Start(rte);
            Overlay.IsOpen = true;
            return;
        }

        Alerts.Error($"No routes found for item {it.Name}");
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
