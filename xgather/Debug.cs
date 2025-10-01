using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Common.Component.BGCollision;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Numerics;

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

    private delegate* unmanaged<BGCollisionModule*, RaycastHit*, Vector3*, Vector3*, float, int, byte> _raycastSimple;

    public Debug()
    {
        _raycastSimple = (delegate* unmanaged<BGCollisionModule*, RaycastHit*, Vector3*, Vector3*, float, int, byte>)Svc.SigScanner.ScanText("E8 ?? ?? ?? ?? 84 C0 75 58 FF C3");
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

    private bool _showFishRay = true;

    private static Vector3 TransformVecByMatrix(Vector3 a2, Matrix4x4 a3)
    {
        var v6 = (a2.X * a3.M13) + (a2.Y * a3.M23) + (a2.Z * a3.M33) + a3.M43;
        var v7 = (a2.X * a3.M12) + (a2.Y * a3.M22) + (a2.Z * a3.M32) + a3.M42;
        var v8 = (a2.Y * a3.M21) + (a2.X * a3.M11) + (a2.Z * a3.M31) + a3.M41;
        return new Vector3()
        {
            X = v8,
            Y = v7,
            Z = v6
        };
    }

    public const uint Success = 0xFF00FF00;
    public const uint Failure = 0xFF00FFFF;

    public unsafe void Draw()
    {
        if (Svc.ClientState.LocalPlayer is not { } player)
            return;

        ImGui.Checkbox("Show fishing spot raycast", ref _showFishRay);

        if (!_showFishRay)
            return;

        var position = player.Position;
        var rotation = player.Rotation;

        var v8 = MathF.Cos(rotation);
        var v9 = MathF.Sin(rotation);

        var playerRotationMatrix = new Matrix4x4()
        {
            M11 = v8,
            M13 = -v9,
            M31 = v9,
            M33 = v8,
            M22 = 1.0f
        };

        var v43 = new Vector3()
        {
            Z = 2.0f
        };

        // point in 3D space, 2 units above player origin and 2 units forward in facing direction
        var rodPoint = TransformVecByMatrix(v43, playerRotationMatrix);
        rodPoint += position;
        rodPoint.Y += 2;

        static void drawVector(Vector3 a, Vector3 b, uint color)
        {
            Svc.GameGui.WorldToScreen(a, out var posScreen);
            Svc.GameGui.WorldToScreen(b, out var normalScreen);
            ImGui.GetBackgroundDrawList().AddLine(posScreen, normalScreen, color, 3);
        }

        static void drawPoint(Vector3 a, uint color)
        {
            if (Svc.GameGui.WorldToScreen(a, out var pos))
                ImGui.GetBackgroundDrawList().AddCircle(pos, 10, color, 2f);
        }

        var playerRayOrigin = new Vector3()
        {
            X = position.X,
            Y = position.Y + 0.87f,
            Z = position.Z
        };

        void drawCast(Vector3 pointA, Vector3? pointB, uint color)
        {
            if (pointB is { } p)
            {
                drawVector(playerRayOrigin, pointA, color);
                drawVector(pointA, p, color);
                drawPoint(p, color);
            }
            else
            {
                drawVector(playerRayOrigin, pointA, color);
                drawPoint(pointA, color);
            }
        }

        var rodDirection = rodPoint - playerRayOrigin;
        rodDirection /= rodDirection.Length();

        if (BGCollisionModule.RaycastMaterialFilter(playerRayOrigin, rodDirection, out var hitInfo, 2))
        {
            // player line of sight is blocked by object
            drawCast(hitInfo.Point, null, Failure);
            return;
        }

        var fishRay = TransformVecByMatrix(new Vector3(0, -80, 40), playerRotationMatrix);

        var fishRayLen = fishRay.Length();
        var fishRayNormalized = fishRay / fishRayLen;

        RaycastHit castHitInfo;

        if (_raycastSimple(Framework.Instance()->BGCollisionModule, &castHitInfo, &rodPoint, &fishRayNormalized, fishRayLen, 1) == 1)
        {
            // point is fishable
            if ((castHitInfo.Material & 0x8000) != 0)
            {
                drawCast(rodPoint, castHitInfo.Point, Success);
                return;
            }

            if (castHitInfo.Material == 0x2000)
            {
                // dude what the fuck is this
                var extraHitTest = castHitInfo.Point + (fishRayNormalized * 0.01f);
                RaycastHit castHitInfo2;

                if (_raycastSimple(Framework.Instance()->BGCollisionModule, &castHitInfo2, &extraHitTest, &fishRayNormalized, fishRayLen, 1) == 1)
                {
                    drawCast(rodPoint, castHitInfo2.Point, (castHitInfo2.Material & 0x8000) == 0 ? Failure : Success);
                    return;
                }
                return;
            }

            drawCast(rodPoint, castHitInfo.Point, Failure);
        }

        //if (Svc.TextureProvider.GetFromGame("ui/uld/WKSMission_hr1.tex") is { } tex)
        //{
        //    if (tex.TryGetWrap(out var wrap, out var exc))
        //    {
        //        ImGui.Image(wrap.Handle, new Vector2(32, 32), new Vector2(0.2347f, 0.3500f), new Vector2(0.2959f, 0.6500f));
        //    }
        //}

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
