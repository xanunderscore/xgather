using Dalamud.Game.ClientState.Conditions;
using Lumina.Excel.GeneratedSheets;
using System.Collections.Generic;
using System.Linq;

namespace xgather.Executors;

public sealed class ListPlanner : Planner
{
    private UnorderedRoutePlanner? CurrentRoute;
    public TodoList CurrentList { get; init; }

    public ListPlanner(TodoList list)
    {
        CurrentList = list;
        CurrentRoute = NextRoute(list);
        Svc.Condition.ConditionChange += OnChange;
    }

    private void OnChange(ConditionFlag flag, bool isActive)
    {
        if ((uint)flag == 85 && !isActive)
        {
            CurrentRoute = NextRoute();
            if (CurrentRoute == null)
                ReportSuccess();
        }
    }

    private UnorderedRoutePlanner? NextRoute() => NextRoute(CurrentList);

    private static UnorderedRoutePlanner? NextRoute(TodoList list)
    {
        var nextRoute =
            (from item in list.Items
             where item.Value.QuantityNeeded > 0
             from route in Svc.Config.GetGatherPointGroupsForItem(item.Key)
             orderby Utils.GetNextAvailable(route).Start ascending, route.Zone == Svc.ClientState.TerritoryType descending
             select route).FirstOrDefault();

        if (nextRoute == null)
            return null;

        return new UnorderedRoutePlanner(nextRoute, 0);
    }

    public override IEnumerable<uint> DesiredItems() => CurrentList.Items.Keys;

    public override IWaypoint? NextDestination(ICollection<uint> skippedPoints) => CurrentRoute?.NextDestination(skippedPoints);
    public override ClassJob? DesiredClass() => CurrentRoute?.DesiredClass();

    public override void Debug()
    {
        CurrentList.Debug();
        CurrentRoute?.Debug();
    }
}
