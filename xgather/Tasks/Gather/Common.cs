using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System;
using System.Numerics;
using System.Threading.Tasks;
using xgather.Utils;

namespace xgather.Tasks.Gather;

public abstract class GatherBase : AutoTask
{
    protected async Task<Vector2> Survey()
    {
        using var _ = BeginScope("Survey");
        var (actionId, statusId) = Svc.Player?.ClassJob.RowId switch
        {
            16 => (228, 234),
            17 => (211, 233),
            18 => (7904, 1167),
            _ => (0, 0)
        };

        ErrorIf(actionId == 0, "Current job has no survey action");
        ErrorIf(!Util.UseAction((uint)actionId), "Unable to use survey action");

        await WaitWhile(() => !Util.PlayerHasStatus(statusId), "Survey");

        unsafe
        {
            var map = AgentMap.Instance();
            // i think this is only for temporary markers?
            var mk = map->MiniMapGatheringMarkers[0];
            ErrorIf(mk.MapMarker.IconId == 0, "No valid map marker found");
            return new Vector2(mk.MapMarker.X, mk.MapMarker.Y) / 16f * map->CurrentMapSizeFactorFloat;
        }
    }

    protected async Task DoNormalGather(Func<uint?> getItem)
    {
        await WaitWhile(() => !Util.IsGatheringAddonReady(), "GatherStart");

        while (Svc.Condition[ConditionFlag.Gathering])
        {
            if (Util.GatheringIntegrityLeft() == 0)
            {
                // node ran out, wait for addon to disappear or revisit to proc
                await NextFrame(10);
                continue;
            }
            var itemId = getItem();
            if (itemId == null)
                Util.GatheringSelectFirst();
            else
                Util.GatheringSelectItem(itemId.Value);
            await WaitWhile(() => Svc.Condition[ConditionFlag.ExecutingGatheringAction], "GatherItemFinish");
        }
    }

    protected async Task DoNormalGather(uint itemId) => await DoNormalGather(() => itemId);

    protected record struct GatheringMasterpiece
    (
        uint ItemId,
        uint QualityCur,
        uint QualityMax,
        uint IntegrityCur,
        uint IntegrityMax,

        uint Breakpoint1,
        uint Breakpoint2,
        uint Breakpoint3,

        uint ScourProgress,
        (uint Min, uint Max) BrazenProgress,
        uint MeticulousProgress
    );

    protected async Task DoCollectableGather(uint itemId) => await DoCollectableGather(() => itemId);

    protected async Task DoCollectableGather(Func<uint> getItem)
    {
        await WaitWhile(() => !Util.IsGatheringAddonReady(), "GatherStart");

        var iters = 0;
        while (Svc.Condition[ConditionFlag.Gathering])
        {
            ErrorIf(iters++ > 100000, "too many iterations");
            if (Util.GatheringIntegrityLeft() == 0)
            {
                // node ran out, wait for addon to disappear or revisit to proc
                await NextFrame(10);
                continue;
            }

            Util.GatheringSelectItem(getItem());

            await WaitAddon("GatheringMasterpiece");

            while (true)
            {
                ErrorIf(iters++ > 100000, "too many iterations");
                var act = GetNextAction();
                if (act == default)
                    break;

                var aid = act.GetActionID();
                ErrorIf(aid < 0, "Solver returned regular Gather action, but we can't use that here");

                await UseGatheringAction((uint)aid);
            }
        }
    }

    private GatheringAction GetNextAction()
    {
        var status = GetCollectableStatus();

        if (status.IntegrityCur == 0)
            return GatheringAction.None;

        var gp = Svc.Player?.CurrentGp ?? 0;

        // if max quality, start collecting (TODO: we actually want maximum collectability for some items even though the third breakpoint is lower than 1000)
        if (status.QualityCur >= status.Breakpoint3)
        {
            if (status.IntegrityCur < status.IntegrityMax)
            {
                if (Util.PlayerHasStatus(StatusID.EurekaMoment))
                    return GatheringAction.RestoreCombo;

                if (gp >= 300)
                    return GatheringAction.Restore;
            }

            return GatheringAction.Collect;
        }

        var qualityWanted = status.Breakpoint3 - status.QualityCur;

        if (status.MeticulousProgress < qualityWanted && !Util.PlayerHasStatus(StatusID.Scrutiny) && gp >= 200)
            return GatheringAction.Scrutiny;

        return GatheringAction.Meticulous;
    }

    private async Task UseGatheringAction(uint id)
    {
        Util.UseAction(id, Svc.Player?.TargetObjectId ?? 0xE0000000);
        await WaitWhile(() => !Svc.Condition[ConditionFlag.ExecutingGatheringAction], "ActionStart");
        await WaitWhile(() =>
        {
            if (!Svc.Condition[ConditionFlag.Gathering])
                return false;

            if (Svc.Condition[ConditionFlag.ExecutingGatheringAction])
                return true;

            unsafe
            {
                var ev = Util.GetGatheringEventHandler();
                return ev == null || ev->Scene != 2;
            }
        }, "ActionFinish");
    }

    protected GatheringMasterpiece GetCollectableStatus()
    {
        unsafe
        {
            var gm = (AddonGatheringMasterpiece*)Util.GetAddonByName("GatheringMasterpiece");

            return new(
                gm->AtkValues[2].UInt,
                gm->AtkValues[13].UInt,
                gm->AtkValues[14].UInt,
                gm->AtkValues[62].UInt,
                gm->AtkValues[63].UInt,

                gm->AtkValues[65].UInt,
                gm->AtkValues[66].UInt,
                gm->AtkValues[67].UInt,

                gm->AtkValues[48].UInt,
                (gm->AtkValues[49].UInt, gm->AtkValues[50].UInt),
                gm->AtkValues[51].UInt
            );
        }
    }
}
