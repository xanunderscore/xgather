using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Ipc;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using xgather.GameData;

// copied from vsatisfy
namespace xgather;

// base class for automation tasks
// all tasks are cancellable, and all continuations are executed on the main thread (in framework update)
// tasks also support progress reporting
// note: it's assumed that any created task will be executed (either by calling Run directly or by passing to Automation.Start)
public abstract class AutoTask
{
    // debug context scope
    protected readonly struct DebugContext : IDisposable
    {
        private readonly AutoTask ctx;
        private readonly int depth;

        public DebugContext(AutoTask ctx, string name)
        {
            this.ctx = ctx;
            depth = this.ctx.debugContext.Count;
            this.ctx.debugContext.Add(name);
            this.ctx.Log("Scope enter");
        }

        public void Dispose()
        {
            ctx.Log($"Scope exit (depth={depth}, cur={ctx.debugContext.Count - 1})");
            if (depth < ctx.debugContext.Count)
                ctx.debugContext.RemoveRange(depth, ctx.debugContext.Count - depth);
        }

        public void Rename(string newName)
        {
            ctx.Log($"Transition to {newName} @ {depth}");
            if (depth < ctx.debugContext.Count)
                ctx.debugContext[depth] = newName;
        }
    }

    public string Status
    {
        get => _status;
        protected set
        {
            _status = value;
            OnStatusChange.Invoke(value);
        }
    }
    private string _status = "";
    private CancellationTokenSource cts = new();
    private readonly List<string> debugContext = [];

    private event Action<string> OnStatusChange = delegate { };

    public void Cancel() => cts.Cancel();

