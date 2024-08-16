using Lumina.Excel.GeneratedSheets;
using System.Collections.Generic;
using System.Linq;

namespace xgather.Executors;

public class UnorderedRouteExecutor : ExecutorBase
{
    public GatherPointBase? CurrentRoute { get; protected set; }

    public void Start(GatherPointBase r)
    {
        Svc.Log.Debug($"setting route to {r}");
        CurrentRoute = r;
        Start();
    }

    public override IWaypoint? NextDestination()
    {
        if (CurrentRoute == null)
            return null;

        var closePoints = FindGatherPoints(obj => CurrentRoute.Contains(obj.DataId) && obj.IsTargetable && !SkippedPoints.Contains(obj.DataId));
        Svc.Log.Debug($"closePoints: {string.Join(", ", closePoints.ToList())}");
        if (closePoints.Any())
            return CreateWaypointFromObject(closePoints.MinBy(x => x.Position.DistanceFromPlayer())!);
        var allPoints = CurrentRoute.Nodes.Where(x => !SkippedPoints.Contains(x)).SelectMany(Svc.Config.GetKnownPoints).Where(x => x.Position.DistanceFromPlayer() > UnloadRadius);
        Svc.Log.Debug($"allPoints (out of range): {string.Join(", ", allPoints.ToList())}");
        Svc.Log.Debug($"skipped points: {string.Join(", ", SkippedPoints)}");

        return SearchForUnloadedPoint(allPoints, CurrentRoute.Nodes, CurrentRoute.Zone);
    }

    public override ClassJob? DesiredClass() => CurrentRoute?.Class.GetClassJob();

    public override void OnStop()
    {
        CurrentRoute = null;
    }

    public override IEnumerable<uint> DesiredItems() => [];
}
