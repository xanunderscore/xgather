using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using Lumina.Excel.Sheets;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;


namespace xgather.Tasks;

internal class GatherMoon : GatherBase
{
    private readonly Dictionary<uint, int> _requiredItems;
    private readonly Vector2 _gatherCenter;
    private readonly float _gatherRadius;

    internal GatherMoon()
    {
        var mission = MoonUtils.CurrentMission();
        ErrorIf(mission <= 0, "No active mission");
        var unit = Svc.ExcelRow<WKSMissionUnit>(mission);
        var todo = unit.MissionToDo.First().Value;
        var marker = Svc.ExcelRow<WKSMissionMapMarker>(todo.Unknown13);

        _gatherCenter = new(marker.Unknown1 - 1024, marker.Unknown2 - 1024);
        _gatherRadius = marker.Unknown3;

        _requiredItems = [];

        void requireItem(WKSItemInfo id, ushort quant)
        {
            if (id.RowId > 0)
                _requiredItems[id.Item.RowId] = quant;
        }

        foreach (var (item, quantity) in todo.RequiredItem.Zip(todo.RequiredItemQuantity))
            requireItem(item.Value, quantity);
    }

    protected override async Task Execute()
    {
        var iters = 0;
        while (MoonUtils.CurrentMission() > 0)
        {
            ErrorIf(++iters > 1000, "loop");
            await GatherNext();
        }
    }

    private async Task GatherNext()
    {
        var closest = await FindPoint();

        await MoveTo(closest.Position, 3.5f);
        await DoGather(closest);
    }

    private async Task<IGameObject> FindPoint()
    {
        static IGameObject? find() =>
            Svc.ObjectTable.Where(obj => obj.ObjectKind is ObjectKind.GatheringPoint && obj.IsTargetable).MinBy(obj => obj.Position.DistanceFromPlayerXZ());

        if (find() is { } nearby)
            return nearby;

        var surveyPoint = await Survey();
        var surveyFloor = await PointOnFloor(new Vector3(surveyPoint.X, 1024, surveyPoint.Y), false, 10);
        await MoveTo(surveyFloor, 10, interrupt: () => find() != null);
        if (find() is { } s)
            return s;

        Error("Unable to find a gather point");
        return null!;
    }

    private async Task DoGather(IGameObject obj)
    {
        using var scope = BeginScope("GatherAtPoint");

        ErrorIf(!obj.IsTargetable, "Gather point disappeared!");

        if (!Svc.Condition[ConditionFlag.Unknown85])
            Util.InteractWithObject(obj);

        await DoNormalGather(GetNeededItem);
    }

    private async Task DoCollectableGather()
    {
        await WaitWhile(() => !Util.IsGatheringAddonReady(), "GatherStart");
        Util.GatheringSelectFirst();

        await WaitWhile(() => !Util.IsAddonReady("GatheringMasterpiece"), "GatherStart");

        Error("Collectables gathering isn't implemented!");
    }

    private unsafe uint? GetNeededItem()
    {
        var im = InventoryManager.Instance();
        foreach (var (itemId, count) in _requiredItems)
        {
            var cnt = im->GetItemCountInContainer(itemId, InventoryType.Cosmopouch1) + im->GetItemCountInContainer(itemId, InventoryType.Cosmopouch2);
            if (cnt < count)
                return itemId;
        }

        return null;
    }
}

internal static class MoonUtils
{
    public static unsafe uint CurrentMission()
    {
        var wk = WKSManager.Instance();
        return wk == null ? 0u : wk->CurrentMissionUnitRowId;
    }
}
