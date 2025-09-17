using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Ipc;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Reflection;
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
    private readonly ICallGateSubscriber<uint, object> _swapBait = Svc.PluginInterface.GetIpcSubscriber<uint, object>("AutoHook.SwapBaitById");
    private readonly ICallGateSubscriber<string, object> _swapAHPreset = Svc.PluginInterface.GetIpcSubscriber<string, object>("AutoHook.CreateAndSelectAnonymousPreset");
    private readonly ICallGateSubscriber<object> _clearAHPresets = Svc.PluginInterface.GetIpcSubscriber<object>("AutoHook.DeleteAllAnonymousPresets");

    // supported missions
    // 509: moon gel (FSH + ALC)
    // 542: edible fish (FSH crit 1)
    // 544: mutated fish (FSH crit 2)

    //private static readonly Dictionary<uint, (Vector3, Func<Task>)> _supportedMissions = new()
    //{
    //    { 509, (new Vector3(-299.574f, 24.338f, -101.603f), async () => await DoMissionMoonGel()) }
    //};

    private readonly Dictionary<string, string> _ahPresets;

    public MoonGel()
    {
        var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("xgather.resources.autohookPresets.json");
        if (resource == null)
            throw new InvalidDataException("AH presets file is not found, can't fish");

        _ahPresets = Json.Deserialize<Dictionary<string, string>>(resource);
    }

    protected override async Task Execute()
    {
        await ChangeClass(GatherClass.FSH);

        if (!_iceIsRunning.InvokeFunc())
            _iceEnable.InvokeAction();

        // 110.022f, 18.805f, -229.596f

        while (true)
        {
            uint missionId;
            while (true)
            {
                if (_iceState.InvokeFunc() == "ManualMode")
                {
                    missionId = _iceMission.InvokeFunc();
                    if (missionId is 509 or 542 or 544)
                        break;
                }

                if (_iceState.InvokeFunc() == "Idle")
                {
                    Svc.Log.Debug("ICE is off, ending task");
                    return;
                }

                await NextFrame(10);
            }

            switch (missionId)
            {
                case 509:
                    await DoMissionMoonGel();
                    break;
                default:
                    Error($"Proceeded with unsupported mission {missionId}, giving up");
                    break;
            }

        }
    }

    private async Task DoMissionMoonGel()
    {
        await MoveTo(new(-299.574f, 24.338f, -101.603f), 1, mount: true, dismount: true);
        await ChangeClass(GatherClass.FSH);
        await FaceDirection(new(5, 0, 0));

        // for some reason it takes a moment to face the fishing spot, maybe to do with rotation interpolation bs
        await NextFrame(10);

        // stellar salmon roe
        _swapBait.InvokeFunc(45960);
        _clearAHPresets.InvokeAction();
        _swapAHPreset.InvokeAction(_ahPresets["Refined Moon Gel"]);

        // cast line
        Util.UseAction(289);

        await WaitFlipflop(() => Svc.Condition[ConditionFlag.Gathering], "Fishing");

        await ChangeClass(Svc.ExcelRow<ClassJob>(14));

        // craft moon gel
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

    private static async Task FaceDirection(Vector3 offset)
    {
        await Svc.Framework.RunOnFrameworkThread(() =>
        {
            var pos = Svc.Player!.Position;
            pos += offset;
            unsafe
            {
                ActionManager.Instance()->AutoFaceTargetPosition(&pos);
            }
        });
    }
}
