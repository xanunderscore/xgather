using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using xgather.UI;

namespace xgather.Executors;

public abstract class Planner : IDisposable
{
    public abstract IWaypoint? NextDestination(ICollection<uint> pointsToSkip);
    public abstract ICollection<uint> DesiredItems();
    public abstract ClassJob? DesiredClass();
    public abstract void Debug();

    public virtual void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    public delegate void SuccessHandler(object sender, string message);
    public event SuccessHandler OnSuccess = delegate { };
    public void ReportSuccess(string message = "All done!") => OnSuccess.Invoke(this, message);

    public delegate void ErrorHandler(object sender, string message);
    public event ErrorHandler OnError = delegate { };
    public void ReportError(string message) => OnError.Invoke(this, message);
}

public sealed class GatherExecutor : IDisposable
{
    #region Properties
    // actual radius seems to be around 135y, but trying to use an exact value will make the code brittle
    public const float UnloadRadius = 100f;

    public enum State : uint
    {
        Stopped = 0,
        Paused,
        Idle,
        Gathering,
        Mount,
        Dismount,
        Gearset,
        Teleport,
        Time,
        Weather
    }

    public Planner Planner { get; private set; }
    public AutoGather AutoGather { get; private set; }

    public State CurrentState { get; private set; }

    private DateTime Retry;
    public IWaypoint? Destination { get; private set; }
    private readonly HashSet<uint> SkippedPoints = [];

    public static bool PathfindInProgress => !IPCHelper.NavmeshIsReady() || IPCHelper.PathfindInProgress() || IPCHelper.PathIsRunning() || IPCHelper.PathfindQueued() > 0;

    public delegate void RouteStopHandler(object sender);
    public event RouteStopHandler OnRouteStopped = delegate { };
    #endregion

    public GatherExecutor(Planner child)
    {
        AutoGather = new();
        Planner = child;
        Svc.Framework.Update += Tick;
        Svc.Condition.ConditionChange += ConditionChange;

        AutoGather.OnError += (obj, msg) =>
        {
            Alerts.Error(msg);
            Stop();
        };
        Planner.OnSuccess += (obj, msg) =>
        {
            Alerts.Success(msg);
            Stop();
        };
        Planner.OnError += (obj, msg) =>
        {
            Alerts.Error(msg);
            Stop();
        };
    }

    public void Dispose()
    {
        AutoGather.Dispose();
        Planner.Dispose();
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
        ResetState();
        OnRouteStopped.Invoke(this);
    }

    public void Pause()
    {
        CurrentState = State.Paused;
        StopMoving();
    }

    public static void StopMoving()
    {
        IPCHelper.PathStop();
        IPCHelper.PathfindCancel();
    }
    #endregion

    #region Wait
    private void WaitFor(State type, TimeSpan retryTime)
    {
        CurrentState = type;
        Retry = DateTime.Now.Add(retryTime);
    }

    private void WaitFor(State type)
    {
        CurrentState = type;
        Retry = DateTime.MaxValue;
    }

    private bool WaitingFor(State type) => CurrentState == type && Retry > DateTime.Now;

    private void Done(State type)
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
        {
            AutoGather.DesiredItems.Clear();
            CurrentState = State.Idle;
        }

        if (flag == ConditionFlag.Gathering && isActive)
        {
            CurrentState = State.Gathering;
            Destination = null;
            SkippedPoints.Clear();
            AutoGather.DesiredItems = Planner.DesiredItems();

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
        AutoGather.Paused = CurrentState is State.Stopped or State.Paused;

        switch (CurrentState)
        {
            case State.Stopped:
            case State.Paused:
            case State.Gathering:
                return;

            // route logic
            case State.Idle:
                break;

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
                var dc = Planner.DesiredClass();
                if (dc == null || dc.Value.RowId == Svc.Player!.ClassJob.RowId)
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
        else if (Destination.GetNextAvailable().Start > DateTime.Now)
            return;

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

    private void GatherNext()
    {
        Destination = Planner.NextDestination(SkippedPoints);
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
        TargetSystem.Instance()->OpenObjectInteraction((GameObject*)obj.Address);
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
        var wantJob = Planner.DesiredClass();
        if (wantJob == null || wantJob.Value.RowId == Svc.Player!.ClassJob.RowId)
            return false;

        var needClass = wantJob.Value.NameEnglish.ToString();
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
            Zone = Svc.ClientState.TerritoryType,
            Available = Utils.GetNextAvailable(g.DataId),
        };

        Svc.Log.Debug($"Created waypoint from object {g}");

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
            Zone = zone,
            Available = Utils.GetNextAvailable(ids.First())
        };
    }
}

public interface IWaypoint
{
    public uint GetZone();
    public Vector3 GetPosition();
    public bool GetLandable();
    public (DateTime Start, DateTime End) GetNextAvailable();
}

public class GatherPointSearch : IWaypoint
{
    public required (DateTime Start, DateTime End) Available;
    public required uint Zone;
    public Vector3 Center;
    public required List<uint> DataIDs;
    public required bool Landable;

    public uint GetZone() => Zone;
    public Vector3 GetPosition() => Center;
    public bool GetLandable() => Landable;
    public (DateTime Start, DateTime End) GetNextAvailable() => Available;

    public override string ToString() => $"Search({Utils.ShowV3(Center)}, {string.Join(", ", DataIDs.Select(d => $"0x{d:X}"))})";
}

public class GatherPointKnown : IWaypoint
{
    public required (DateTime Start, DateTime End) Available;
    public required uint Zone;
    public Vector3 Position;
    public uint DataID;

    public uint GetZone() => Zone;
    public Vector3 GetPosition() => Position;
    public bool GetLandable() => false;
    public (DateTime Start, DateTime End) GetNextAvailable() => Available;

    public override string ToString() => $"0x{DataID:X} @ {Utils.ShowV3(Position)}";
}

public class FloorPoint : IWaypoint
{
    public Vector3 FloorPosition;
    public required GatherPointKnown Node;

    public uint GetZone() => Node.GetZone();
    public Vector3 GetPosition() => FloorPosition;
    public bool GetLandable() => true;
    public (DateTime Start, DateTime End) GetNextAvailable() => Node.GetNextAvailable();

    public override string ToString() => $"Floor({Node}, {Utils.ShowV3(FloorPosition)})";
}
