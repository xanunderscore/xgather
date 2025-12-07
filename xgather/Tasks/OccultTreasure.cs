using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Data.Files;
using Lumina.Extensions;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using xgather.Utils;

namespace xgather.Tasks;

public class OccultTreasure : AutoTask
{
    private readonly Dictionary<uint, Coffer> allCoffers;

    private readonly List<Route> routes = [
        new("Green", 0, [0xB06294, 0xB06297, 0xB06298, 0xB0629A, 0xB0629F, 0xB062A3, 0xB06272]),
        new("Orange", 1, [0xB062B2, 0xB062B0, 0xB062AD, 0xB0629D, 0xB0629B, 0xB062A1, 0xB062A2, 0xB0636A, 0xB0636B, 0xB0636C]),
        new("Purple", 1, [0xB062AF, 0xB063E3, 0xB062D2, 0xB062D3, 0xB0636D, 0xB0636E, 0xB06277, 0xB06371, 0xB0636F]),
        new("Blue", 2, [0xB062D8, 0xB062DC, 0xB062E2, 0xB06274]),
        new("Red", 2, [0xB062D9, 0xB062D5, 0xB062D4, 0xB062D6, 0xB063DC, 0xB06273, 0xB063D8, 0xB06321, 0xB0632C, 0xB06333, 0xB06327, 0xB06369, 0xB06356, 0xB06366, 0xB06381, 0xB063CB, 0xB063CD, 0xB063D1, 0xB063CE, 0xB063CF, 0xB06279]),
        new("White", 4, [0xB06367, 0xB06368, 0xB06302, 0xB06319, 0xB06374, 0xB0637A, 0xB0637E, 0xB06278, 0xB06380]),
        new("Yellow", 3, [0xB062D7, 0xB06276, 0xB06337, 0xB0633D, 0xB06344, 0xB06346, 0xB06275, 0xB063D2]),
    ];

    public static readonly Vector3[] AetherytePositions = [
        new(830.75f, 72.98f, -695.98f),
        new(-173.02f, 8.19f, -611.14f),
        new(-358.14f, 101.98f, -120.96f),
        new(306.94f, 105.18f, 305.65f),
        new(-384.12f, 99.20f, 281.42f)
    ];

    public record struct Coffer(uint InstanceId, Vector3 Position);

    public record struct Route(string Name, int StartAetheryte, List<uint> Coffers);

    public OccultTreasure()
    {
        var lvb = Svc.Data.GetFile<LgbFile>("bg/ex5/03_ocn_o6/btl/o6b1/level/planmap.lgb") ?? throw new InvalidDataException("planmap is missing");

        var layer = lvb.Layers.FirstOrNull(l => l.Name == "Field_Treasure") ?? throw new InvalidDataException("Field_Treasure layer is missing from lvb");

        allCoffers = layer.InstanceObjects.Where(obj => obj.AssetType == Lumina.Data.Parsing.Layer.LayerEntryType.Treasure).Select(obj => (obj.InstanceId, new Coffer(obj.InstanceId, Util.Convert(obj.Transform.Translation)))).ToDictionary();
    }

    protected override async Task Execute()
    {
        foreach (var rte in routes)
            await ExecuteRoute(rte);
    }

    private async Task ExecuteRoute(Route r)
    {
        var home = AetherytePositions[r.StartAetheryte];
        var route = r.Coffers.Select(r => allCoffers[r]).ToList();

        if (home.DistanceFromPlayer() > 30 && route[0].Position.DistanceFromPlayer() > 30)
            await GoToAetheryte(r.StartAetheryte);

        for (var startIndex = 0; startIndex < route.Count; startIndex++)
            await Open(startIndex, route[startIndex], r.Name);
    }

    private async Task GoToAetheryte(int index)
    {
        if (AetherytePositions[0].DistanceFromPlayer() > 30)
            await Return();

        if (index == 0)
            return;

        await MoveDirectlyTo(AetherytePositions[0], 4);

        var aeth = Svc.ObjectTable.FirstOrDefault(t => t.BaseId == 0x1EBDC8);
        ErrorIf(aeth == null, "Aetheryte is missing??");

        Util.InteractWithObject(aeth);
        await WaitAddon("TelepotTown");

        unsafe
        {
            var tp = AgentTelepotTown.Instance();
            ErrorIf(tp == null || !tp->IsAddonReady(), "Aethernet agent is not ready");
            tp->TeleportToAetheryte((byte)index);
        }

        await WaitForBusy("Aethernet");
    }

