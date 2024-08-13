using Dalamud.Plugin.Ipc;
using System.Numerics;

namespace xgather;

public static class IPCHelper
{
    private static readonly ICallGateSubscriber<Vector3, bool, bool> _pathfindAndMoveTo;
    private static readonly ICallGateSubscriber<object> _pathStop;
    private static readonly ICallGateSubscriber<object> _pathfindCancelAll;
    private static readonly ICallGateSubscriber<Vector3, bool, float, Vector3?> _pointOnFloor;
    private static readonly ICallGateSubscriber<bool> _pathIsRunning;
    private static readonly ICallGateSubscriber<bool> _pathfindInProgress;
    private static readonly ICallGateSubscriber<bool> _navmeshIsReady;
    private static readonly ICallGateSubscriber<uint> _pathfindQueue;

    private static readonly ICallGateSubscriber<uint, byte, bool> _teleport;

    public static bool PathfindAndMoveTo(Vector3 dest, bool fly) => _pathfindAndMoveTo.InvokeFunc(dest, fly);
    public static void PathStop() => _pathStop.InvokeAction();
    public static void PathfindCancel() => _pathfindCancelAll.InvokeAction();
    public static Vector3? PointOnFloor(Vector3 pos, bool allowUnlandable, float radius) => _pointOnFloor.InvokeFunc(pos, allowUnlandable, radius);
    public static bool PathIsRunning() => _pathIsRunning.InvokeFunc();
    public static bool PathfindInProgress() => _pathfindInProgress.InvokeFunc();
    public static bool NavmeshIsReady() => _navmeshIsReady.InvokeFunc();
    public static uint PathfindQueued() => _pathfindQueue.InvokeFunc();

    public static bool Teleport(uint aetheryteId) => _teleport.InvokeFunc(aetheryteId, 0);

    static IPCHelper()
    {
        _pathfindAndMoveTo = Svc.PluginInterface.GetIpcSubscriber<Vector3, bool, bool>("vnavmesh.SimpleMove.PathfindAndMoveTo");
        _pathStop = Svc.PluginInterface.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
        _pathfindCancelAll = Svc.PluginInterface.GetIpcSubscriber<object>("vnavmesh.Nav.PathfindCancelAll");
        _pathIsRunning = Svc.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
        _pointOnFloor = Svc.PluginInterface.GetIpcSubscriber<Vector3, bool, float, Vector3?>("vnavmesh.Query.Mesh.PointOnFloor");
        _pathfindInProgress = Svc.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.PathfindInProgress");
        _navmeshIsReady = Svc.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
        _pathfindQueue = Svc.PluginInterface.GetIpcSubscriber<uint>("vnavmesh.Nav.PathfindNumQueued");

        _teleport = Svc.PluginInterface.GetIpcSubscriber<uint, byte, bool>("Teleport");
    }
}
