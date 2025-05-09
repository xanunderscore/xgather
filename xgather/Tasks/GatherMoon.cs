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
        unsafe
        {
            var wk = WKSManager.Instance();

            var unit = Svc.ExcelRow<WKSMissionUnit>(mission);
            var todo = Svc.ExcelRow<WKSMissionToDo>((uint)(unit.Unknown7 + *((byte*)wk + 0xC62)));
            var marker = Svc.ExcelRow<WKSMissionMapMarker>(todo.Unknown13);

            _gatherCenter = new(marker.Unknown1 - 1024, marker.Unknown2 - 1024);
            _gatherRadius = marker.Unknown3;

            _requiredItems = [];

            void requireItem(ushort id, ushort quant)
            {
                if (id > 0)
                {
                    var realid = Svc.ExcelRow<WKSItemInfo>(id).Item;
                    _requiredItems[realid] = quant;
                }
            }

            requireItem(todo.Unknown3, todo.Unknown6);
            requireItem(todo.Unknown4, todo.Unknown7);
            requireItem(todo.Unknown5, todo.Unknown8);
        }
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

        Status = $"Gathering at {obj.Position}";

        if (!Svc.Condition[ConditionFlag.Unknown85])
            Utils.InteractWithObject(obj);

        await DoNormalGather(GetNeededItem);
    }

    private async Task DoCollectableGather()
    {
        await WaitWhile(() => !Utils.GatheringAddonReady(), "GatherStart");
        Utils.GatheringSelectFirst();

        await WaitWhile(() => !Utils.AddonReady("GatheringMasterpiece"), "GatherStart");

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
