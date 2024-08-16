using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.GeneratedSheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using xgather.UI;
using FFXIVGame = FFXIVClientStructs.FFXIV.Client.Game;

namespace xgather.Executors;

public abstract class ExecutorBase : IDisposable
{
    #region Properties
    // actual radius seems to be around 135y, but trying to use an exact value will make the code brittle
    protected const float UnloadRadius = 100f;

    public enum State : uint
    {
        Stopped = 0,
        Idle,
        Gathering,
        Mount,
        Dismount,
        Gearset,
        Teleport,
        Time,
        Weather
    }

    public State CurrentState { get; protected set; }
    public State WantedState { get; protected set; }
    // public GatherPointBase? CurrentNodeGroup { get; protected set; }

    protected DateTime Retry;
    public IWaypoint? Destination { get; protected set; }
    protected HashSet<uint> SkippedPoints = [];

    public bool IsActive => CurrentState != State.Stopped;

    public static bool PathfindInProgress => !IPCHelper.NavmeshIsReady() || IPCHelper.PathfindInProgress() || IPCHelper.PathIsRunning() || IPCHelper.PathfindQueued() > 0;
    #endregion

    public ExecutorBase()
    {
        Svc.Framework.Update += Tick;
        Svc.Condition.ConditionChange += ConditionChange;
    }

    public void Dispose()
    {
        Svc.Framework.Update -= Tick;
        Svc.Condition.ConditionChange -= ConditionChange;
        GC.SuppressFinalize(this);
    }

    #region Start/stop
    public void Start()
    {
        ResetState();
        CurrentState = State.Idle;
    }

    private bool IsRightTerritory()
    {
        var dest = Destination?.GetZone();
        if (dest == null)
            return true;

        if (dest == 901 && Svc.ClientState.TerritoryType == 939)
            return true;

        return dest == Svc.ClientState.TerritoryType;
    }

    private void ResetState()
    {
        StopMoving();
        CurrentState = State.Stopped;
        Retry = DateTime.MinValue;
        SkippedPoints.Clear();
    }

    public void Stop()
    {
        OnStop();
        ResetState();
    }

    public static void StopMoving()
    {
        IPCHelper.PathStop();
        IPCHelper.PathfindCancel();
    }

    public abstract IWaypoint? NextDestination();
    public abstract IEnumerable<uint> DesiredItems();
    public abstract ClassJob? DesiredClass();
    public abstract void OnStop();
    #endregion

    #region Wait
    protected void WaitFor(State type, TimeSpan retryTime)
    {
        CurrentState = type;
        Retry = DateTime.Now.Add(retryTime);
    }

    protected void WaitFor(State type)
    {
        CurrentState = type;
        Retry = DateTime.MaxValue;
    }

    protected bool WaitingFor(State type) => CurrentState == type && Retry > DateTime.Now;

    protected void Done(State type)
    {
        if (CurrentState == type)
        {
            CurrentState = State.Idle;
            Retry = default;
        }
    }
    #endregion

    public void ConditionChange(ConditionFlag flag, bool isActive)
    {
        if ((uint)flag == 85 && !isActive && CurrentState == State.Gathering)
            CurrentState = State.Idle;

        if (flag == ConditionFlag.Gathering && isActive)
        {
            CurrentState = State.Gathering;
            Destination = null;
            SkippedPoints.Clear();

            Svc.Gather.DesiredItems = DesiredItems().ToHashSet();

            var tar = Svc.TargetManager.Target;
            if (tar != null)
            {
                Svc.Config.RecordPosition(tar);
                Svc.Config.UpdateFloorPoint(tar, p => p ?? Svc.Player!.Position);
            }
        }
    }

