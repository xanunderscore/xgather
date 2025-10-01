using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Ipc;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using xgather.Utils;

namespace xgather.Tasks.Gather;

internal class MoonGel : AutoTask
{
    private readonly ICallGateSubscriber<string> _iceState = Svc.PluginInterface.GetIpcSubscriber<string>("ICE.CurrentState");
    private readonly ICallGateSubscriber<uint> _iceMission = Svc.PluginInterface.GetIpcSubscriber<uint>("ICE.CurrentMission");
    private readonly ICallGateSubscriber<object> _iceEnable = Svc.PluginInterface.GetIpcSubscriber<object>("ICE.Enable");
    private readonly ICallGateSubscriber<string, bool, object> _iceConfig = Svc.PluginInterface.GetIpcSubscriber<string, bool, object>("ICE.ChangeSetting");
    private readonly ICallGateSubscriber<ushort, int, object> _artisanCraft = Svc.PluginInterface.GetIpcSubscriber<ushort, int, object>("Artisan.CraftItem");
    private readonly ICallGateSubscriber<bool> _artisanBusy = Svc.PluginInterface.GetIpcSubscriber<bool>("Artisan.IsBusy");
    private readonly ICallGateSubscriber<uint, bool> _swapBait = Svc.PluginInterface.GetIpcSubscriber<uint, bool>("AutoHook.SwapBaitById");
    private readonly ICallGateSubscriber<string, object> _swapAHPreset = Svc.PluginInterface.GetIpcSubscriber<string, object>("AutoHook.CreateAndSelectAnonymousPreset");
    private readonly ICallGateSubscriber<object> _clearAHPresets = Svc.PluginInterface.GetIpcSubscriber<object>("AutoHook.DeleteAllAnonymousPresets");

    private string IceState() => _iceState.InvokeFunc();
    private uint IceMission() => _iceMission.InvokeFunc();
    private void IceEnable() => _iceEnable.InvokeAction();
    private void IceChangeSetting(string option, bool value) => _iceConfig.InvokeAction(option, value);
    private void CraftItem(ushort recipe, int quantity) => _artisanCraft.InvokeAction(recipe, quantity);
    private bool SetBait(uint baitId) => _swapBait.InvokeFunc(baitId);
    private void SetAHPreset(string contents) => _swapAHPreset.InvokeAction(contents);
    private void ClearAHPresets() => _clearAHPresets.InvokeAction();

    private bool _shouldStop;

    // supported missions
    // 509: moon gel (FSH + ALC)
    // 542: edible fish (FSH crit 1)
    // 544: mutated fish (FSH crit 2)

    private readonly Dictionary<string, string> _ahPresets;

    public MoonGel()
    {
        var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("xgather.resources.autohookPresets.json") ?? throw new InvalidDataException("AH presets file is not found, can't fish");

        _ahPresets = Json.Deserialize<Dictionary<string, string>>(resource);
    }

    protected override async Task Execute()
    {
        // 110.022f, 18.805f, -229.596f

        while (!_shouldStop)
        {
            await ChangeClass(GatherClass.FSH);
            IceEnable();

            await WaitUntil(() => IceState() == "ManualMode" && IceMission() is 509 or 542 or 544, "MissionStart");

            switch (IceMission())
            {
                case 509:
                    // use turnin to end crafting stance, but then we have to swap back to FSH before accepting next mission so that ICE will do autorepair check
                    IceChangeSetting("StopAfterCurrent", true);
                    await DoMoonGel();
                    break;
                case 542:
                    await DoEdibleFish();
                    break;
                case var x:
                    Error($"Proceeded with unsupported mission {x}, giving up");
                    break;
            }

        }
    }

    private async Task DoEdibleFish()
    {
        await MoveTo(new(-299.574f, 24.338f, -101.603f), 1, mount: true, dismount: true);
        await FaceDirection(new(1, 0, 0));

        await NextFrame(10);

        // etheirys ball
        SetBait(45966);
        ClearAHPresets();
        SetAHPreset(_ahPresets["Critical Fish"]);

        Util.UseAction(289);

        await WaitFlipflop(() => Svc.Condition[ConditionFlag.Gathering], "Fishing");

        await MoveTo(new(-461.488f, 40.037f, -66.527f), 3.5f, mount: true, dismount: true);

        var turnin = Svc.ObjectTable.Where(t => t.DataId == 0x1EBD9A && t.IsTargetable).MinBy(t => t.Position.DistanceFromPlayerXZ());
        ErrorIf(turnin == null, "No collection point!");

        Util.InteractWithObject(turnin);

        await WaitWhile(() => _iceState.InvokeFunc() == "ManualMode", "WaitTurnin");
    }

    private async Task DoMoonGel()
    {
        await MoveTo(new(-299.574f, 24.338f, -101.603f), 1, mount: true, dismount: true);
        await FaceDirection(new(1, 0, 0));

        // for some reason it takes a moment to face the fishing spot, maybe to do with rotation interpolation bs
        await NextFrame(10);

        // stellar salmon roe
        SetBait(45960);
        ClearAHPresets();
        SetAHPreset(_ahPresets["Refined Moon Gel"]);

        // cast line
        Util.UseAction(289);

        await WaitFlipflop(() => Svc.Condition[ConditionFlag.Gathering], "Fishing");

        await ChangeClass(Svc.ExcelRow<ClassJob>(14));

        // craft moon gel
        CraftItem(36682, 2);

        await WaitFlipflop(_artisanBusy.InvokeFunc, "Crafting");

        if (!Util.IsAddonReady("WKSMissionInfomation"))
        {
            unsafe
            {
                Util.GetAddonByName("WKSHud")->FireCallbackInt(11);
            }
            await WaitAddon("WKSMissionInfomation");
        }

        unsafe
        {
            Util.GetAddonByName("WKSMissionInfomation")->FireCallbackInt(11);
        }

        await WaitWhile(() => Svc.Condition.Any(ConditionFlag.Crafting, ConditionFlag.PreparingToCraft), "Turnin");
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

    public override void DrawDebug()
    {
        ImGui.Checkbox("Stop after current mission", ref _shouldStop);
    }
}
