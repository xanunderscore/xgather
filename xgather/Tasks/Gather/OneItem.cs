using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using xgather.GameData;
using xgather.Utils;

namespace xgather.Tasks.Gather;

public class OneItem : GatherBase
{
    private Vector3? _lastPoint;
    private readonly uint itemId;
    private readonly uint quantity;

    private readonly bool IsCollectable;
    private readonly bool IsFishing;

    public OneItem(uint itemId, uint quantity)
    {
        this.itemId = itemId;
        this.quantity = quantity + (uint)Util.GetQuantityOwned(itemId);

        var it = Util.Item(itemId);
        IsFishing = it.ItemUICategory.RowId == 47; // 47 = seafood
        IsCollectable = it.IsCollectable;
    }

    protected override async Task Execute()
    {
        var needed = GetQuantityNeeded();
        Log($"Gathering {needed}x {Util.ItemName(itemId)}");

        if (needed == 0)
            return;

        var route = Svc.ItemDB.GetGatherPointGroupsForItem(itemId).FirstOrDefault();
        ErrorIf(route == null, $"No route found for item {itemId}");

        var iters = 0;
        while (GetQuantityNeeded() > 0)
        {
            ErrorIf(++iters > 1000, "loop");
            await GatherNext(route);
        }
    }

    private async Task GatherNext(GatheringPointBase gpBase)
    {
        var pos = new Vector3(gpBase.WorldPos.X, 0, gpBase.WorldPos.Y);
        await TeleportToZone(gpBase.Zone, pos);
        await ChangeClass(gpBase.Class);

        if (Util.GetNextAvailable(gpBase) is (var nextStart, _))
            await WaitWhile(() => nextStart > DateTime.Now.AddSeconds(5), "WaitSpawn", 100);

        if (IsCollectable && IsFishing)
            await UseCollectorsGlove();

        var point = await FindPoint(gpBase);

        var flying = Svc.Condition.Any(ConditionFlag.InFlight, ConditionFlag.Diving);

        Vector3 groundPoint;
        var tolerance = 3.5f;
        if (gpBase.Class == GatherClass.FSH || !Svc.Condition[ConditionFlag.InFlight])
            groundPoint = point.Position;
        else
        {
            groundPoint = await PointOnFloor(point.Position, 3.5f);
            tolerance = 1;
        }

        var mount = groundPoint.DistanceFromPlayer() > 20;

        await MoveTo(groundPoint, tolerance, mount: mount, fly: flying, dismount: mount || Svc.Condition[ConditionFlag.Mounted]);

        await MoveTo(point.Position, 3.5f);

        await DoGather(point);
    }

    private async Task<IGameObject> FindPoint(GatheringPointBase gpBase)
    {
        using var scope = BeginScope("FindPoint");

        IGameObject? find() =>
            Svc.ObjectTable.Where(obj => obj.ObjectKind is ObjectKind.GatheringPoint && obj.IsTargetable && gpBase.Nodes.Contains(obj.BaseId)).MinBy(obj => obj.Position.DistanceFromPlayerXZ());

        if (find() is { } nearby)
            return nearby;

        Vector3 guess;
        if (Svc.Condition[ConditionFlag.Diving])
            guess = gpBase.WorldPos.WithY(Svc.Player!.Position.Y);
        else
            guess = await PointOnFloor(gpBase.WorldPos.WithY(0), 10);

        await MoveTo(guess, 10, true, true, interrupt: () => find() != null);

        if (find() is { } n)
            return n;

        var surveyPoint = await Survey();
        ErrorIf(!gpBase.ContainsPoint(surveyPoint), "Gathering marker is for a different node");

        Vector3 surveyFloor;
        if (Svc.Condition[ConditionFlag.Diving])
            surveyFloor = new Vector3(surveyPoint.X, Svc.Player!.Position.Y, surveyPoint.Y);
        else
            surveyFloor = await PointOnFloor(surveyPoint.WithY(0), 10);

        await MoveTo(surveyFloor, 10, true, true, interrupt: () => find() != null);

        if (find() is { } s)
            return s;

        Error("Unable to find a gather point");
        return null!;
    }

    private async Task DoGather(IGameObject obj)
    {
        using var scope = BeginScope($"Gathering {Util.ItemName(itemId)}");

        _lastPoint = obj.Position;
        if (!Svc.Condition[ConditionFlag.Unknown85])
            Util.InteractWithObject(obj);

        if (IsFishing)
            await DoSpearfish();
        else if (IsCollectable)
            await DoCollectableGather(itemId);
        else
            await DoNormalGather(itemId);
    }

    private async Task DoSpearfish()
    {
        await WaitWhile(() => !Util.IsAddonReady("SpearFishing"), "FishStart");

        // assuming autohook
        await WaitWhile(() => Svc.Condition[ConditionFlag.Unknown85], "FishFinish");
    }

    private unsafe uint GetQuantityNeeded()
    {
        var owned = Util.GetQuantityOwned(itemId);
        return (uint)Math.Max(0, quantity - owned);
    }
}
