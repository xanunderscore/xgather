using System.Collections.Generic;

namespace xgather.Executors;

public sealed class MultipurposeExecutor
{
    public GatherExecutor? Gather { get; private set; }

    public void StartList(TodoList list)
    {
        OnRouteStopped(this);
        Gather = new GatherExecutor(new ListExecutor(list));
        Gather.OnRouteStopped += OnRouteStopped;
        Gather.Start();
    }

    public void StartAdHoc(GatherPointBase gpb, IEnumerable<uint> itemIDs)
    {
        OnRouteStopped(this);
        Gather = new GatherExecutor(new UnorderedRouteExecutor(gpb, itemIDs));
        Gather.OnRouteStopped += OnRouteStopped;
        Gather.Start();
    }

    private void OnRouteStopped(object sender)
    {
        Gather?.Dispose();
        Gather = null;
    }
}
