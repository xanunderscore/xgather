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
        unsafe
        {
            if (MJIManager.Instance()->CurrentMode == 1)
                return;

            var hud = Util.Util.GetAddonByName("MJIHud");
            var hv = stackalloc AtkValue[2];
            hv[0].Type = ValueType.Int;
            hv[0].Int = 11;
            hv[1].Type = ValueType.Int;
            hud->FireCallback(2, hv);
        }

        await WaitAddon("ContextIconMenu");

        unsafe
        {
            var icon = RaptureAtkUnitManager.Instance()->GetAddonByName("ContextIconMenu");
            var cv = stackalloc AtkValue[5];
            cv[0].Type = ValueType.Int;
            cv[1].Type = ValueType.Int;
            cv[1].Int = 1;
            cv[2].Type = ValueType.UInt;
            cv[2].UInt = 82042;
            cv[3].Type = ValueType.UInt;
            icon->FireCallback(5, cv, true);
            icon->FireCallbackInt(-1);
        }

        await WaitWhile(IsleUtils.IsContextMenuOpen, "MenuClose");
    }
}

public static class IsleUtils
{
    public static unsafe bool OnIsland() => MJIManager.Instance()->IsPlayerInSanctuary == 1;

    public static unsafe void ToggleCraftSchedule() => RaptureAtkUnitManager.Instance()->GetAddonByName("MJIHud")->FireCallbackInt(20);

    public static unsafe bool IsScheduleOpen()
    {
        var agent = AgentMJICraftSchedule.Instance();
        return agent->IsAgentActive() && agent->Data != null && agent->Data->UpdateState == 2;
    }

    public static unsafe ushort[] GetUsedMaterials() => AgentMJICraftSchedule.Instance()->Data->MaterialUse.Entries[2].UsedAmounts.ToArray();

    public static unsafe int[] GetIsleventory()
    {
        var counts = new int[109];
        Array.Fill(counts, -1);
        foreach (var mat in ((PouchInventoryData*)AgentMJIPouch.Instance()->InventoryData)->Materials)
            counts[mat.Value->RowId] = mat.Value->StackSize;
        return counts;
    }

    public static unsafe bool IsContextMenuOpen() => RaptureAtkUnitManager.Instance()->GetAddonByName("ContextIconMenu")->IsVisible;

    public static unsafe void HideMenu() => RaptureAtkUnitManager.Instance()->GetAddonByName("ContextIconMenu")->FireCallbackInt(-1);
}

[StructLayout(LayoutKind.Explicit, Size = 0x1A0)]
public struct PouchInventoryData
{
    [FieldOffset(0x90 + 40)] public StdVector<Pointer<PouchInventoryItem>> Materials;
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
