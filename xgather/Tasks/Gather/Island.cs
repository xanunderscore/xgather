using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.MJI;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Interop;
using FFXIVClientStructs.STD;
using Lumina.Data.Files;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using xgather.GameData;
using xgather.Utils;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace xgather.Tasks.Gather;

public class Island : AutoTask
{
    private readonly Dictionary<uint, Vector3> _itemsLeft = [];
    private readonly Dictionary<uint, IslandGatherPoint> _itemsFound = [];

    // most (or all) of the cotton bushes have two identical entries in the lgb instead of 1, who knows why
    private static readonly HashSet<uint> _badObjects = [
        0x9017F0,
        0x9017F1,
        0x9017F2,
        0x9017F3,
        0x9017F4,
        0x9017F5,
        0x9017F6,
        0x9017F7,
        0x9017F8,
        0x9017F9,
        0x9017FA,
        0x9017FB,
        0x9017FC,
        0x9017FD,
        0x9017FE,
        0x9017FF,
        0x901800,
        0x901801,
        0x901802,
        0x901803,
        0x901CE0,
    ];

    public Island()
    {
        var layout = Svc.Data.GetFile<LgbFile>("bg/ffxiv/hou_xx/hou/h1m2/level/planlive.lgb");
        if (layout == null)
        {
            Error("Unable to load Island Sanctuary level data, island nodes will be unavailable");
            return;
        }

        foreach (var layer in layout.Layers)
        {
            if (!layer.Name.EndsWith("_GATHERING"))
                continue;

            foreach (var obj in layer.InstanceObjects)
            {
                if (obj.Object is Lumina.Data.Parsing.Layer.LayerCommon.SharedGroupInstanceObject)
                {
                    if (_badObjects.Contains(obj.InstanceId))
                        continue;

                    _itemsLeft.Add(obj.InstanceId, Util.Convert(obj.Transform.Translation));
                }
            }
        }
    }

    private readonly Dictionary<int, int> itemsNeeded = [];
    private readonly Queue<uint> unloadedObjects = [];

    private ushort[] _usedMaterials = [];

    protected override async Task Execute()
    {
        ErrorIf(!IsleUtils.OnIsland(), "Not on Island Sanctuary");

        if (!IsleUtils.IsScheduleOpen())
        {
            IsleUtils.ToggleCraftSchedule();
            await WaitWhile(() => !IsleUtils.IsScheduleOpen(), "WaitSchedule");
        }
        _usedMaterials = IsleUtils.GetUsedMaterials();
        IsleUtils.ToggleCraftSchedule();
        await SetGatherMode();

        while (true)
        {
            CalculateNeeded();
            if (itemsNeeded.Count == 0)
                return;

            await GatherNextMaterial();
        }
    }

    private void CalculateNeeded()
    {
        itemsNeeded.Clear();

        var total = IsleUtils.GetIsleventory();
        for (var slot = 0; slot < _usedMaterials.Length; slot++)
            // items with quantity -1 are produce/leavings
            if (total[slot] >= 0 && total[slot] < _usedMaterials[slot])
                itemsNeeded[slot] = _usedMaterials[slot] - total[slot];
    }

    private void SkipNode(uint layoutId)
    {
        unloadedObjects.Enqueue(layoutId);
        while (unloadedObjects.Count > 10)
            unloadedObjects.TryDequeue(out var _);
    }

    private async Task GatherRandomNode()
    {
        var closest = Svc.ObjectTable.Where(obj => obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.CardStand && obj.IsTargetable).MinBy(obj => obj.Position.DistanceFromPlayer());
        ErrorIf(closest == null, "No nodes found at all");

        var shouldMount = closest.Position.DistanceFromPlayerXZ() > 20;
        var fly = shouldMount || Svc.Condition[ConditionFlag.InFlight];

        await MoveTo(closest.Position, 4, mount: shouldMount, fly: fly, dismount: true);

        await GatherObj(closest);

        SkipNode(Util.GetLayoutId(closest));
    }

