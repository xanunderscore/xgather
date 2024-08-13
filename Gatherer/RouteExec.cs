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
using FFXIVGame = FFXIVClientStructs.FFXIV.Client.Game;
using GatherPointId = uint;

namespace xgather;

public sealed class RouteExec : IDisposable
{
    // actual radius seems to be around 135y, but trying to use an exact value will make the code brittle
    private static readonly float UnloadRadius = 100f;

    internal GatherRoute? _currentRoute;
    internal IWaypoint? _destination;
    internal int _loopIndex = 0;
    internal HashSet<GatherPointId> _skippedPoints = [];
    internal bool _isMoving = false;
    internal bool _disableFly = false;
    internal bool _diademMode = false;
    internal int _lastDiademWeather = 0;
    internal int _thisDiademWeather = 0;
    internal bool _wantDiadem = false;
    internal GatherClass _wantClass = GatherClass.None;
    internal DateTime _retry = DateTime.MinValue;

    internal static unsafe bool PlayerIsFalling
    {
        get
        {
            var p = Svc.ClientState.LocalPlayer;
            if (p == null)
                return true;

            // 0 if grounded
            // 1 = "jumpsquat"
            // 3 = going up
            // 4 = stopped
            // 5 = going down
            var isJumping = *(byte*)(p.Address + 736) > 0;
            // 1 iff dismounting and haven't hit the ground yet
            var isAirDismount = **(byte**)(p.Address + 1432) == 1;

            return isJumping || isAirDismount;
        }
    }

    public record struct Wait(State Type, DateTime Retry);

    public enum State : uint
    {
        Stopped = 0,
        Running,
        Gathering,
        Mount,
        Dismount,
        Gearset,
        Teleport,
        Time,
        Weather
    }

    public State CurrentState { get; private set; }

    public bool IsActive => CurrentState != State.Stopped;

    public void Init()
    {
        Svc.Framework.Update += OnRouteTick;
        Svc.Condition.ConditionChange += OnConditionChange;
    }

    public void Start(GatherRoute r) => Start(r, 0);

    public void Start(GatherRoute r, int loopIndex)
    {
        Stop();

        if (IsRightTerritory(r))
            CurrentState = State.Running;
        else
        {
            CurrentState = State.Teleport;

            // idyllshire -> dravanian hinterlands
            if (r.TerritoryType.RowId == 399 && Svc.ClientState.TerritoryType != 478)
            {
                IPCHelper.Teleport(75);
                return;
            }

            var closest = Svc.Plugin.Aetherytes.MinBy(a => a.DistanceToRoute(r));
            if (closest == null)
            {
                UiMessage.Error($"No aetheryte close to destination.");
                Stop();
                return;
            }
            else
                IPCHelper.Teleport(closest.GameAetheryte.RowId);
        }

        _wantClass = r.Class;
        _loopIndex = loopIndex;
        _skippedPoints = [];
        _currentRoute = r;
    }

    public void NavigateToCenter(GatherRoute r)
    {
        if (r.GatherAreaCenter() is Vector3 v)
        {
            var maxY = r.Zone == 1192 ? 130 : 1024;
            var floor = IPCHelper.PointOnFloor(v with { Y = maxY }, Svc.Config.Fly, 5);
            IPCHelper.PathfindAndMoveTo(floor ?? v, Svc.Config.Fly);
        }
        else
            UiMessage.Error($"No center point recorded for route");
    }

    public static GatherRoute? CreateRouteFromObject(IGameObject? obj)
    {
        if (obj == null || obj.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.GatheringPoint)
            return null;

        var gpSheet = Svc.ExcelSheet<GatheringPoint>();
        var gp = gpSheet.GetRow(obj.DataId)!;
        var gbase = gp.GatheringPointBase.Value!;
        var points = gpSheet.Where(x => x.GatheringPointBase.Row == gp.GatheringPointBase.Row);

        var gatherableItems = new List<uint>();

        foreach (var it in gbase.Item.Where(x => x > 0))
        {
            var git = Svc.ExcelRow<GatheringItem>((uint)it);
            gatherableItems.Add((uint)git.Item);
        }

        var tt = Svc.Data.Excel.GetSheet<TerritoryType>()!.GetRow(Svc.ClientState.TerritoryType)!;

        var label = $"Level {gbase.GatheringLevel} {gbase.GatheringType.Value!.Name} @ {tt.PlaceName.Value!.Name}";

        var rte = new GatherRoute
        {
            Label = label,
            Nodes = points.Select(x => x.RowId).ToList(),
            Class = gbase.GetRequiredClass(),
            Items = [.. gatherableItems],
            GatheringPointBaseId = gbase.RowId,
            Zone = Svc.ClientState.TerritoryType
        };

        foreach (var nearbyPoint in Svc.ObjectTable.Where(x => rte.Contains(x.DataId)))
            Svc.Config.RecordPosition(nearbyPoint);

        return rte;
    }

