using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using System;
using System.Numerics;

namespace xgather;

public unsafe class Debug : IDisposable
{
    private readonly Hook<ActionManager.Delegates.UseAction> _useActionHook;
    private readonly Hook<ActionManager.Delegates.UseActionLocation> _useActionLocationHook;
    private readonly Hook<ResolveTargetDelegate> _resolveTargetHook;
    private readonly Hook<ActionManager.Delegates.GetActionInRangeOrLoS> _getLosHook;
    private readonly Hook<CanUseAction2Delegate> _canUse2Hook;

    private delegate GameObject* ResolveTargetDelegate(ActionManager* thisPtr, uint spellId, byte* actionRow, ulong targetId);
    private delegate char CanUseAction2Delegate(uint spellId, byte* actionRow, GameObject* target);

    public Debug()
    {
        _useActionHook = Svc.Hook.HookFromAddress<ActionManager.Delegates.UseAction>(ActionManager.Addresses.UseAction.Value, UseActionDetour);
        _useActionLocationHook = Svc.Hook.HookFromAddress<ActionManager.Delegates.UseActionLocation>(ActionManager.Addresses.UseActionLocation.Value, UseActionLocationDetour);
        _resolveTargetHook = Svc.Hook.HookFromSignature<ResolveTargetDelegate>("48 89 74 24 ?? 48 89 7C 24 ?? 41 56 48 83 EC 20 48 8B 35 ?? ?? ?? ?? 49 8B F8", ResolveTargetDetour);
        _getLosHook = Svc.Hook.HookFromAddress<ActionManager.Delegates.GetActionInRangeOrLoS>(ActionManager.Addresses.GetActionInRangeOrLoS.Value, GetActionInRangeDetour);
        _canUse2Hook = Svc.Hook.HookFromSignature<CanUseAction2Delegate>("E8 ?? ?? ?? ?? 84 C0 0F 84 ?? ?? ?? ?? 80 7E 34 06", CanUseAction2Detour);

        //_useActionHook.Enable();
        //_useActionLocationHook.Enable();
        //_resolveTargetHook.Enable();
        //_getLosHook.Enable();
        //_canUse2Hook.Enable();
    }

    public void Draw()
    {
    }

    private bool UseActionDetour(ActionManager* thisPtr, ActionType actionType, uint actionId, ulong targetId, uint extraParam, ActionManager.UseActionMode mode, uint comboRouteId, bool* outOptAreaTargeted)
    {
        Svc.Log.Debug($"UseAction({actionType}, {actionId}, {targetId}, {extraParam}, {mode}, {comboRouteId}) = ...");
        var result = _useActionHook.Original(thisPtr, actionType, actionId, targetId, extraParam, mode, comboRouteId, outOptAreaTargeted);
        Svc.Log.Debug($"...{result}");
        return result;
    }

    private bool UseActionLocationDetour(ActionManager* thisPtr, ActionType actionType, uint actionId, ulong targetId, Vector3* location, uint extraParam)
    {
        Svc.Log.Debug($"UseActionLocation({actionType}, {actionId}, {targetId}, {*location}, {extraParam}) = ...");
        var result = _useActionLocationHook.Original(thisPtr, actionType, actionId, targetId, location, extraParam);
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

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        _useActionHook.Dispose();
        _useActionLocationHook.Dispose();
        _resolveTargetHook.Dispose();
        _getLosHook.Dispose();
        _canUse2Hook.Dispose();
        // cleanup hooks...
    }
}
