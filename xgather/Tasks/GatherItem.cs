using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace xgather.Tasks;

public class GatherItem : AutoTask
{
    private Vector3? _lastPoint;
    private readonly uint itemId;
    private readonly uint quantity;

    private readonly bool IsCollectable;
    private readonly bool IsFishing;
    private bool IsNormalCollectable => IsCollectable && !IsFishing;

    public GatherItem(uint itemId, uint quantity)
    {
        this.itemId = itemId;
        this.quantity = quantity;

        var it = Utils.Item(itemId);
        IsFishing = it.ItemUICategory.RowId == 47; // 47 = seafood
        IsCollectable = it.IsCollectable;
    }

    protected override async Task Execute()
    {
        var needed = GetQuantityNeeded();
        Log($"Gathering {needed}x {Utils.ItemName(itemId)}");

        if (needed == 0)
            return;

        if (IsCollectable && IsFishing && !Utils.PlayerHasStatus(805))
            ErrorIf(!Utils.UseAction(ActionType.Action, 4101), "Unable to use Collector's Glove");

        var route = Svc.Config.GetGatherPointGroupsForItem(itemId).FirstOrDefault();
        ErrorIf(route == null, $"No route found for item {itemId}");

        var iters = 0;
        while (GetQuantityNeeded() > 0)
        {
            ErrorIf(++iters > 20, "loop");
            await GatherNext(route);
        }
    }

    private async Task GatherNext(GatherPointBase gpBase)
    {
        await TeleportToZone(gpBase.Zone, gpBase.Location);
        await ChangeClass(gpBase.Class);

        var point = await FindPoint(gpBase);
        await MoveTo(point.Position, 3.5f, mount: point.Position.DistanceFromPlayer() > 20, fly: Svc.Condition[ConditionFlag.InFlight] | Svc.Condition[ConditionFlag.Diving], dismount: Svc.Condition[ConditionFlag.Mounted]);

        await DoGather(point);
    }

    private async Task<IGameObject> FindPoint(GatherPointBase gpBase)
    {
        using var scope = BeginScope("FindPoint");

        IGameObject? find() =>
            Svc.ObjectTable.Where(obj => obj.ObjectKind is ObjectKind.GatheringPoint && obj.IsTargetable && gpBase.Nodes.Contains(obj.DataId)).MinBy(obj => obj.Position.DistanceFromPlayerXZ());

        if (find() is { } nearby)
            return nearby;

        Vector3 guess;
        if (Svc.Condition[ConditionFlag.Diving])
            guess = gpBase.Location with { Y = Svc.Player!.Position.Y };
        else
            guess = await PointOnFloor(gpBase.Location with { Y = 1024 }, false, 10);

        await MoveTo(guess, 10, true, true, interrupt: () => find() != null);

        if (find() is { } n)
            return n;

        var surveyPoint = await Survey();
        ErrorIf(!gpBase.ContainsPoint(surveyPoint), "Gathering marker is for a different node");

        var surveyFloor = await PointOnFloor(new Vector3(surveyPoint.X, 1024, surveyPoint.Y), false, 10);
        await MoveTo(surveyFloor, 10, true, true, interrupt: () => find() != null);

        if (find() is { } s)
            return s;

        Error("Unable to find a gather point");
        return null!;
    }

    private async Task<Vector2> Survey()
    {
        Status = "Searching for point";

        var (actionId, statusId) = Svc.Player?.ClassJob.RowId switch
        {
            16 => (228, 234), // lay of the land
            17 => (211, 233), // arbor call
            18 => (7904, 1167), // shark eye
            _ => (0, 0)
        };

        ErrorIf(actionId == 0, "Current job has no survey action");
        ErrorIf(!Utils.UseAction(ActionType.Action, (uint)actionId), "Unable to use survey action");

        await WaitWhile(() => !Utils.PlayerHasStatus(statusId), "Survey");

        unsafe
        {
            var map = AgentMap.Instance();
            // i think this is only for temporary markers?
            var mk = map->MiniMapGatheringMarkers[0];
            ErrorIf(mk.MapMarker.IconId == 0, "No valid map marker found");
            return new Vector2(mk.MapMarker.X, mk.MapMarker.Y) / 16f * map->CurrentMapSizeFactorFloat;
        }
    }

    private async Task DoGather(IGameObject obj)
    {
        using var scope = BeginScope("GatherAtPoint");

        Status = $"Gathering {itemId} at {obj.Position}";

        _lastPoint = obj.Position;
        Utils.InteractWithObject(obj);

        if (IsFishing)
            await DoSpearfish();
        else if (IsCollectable)
            await DoCollectableGather();
        else
            await DoNormalGather();
    }

    private async Task DoNormalGather()
    {
        await WaitWhile(() => !Utils.GatheringAddonReady(), "GatherStart");

        while (Utils.GatheringIntegrityLeft() > 0)
        {
            Utils.GatheringSelectItem(itemId);
            await WaitWhile(() => Svc.Condition[ConditionFlag.Gathering42], "GatherItemFinish");
        }
    }

    private async Task DoCollectableGather()
    {
        await WaitWhile(() => !Utils.GatheringAddonReady(), "GatherStart");
        Utils.GatheringSelectItem(itemId);

        await WaitWhile(() => !Utils.AddonReady("GatheringMasterpiece"), "GatherStart");

        Error("Collectables gathering isn't implemented!");
    }

    private async Task DoSpearfish()
    {
        await WaitWhile(() => !Utils.AddonReady("SpearFishing"), "FishStart");
    }

    private unsafe uint GetQuantityNeeded()
    {
        var owned = (uint)InventoryManager.Instance()->GetInventoryItemCount(itemId, minCollectability: (short)Utils.GetMinCollectability(itemId));
        return owned >= quantity ? 0 : quantity - owned;
    }
}