    public void Tick(IFramework fw)
    {
        if (CurrentState is not State.Stopped and not State.Idle)
            Svc.Log.Verbose($"Waiting for {CurrentState} until {Retry}");

        switch (CurrentState)
        {
            // unreachable
            case State.Stopped:
                return;

            // route logic
            case State.Idle:
                break;

            // see GatheringHandler
            case State.Gathering:
                return;

            case State.Teleport:
                if (Svc.ClientState.TerritoryType == Destination?.GetZone() && !Svc.Condition[ConditionFlag.BetweenAreas51])
                    Done(State.Teleport);
                return;

            case State.Mount:
                if (Svc.Condition[ConditionFlag.Mounted])
                    Done(State.Mount);
                else if (Retry < DateTime.Now && !Svc.Condition[ConditionFlag.Unknown57] && !Svc.Condition[ConditionFlag.Casting])
                    Mount();
                return;

            case State.Dismount:
                if (!Svc.Condition[ConditionFlag.Mounted] && !Utils.PlayerIsFalling)
                    Done(State.Dismount);
                else if (Retry < DateTime.Now)
                    Dismount();
                return;

            case State.Gearset:
                var dc = DesiredClass();
                if (dc == null || dc.RowId == Svc.Player!.ClassJob.Id)
                    Done(State.Gearset);
                return;

            default:
                Alerts.Error($"Unrecognized state {CurrentState}");
                Stop();
                return;
        }

        if (ChangeGearset())
            return;

        if (!IsRightTerritory())
        {
            Teleport();
            return;
        }

        if (Destination == null)
        {
            GatherNext();
            return;
        }

        if (Destination is FloorPoint p && p.FloorPosition.DistanceFromPlayer() < 1 && !PathfindInProgress)
        {
            if (Svc.Condition[ConditionFlag.Mounted])
            {
                Dismount();
                return;
            }

            Destination = p.Node;
            return;
        }

        if (Destination is GatherPointKnown g && g.Position.DistanceFromPlayerXZ() < 1.5 && g.Position.DistanceFromPlayer() < 3)
        {
            StopMoving();
            if (Svc.Condition[ConditionFlag.Mounted])
            {
                Dismount();
                return;
            }

            InteractWithObject(Svc.ObjectTable.First(x => x.DataId == g.DataID && x.IsTargetable));
            return;
        }

        if (Destination is GatherPointSearch c)
        {
            var objIDs = c.DataIDs;
            if (objIDs.Count == 0)
                Stop();

            var candidatePoint = Svc.ObjectTable.FirstOrDefault(x => objIDs.Contains(x.DataId) && x.IsTargetable);
            if (candidatePoint != null)
            {
                Destination = CreateWaypointFromObject(candidatePoint);
                StopMoving();
                return;
            }
            else if (c.Center.DistanceFromPlayer() < 70)
            {
                foreach (var i in Svc.ObjectTable.Where(x => c.DataIDs.Contains(x.DataId)))
                    SkippedPoints.Add(i.DataId);
                GatherNext();
                return;
            }
        }

        if (!PathfindInProgress)
        {
            var diving = Svc.Condition[ConditionFlag.Diving];
            var shouldFly = Svc.Config.Fly;
            var pos = Destination.GetPosition();
            if (pos.DistanceFromPlayer() > 20 && !Svc.Condition[ConditionFlag.Mounted])
            {
                Mount();
                return;
            }
            if (shouldFly && pos.DistanceFromPlayer() > 20)
            {
                if (Destination.GetLandable() || diving)
                    MoveTo(pos, true);
                else
                {
                    var pos2 = IPCHelper.PointOnFloor(pos, false, 2);
                    MoveTo(pos2 ?? pos, Svc.Condition[ConditionFlag.InFlight]);
                }
            }
            else if (Destination is FloorPoint f && !diving)
                Destination = f.Node;
            else
                MoveTo(pos, diving);
        }
    }

    protected void GatherNext()
    {
        Destination = NextDestination();
        if (Destination == null)
        {
            Alerts.Error("No waypoints in range.");
            Stop();
        }
        else
            StopMoving();
    }

    private static unsafe void InteractWithObject(IGameObject obj)
    {
        TargetSystem.Instance()->OpenObjectInteraction((FFXIVGame.Object.GameObject*)obj.Address);
    }

    private unsafe void Dismount()
    {
        ActionManager.Instance()->UseAction(ActionType.GeneralAction, 23);
        WaitFor(State.Dismount, TimeSpan.FromMilliseconds(250));
    }

    private unsafe void Mount()
    {
        ActionManager.Instance()->UseAction(ActionType.GeneralAction, 24);
        WaitFor(State.Mount, TimeSpan.FromMilliseconds(1000));
    }

