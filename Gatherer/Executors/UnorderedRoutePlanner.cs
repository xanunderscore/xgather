using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace xgather.Executors;

public class UnorderedRoutePlanner(GatherPointBase route, uint wantItem) : Planner
{
    public GatherPointBase CurrentRoute { get; init; } = route;
    private readonly uint WantItem = wantItem;

    public override IWaypoint? NextDestination(ICollection<uint> SkippedPoints)
    {
        var avail = Utils.GetNextAvailable(CurrentRoute);
        if (avail.Start > DateTime.Now)
        {
            return new GatherPointSearch()
            {
                Center = CurrentRoute.GatherAreaCenter(),
                DataIDs = CurrentRoute.Nodes,
                Landable = false,
                Zone = CurrentRoute.Zone,
                Available = avail
            };
        }

        var closePoints = GatherExecutor.FindGatherPoints(obj => CurrentRoute.Contains(obj.DataId) && obj.IsTargetable && !SkippedPoints.Contains(obj.DataId));
        Svc.Log.Debug($"closePoints: {string.Join(", ", closePoints.ToList())}");
        if (closePoints.Any())
            return GatherExecutor.CreateWaypointFromObject(closePoints.MinBy(x => x.Position.DistanceFromPlayer())!);
        var allPoints = CurrentRoute.Nodes.Where(x => !SkippedPoints.Contains(x)).SelectMany(Svc.Config.GetKnownPoints).Where(x => x.Position.DistanceFromPlayer() > GatherExecutor.UnloadRadius);
        Svc.Log.Debug($"allPoints (out of range): {string.Join(", ", allPoints.ToList())}");
        Svc.Log.Debug($"skipped points: {string.Join(", ", SkippedPoints)}");

        return GatherExecutor.SearchForUnloadedPoint(allPoints, CurrentRoute.Nodes, CurrentRoute.Zone);
    }

    public override ClassJob? DesiredClass() => CurrentRoute?.Class.GetClassJob();

    public override ICollection<uint> DesiredItems() => [WantItem];
    public override void Debug()
    {
        UI.Helpers.DrawItem(WantItem);
        CurrentRoute.Debug();
    }
}