    private async Task Open(int order, Coffer c, string label)
    {
        using var _ = BeginScope($"Opening {label} #{order} at {c.Position}");

        IGameObject? findCoffer()
        {
            var coffer = Svc.ObjectTable.FirstOrDefault(t => Util.GetLayoutId(t) == c.InstanceId);
            return coffer == null || Util.IsTreasureOpen(coffer) ? null : coffer;
        }

        bool shouldInterrupt() => c.Position.DistanceFromPlayerXZ() < 60 && findCoffer() == null;

        await MoveTo(c.Position, 2.1f, mount: true, dismount: false, interrupt: shouldInterrupt);

        while (true)
        {
            if (findCoffer() is not IGameObject coffer)
                return;

            Util.InteractWithObject(coffer);
            await NextFrame(10);
        }
    }

    public const float MapX = 1200;
    public const float MapZ = 1200;

    //public override void DrawDebug()
    //{
    //    if (_currentRoute != Route.None)
    //        return;

    //    using var cum = ImRaii.Combo("Route", "-");
    //    if (cum)
    //    {
    //        for (var c = Route.Green; c <= Route.Yellow; c++)
    //        {
    //            if (ImGui.Selectable(c.ToString()))
    //            {
    //                _currentRoute = c;
    //                return;
    //            }
    //        }
    //    }
    //}

    private readonly List<uint> selectedPath = [];

    private void DrawRouteSelector()
    {
        if (ImGui.Button("Copy path"))
        {
            var ids = string.Join(", ", selectedPath.Select(p => "0x" + p.ToString("X6")));
            ImGui.SetClipboardText($"[{ids}]");
            selectedPath.Clear();
        }

        var tex = Svc.TextureProvider.GetFromGame("ui/map/o6b1/01/o6b101_m.tex");
        if (tex == null)
            return;

        if (tex.TryGetWrap(out var wrap, out var exc))
        {
            if (exc != null)
            {
                Svc.Log.Error(exc, "error fetching wrap");
                return;
            }

            var cursor = ImGui.GetCursorScreenPos();
            var dl = ImGui.GetWindowDrawList();

            ImGui.Image(wrap.Handle, new(MapX, MapZ));

            foreach (var (_, c) in allCoffers)
            {
                var isSelected = selectedPath.Contains(c.InstanceId);
                var col = c.InstanceId < 0xB0627A ? 0xFF0000FF : 0xFFFF0000;
                var screenPos = cursor + GetMapScreenPos(c.Position, MapX);
                var userScreenPos = ImGui.GetMousePos();
                var isHovered = !isSelected && (userScreenPos - screenPos).LengthSquared() < 625;
                if (isHovered)
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

                if (isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    selectedPath.Add(c.InstanceId);

                dl.AddCircleFilled(screenPos, isHovered ? 20 : 10, col);
            }

            for (var i = 0; i < selectedPath.Count - 1; i++)
            {
                var cur = selectedPath[i];
                var next = selectedPath[i + 1];
                var p1 = cursor + GetMapScreenPos(allCoffers[cur].Position, MapX);
                var p2 = cursor + GetMapScreenPos(allCoffers[next].Position, MapX);
                dl.AddLine(p1, p2, 0xFF00FF00, 2);
            }
        }
    }

    private static Vector2 GetMapScreenPos(Vector3 worldPos, float mapSideLen)
    {
        var x = (worldPos.X + 1024) * mapSideLen / 2048f;
        var z = (worldPos.Z + 1024) * mapSideLen / 2048f;
        return new(x, z);
    }

    private async Task Return()
    {
        using var _ = BeginScope("OccultReturn");

        ErrorIf(Svc.ExcelRow<Lumina.Excel.Sheets.TerritoryType>(Svc.ClientState.TerritoryType).TerritoryIntendedUse.RowId != 61, "Occult Return not usable here");

        await WaitWhile(Util.PlayerIsBusy, "WaitBusy");
        ErrorIf(!Util.UseAction(ActionType.GeneralAction, 8), "Unable to use Occult Return");

        using (BeginScope("WaitSelectYesno"))
        {
            while (true)
            {
                if (Util.IsAddonReady("SelectYesno"))
                {
                    await WaitSelectYes();
                    break;
                }

                if (Svc.Condition[ConditionFlag.BetweenAreas])
                    break;

                Log("waiting...");
                await NextFrame(10);
            }
        }

        await WaitWhile(() => !Svc.Condition[ConditionFlag.BetweenAreas], "ReturnStart");
        await WaitWhile(Util.PlayerIsBusy, "ReturnFinish");
    }
}