    public static (uint, IEnumerable<IGameObject>)? CreateNodeFromObject(IGameObject? obj)
    {
        if (obj == null || obj.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.GatheringPoint)
            return null;

        return (obj.DataId, Svc.ObjectTable.Where(x => x.DataId == obj.DataId));
    }

    public void Stop()
    {
        StopMoving();
        _destination = null;
        _currentRoute = null;
        _loopIndex = 0;
        _skippedPoints = [];
        _retry = DateTime.MinValue;
        _disableFly = false;
        CurrentState = State.Stopped;
    }

    private static void StopMoving()
    {
        IPCHelper.PathStop();
        IPCHelper.PathfindCancel();
    }

    public void Dispose()
    {
        Svc.Framework.Update -= OnRouteTick;
        Svc.Condition.ConditionChange -= OnConditionChange;
    }

    private bool WaitingFor(State type) => CurrentState == type && _retry > DateTime.Now;

    private void WaitFor(State type, TimeSpan retryTime)
    {
        CurrentState = type;
        _retry = DateTime.Now.Add(retryTime);
    }

    private void WaitFor(State type)
    {
        CurrentState = type;
        _retry = DateTime.MaxValue;
    }

    private void Done(State type)
    {
        if (CurrentState == type)
        {
            CurrentState = State.Running;
            _retry = default;
        }
    }

    private unsafe void OnRouteTick(IFramework fw)
    {
        if (CurrentState == State.Stopped)
            return;
        else
            Svc.Log.Verbose($"waiting for {CurrentState} until {_retry}");

        var weatherman = WeatherManager.Instance();
        if (Svc.ClientState.TerritoryType is 939)
        {
            _thisDiademWeather = weatherman->WeatherId;

            if (_lastDiademWeather > 0 && _thisDiademWeather > 0 && _lastDiademWeather != _thisDiademWeather)
                OnConditionChange(ConditionFlag.OccupiedInCutSceneEvent, false);

            _lastDiademWeather = _thisDiademWeather;
        }
        else
        {
            _thisDiademWeather = 0;
            _lastDiademWeather = 0;
        }


        if (_wantDiadem)
        {
            if (!_diademMode)
                _wantDiadem = false;
            else
                EnterDiadem();
        }

        if (WaitingFor(State.Dismount) || CurrentState == State.Teleport || !ChangeGearset(_currentRoute!))
            return;

        if (_destination == null)
        {
            if (CurrentState == State.Running)
                GatherNext();
            return;
        }

        var isMounted = Svc.Condition[ConditionFlag.Mounted];

        if (_destination.Pos().DistanceFromPlayer() > 20 && !_disableFly && !Mount())
            return;

        if (_destination is FloorPoint p && p.FloorPosition.DistanceFromPlayer() < 1 && PathfindComplete)
        {
            if (!Dismount())
                return;

            _destination = p.Node;

            return;
        }

        if (_destination is GatherPointKnown g && g.Position.DistanceFromPlayerXZ() < 1.5 && g.Position.DistanceFromPlayer() < 3)
        {
            StopMoving();
            if (!Dismount())
                return;

            var pt = Svc.ObjectTable.First(x => x.DataId == g.DataID && x.IsTargetable);
            InteractWithObject(pt);

            return;
        }

        if (_destination is GatherPointSearch c)
        {
            var objIds = c.DataIDs;
            if (objIds.Count == 0)
                Stop();

            var candidatePoint = Svc.ObjectTable.FirstOrDefault(x => objIds.Contains(x.DataId) && x.IsTargetable);
            if (candidatePoint != null)
            {
                _destination = CreateWaypointFromObject(candidatePoint);
                Svc.Log.Debug($"search ended, found candidate point: {_destination}");
                StopMoving();
            }
            else if (c.Center.DistanceFromPlayer() < 70)
            {
                // gather nodes should be loaded now.
                // TODO this doesn't work correctly with legendary nodes; it gives up when the nearest node is untargetable even if a further one isn't
                foreach (var i in Svc.ObjectTable.Where(x => c.DataIDs.Contains(x.DataId)))
                    _skippedPoints.Add(i.DataId);
                _loopIndex += 1;
                if (_loopIndex >= _currentRoute!.Nodes.Count)
                    _loopIndex = 0;
                GatherNext();
            }
        }

        if (PathfindComplete)
        {
            var isDiving = Svc.Condition[ConditionFlag.Diving];

            var shouldFly = Svc.Config.Fly && !_disableFly;
            var pos = _destination.Pos();
            if (shouldFly && pos.DistanceFromPlayer() > 20)
            {
                if (_destination.KnownLandable() || isDiving)
                    IPCHelper.PathfindAndMoveTo(pos, true);
                else
                {
                    Svc.Log.Debug($"position: {pos}");
                    var pos2 = IPCHelper.PointOnFloor(pos, false, 2);
                    IPCHelper.PathfindAndMoveTo(pos2 ?? pos, /*true*/Svc.Condition[ConditionFlag.InFlight]);
                }
            }
            // ignore remembered FloorPoint if we aren't planning on fly pathfinding, just navigate directly to target
            else if (_destination is FloorPoint f && !isDiving)
                _destination = f.Node;
            else
                IPCHelper.PathfindAndMoveTo(pos, isDiving);
        }
    }

