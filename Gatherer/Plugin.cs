using Dalamud.Game.Command;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using System.Collections.Generic;
using System.Linq;
using xgather.UI;
using xgather.UI.Windows;

namespace xgather;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "xgather";

    public WindowSystem WindowSystem = new("xgather");

    internal MainWindow MainWindow { get; init; }
    private Overlay Overlay { get; init; }

    internal List<GameData.Aetheryte> Aetherytes;

    public bool RecordMode { get; set; } = false;

    public Plugin(IDalamudPluginInterface pluginInterface, ICommandManager commandManager)
    {
        Svc.Init(this, pluginInterface);

        Svc.Config.RegisterGameItems();

        MainWindow = new(new Routes(), new ItemSearch(Svc.Config.ItemSearchText), new Lists());
        Overlay = new() { IsOpen = Svc.Config.OverlayOpen };

        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(Overlay);

        commandManager.AddHandler("/xgather", new CommandInfo(OnCommand) { HelpMessage = "Open it" });
        commandManager.AddHandler("/xgatherfish", new CommandInfo(Gatherfish) { HelpMessage = "Gather fish" });

        Svc.PluginInterface.UiBuilder.Draw += DrawUI;
        Svc.Framework.Update += Tick;

        Aetherytes = GameData.Aetheryte.LoadAetherytes().ToList();
    }

    public void Dispose()
    {
        WindowSystem.RemoveAllWindows();
        Svc.Config.Save();
        IPCHelper.PathStop();
        Svc.CommandManager.RemoveHandler("/xgather");
        Svc.CommandManager.RemoveHandler("/xgatherfish");
        Svc.Framework.Update -= Tick;
        Svc.PluginInterface.UiBuilder.Draw -= DrawUI;
    }

    private void Gatherfish(string command, string args)
    {
        if (args == "")
        {
            Alerts.Error($"Usage: /xgatherfish name-of-fish");
            return;
        }

        var gfish = Svc.ExcelSheet<SpearfishingItem>()?.FirstOrDefault(it => it.Item.Value.Name.ToString().Contains(args, System.StringComparison.InvariantCultureIgnoreCase));
        if (gfish == null)
        {
            Alerts.Error($"No fish found for query {args}");
            return;
        }

        DoGatherItem(args, gfish.Value.Item.Value!);
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

        var git = Svc.ExcelSheet<GatheringItem>()?.FirstOrDefault(it =>
        Svc.ExcelRow<Item>(it.Item.RowId).Name.ToString().Contains(args, System.StringComparison.InvariantCultureIgnoreCase));
        if (git == null)
        {
            Alerts.Error($"No item found for query {args}");
            return;
        }

        var it = Svc.ExcelRow<Item>(git.Value.RowId)!;

        DoGatherItem(args, it);
    }

    private void DoGatherItem(string args, Item it)
    {
        foreach (var rte in Svc.Config.GetGatherPointGroupsForItem(it.RowId))
        {
            var msg = new SeString().Append("Identified ").Append(new UIForegroundPayload(1)).Append(new ItemPayload(it.RowId)).Append(it.Name.ToString()).Append(RawPayload.LinkTerminator).Append(new UIForegroundPayload(0)).Append($" for \"{args}\"");
            Alerts.Info(msg);
            Svc.Executor.StartAdHoc(rte, it.RowId);
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
