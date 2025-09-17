using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace xgather;

[StructLayout(LayoutKind.Explicit, Size = 0x20)]
struct OurMissionEntry
{
    [FieldOffset(0)] public uint MissionUnitId;
    [FieldOffset(4)] public uint IconId;
    [FieldOffset(8)] public nint Unk8;
    [FieldOffset(16)] public uint Unk16;
    [FieldOffset(20)] public AgentWKSMission.MissionFlags Flags;
    [FieldOffset(24)] public byte MissionGroup;
}

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

    public struct MissionData
    {
        public uint Id;
        public string Name;
        public bool Completed;
        public bool Gold;
    }

    public unsafe void Draw()
    {
        //if (Svc.ClientState.LocalPlayer is { } p)
        //{
        //    var (s1, c2) = MathF.SinCos(p.Rotation);
        //    ImGui.TextUnformatted($"rot: {p.Rotation:f3}, sin: {s1:f3}, cos: {c2:f3}");
        //    var orthoR = new Vector3(-c2, 0, s1);
        //    if (ImGui.Button("Rotate"))
        //    {
        //        var target = orthoR + p.Position;
        //        ActionManager.Instance()->AutoFaceTargetPosition(&target);
        //    }
        //}

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

    private List<MissionData> _allMissions = [];
    private bool _init;

    private void DrawWKS()
    {
        var wks = WKSManager.Instance();

        if (_allMissions.Count == 0)
        {
            foreach (var unit in Svc.ExcelSheet<WKSMissionUnit>())
            {
                var unitId = unit.RowId;
                var v17 = (byte)(unitId >> 3);
                var v18 = 1 << ((int)unitId & 7);

                var completed = v18 & *(byte*)((nint)wks + 0xC50 + v17 + 5);
                var gold = v18 & *(byte*)((nint)wks + 0xCD8 + v17 + 5);

                _allMissions.Add(new MissionData()
                {
                    Id = unitId,
                    Name = unit.Name.ToString(),
                    Completed = completed > 0,
                    Gold = gold > 0
                });
            }
        }

        ImGui.TextUnformatted($"{_allMissions.Count} missions");

        ImGui.BeginTable("missions", 4, ImGuiTableFlags.BordersInnerV);
        ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed, 100);
        ImGui.TableSetupColumn("Name");
        ImGui.TableSetupColumn("Completed");
        ImGui.TableSetupColumn("Golded");
        ImGui.TableHeadersRow();

        foreach (var m in _allMissions)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(m.Id.ToString());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(m.Name.ToString());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(m.Completed.ToString());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(m.Gold.ToString());
        }

        ImGui.EndTable();
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