    private unsafe void OnConditionChange(ConditionFlag flag, bool flagIsActive)
    {
        // should add some other conditions here but idk which
        if (flag == ConditionFlag.WaitingForDutyFinder && flagIsActive)
            Stop();

        if (_diademMode)
        {
            // queued into diadem, stop trying to talk to aurvael
            if (flag == ConditionFlag.InDutyQueue && flagIsActive)
                _wantDiadem = false;

            // left diadem, try to talk to aurvael
            if (flag == ConditionFlag.BetweenAreas51 && !flagIsActive)
                _wantDiadem = Svc.ClientState.TerritoryType == 886;

            // loaded into diadem, start route
            if (flag == ConditionFlag.OccupiedInCutSceneEvent && !flagIsActive && Svc.ClientState.TerritoryType == 939)
                MaybeStartDiademRoute();
        }

        if (flag == ConditionFlag.Gathering && flagIsActive)
        {
            CurrentState = State.Gathering;
            _destination = null;
            _skippedPoints = [];

            var tar = Svc.TargetManager.Target;
            if (tar != null)
            {
                Svc.Config.RecordPosition(tar);
                Svc.Config.UpdateFloorPoint(tar, p => p ?? Svc.Player!.Position);
                Svc.Config.Save();
            }
        }

        if (flag == ConditionFlag.BetweenAreas && !flagIsActive && CurrentState == State.Teleport)
            CurrentState = State.Running;

        if (!IsActive)
            return;

        // finished gathering, go to next object
        // if we use the Gathering flag here instead of flag 85, the "next" node may not be targetable yet, so we waste time
        // pathfinding to one that's further away; 85 seems to be the last one that turns off though idk what it does
        if ((uint)flag == 85 && !flagIsActive && CurrentState == State.Gathering)
        {
            _disableFly = false;
            CurrentState = State.Running;
        }
    }

    private static unsafe void InteractWithObject(IGameObject obj)
    {
        TargetSystem.Instance()->OpenObjectInteraction((FFXIVGame.Object.GameObject*)obj.Address);
    }

    internal static void EnterDiadem()
    {
        var aurv = Svc.ObjectTable.FirstOrDefault(x => x.DataId is 0xFBE0E);
        if (aurv == null)
        {
            Svc.Log.Debug("Can't find Aurvael.");
            return;
        }

        if (aurv.Position.DistanceFromPlayer() > 7)
        {
            Svc.Log.Debug("Not within interact range of Aurvael, doing nothing.");
            return;
        }

        InteractWithObject(aurv);
    }

    private static Dictionary<byte, int> DiademRoutes = new()
    {
        [135] = 431,
        [136] = 432,
        [134] = 433,
        [133] = 430
    };

    internal unsafe void MaybeStartDiademRoute()
    {
        var wt = WeatherManager.Instance()->WeatherId;
        // it's snowy outside
        if (wt == 15)
            Svc.Log.Debug("it's snowing in the Diadem. Waiting...");

        if (DiademRoutes.TryGetValue(wt, out var rteId) && Svc.Config.TryGetRoute(rteId, out var diademRoute))
        {
            Svc.Config.SelectedRoute = rteId;
            Start(diademRoute);
        }
    }

