using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Ipc;
using FFXIVClientStructs.FFXIV.Client.Game;
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
        await MoveTo(new(-299.574f, 24.338f, -101.603f), 1, mount: true, dismount: true);

        if (!_iceIsRunning.InvokeFunc())
            _iceEnable.InvokeAction();

        while (true)
        {
            await WaitWhile(() => _iceState.InvokeFunc() != "ManualMode" || _iceMission.InvokeFunc() != 509, "WaitMission");

            await ChangeClass(GatherClass.FSH);
            await FaceTheWater();

            Util.UseAction(289);

            await WaitFlipflop(() => Svc.Condition[ConditionFlag.Gathering], "Fishing");

            await ChangeClass(Svc.ExcelRow<ClassJob>(14));

            _artisanCraft.InvokeAction(36682, 2);

            await WaitFlipflop(_artisanBusy.InvokeFunc, "Crafting");

            if (!Util.IsAddonReady("WKSMissionInfomation"))
            {
                unsafe
                {
                    Util.GetAddonByName("WKSHud")->FireCallbackInt(11);
                }
                await WaitAddon("WKSMissionInfomation", 10);
            }

            unsafe
            {
                Util.GetAddonByName("WKSMissionInfomation")->FireCallbackInt(11);
            }

            await WaitWhile(() => _iceState.InvokeFunc() == "ManualMode", "Turnin");
        }
    }

    private static async Task FaceTheWater()
    {
        await Svc.Framework.RunOnFrameworkThread(() =>
        {
            var pos = Svc.Player!.Position;
            pos.X += 5;
            unsafe
            {
                ActionManager.Instance()->AutoFaceTargetPosition(&pos);
            }
        });
    }
}
