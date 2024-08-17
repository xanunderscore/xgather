using Dalamud.Game.ClientState.Conditions;
using Lumina.Excel.GeneratedSheets;
using System.Collections.Generic;
using System.Linq;

namespace xgather.Executors;

public sealed class ListPlanner : GatherPlanner
{
    private UnorderedRoutePlanner? RoutePlanner;
    public TodoList CurrentList { get; init; }

    public ListPlanner(TodoList list)
    {
        CurrentList = list;
        RoutePlanner = NextPlanner(list);
        Svc.Condition.ConditionChange += OnChange;
    }

    private void OnChange(ConditionFlag flag, bool isActive)
    {
        if ((uint)flag == 85 && !isActive)
        {
            RoutePlanner = NextPlanner();
            if (RoutePlanner == null)
                TriggerSuccess();
        }
    }

    private UnorderedRoutePlanner? NextPlanner() => NextPlanner(CurrentList);

    private static UnorderedRoutePlanner? NextPlanner(TodoList list)
    {
        var nextRoute =
         (from item in list.Items
          where item.Value.QuantityNeeded > 0
          from route in Svc.Config.GetGatherPointGroupsForItem(item.Key)
          orderby Utils.GetNextAvailable(route).Start ascending, route.Zone == Svc.ClientState.TerritoryType descending
          select route).FirstOrDefault();

        if (nextRoute == null)
            return null;

        return new UnorderedRoutePlanner(nextRoute, from item in list.Items where item.Value.QuantityNeeded > 0 select item.Key);
    }

    public override IEnumerable<uint> DesiredItems() => from item in CurrentList.Items select item.Key;

    public override IWaypoint? NextDestination(ICollection<uint> skippedPoints) => RoutePlanner?.NextDestination(skippedPoints);
    public override ClassJob? DesiredClass() => RoutePlanner?.DesiredClass();

    public override void Debug()
    {
        CurrentList.Debug();
        RoutePlanner?.Debug();
    }
}