    public void Run(Action<AggregateException?> completed)
    {
        Svc.Framework.Run(async () =>
        {
            var task = Execute();
            await task.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing); // we don't really care about cancelation...
            if (task.IsFaulted)
                Svc.Log.Warning($"Task ended with error: {task.Exception}");
            completed(task.Exception);
            cts.Dispose();
        }, cts.Token);
    }

    // implementations are typically expected to be async (coroutines)
    protected abstract Task Execute();

    // run another AutoTask, it inherits our cancellation token and we inherit its status
    protected async Task RunSubtask(AutoTask t, Action<string>? onStatusChange = null)
    {
        t.cts = cts;
        if (onStatusChange != null)
            t.OnStatusChange = onStatusChange;
        await t.Execute();
    }

    protected CancellationToken CancelToken => cts.Token;

    // wait for a few frames
    protected Task NextFrame(int numFramesToWait = 1) => Svc.Framework.DelayTicks(numFramesToWait, cts.Token);

    // wait until condition function returns false, checking once every N frames
    protected async Task WaitWhile(Func<bool> condition, string scopeName, int checkFrequency = 10)
    {
        using var scope = BeginScope(scopeName);
        while (condition())
        {
            Log("waiting...");
            await NextFrame(checkFrequency);
        }
    }

    protected void Log(string message) => Svc.Log.Debug($"[{GetType().Name}] [{string.Join(" > ", debugContext)}] {message}");

    // start a new debug context; should be disposed, so usually should be assigned to RAII variable
    protected DebugContext BeginScope(string name) => new(this, name);

    // abort a task unconditionally
    [DoesNotReturn]
    protected void Error(string message)
    {
        Log($"Error: {message}");
        throw new Exception($"[{GetType().Name}] [{string.Join(" > ", debugContext)}] {message}");
    }

    // abort a task if condition is true
    protected void ErrorIf([DoesNotReturnIf(true)] bool condition, string message)
    {
        if (condition)
            Error(message);
    }

    private readonly ICallGateSubscriber<float> _navBuildProgress = Svc.PluginInterface.GetIpcSubscriber<float>("vnavmesh.Nav.BuildProgress");
    private readonly ICallGateSubscriber<bool> _navIsReady = Svc.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
    private readonly ICallGateSubscriber<Vector3, bool, bool> _navPathfindAndMoveTo = Svc.PluginInterface.GetIpcSubscriber<Vector3, bool, bool>("vnavmesh.SimpleMove.PathfindAndMoveTo");
    private readonly ICallGateSubscriber<bool> _navPathfindInProgress = Svc.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress");
    private readonly ICallGateSubscriber<object> _navStop = Svc.PluginInterface.GetIpcSubscriber<object>("vnavmesh.Path.Stop");

    protected bool NavIsReady() => _navIsReady.InvokeFunc();
    protected float NavBuildProgress() => _navBuildProgress.InvokeFunc();
    protected bool PathMove(Vector3 dest, bool fly = false) => _navPathfindAndMoveTo.InvokeFunc(dest, fly);
    protected void NavStop() => _navStop.InvokeAction();
    protected bool PathInProgress() => _navPathfindInProgress.InvokeFunc();

    protected async Task WaitForBusy(string tag)
    {
        await WaitWhile(() => !Utils.PlayerIsBusy(), $"{tag}Start");
        await WaitWhile(Utils.PlayerIsBusy, $"{tag}Finish");
    }

    protected async Task TeleportToZone(uint territoryId, Vector3 destination)
    {
        var currentZone = Svc.ClientState.TerritoryType;
        var goalZone = territoryId;
        if (goalZone == currentZone || (goalZone == 901 && currentZone == 939))
            return;

        ErrorIf(goalZone == 901, "Diadem teleportation not implemented yet");

        using var scope = BeginScope("Teleport");

        var closest = AetheryteDatabase.Closest(goalZone, destination);
        ErrorIf(closest == null, $"No aetheryte near zone {goalZone}");

        Status = "Teleporting";

        bool success;
        unsafe
        {
            success = UIState.Instance()->Telepo.Teleport(closest.GameAetheryte.RowId, 0);
        }
        ErrorIf(!success, $"Failed to teleport to {closest.GameAetheryte.RowId}");
        await WaitForBusy("Teleport");
    }

    protected async Task WaitNavmesh()
    {
        Status = "Waiting for Navmesh";
        await WaitWhile(() => NavBuildProgress() >= 0, "BuildMesh");
        ErrorIf(!NavIsReady(), "Failed to build navmesh");
        await WaitWhile(PathInProgress, "PathfindInProgress");
    }

    protected async Task MoveTo(Vector3 destination, float tolerance, bool mount = false, bool fly = false, bool dismount = false, Func<bool>? interrupt = null)
    {
        if (Utils.PlayerInRange(destination, tolerance))
            return;

        using var scope = BeginScope("MoveTo");
        await WaitNavmesh();
        ErrorIf(!PathMove(destination, fly), "Failed to start pathfind");
        Status = $"Moving to {destination}";

        using (new OnDispose(NavStop))
        {
            if (mount || fly)
                await Mount();

            bool shouldStop() => (interrupt?.Invoke() ?? false) || Utils.PlayerInRange(destination, tolerance);

            await WaitWhile(() => !shouldStop(), "Navigate");
        }

        if (dismount)
            await Dismount();
    }

    protected async Task<Vector3> PointOnFloor(Vector3 destination, bool allowUnlandable, float radius)
    {
        await WaitNavmesh();
        var point = IPCHelper.PointOnFloor(destination, allowUnlandable, radius);
        ErrorIf(point == null, $"Unable to find point near {destination}");
        return point.Value;
    }

    protected async Task Mount()
    {
        using var scope = BeginScope("Mount");
        if (Svc.Condition[ConditionFlag.Mounted])
            return;

        await WaitWhile(Utils.PlayerIsBusy, "MountBusy");
        ErrorIf(!Utils.UseAction(ActionType.GeneralAction, 9), "Failed to mount");
        await WaitWhile(() => !Svc.Condition[ConditionFlag.Mounted], "Mounting");
        ErrorIf(!Svc.Condition[ConditionFlag.Mounted], "Failed to mount");
    }

    protected async Task Dismount()
    {
        using var scope = BeginScope("Dismount");
        if (!Svc.Condition[ConditionFlag.Mounted])
            return;

        if (Svc.Condition[ConditionFlag.InFlight])
        {
            Utils.UseAction(ActionType.GeneralAction, 23);
            await WaitWhile(() => Svc.Condition[ConditionFlag.InFlight], "WaitingToLand");
        }
        if (Svc.Condition[ConditionFlag.Mounted] && !Svc.Condition[ConditionFlag.InFlight])
        {
            Utils.UseAction(ActionType.GeneralAction, 23);
            await WaitWhile(() => Svc.Condition[ConditionFlag.Mounted], "WaitingToDismount");
        }
        await WaitWhile(() => Utils.PlayerIsFalling, "WaitingToLand2");
        ErrorIf(Svc.Condition[ConditionFlag.Mounted], "Failed to dismount");
    }

    protected async Task ChangeClass(GatherClass cls)
    {
        using var scope = BeginScope("Gearset");

        if (cls.GetClassJob() is not { } desired)
            return;

        if (Svc.Player?.ClassJob is not { } current)
            return;

        if (current.RowId == desired.RowId)
            return;

        var equipped = false;
        unsafe
        {
            var gm = RaptureGearsetModule.Instance();
            foreach (var gs in gm->Entries)
            {
                if (gs.ClassJob == 0)
                    break;

                if (gs.ClassJob == desired.RowId)
                {
                    equipped = gm->EquipGearset(gs.Id) == 0;
                    break;
                }
            }
        }
        ErrorIf(!equipped, $"No gearset found for {cls}");

        await WaitWhile(() => Svc.Player?.ClassJob.RowId != desired.RowId, "WaitEquip");
    }

    protected async Task UseCollectorsGlove()
    {
        if (Utils.PlayerHasStatus(805))
            return;

        ErrorIf(!Utils.UseAction(ActionType.Action, 4101), "Unable to use Collector's Glove");
        await WaitForBusy("UseAction");
    }
}

// utility that allows concurrently executing only one task; starting a new task if one is already in progress automatically cancels olds one
public sealed class Automation : IDisposable
{
    public AutoTask? CurrentTask { get; private set; }
    public AggregateException? LastError { get; private set; }

    public bool Running => CurrentTask != null;

    public void Dispose() => Stop();

    // stop executing any running task
    // this requires tasks to cooperate by checking the token
    public void Stop()
    {
        CurrentTask?.Cancel();
        CurrentTask = null;
    }

    // if any other task is running, it's cancelled
    public void Start(AutoTask task)
    {
        Stop();
        CurrentTask = task;
        task.Run((exc) =>
        {
            LastError = exc;
            if (CurrentTask == task)
                CurrentTask = null;
            // else: some other task is now executing
        });
    }
}
