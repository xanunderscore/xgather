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

namespace xgather;

public sealed class RouteExec : IDisposable
{
    // actual radius seems to be around 135y, but trying to use an exact value will make the code brittle
    private static readonly float UnloadRadius = 100f;

    internal GatherRoute? _currentRoute;
    internal NextWaypoint? _destination;
    internal int _loopIndex = 0;
    internal HashSet<uint> _skippedPoints = [];
    internal bool _waitMount = false;
    internal bool _isMoving = false;
    internal bool _disableFly = false;
    internal DateTime _waitDismount = DateTime.MinValue;
    internal IGameObject? _nearbyTarget;
    internal bool _diademMode = true;
    internal int _lastDiademWeather = 0;
    internal int _thisDiademWeather = 0;
    internal bool _wantDiadem = false;

    public enum WaitType : uint
    {
        None = 0,
        Mount = 1,
        Dismount = 2,
        Gearset = 3,
        Teleport = 4,
        Time = 5,
        Weather = 6
    }

    public record struct Wait(WaitType Type, DateTime Retry);

    public enum State : uint
    {
        Stopped = 0,
        Running = 1
    }

    public State CurrentState { get; private set; }

    public bool IsActive => CurrentState == State.Running;

    public void Init()
    {
        Svc.Framework.Update += OnRouteTick;
        Svc.Condition.ConditionChange += OnConditionChange;
    }

    public void Start(GatherRoute r) => Start(r, 0);

