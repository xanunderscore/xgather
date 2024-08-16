using Dalamud.Game.ClientState.Conditions;
using System.Collections.Generic;
using System.Linq;

namespace xgather.Executors;

public class ListExecutor : UnorderedRouteExecutor
{
    public TodoList? CurrentList { get; private set; }

    public ListExecutor()
    {
        Svc.Condition.ConditionChange += OnChange;
    }

    public void Start(TodoList list)
    {
        CurrentList = list;
        if (NextNodeGroup() is GatherPointBase b)
            Start(b);
    }

    private void OnChange(ConditionFlag flag, bool isActive)
    {
        if ((uint)flag == 85 && !isActive)
            if (NextNodeGroup() is GatherPointBase b)
                CurrentRoute = b;
    }

    private GatherPointBase? NextNodeGroup()
    {
        if (CurrentList != null)
        {
            var nextRoute =
             (from item in CurrentList.Value.Items
              where item.Value.QuantityNeeded > 0
              from route in Svc.Config.GetGatherPointGroupsForItem(item.Key)
              orderby route.Zone == Svc.ClientState.TerritoryType descending
              select route).FirstOrDefault();
            if (nextRoute == null)
            {
                UI.Alerts.Success("All done!");
                Stop();
            }
            else return nextRoute;
        }

        return null;
    }

    public override IEnumerable<uint> DesiredItems() => from item in CurrentList?.Items ?? [] where item.Value.QuantityNeeded > 0 select item.Key;

    public override void OnStop()
    {
        CurrentList = null;
        base.OnStop();
    }
}