    internal unsafe bool WeatherIsUmbral => WeatherManager.Instance()->WeatherId is >= 133 and <= 136;

    private unsafe void GatherNext()
    {
        var dest = FindNextDestination();
        if (dest != null)
        {
            StopMoving();
            _destination = dest;
            Svc.Log.Debug($"moving to next point: {_destination.Pos()}");
        }
        else if (_diademMode && WeatherIsUmbral)
        {
            Utils.Chat.Instance.Send("/pdfleave");
        }
        else
        {
            UiMessage.Error("No waypoints in range.");
            Stop();
        }
    }

    private IWaypoint? FindNextDestination()
    {
        if (_currentRoute == null)
            return null;

        if (_currentRoute.Nodes.All(x => !Svc.Config.GetKnownPoints(x).Any()) && _currentRoute.GatherAreaCenter() is Vector3 p && p.DistanceFromPlayer() > 100)
        {
            var fl = _currentRoute.Class == GatherClass.FSH
                ? p with { Y = -25 }
                : IPCHelper.PointOnFloor(p with { Y = PathfindMaxY }, true, 5);
            if (fl == null)
            {
                UiMessage.Error($"No point near {p}, find it manually");
                return null;
            }

            return new GatherPointSearch() { Center = fl.Value, DataIDs = _currentRoute.Nodes, Landable = true };
        }

        return _currentRoute.Ordered ? FindNextOrderedDestination() : FindNextClosestDestination();
    }

    private float PathfindMaxY => Svc.ClientState.TerritoryType == 1192 ? 135 : 1024;

    private IWaypoint? FindNextClosestDestination()
    {
        var closePoints = FindGatherPoints(obj => _currentRoute!.Contains(obj.DataId) && obj.IsTargetable && !_skippedPoints.Contains(obj.DataId));
        Svc.Log.Debug($"closePoints: {string.Join(", ", closePoints.ToList())}");
        if (closePoints.Any())
            return CreateWaypointFromObject(closePoints.MinBy(x => x.Position.DistanceFromPlayer())!);
        var allPoints = _currentRoute!.Nodes.Where(x => !_skippedPoints.Contains(x)).SelectMany(Svc.Config.GetKnownPoints).Where(x => x.Position.DistanceFromPlayer() > UnloadRadius);
        Svc.Log.Debug($"allPoints (out of range): {string.Join(", ", allPoints.ToList())}");
        Svc.Log.Debug($"skipped points: {string.Join(", ", _skippedPoints)}");

        return SearchForUnloadedPoint(allPoints, _currentRoute.Nodes);
    }

    private static GatherPointSearch? SearchForUnloadedPoint(IEnumerable<GatherPointObject> allPoints, ICollection<uint> ids)
    {
        if (!allPoints.Any())
            return null;

        var closestGroup = allPoints.GroupBy(x => x.DataId).MinBy(points => points.Min(obj => obj.DistanceFromPlayer));
        var furthestNodeInGroup = closestGroup!.MaxBy(obj => obj.DistanceFromPlayer);

        return new GatherPointSearch()
        {
            Center = furthestNodeInGroup.NaviPosition,
            DataIDs = [.. ids],
            Landable = furthestNodeInGroup.GatherLocation != null
        };
    }

    private IWaypoint? FindNextOrderedDestination()
    {
        var objId = _currentRoute!.Nodes[_loopIndex];
        Svc.Log.Debug($"searching for nodes matching {objId}");

        if (_skippedPoints.Contains(objId))
        {
            UiMessage.Error("We tried everything and nothing worked");
            Stop();
            return null;
        }

        var closePoints = FindGatherPoints(obj => obj.DataId == objId);
        if (closePoints.Any())
        {
            Svc.Log.Debug($"found {closePoints.Count()} points in range");
            var targetablePoints = closePoints.Where(x => x.IsTargetable);
            if (targetablePoints.Any())
            {
                return CreateWaypointFromObject(targetablePoints.MinBy(x => x.Position.DistanceFromPlayer())!);
            }
            else
            {
                _skippedPoints.Add(objId);
                _loopIndex += 1;
                if (_loopIndex >= _currentRoute!.Nodes.Count)
                    _loopIndex = 0;
                Svc.Log.Debug($"Skipping {objId}");
                return FindNextOrderedDestination();
            }
        }

        var allPoints = Svc.Config.GetKnownPoints(_currentRoute.Nodes[_loopIndex]).Where(x => x.Position.DistanceFromPlayer() > UnloadRadius);
        Svc.Log.Debug($"found {allPoints.Count()} points out of range (50y away)");
        // nodes without known locations may exist if someone manually adds a data ID while the object isn't in range
        return SearchForUnloadedPoint(allPoints, [_currentRoute.Nodes[_loopIndex]]);
    }

