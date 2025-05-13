using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.MJI;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Interop;
using FFXIVClientStructs.STD;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace xgather.Tasks;

public class GatherIsland : AutoTask
{
    protected override async Task Execute()
    {
        ErrorIf(!IsleUtils.OnIsland(), "Not on Island Sanctuary");

        if (!IsleUtils.IsScheduleOpen())
        {
            IsleUtils.ToggleCraftSchedule();
            await WaitWhile(() => !IsleUtils.IsScheduleOpen(), "WaitSchedule");
        }
        var used = IsleUtils.GetUsedMaterials();
        IsleUtils.ToggleCraftSchedule();

        var total = IsleUtils.GetIsleventory();
        var needed = new Dictionary<int, int>();
        for (var slot = 0; slot < used.Length; slot++)
        {
            // items with quantity -1 are produce/leavings
            if (total[slot] >= 0 && total[slot] < used[slot])
                needed[slot] = used[slot] - total[slot];
        }

        if (needed.Count == 0)
        {
            Log("Nothing to do");
            return;
        }

        await SetGatherMode();

        foreach (var (k, v) in needed)
        {
            var name = Svc.ExcelRow<MJIItemPouch>((uint)k).Item.Value.Name;
            Log($"Gathering {v}x {name}");
        }
    }

    private async Task SetGatherMode()
    {
        if (IsleUtils.SetGatherMode())
        {
            await WaitWhile(() => !IsleUtils.IsContextMenuOpen(), "MenuOpen");
            IsleUtils.HideMenu();
            await WaitWhile(IsleUtils.IsContextMenuOpen, "MenuClose");
        }
    }
}

public static class IsleUtils
{
    public static bool OnIsland()
    {
        unsafe
        {
            return GameMain.Instance()->CurrentTerritoryIntendedUseId == 49;
        }
    }

    public static void ToggleCraftSchedule()
    {
        unsafe
        {
            var addon = RaptureAtkUnitManager.Instance()->GetAddonByName("MJIHud");
            addon->FireCallbackInt(20);
        }
    }

    public static bool IsScheduleOpen()
    {
        unsafe
        {
            var agent = AgentMJICraftSchedule.Instance();
            return agent->IsAgentActive() && agent->Data != null && agent->Data->UpdateState == 2;
        }
    }

    public static ushort[] GetUsedMaterials()
    {
        unsafe
        {
            return AgentMJICraftSchedule.Instance()->Data->MaterialUse.Entries[2].UsedAmounts.ToArray();
        }
    }

    public static int[] GetIsleventory()
    {
        var counts = new int[109];
        Array.Fill(counts, -1);
        unsafe
        {
            foreach (var mat in ((PouchInventoryData*)AgentMJIPouch.Instance()->InventoryData)->Materials)
            {
                counts[mat.Value->RowId] = mat.Value->StackSize;
            }
        }
        return counts;
    }

    public static bool SetGatherMode()
    {
        unsafe
        {
            if (MJIManager.Instance()->CurrentMode != 1)
            {
                var hud = RaptureAtkUnitManager.Instance()->GetAddonByName("MJIHud");
                var hv = stackalloc AtkValue[2];
                hv[0].Type = ValueType.Int;
                hv[0].Int = 11;
                hv[1].Type = ValueType.Int;
                hud->FireCallback(2, hv);
                var ctx = RaptureAtkUnitManager.Instance()->GetAddonByName("ContextIconMenu");
                var cv = stackalloc AtkValue[5];
                cv[0].Type = ValueType.Int;
                cv[1].Type = ValueType.Int;
                cv[1].Int = 1;
                cv[2].Type = ValueType.UInt;
                cv[2].UInt = 82042;
                cv[3].Type = ValueType.UInt;
                ctx->FireCallback(5, cv, true);
                return true;
            }
        }
        return false;
    }

    public static bool IsContextMenuOpen()
    {
        unsafe
        {
            return RaptureAtkUnitManager.Instance()->GetAddonByName("ContextIconMenu")->IsVisible;
        }
    }

    public static void HideMenu()
    {
        unsafe
        {
            var ctx = RaptureAtkUnitManager.Instance()->GetAddonByName("ContextIconMenu");
            ctx->FireCallbackInt(-1);
        }
    }
}

[StructLayout(LayoutKind.Explicit, Size = 0x1A0)]
public struct PouchInventoryData
{
    [FieldOffset(0x90 + 40)] public StdVector<Pointer<PouchInventoryItem>> Materials;
    [FieldOffset(0xA8 + 40)] public StdVector<Pointer<PouchInventoryItem>> Produce;
}

[StructLayout(LayoutKind.Explicit, Size = 0x80)]
public struct PouchInventoryItem
{
    [FieldOffset(0x00)] public uint ItemId;
    [FieldOffset(0x04)] public uint IconId;
    [FieldOffset(0x08)] public int RowId;
    [FieldOffset(0x0C)] public int StackSize;
    [FieldOffset(0x10)] public int MaxStackSize;
    [FieldOffset(0x14)] public byte InventoryIndex;
    [FieldOffset(0x15)] public byte ItemCategory;
    [FieldOffset(0x16)] public byte Sort;

    [FieldOffset(0x20)] public Utf8String Name;
}
