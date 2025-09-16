using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Ipc;
using Lumina.Excel.Sheets;
using System.Threading.Tasks;
using xgather.Utils;

namespace xgather.Tasks.Gather;

internal class MoonGel : AutoTask
{
    private readonly ICallGateSubscriber<string> _iceState = Svc.PluginInterface.GetIpcSubscriber<string>("ICE.CurrentState");
    private readonly ICallGateSubscriber<uint> _iceMission = Svc.PluginInterface.GetIpcSubscriber<uint>("ICE.CurrentMission");
    private readonly ICallGateSubscriber<bool> _iceIsRunning = Svc.PluginInterface.GetIpcSubscriber<bool>("ICE.IsRunning");
    private readonly ICallGateSubscriber<object> _iceEnable = Svc.PluginInterface.GetIpcSubscriber<object>("ICE.Enable");
    private readonly ICallGateSubscriber<object> _iceDisable = Svc.PluginInterface.GetIpcSubscriber<object>("ICE.Disable");
    private readonly ICallGateSubscriber<ushort, int, object> _artisanCraft = Svc.PluginInterface.GetIpcSubscriber<ushort, int, object>("Artisan.CraftItem");
    private readonly ICallGateSubscriber<bool> _artisanBusy = Svc.PluginInterface.GetIpcSubscriber<bool>("Artisan.IsBusy");

    protected override async Task Execute()
    {
        await MoveTo(new(-299.574f, 24.338f, -101.603f), 1);

        if (!_iceIsRunning.InvokeFunc())
            _iceEnable.InvokeAction();

        while (true)
        {
            await WaitWhile(() => _iceState.InvokeFunc() != "ManualMode" || _iceMission.InvokeFunc() != 509, "WaitManualMode");

            await ChangeClass(GatherClass.FSH);

            Util.UseAction(289);

            await WaitCondition(() => Svc.Condition[ConditionFlag.Gathering], "WaitFishing");

            await ChangeClass(Svc.ExcelRow<ClassJob>(14));

            _artisanCraft.InvokeAction(36682, 2);

            await WaitCondition(_artisanBusy.InvokeFunc, "WaitCraft");

            Error("Unimplemented: stop crafting");
        }
    }
}
