using System;

namespace xgather;

public unsafe class Debug : IDisposable
{
    //private readonly Hook<ActionManager.Delegates.UseAction> _useActionHook;
    //private readonly Hook<ActionManager.Delegates.UseActionLocation> _useActionLocationHook;
    //private readonly Hook<ResolveTargetDelegate> _resolveTargetHook;
    //private readonly Hook<ActionManager.Delegates.GetActionInRangeOrLoS> _getLosHook;
    //private readonly Hook<CanUseAction2Delegate> _canUse2Hook;
    //private readonly Hook<CanUseGatheringActionDelegate> _canUseGatheringHook;

    //private delegate GameObject* ResolveTargetDelegate(ActionManager* thisPtr, uint spellId, byte* actionRow, ulong targetId);
    //private delegate char CanUseAction2Delegate(uint spellId, byte* actionRow, GameObject* target);
    //private delegate ulong CanUseGatheringActionDelegate(BattleChara* player, uint actionId);

    //private delegate* unmanaged<EventFramework*, uint> _getUnknownId;

    public Debug()
    {
        //_useActionHook = Svc.Hook.HookFromAddress<ActionManager.Delegates.UseAction>(ActionManager.Addresses.UseAction.Value, UseActionDetour);
        //_useActionLocationHook = Svc.Hook.HookFromAddress<ActionManager.Delegates.UseActionLocation>(ActionManager.Addresses.UseActionLocation.Value, UseActionLocationDetour);
        //_resolveTargetHook = Svc.Hook.HookFromSignature<ResolveTargetDelegate>("48 89 74 24 ?? 48 89 7C 24 ?? 41 56 48 83 EC 20 48 8B 35 ?? ?? ?? ?? 49 8B F8", ResolveTargetDetour);
        //_getLosHook = Svc.Hook.HookFromAddress<ActionManager.Delegates.GetActionInRangeOrLoS>(ActionManager.Addresses.GetActionInRangeOrLoS.Value, GetActionInRangeDetour);
        //_canUse2Hook = Svc.Hook.HookFromSignature<CanUseAction2Delegate>("E8 ?? ?? ?? ?? 84 C0 0F 84 ?? ?? ?? ?? 80 7E 34 06", CanUseAction2Detour);
        //_canUseGatheringHook = Svc.Hook.HookFromSignature<CanUseGatheringActionDelegate>("E8 ?? ?? ?? ?? 85 C0 0F 85 ?? ?? ?? ?? 4D 8B CD 44 8B C3", CanUseGatheringActionDetour);

        //_getUnknownId = (delegate* unmanaged<EventFramework*, uint>)Svc.SigScanner.ScanText("E8 ?? ?? ?? ?? 39 43 20");

        //_getRowHook.Enable();

        //_useActionHook.Enable();
        //_useActionLocationHook.Enable();
        //_resolveTargetHook.Enable();
        //_getLosHook.Enable();
        //_canUse2Hook.Enable();
        //_canUseGatheringHook.Enable();
    }

    public unsafe void Draw()
    {
        //var ev = EventFramework.Instance()->GetEventHandlerById(0x3E0000);
        //if (ev != null)
        //{
        //    var stat = ev->LuaStatus;
        //    var eid = ev->GetEventId();
        //    ImGui.TextUnformatted($"Event ID: {eid.Id} {eid.EntryId}");
        //    ImGui.TextUnformatted($"Lua status: {stat}");
        //}

        //if (ImGuiComponents.IconButton(Dalamud.Interface.FontAwesomeIcon.Camera))
        //{
        //    var cam = CameraManager.Instance()->GetActiveCamera()->SceneCamera.RenderCamera;
        //    cam->IsOrtho = !cam->IsOrtho;
        //}
        //var id = _getUnknownId(EventFramework.Instance());
        //var gp = (GatheringPointEventHandler*)EventFramework.Instance()->GetEventHandlerById(id);
        //if (gp != null)
        //{
        //    Svc.Log.Debug($"scene: {gp->Scene}");
        //}
        //else
        //    ImGui.Text($"Scene ({id:X}): nothing");
    }

    /*
    private bool UseActionDetour(ActionManager* thisPtr, ActionType actionType, uint actionId, ulong targetId, uint extraParam, ActionManager.UseActionMode mode, uint comboRouteId, bool* outOptAreaTargeted)
    {
        Svc.Log.Debug($"UseAction({actionType}, {actionId}, {targetId}, {extraParam}, {mode}, {comboRouteId}) = ...");
        var result = _useActionHook.Original(thisPtr, actionType, actionId, targetId, extraParam, mode, comboRouteId, outOptAreaTargeted);
        Svc.Log.Debug($"...{result}");
        return result;
    }

    private bool UseActionLocationDetour(ActionManager* thisPtr, ActionType actionType, uint actionId, ulong targetId, Vector3* location, uint extraParam, byte a7)
    {
        Svc.Log.Debug($"UseActionLocation({actionType}, {actionId}, {targetId}, {*location}, {extraParam}, {a7}) = ...");
        var result = _useActionLocationHook.Original(thisPtr, actionType, actionId, targetId, location, extraParam, a7);
        Svc.Log.Debug($"...{result}");
        return result;
    }

    private GameObject* ResolveTargetDetour(ActionManager* thisPtr, uint spellId, byte* row, ulong targetId)
    {
        var tgt = _resolveTargetHook.Original(thisPtr, spellId, row, targetId);
        Svc.Log.Debug($"ResolveTarget({spellId}, 0x{(nint)row:X}, {targetId:X}) = {(nint)tgt:X}");
        if (tgt != null)
        {
            var id = tgt->GetGameObjectId();
            Svc.Log.Debug($"target ID: {id.Id:X}");
        }
        return tgt;
    }

    private uint GetActionInRangeDetour(uint actionId, GameObject* src, GameObject* target)
    {
        var x = _getLosHook.Original(actionId, src, target);
        Svc.Log.Debug($"GetActionInRangeOrLoS({actionId}, {(nint)src:X}, {(nint)target:X}) = {x}");
        return x;
    }

    private char CanUseAction2Detour(uint actionId, byte* row, GameObject* target)
    {
        var result = _canUse2Hook.Original(actionId, row, target);
        Svc.Log.Debug($"CanUseActionOnTarget2({actionId}, {(nint)row:X}, {(nint)target:X}) = {(byte)result}");
        return result;
    }

    private ulong CanUseGatheringActionDetour(BattleChara* player, uint actionId)
    {
        var x = _canUseGatheringHook.Original(player, actionId);
        if (actionId == 22184)
            Svc.Log.Debug($"CanUseGatheringAction({(nint)player:X}, {actionId}) = {x}");
        return x;
    }
    */

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        //_useActionHook.Dispose();
        //_useActionLocationHook.Dispose();
        //_resolveTargetHook.Dispose();
        //_getLosHook.Dispose();
        //_canUse2Hook.Dispose();
        //_canUseGatheringHook.Dispose();
        // cleanup hooks...
    }
}