    private bool IsRightTerritory(GatherRoute rte)
    {
        if (rte.TerritoryType.RowId == 901 && Svc.ClientState.TerritoryType == 939)
            return true;

        return rte.TerritoryType.RowId == Svc.ClientState.TerritoryType;
    }

    public static IEnumerable<IGameObject> FindGatherPoints(Func<IGameObject, bool>? filter = null) =>
        Svc.ObjectTable.Where(
            x =>
                x.ObjectKind is Dalamud.Game.ClientState.Objects.Enums.ObjectKind.GatheringPoint
                && (filter == null || filter(x))
        );

    private unsafe bool ChangeGearset(GatherRoute? rte)
    {
        var cj = rte?.Class.GetClassJob();

        // None = do nothing
        if (cj == null)
            return true;

        if (Svc.Player!.ClassJob.Id == cj.RowId)
        {
            Done(State.Gearset);
            return true;
        }

        if (WaitingFor(State.Gearset))
            return false;

        var needClass = cj.NameEnglish.ToString();

        var gearsetModule = RaptureGearsetModule.Instance();
        var gearsetId = gearsetModule->FindGearsetIdByName(Utf8String.FromString(needClass));
        if (gearsetId == -1)
        {
            UiMessage.Error($"No gearset registered for {needClass}.");
            Done(State.Gearset);
            // assume user knows what they're doing
            return true;
        }

        gearsetModule->EquipGearset(gearsetId);
        WaitFor(State.Gearset);

        return false;
    }

    private unsafe bool Mount()
    {
        if (Svc.Condition[ConditionFlag.Mounted])
        {
            Done(State.Mount);
            return true;
        }

        if (WaitingFor(State.Mount))
            return false;

        ActionManager.Instance()->UseAction(ActionType.GeneralAction, 24);
        WaitFor(State.Mount, TimeSpan.FromMilliseconds(1000));
        return false;
    }

    private unsafe bool Dismount()
    {
        if (!Svc.Condition[ConditionFlag.Mounted] && !PlayerIsFalling)
        {
            Done(State.Dismount);
            return true;
        }

        if (WaitingFor(State.Dismount) || PlayerIsFalling)
            return false;

        ActionManager.Instance()->UseAction(ActionType.GeneralAction, 23);
        WaitFor(State.Dismount, TimeSpan.FromMilliseconds(250));
        return false;
    }

    private bool PathfindComplete => IPCHelper.NavmeshIsReady() && !IPCHelper.PathfindInProgress() && !IPCHelper.PathIsRunning() && IPCHelper.PathfindQueued() == 0 && _destination != null;

    private IWaypoint CreateWaypointFromObject(IGameObject g)
    {
        var actualObj = new GatherPointKnown()
        {
            Position = g.Position,
            DataID = g.DataId
        };

        if (Svc.Config.GetFloorPoint(g, out var point))
            return new FloorPoint() { FloorPosition = point, Node = actualObj };

        return actualObj;
    }
}

interface IWaypoint
{
    public Vector3 Pos();
    public bool KnownLandable();
}

class GatherPointSearch : IWaypoint
{
    public Vector3 Center;
    public required List<uint> DataIDs;
    public required bool Landable;

    public Vector3 Pos() => Center;
    public bool KnownLandable() => Landable;

    public override string ToString() =>
        $"Unloaded gather point near {Utils.ShowV3(Center)}\n" +
        $"Candidate IDs: {string.Join(", ", DataIDs.Select(d => $"0x{d:X}"))}";
}

class GatherPointKnown : IWaypoint
{
    public Vector3 Position;
    public uint DataID;

    public Vector3 Pos() => Position;
    public bool KnownLandable() => false;

    public override string ToString() => $"Gather point with ID 0x{DataID:X} at {Utils.ShowV3(Position)}";
}

class FloorPoint : IWaypoint
{
    public Vector3 FloorPosition;
    public required GatherPointKnown Node;

    public Vector3 Pos() => FloorPosition;
    public bool KnownLandable() => true;

    public override string ToString() => $"Landing point override for {Node}";
}
