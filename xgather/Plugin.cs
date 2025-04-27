using Dalamud.Game.Command;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using System.Collections.Generic;
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

    internal List<GameData.Aetheryte> Aetherytes;
    internal readonly Automation _auto = new();

    public bool RecordMode { get; set; } = false;
    private Debug? Debug;

    public Plugin(IDalamudPluginInterface pluginInterface, ICommandManager commandManager)
    {
        Svc.Init(this, pluginInterface);

        Svc.Config.RegisterGameItems();

        MainWindow = new(new Routes(), new ItemSearch(_auto, Svc.Config.ItemSearchText), new Lists()) { IsOpen = Svc.Config.MainWindowOpen };
        Overlay = new(_auto) { IsOpen = Svc.Config.OverlayOpen };

        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(Overlay);

        commandManager.AddHandler("/xgather", new CommandInfo(OnCommand) { HelpMessage = "Open it" });
        commandManager.AddHandler("/xgatherfish", new CommandInfo(Gatherfish) { HelpMessage = "Gather fish" });

        Svc.PluginInterface.UiBuilder.Draw += DrawUI;
        Svc.PluginInterface.UiBuilder.OpenConfigUi += () => MainWindow.IsOpen = true;
        Svc.PluginInterface.UiBuilder.OpenMainUi += () => Overlay.IsOpen = true;
        Svc.Framework.Update += Tick;

        Aetherytes = [.. GameData.Aetheryte.LoadAetherytes()];

        Debug = new();
    }

    public void Dispose()
    {
        WindowSystem.RemoveAllWindows();
        MainWindow.Dispose();
        Svc.Config.Save();
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

        var it = FindGatheringItemByName(args);
        if (it == null)
        {
            Alerts.Error($"No item found for query {args}");
            return;
        }

        Svc.Log.Debug($"Identified {it.Value.RowId}");

        DoGatherItem(args, it.Value);
    }

    private Item? FindGatheringItemByName(string query)
    {
        Item? filter(IEnumerable<Item>? list) =>
            list?.Where(i => i.Name.ToString().Contains(query, System.StringComparison.InvariantCultureIgnoreCase))
                .Select(i => (Item?)i)
                .FirstOrDefault();

        if (
            filter(
                Svc.ExcelSheet<GatheringItem>()
                    ?.SelectMany(i => i.Item.TryGetValue<Item>(out var realItem) ? new Item[] { realItem } : [])
            )
            is Item i
        )
            return i;

        if (filter(Svc.ExcelSheet<SpearfishingItem>()?.Select(s => s.Item.Value)) is Item s)
            return s;

        return null;
    }

    private void DoGatherItem(string args, Item it)
    {
        foreach (var rte in Svc.Config.GetGatherPointGroupsForItem(it.RowId))
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
                Svc.Config.RecordPosition(obj);
    }
}
