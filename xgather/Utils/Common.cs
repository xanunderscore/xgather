using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using xgather.GameData;

namespace xgather.Utils;

public enum GatheringAction
{
    None,
    Gather,
    Collect,
    Scour,
    Brazen,
    Meticulous,
    Scrutiny,
    BountifulYield,
    Boon1,
    Boon2,
    BoonYield,
    Yield1,
    Yield2,
    Restore,
    RestoreCombo,
}

public static class ActionExtensions
{
    public static int GetActionID(this GatheringAction act, uint rowId)
    {
        if (rowId is not (16 or 17 or 18))
            throw new InvalidOperationException($"Called GetActionID({act}) with invalid class ID {rowId}");

        var isMiner = rowId == 16;

        return act switch
        {
            GatheringAction.Collect => isMiner ? 240 : 815,
            GatheringAction.Scour => isMiner ? 22182 : 22186,
            GatheringAction.Brazen => isMiner ? 22183 : 22187,
            GatheringAction.Meticulous => isMiner ? 22184 : 22188,
            GatheringAction.Scrutiny => isMiner ? 22185 : 22189,
            GatheringAction.BountifulYield => isMiner ? 272 : 273,
            GatheringAction.Boon1 => isMiner ? 21177 : 21178,
            GatheringAction.Boon2 => isMiner ? 25589 : 25590,
            GatheringAction.BoonYield => isMiner ? 21203 : 21204,
            GatheringAction.Yield1 => isMiner ? 239 : 222,
            GatheringAction.Yield2 => isMiner ? 241 : 224,
            GatheringAction.Restore => isMiner ? 232 : 215,
            GatheringAction.RestoreCombo => isMiner ? 26521 : 26522,
            GatheringAction.Gather => -1,
            _ => 0
        };
    }

    public static int GetActionID(this GatheringAction act) => GetActionID(act, Svc.PlayerState.ClassJob.RowId);
}

public enum StatusID : uint
{
    None = 0,
    LayOfTheLand = 234,
    ArborCall = 233,
    Scrutiny = 757,
    CollectorsGlove = 805,
    SharkEye = 1167,
    FATEParticipant = 2577,
    EurekaMoment = 2765,
}

internal static unsafe partial class Util
{
    private static readonly delegate* unmanaged<EventFramework*, uint> _getActiveGatheringEventHandlerId = (delegate* unmanaged<EventFramework*, uint>)Svc.SigScanner.ScanText("E8 ?? ?? ?? ?? 39 43 20");

    public static unsafe T ReadField<T>(void* addr, int off) where T : unmanaged => *(T*)((nint)addr + off);

    public static bool IsGatherer => Svc.Player?.ClassJob.RowId is 16 or 17 or 18;

    internal static string ShowV3(Vector3 vec) => $"[{vec.X:F2}, {vec.Y:F2}, {vec.Z:F2}]";

    internal static Vector3 Convert(Lumina.Data.Parsing.Common.Vector3 v3) => new(v3.X, v3.Y, v3.Z);

    public static uint GetMinCollectability(uint itemId)
    {
        foreach (var group in Svc.SubrowExcelSheet<Lumina.Excel.Sheets.CollectablesShopItem>())
            foreach (var item in group)
                if (item.Item.RowId == itemId)
                    return item.CollectablesShopRefine.Value.LowCollectability;

        return 0;
    }

    public static bool PlayerIsFalling => ((Character*)Player())->IsJumping();

    public static (DateTime Start, DateTime End)? GetNextAvailable(GatheringPointBase b) => GetNextAvailable(b.Nodes[0]);
    public static (DateTime Start, DateTime End)? GetNextAvailable(uint gatheringNodeId)
    {
        if (Svc.ExcelRowMaybe<Lumina.Excel.Sheets.GatheringPointTransient>(gatheringNodeId) is not { } gpt)
            return null;

        if (gpt.EphemeralStartTime != 65535 && gpt.EphemeralEndTime != 65535)
            return CalcAvailability(gpt.EphemeralStartTime, gpt.EphemeralEndTime);

        if (gpt.GatheringRarePopTimeTable.Value is Lumina.Excel.Sheets.GatheringRarePopTimeTable gptt && gptt.RowId > 0)
            return CalcAvailability(gptt).MinBy(x => x.Start);

        return null;
    }