    private unsafe bool ChangeGearset()
    {
        var wantJob = DesiredClass();
        if (wantJob == null || wantJob.RowId == Svc.Player!.ClassJob.Id)
            return false;

        var needClass = wantJob.NameEnglish.ToString();
        var gearsetModule = RaptureGearsetModule.Instance();
        var gearsetId = gearsetModule->FindGearsetIdByName(Utf8String.FromString(needClass));
        if (gearsetId == -1)
        {
            Alerts.Info($"No gearset registered for {needClass}.");
            return false;
        }

        gearsetModule->EquipGearset(gearsetId);
        WaitFor(State.Gearset, TimeSpan.FromMilliseconds(1000));
        return true;
    }

    private unsafe void MoveTo(Vector3 destination, bool fly)
    {
        IPCHelper.PathfindAndMoveTo(destination, fly);
        if (fly)
            CurrentState = State.Mount;
    }

    private void Teleport()
    {
        if (Destination == null)
            return;

        var closest = Svc.Plugin.Aetherytes.MinBy(a => a.DistanceToPoint(Destination.GetZone(), Destination.GetPosition()));
        if (closest == null)
        {
            Alerts.Error("No aetheryte near destination.");
            Stop();
        }
        else
            IPCHelper.Teleport(closest.GameAetheryte.RowId);
        WaitFor(State.Teleport, TimeSpan.FromSeconds(6));
    }

    public static IWaypoint CreateWaypointFromObject(IGameObject g)
    {
        var actualObj = new GatherPointKnown()
        {
            Position = g.Position,
            DataID = g.DataId,
            Zone = Svc.ClientState.TerritoryType
        };

        if (Svc.Config.GetFloorPoint(g, out var point))
            return new FloorPoint() { FloorPosition = point, Node = actualObj };

        return actualObj;
    }

    public static IEnumerable<IGameObject> FindGatherPoints(Func<IGameObject, bool>? filter = null) =>
        Svc.ObjectTable.Where(
            x =>
                x.ObjectKind is Dalamud.Game.ClientState.Objects.Enums.ObjectKind.GatheringPoint
                && (filter == null || filter(x))
        );

    public static GatherPointSearch? SearchForUnloadedPoint(IEnumerable<GatherPoint> allPoints, ICollection<uint> ids, uint zone)
    {
        if (!allPoints.Any())
            return null;

        var closestGroup = allPoints.GroupBy(x => x.DataId).MinBy(points => points.Min(obj => obj.DistanceFromPlayer));
        var furthestNodeInGroup = closestGroup!.MaxBy(obj => obj.DistanceFromPlayer);

        return new GatherPointSearch()
        {
            Center = furthestNodeInGroup.NaviPosition,
            DataIDs = [.. ids],
            Landable = furthestNodeInGroup.GatherLocation != null,
            Zone = zone
        };
    }
}

public interface IWaypoint
{
    public uint GetZone();
    public Vector3 GetPosition();
    public bool GetLandable();
}

public class GatherPointSearch : IWaypoint
{
    public required uint Zone;
    public Vector3 Center;
    public required List<uint> DataIDs;
    public required bool Landable;

    public uint GetZone() => Zone;
    public Vector3 GetPosition() => Center;
    public bool GetLandable() => Landable;

    public override string ToString() => $"Search({Utils.ShowV3(Center)}, {string.Join(", ", DataIDs.Select(d => $"0x{d:X}"))})";
}

public class GatherPointKnown : IWaypoint
{
    public required uint Zone;
    public Vector3 Position;
    public uint DataID;

    public uint GetZone() => Zone;
    public Vector3 GetPosition() => Position;
    public bool GetLandable() => false;

    public override string ToString() => $"0x{DataID:X} @ {Utils.ShowV3(Position)}";
}

public class FloorPoint : IWaypoint
{
    public Vector3 FloorPosition;
    public required GatherPointKnown Node;

    public uint GetZone() => Node.GetZone();
    public Vector3 GetPosition() => FloorPosition;
    public bool GetLandable() => true;

    public override string ToString() => $"Floor({Node}, {Utils.ShowV3(FloorPosition)})";
}