    private async Task GatherNextMaterial()
    {
        var availableMaterials = itemsNeeded.Keys;
        var eligibleNodeTypes = availableMaterials.SelectMany(ItemDatabase.GetIslandNodesForMaterial);
        var eligibleNodes = eligibleNodeTypes.SelectMany(t => Svc.ItemDB.IslandNodesByNameId[t]);

        var closest = eligibleNodes.Where(n => !unloadedObjects.Contains(n.LayoutId)).MinBy(n => n.Position.DistanceFromPlayer());

        if (closest == null)
        {
            Log($"No eligible nodes found for any needed material, gathering random nodes to trigger respawn");
            await GatherRandomNode();
            return;
        }

        using var _ = BeginScope($"Gathering {closest.Name}");

        var nodeIsUnloaded = false;

        bool checkUnloaded()
        {
            var obj = Svc.ObjectTable.FirstOrDefault(t => Util.GetLayoutId(t) == closest.LayoutId);
            // objects at the very edge of load range start as untargetable even if they aren't despawned
            if (obj?.IsTargetable == false && obj.Position.DistanceFromPlayerXZ() < 80)
            {
                Svc.Log.Debug($"object {obj} is not targetable");
                nodeIsUnloaded = true;
                return true;
            }

            return false;
        }

        var shouldMount = closest.Position.DistanceFromPlayerXZ() > 20;
        var fly = shouldMount || Svc.Condition[ConditionFlag.InFlight];

        await MoveTo(closest.Position, 4, mount: shouldMount, fly: fly, dismount: true, interrupt: checkUnloaded);

        if (nodeIsUnloaded)
        {
            Log($"{closest.Name} @ {closest.Position} is untargetable, moving to next candidate");
            SkipNode(closest.LayoutId);
            return;
        }

        await GatherObj(Svc.ObjectTable.First(t => Util.GetLayoutId(t) == closest.LayoutId));

        unloadedObjects.Clear();
    }

    private async Task GatherObj(IGameObject obj)
    {
        Util.InteractWithObject(obj);

        await WaitCondition(() => Svc.Condition[ConditionFlag.OccupiedInQuestEvent], "Gather");
    }

    private async Task SetGatherMode()
    {
        unsafe
        {
            if (MJIManager.Instance()->CurrentMode == 1)
                return;

            var hud = Util.GetAddonByName("MJIHud");
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

    public override void DrawDebug()
    {
        foreach (var obj in unloadedObjects)
        {
            var pt = Svc.ItemDB.IslandNodesByLayoutId[obj];
            var dl = ImGui.GetBackgroundDrawList();
            var pointA = Svc.Player!.Position;
            var pointB = pt.Position;
            Svc.GameGui.WorldToScreen(pointA, out var screenA);
            Svc.GameGui.WorldToScreen(pointB, out var screenB);
            dl.AddLine(screenA, screenB, 0xFF0000FF, 2);
            dl.AddText(screenB, 0xFF00FF00, pt.LayoutId.ToString("X6"));
        }
    }

    /*
    public override unsafe void DrawDebug()
    {
        if (Svc.Player == null)
            return;

        if (ImGui.Button("Copy items to clipboard"))
        {
            var js = Newtonsoft.Json.JsonConvert.SerializeObject(_itemsFound);
            ImGui.SetClipboardText(js);
        }

        foreach (var obj in Svc.ObjectTable)
        {
            if (obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.CardStand)
            {
                var go = (GameObject*)obj.Address;
                var layoutId = go->LayoutId;
                if (_itemsLeft.Remove(layoutId))
                {
                    Svc.Log.Debug($"found gathering point {obj}, removing");
                    _itemsFound.Add(layoutId, new(go->GetNameId(), obj.Name.ToString(), obj.DataId, obj.Position));
                }
            }
        }

        var unfoundItems = _itemsLeft.OrderBy(v => (v.Value - Svc.Player!.Position).LengthSquared()).Take(5);

        var dl = ImGui.GetBackgroundDrawList();

        foreach (var (instanceId, pt) in unfoundItems)
        {
            var pointA = Svc.Player.Position;
            var pointB = pt;

            Svc.GameGui.WorldToScreen(pointA, out var screenA);
            Svc.GameGui.WorldToScreen(pointB, out var screenB);
            dl.AddLine(screenA, screenB, 0xFF00FF00, 2);

            dl.AddText(screenB, 0xFF00FF00, instanceId.ToString("X6"));
        }
    }
    */
}

public static class IsleUtils
{
    public static unsafe bool OnIsland() => MJIManager.Instance()->IsPlayerInSanctuary;

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