    public static IEnumerable<(DateTime Start, DateTime End)> CalcAvailability(Lumina.Excel.Sheets.GatheringRarePopTimeTable obj)
    {
        foreach (var (start, dur) in obj.StartTime.Zip(obj.Duration))
        {
            if (start == 65535)
                yield return (DateTime.MaxValue, DateTime.MaxValue);

            yield return CalcAvailability(start, start + dur);
        }
    }

    public static (DateTime Start, DateTime End) CalcAvailability(int EorzeaMinStart, int EorzeaMinEnd)
    {
        var currentTime = Timestamp.Now;
        var (startHr, startMin) = (EorzeaMinStart / 100, EorzeaMinStart % 100);
        var (endHr, endMin) = (EorzeaMinEnd / 100, EorzeaMinEnd % 100);

        var realStartMin = startHr * 60 + startMin;
        var realEndMin = endHr * 60 + endMin;

        if (realEndMin < realStartMin)
            realEndMin += Timestamp.MinPerDay;

        var realStartSec = realStartMin * 60;
        var realEndSec = realEndMin * 60;

        var curSec = currentTime.CurrentEorzeaSecondOfDay;

        if (curSec >= realEndSec)
            realStartSec += Timestamp.SecPerDay;

        var secondsToWait = realStartSec - curSec;
        var ts = currentTime.AddEorzeaSeconds(secondsToWait);
        var tsend = ts.AddEorzeaMinutes(realEndMin - realStartMin);
        return (ts.AsDateTime, tsend.AsDateTime);
    }

    public static unsafe bool PlayerIsBusy()
        => Svc.Condition.Any(ConditionFlag.BetweenAreas, ConditionFlag.Casting, ConditionFlag.InCombat)
        || ActionManager.Instance()->AnimationLock > 0
        || (Player() is var p && p != null && !p->GetIsTargetable());

    public static bool PlayerIsZiplining => Svc.Condition[ConditionFlag.Unknown101];

    public static Lumina.Excel.Sheets.Item Item(uint itemId) => Svc.ExcelRow<Lumina.Excel.Sheets.Item>(itemId);
    public static string ItemName(uint itemId) => Item(itemId).Name.ToString();

    public static GameObject* Player() => GameObjectManager.Instance()->Objects.IndexSorted[0].Value;
    public static Vector3 PlayerPosition() => Player() is var p && p != null ? p->Position : default;

    public static bool PlayerInRange(Vector3 dest, float dist)
    {
        var d = dest - (Vector3)Player()->Position;
        return d.LengthSquared() <= dist * dist;
    }

    public static unsafe bool UseAction(ActionType actionType, uint actionId, ulong targetId = 0xE0000000) => ActionManager.Instance()->UseAction(actionType, actionId, targetId);

    public static unsafe bool UseAction(uint id, ulong targetId = 0xE0000000) => UseAction(ActionType.Action, id, targetId);

    public static unsafe bool CanUseAction(uint id) => ActionManager.Instance()->GetActionStatus(ActionType.Action, id) == 0;

    public static unsafe uint GetActionStatus(ActionType actionType, uint actionId) => ActionManager.Instance()->GetActionStatus(actionType, actionId);

    public static unsafe void InteractWithObject(IGameObject obj) => TargetSystem.Instance()->OpenObjectInteraction((GameObject*)obj.Address);

    public static unsafe AtkUnitBase* GetAddonByName(string name) => RaptureAtkUnitManager.Instance()->GetAddonByName(name);

    public static uint GetLayoutId(IGameObject obj) => ((GameObject*)obj.Address)->LayoutId;
    public static bool IsTreasureOpen(IGameObject obj) => ((Treasure*)obj.Address)->Flags.HasFlag(Treasure.TreasureFlags.Opened);

    public static unsafe bool IsAddonReady(string name)
    {
        var sp = GetAddonByName(name);
        return sp != null && sp->IsVisible && sp->IsReady;
    }

