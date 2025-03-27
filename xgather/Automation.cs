using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Ipc;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

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

    public string Status { get; protected set; } = ""; // user-facing status string
    private readonly CancellationTokenSource cts = new();
    private readonly List<string> debugContext = [];

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

    protected CancellationToken CancelToken => cts.Token;

    // wait for a few frames
    protected Task NextFrame(int numFramesToWait = 1) => Svc.Framework.DelayTicks(numFramesToWait, cts.Token);

    // wait until condition function returns false, checking once every N frames
    protected async Task WaitWhile(Func<bool> condition, string scopeName, int checkFrequency = 1)
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

    private bool NavIsReady() => _navIsReady.InvokeFunc();
    private float NavBuildProgress() => _navBuildProgress.InvokeFunc();
    private bool PathMove(Vector3 dest, bool fly = false) => _navPathfindAndMoveTo.InvokeFunc(dest, fly);

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

        using var scope = BeginScope("Teleport");

        var closest = Svc.Plugin.Aetherytes.MinBy(a => a.DistanceToPoint(goalZone, destination));
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

    protected async Task MoveTo(Vector3 destination, float tolerance, bool mount = false, bool fly = false, bool dismount = false)
    {
        using var scope = BeginScope("MoveTo");
        if (Utils.PlayerInRange(destination, tolerance))
            return;

        Status = "Waiting for Navmesh";
        await WaitWhile(() => NavBuildProgress() >= 0, "BuildMesh");
        ErrorIf(!NavIsReady(), "Failed to build navmesh");
        ErrorIf(!PathMove(destination, fly), "Failed to start pathfind");
        Status = $"Moving to {destination}";

        if (mount || fly)
            await Mount();

        await WaitWhile(() => !Utils.PlayerInRange(destination, tolerance), "Navigate");

        if (dismount)
            await Dismount();
    }

    protected async Task Mount()
    {
        using var scope = BeginScope("Mount");
        if (Svc.Condition[ConditionFlag.Mounted])
            return;

        Status = "Mounting";
        ErrorIf(!Utils.UseAction(ActionType.GeneralAction, 24), "Failed to mount");
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
            await WaitWhile(() => Svc.Condition[ConditionFlag.Mounted] || Utils.PlayerIsFalling, "WaitingToDismount");
        }
        ErrorIf(Svc.Condition[ConditionFlag.Mounted], "Failed to dismount");
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
