using Dalamud.Game.Command;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using System.Linq;
using xgather.Tasks;
using xgather.UI;
using xgather.UI.Windows;

namespace xgather;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "xgather";

    public WindowSystem WindowSystem = new("xgather");

    internal MainWindow MainWindow { get; init; }
    private Overlay Overlay { get; init; }

    internal readonly Automation _auto = new();

    public bool RecordMode { get; set; } = false;
    private Debug? Debug;

    public Plugin(IDalamudPluginInterface pluginInterface, ICommandManager commandManager)
    {
        Svc.Init(this, pluginInterface);

        MainWindow = new(new ItemSearch(_auto, Svc.Config.ItemSearchText), new Lists()) { IsOpen = Svc.Config.MainWindowOpen };
        Overlay = new(_auto) { IsOpen = Svc.Config.OverlayOpen };

        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(Overlay);

        commandManager.AddHandler("/xgather", new CommandInfo(OnCommand) { HelpMessage = "Open it" });
        commandManager.AddHandler("/xgatherfish", new CommandInfo(Gatherfish) { HelpMessage = "Gather fish" });

        Svc.PluginInterface.UiBuilder.Draw += DrawUI;
        Svc.PluginInterface.UiBuilder.OpenConfigUi += () => MainWindow.IsOpen = true;
        Svc.PluginInterface.UiBuilder.OpenMainUi += () => Overlay.IsOpen = true;
        Svc.Framework.Update += Tick;

        Debug = new();
    }

    public void Dispose()
    {
        WindowSystem.RemoveAllWindows();
        Svc.OnDispose();
        IPCHelper.PathStop();
        Svc.CommandManager.RemoveHandler("/xgather");
        Svc.CommandManager.RemoveHandler("/xgatherfish");
        Svc.Framework.Update -= Tick;
        Svc.PluginInterface.UiBuilder.Draw -= DrawUI;
        Debug?.Dispose();
    }

    private void Gatherfish(string command, string args)
    {
        if (args == "")
        {
            Alerts.Error($"Usage: /xgatherfish name-of-fish");
            return;
        }

        var gfish = Svc.ExcelSheet<SpearfishingItem>()
            ?.FirstOrDefault(
                it => it.Item.Value.Name.ToString().Contains(args, System.StringComparison.InvariantCultureIgnoreCase)
            );
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

        if (args == "moonfate")
        {
            _auto.Start(new MoonFate());
            return;
        }

        var it = FindItemByName(args);
        if (it == null)
        {
            Alerts.Error($"No item found for query {args}");
            return;
        }

        Svc.Log.Debug($"Identified {it.Value.RowId} for {args}");

        DoGatherItem(args, it.Value);
    }

    private Item? FindItemByName(string query)
    {
        return Svc.ExcelSheet<Item>().Where(i => i.Name.ToString().Contains(query, System.StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();
    }

    private void DoGatherItem(string args, Item it)
    {
        foreach (var rte in Svc.ItemDB.GetGatherPointGroupsForItem(it.RowId))
        {
            var msg = new SeString()
                .Append("Identified ")
                .Append(new UIForegroundPayload(1))
                .Append(new ItemPayload(it.RowId))
                .Append(it.Name.ToString())
                .Append(RawPayload.LinkTerminator)
                .Append(new UIForegroundPayload(0))
                .Append($" for \"{args}\"");
            Alerts.Info(msg);
            _auto.Start(new GatherItem(it.RowId, 999));
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
            foreach (
                var obj in Svc.ObjectTable.Where(
                    x => x.ObjectKind is Dalamud.Game.ClientState.Objects.Enums.ObjectKind.GatheringPoint
                )
            )
                Svc.ItemDB.RecordPosition(obj);
    }
}