    public static unsafe AddonGathering* GetAddonGathering() => (AddonGathering*)GetAddonByName("Gathering");

    public static unsafe GatheringPointEventHandler* GetGatheringEventHandler()
    {
        var fwk = EventFramework.Instance();
        return (GatheringPointEventHandler*)fwk->GetEventHandlerById(_getActiveGatheringEventHandlerId(fwk));
    }

    public static unsafe bool IsGatheringAddonReady()
    {
        var gat = GetAddonGathering();
        // condition flag is briefly set to true when we interact with the node, then goes back to false
        // if we try to initialize the GatheringMasterpiece addon during this time, nothing happens (the callback is fired into the void)
        return gat != null && gat->IsVisible && gat->IsReady && gat->GatherStatus == 1 && !Svc.Condition[ConditionFlag.ExecutingGatheringAction];
    }

    public static unsafe int GatheringIntegrityLeft()
    {
        var gat = GetAddonGathering();
        if (gat == null || gat->IntegrityGaugeBar == null || !gat->IsVisible || !gat->IsReady)
            return 0;

        return gat->IntegrityGaugeBar->Values[0].ValueInt;
    }

    public static unsafe void GatheringSelectItem(uint itemId)
    {
        var gat = GetAddonGathering();
        if (gat == null)
            throw new Exception("Addon is null");

        var items = gat->ItemIds.ToArray();
        var index = Array.IndexOf(items, itemId);
        if (index < 0)
            throw new Exception($"{itemId} not found at gathering point");

        gat->GatheredItemComponentCheckbox[index].Value->AtkComponentButton.IsChecked = true;
        gat->FireCallbackInt(index);
    }

    public static unsafe void GatheringSelectFirst()
    {
        var gat = GetAddonGathering();
        if (gat == null)
            throw new Exception("Addon is null");

        var items = gat->ItemIds.ToArray();
        var index = Array.FindIndex(items, i => i > 0);
        if (index < 0)
            throw new Exception("No items found at gathering point");

        gat->GatheredItemComponentCheckbox[index].Value->AtkComponentButton.IsChecked = true;
        gat->FireCallbackInt(index);
    }

    public static unsafe bool PlayerHasStatus(int statusId)
    {
        foreach (var stat in ((Character*)Player())->GetStatusManager()->Status)
        {
            if (stat.StatusId == statusId)
                return true;
        }

        return false;
    }

    public static unsafe bool PlayerHasStatus(StatusID id) => PlayerHasStatus((int)id);

    public const float Cos120 = -0.5f;
    public const float Sin120 = 0.8660254f;

    public static Vector2 Rotate120Degrees(Vector2 input) => new((input.X * Cos120) - (input.Y * Sin120), (input.X * Sin120) + (input.Y * Cos120));

    public static int GetQuantityOwned(uint itemId) => InventoryManager.Instance()->GetInventoryItemCount(itemId, minCollectability: (short)GetMinCollectability(itemId));
}

internal static class VectorExt
{
    public static float DistanceFromPlayer(this Vector3 vec) =>
        Svc.Player == null ? float.MaxValue : (vec - Svc.Player.Position).Length();

    public static float DistanceFromPlayerXZ(this Vector3 vec) =>
        Svc.Player == null ? float.MaxValue : (vec.XZ() - Svc.Player.Position.XZ()).Length();

    public static Vector2 XZ(this Vector3 vec) => new(vec.X, vec.Z);

    public static Vector3 WithY(this Vector2 xz, float y) => new(xz.X, y, xz.Y);
}

internal static class GPBaseExt
{
    public static GatherClass GetRequiredClass(this Lumina.Excel.Sheets.GatheringPointBase gpBase) =>
        gpBase.GatheringType.RowId switch
        {
            0 or 1 => GatherClass.MIN,
            2 or 3 => GatherClass.BTN,
            4 or 5 => GatherClass.FSH,
            _ => GatherClass.None
        };
}

internal readonly record struct OnDispose(Action a) : IDisposable
{
    public void Dispose() => a();
}