    public void Start(GatherRoute r, int loopIndex)
    {
        Stop();
        if (Svc.ClientState.TerritoryType != r.TerritoryType.RowId)
        {
            var closest = Svc.Plugin.Aetherytes.MinBy(a => a.DistanceToRoute(r));
            if (closest == null)
                Svc.Toast.ShowError($"No aetheryte close to destination.");
            else
                IPCHelper.Teleport(closest.GameAetheryte.RowId);

            return;
        }

        ChangeGearset(r);
        _loopIndex = loopIndex;
        _skippedPoints = [];
        CurrentState = State.Running;
        _currentRoute = r;
        GatherNext();
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

        foreach (var it in gbase.Item)
        {
            // 0: empty item
            // 1-6: shards
            // 7-12: crystals
            // 13-18: clusters (might be worth listing)
            if (it > 0)
            {
                var git = Svc.ExcelRow<GatheringItem>((uint)it);
                gatherableItems.Add((uint)git.Item);
            }
        }

        var tt = Svc.Data.Excel.GetSheet<TerritoryType>()!.GetRow(Svc.ClientState.TerritoryType)!;

        var label = $"Level {gbase.GatheringLevel} {gbase.GatheringType.Value!.Name} @ {tt.PlaceName.Value!.Name}";

        var rte = new GatherRoute
        {
            Label = label,
            Nodes = points.Select(x => x.RowId).ToList(),
            Fly = true,
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
        _waitMount = false;
        _waitDismount = DateTime.MinValue;
        _disableFly = false;
        _nearbyTarget = null;
        CurrentState = State.Stopped;
    }

    private void StopMoving()
    {
        IPCHelper.PathStop();
    }

    public void Dispose()
    {
        Svc.Framework.Update -= OnRouteTick;
        Svc.Condition.ConditionChange -= OnConditionChange;
    }

    private unsafe void OnRouteTick(IFramework fw)
    {
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
            EnterDiadem();

        if (_destination == null || CurrentState == State.Stopped || _waitDismount > DateTime.Now)
            return;

        if (_nearbyTarget != null)
        {
            if (Svc.Condition[ConditionFlag.Jumping])
                return;

            if (Svc.Condition[ConditionFlag.InFlight])
            {
                Dismount();
                return;
            }

            Svc.Config.RecordPosition(_nearbyTarget);
            InteractWithObject(_nearbyTarget);
            _nearbyTarget = null;
            return;
        }

        var isMounted = Svc.Condition[ConditionFlag.Mounted];

        if (_destination.Position.DistanceFromPlayer() > 20 && !isMounted && !_waitMount && !_disableFly)
        {
            _waitMount = true;
            ActionManager.Instance()->UseAction(ActionType.GeneralAction, 24);
            return;
        }

        if (_waitMount)
        {
            if (isMounted)
                _waitMount = false;
            return;
        }

        if (_destination.IsLoaded || _destination.IsDismount)
        {
            if (_destination.Position.DistanceFromPlayer() < 3)
            {
                var destObject = Svc.ObjectTable.FirstOrDefault(x => _destination.TargetIDs.Contains(x.DataId) && x.IsTargetable)!;
                StopMoving();
                _skippedPoints = [];

                if (_destination.IsDismount)
                {
                    _disableFly = true;

                    if (Svc.Condition[ConditionFlag.InFlight])
                    {
                        Dismount();
                        return;
                    }

                    _destination = new NextWaypoint()
                    {
                        Position = destObject.Position,
                        TargetIDs = [destObject.DataId],
                        IsLoaded = true
                    };
                }
                else
                {
                    _nearbyTarget = Svc.ObjectTable.FirstOrDefault(x => _destination.TargetIDs.Contains(x.DataId) && x.IsTargetable)!;
                }

                return;
            }
        }
        else
        {
            var objIds = _destination.TargetIDs;
            // TODO better error handling
            if (objIds.Count == 0)
                Stop();

            var candidatePoint = Svc.ObjectTable.FirstOrDefault(x => objIds.Contains(x.DataId) && x.IsTargetable);
            if (candidatePoint != null)
            {
                _destination = NextWaypoint.FromObject(candidatePoint);
                Svc.Log.Debug($"search ended, found candidate point: {_destination.Position}");
                StopMoving();
            }
            else if (_destination.Position.DistanceFromPlayer() < 70)
            {
                // gather nodes should be loaded now.
                // TODO this doesn't work correctly with legendary nodes; it gives up when the nearest node is untargetable even if a further one isn't
                foreach (var i in Svc.ObjectTable.Where(x => _destination.TargetIDs.Contains(x.DataId)))
                    _skippedPoints.Add(i.DataId);
                _loopIndex += 1;
                if (_loopIndex >= _currentRoute!.Nodes.Count)
                    _loopIndex = 0;
                GatherNext();
            }
        }

        if (IPCHelper.NavmeshIsReady() && !IPCHelper.PathfindInProgress() && !IPCHelper.PathIsRunning() && IPCHelper.PathfindQueued() == 0 && _destination != null)
        {
            var shouldFly = !_disableFly && (_currentRoute?.Fly ?? false);
            if (shouldFly && _destination.Position.DistanceFromPlayer() > 20)
            {
                if (_destination.IsDismount)
                    IPCHelper.PathfindAndMoveTo(_destination.Position, true);
                else
                {
                    Svc.Log.Debug($"position: {_destination.Position}");
                    var pos = IPCHelper.PointOnFloor(_destination.Position, false, 2);
                    IPCHelper.PathfindAndMoveTo(pos ?? _destination.Position, true);
                }
            }
            else
                IPCHelper.PathfindAndMoveTo(_destination.Position, false);
        }
    }

    private unsafe void OnConditionChange(ConditionFlag flag, bool flagIsActive)
    {
        // should add some other conditions here but idk which
        if (flag == ConditionFlag.BetweenAreas && flagIsActive)
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
            var tar = Svc.TargetManager.Target;
            if (tar != null)
                Svc.Config.UpdateFloorPoint(tar.GameObjectId, p => p ?? Svc.Player!.Position);
        }

        if (!IsActive)
            return;

        // finished gathering, go to next object
        // if we use the Gathering flag here instead of flag 85, the "next" node may not be targetable yet, so we waste time
        // pathfinding to one that's further away; 85 seems to be the last one that turns off though idk what it does
        if ((uint)flag == 85 && !flagIsActive && CurrentState == State.Running)
        {
            _disableFly = false;
            GatherNext();
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
            Svc.Log.Debug($"moving to next point: {_destination.Position}");
        }
        else if (_diademMode && WeatherIsUmbral)
        {
            Utils.Chat.Instance.Send("/pdfleave");
        }
        else
        {
            Svc.Chat.PrintError("No waypoints in range.");
            Stop();
        }
    }

    private NextWaypoint? FindNextDestination()
    {
        if (_currentRoute == null)
            return null;

        if (_currentRoute.Nodes.All(x => !Svc.Config.GetKnownPoints(x).Any()) && _currentRoute.GatherAreaCenter() is Vector3 p && p.DistanceFromPlayer() > 100)
        {
            var fl = IPCHelper.PointOnFloor(p with { Y = 1024f }, true, 5);
            return NextWaypoint.FromPoint(fl.Value, _currentRoute.Nodes);
        }

        return _currentRoute.Ordered ? FindNextOrderedDestination() : FindNextClosestDestination();
    }

