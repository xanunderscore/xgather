using Dalamud.Game.ClientState.Conditions;
using Lumina.Excel.GeneratedSheets;
using System.Collections.Generic;
using System.Linq;

namespace xgather.Executors;

public sealed class ListExecutor : GatherPlanner
{
    private UnorderedRouteExecutor? RouteExecutor;
    public TodoList CurrentList { get; init; }

    public ListExecutor(TodoList list)
    {
        CurrentList = list;
        RouteExecutor = NextExecutor(list);
        Svc.Condition.ConditionChange += OnChange;
    }

    private void OnChange(ConditionFlag flag, bool isActive)
    {
        if ((uint)flag == 85 && !isActive)
        {
            RouteExecutor = NextExecutor();
            if (RouteExecutor == null)
                OnSuccess();
        }
    }

    private UnorderedRouteExecutor? NextExecutor() => NextExecutor(CurrentList);

    private static UnorderedRouteExecutor? NextExecutor(TodoList list)
    {
        var nextRoute =
         (from item in list.Items
          where item.Value.QuantityNeeded > 0
          from route in Svc.Config.GetGatherPointGroupsForItem(item.Key)
          orderby Utils.GetNextAvailable(route).Start ascending, route.Zone == Svc.ClientState.TerritoryType descending
          select route).FirstOrDefault();

        if (nextRoute == null)
            return null;

        return new UnorderedRouteExecutor(nextRoute, from item in list.Items where item.Value.QuantityNeeded > 0 select item.Key);
    }

    public override IEnumerable<uint> DesiredItems() => from item in CurrentList.Items select item.Key;

    public override IWaypoint? NextDestination(ICollection<uint> skippedPoints) => RouteExecutor?.NextDestination(skippedPoints);
    public override ClassJob? DesiredClass() => RouteExecutor?.DesiredClass();

    public override void Debug()
    {
        CurrentList.Debug();
        RouteExecutor?.Debug();
    }
}
