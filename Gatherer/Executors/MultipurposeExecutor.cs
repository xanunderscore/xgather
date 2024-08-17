namespace xgather.Executors;

public sealed class MultipurposeExecutor
{
    public GatherExecutor? Gather { get; private set; }

    public void StartList(TodoList list)
    {
        OnRouteStopped(this);
        Gather = new GatherExecutor(new ListPlanner(list));
        Gather.OnRouteStopped += OnRouteStopped;
        Gather.Start();
    }

    public void StartAdHoc(GatherPointBase gpb, uint itemID)
    {
        OnRouteStopped(this);
        Gather = new GatherExecutor(new UnorderedRoutePlanner(gpb, itemID));
        Gather.OnRouteStopped += OnRouteStopped;
        Gather.Start();
    }

    private void OnRouteStopped(object sender)
    {
        Gather?.Dispose();
        Gather = null;
    }
}