    private NextWaypoint? FindNextClosestDestination()
    {
        var closePoints = FindGatherPoints(obj => _currentRoute!.Contains(obj.DataId) && obj.IsTargetable && !_skippedPoints.Contains(obj.DataId));
        Svc.Log.Debug($"closePoints: {string.Join(", ", closePoints.ToList())}");
        if (closePoints.Any())
            return NextWaypoint.FromObject(closePoints.MinBy(x => x.Position.DistanceFromPlayer())!);
        var allPoints = _currentRoute!.Nodes.Where(x => !_skippedPoints.Contains(x)).SelectMany(Svc.Config.GetKnownPoints).Where(x => x.Position.DistanceFromPlayer() > UnloadRadius);
        Svc.Log.Debug($"allPoints (out of range): {string.Join(", ", allPoints.ToList())}");
        Svc.Log.Debug($"skipped points: {string.Join(", ", _skippedPoints)}");
        if (allPoints.Any())
            return NextWaypoint.FromPoint(
                // TODO: this should be MinBy, but legendary nodes
                allPoints.MaxBy(x => x.Position.DistanceFromPlayer())!.Position,
                _currentRoute.Nodes
            );

        return null;
    }

    private NextWaypoint? FindNextOrderedDestination()
    {
        var objId = _currentRoute!.Nodes[_loopIndex];
        Svc.Log.Debug($"searching for nodes matching {objId}");

        if (_skippedPoints.Contains(objId))
        {
            Svc.Chat.PrintError("We tried everything and nothing worked");
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
                return NextWaypoint.FromObject(targetablePoints.MinBy(x => x.Position.DistanceFromPlayer())!);
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
        if (allPoints.Any())
            return NextWaypoint.FromPoint(
                allPoints.MinBy(x => x.Position.DistanceFromPlayer())!.Position,
                [_currentRoute.Nodes[_loopIndex]]
            );

        return null;
    }

    public static IEnumerable<IGameObject> FindGatherPoints(Func<IGameObject, bool>? filter = null) =>
        Svc.ObjectTable.Where(
            x =>
                x.ObjectKind is Dalamud.Game.ClientState.Objects.Enums.ObjectKind.GatheringPoint
                && (filter == null || filter(x))
        );

    private unsafe bool ChangeGearset(GatherRoute rte)
    {
        var cj = rte.Class.GetClassJob();

        // None = do nothing
        if (cj == null)
            return true;

        // already right job
        if (Svc.Player!.ClassJob.Id == cj.RowId)
            return true;

        var needClass = cj.NameEnglish.ToString();

        var gearsetModule = RaptureGearsetModule.Instance();
        var gearsetId = gearsetModule->FindGearsetIdByName(Utf8String.FromString(needClass));
        if (gearsetId == -1)
        {
            Svc.Toast.ShowError($"No gearset registered for {needClass}.");
            // assume user knows what they're doing
            return true;
        }
        else
            gearsetModule->EquipGearset(gearsetId);

        return false;
    }

    private unsafe void Dismount()
    {
        if (_waitDismount > DateTime.Now || !Svc.Condition[ConditionFlag.Mounted])
            return;

        ActionManager.Instance()->UseAction(ActionType.GeneralAction, 23);
        _waitDismount = DateTime.Now.AddMilliseconds(750);
    }

    internal class NextWaypoint
    {
        public Vector3 Position;
        public required List<uint> TargetIDs;
        public bool IsLoaded;
        public bool IsDismount;

        public static NextWaypoint FromObject(IGameObject obj)
        {
            if (Svc.Config.GetFloorPoint(obj.GameObjectId, out var pos))
                return new()
                {
                    Position = pos,
                    TargetIDs = [obj.DataId],
                    IsLoaded = false,
                    IsDismount = true
                };
            else
                return new()
                {
                    Position = obj.Position,
                    TargetIDs = [obj.DataId],
                    IsLoaded = true
                };
        }

        public static NextWaypoint FromPoint(Vector3 pos, IEnumerable<uint> targetIDs)
        {
            return new()
            {
                Position = pos,
                TargetIDs = targetIDs.ToList(),
                IsLoaded = false
            };
        }
    }
}