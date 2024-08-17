using Dalamud.Configuration;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using GatherPointId = uint;
using ItemId = uint;
using RouteId = int;
using SingleGatherPointId = string;

namespace xgather;

public enum GatherClass : uint
{
    None = 0,
    BTN = 1,
    MIN = 2,
    FSH = 3
}

internal static class GCExt
{
    public static ClassJob? GetClassJob(this GatherClass gc) => gc switch
    {
        GatherClass.BTN => Svc.ExcelRow<ClassJob>(17),
        GatherClass.MIN => Svc.ExcelRow<ClassJob>(16),
        GatherClass.FSH => Svc.ExcelRow<ClassJob>(18),
        _ => null
    };
}

[Serializable]
public record struct GatherPoint(uint DataId, Vector3 Position, Vector3? GatherLocation)
{
    public GatherPoint(IGameObject obj) : this(obj.DataId, obj.Position, null) { }

    [JsonIgnore] public readonly Vector3 NaviPosition => GatherLocation ?? Position;
    [JsonIgnore] public readonly float DistanceFromPlayer => NaviPosition.DistanceFromPlayer();
}

[Serializable]
public class GatherPointBase
{
    public required uint Zone;
    public required string Label;
    public required List<GatherPointId> Nodes;
    public List<ItemId> Items = [];
    public required GatherClass Class;
    public uint GatheringPointBaseId;

    public void Debug()
    {
        ImGui.Text($"Zone: {Zone} ({TerritoryType.Name})");
        ImGui.Text($"Label: {Label}");
        ImGui.Text($"Nodes: {string.Join(", ", Nodes)}");
        ImGui.Text($"Items: {string.Join(", ", Items)}");
        ImGui.Text($"Required class: {Class}");
        ImGui.Text($"Gather point base ID: {GatheringPointBaseId}");
    }

    [JsonIgnore] public bool IsUnderwater => Items.Any(x => x >= 20000);

    [JsonIgnore] public TerritoryType TerritoryType => Svc.ExcelRow<TerritoryType>(Zone)!;

    public bool MissingPoints() => Nodes.Any(x => Svc.Config.GetKnownPoints(x).Any(y => y.GatherLocation == null));
    public bool Contains(uint dataId) => Nodes.Contains(dataId);
    public Vector3 GatherAreaCenter()
    {
        var exp = Svc.ExcelRow<ExportedGatheringPoint>(GatheringPointBaseId);
        return new(exp.X, 0, exp.Y);
    }

    public GatheringPointTransient? GetTransient()
    {
        var gpt = Svc.ExcelRow<GatheringPointTransient>(Nodes[0]);
        if (gpt == null)
            return null;

        if (gpt.EphemeralStartTime == 65535 && gpt.EphemeralEndTime == 65535 && gpt.GatheringRarePopTimeTable.Row == 0)
            return null;

        return gpt;
    }

    public (DateTime start, DateTime end) NextReadyIn()
    {
        return (DateTime.MinValue, DateTime.MaxValue);
    }
}

[Serializable]
public record struct TodoList(string? Name, Dictionary<uint, TodoItem> Items, bool Ephemeral = false)
{
    public void Add(TodoItem item)
    {
        if (Items.TryGetValue(item.ItemId, out var entry))
            Items[item.ItemId] = entry with { Required = entry.Required + item.Required };
        else
            Items.Add(item.ItemId, item);
    }

    public void UpdateRequired(uint itemId, uint quantity)
    {
        if (quantity == 0)
        {
            Items.Remove(itemId);
            return;
        }

        if (Items.TryGetValue(itemId, out var entry))
            Items[itemId] = entry with { Required = Math.Max(1u, quantity) };
    }

    public void Debug()
    {
        ImGui.Text($"Todo list: {Name}");
        foreach (var it in Items)
        {
            UI.Helpers.DrawItem(it.Key);
            ImGui.SameLine();
            ImGui.Text($" - {it.Value.Required}");
        }
    }
}

[Serializable]
public record struct TodoItem(uint ItemId, uint Required)
{
    [JsonIgnore]
    public readonly unsafe uint QuantityOwned => (uint)InventoryManager.Instance()->GetInventoryItemCount(ItemId, minCollectability: (short)Utils.GetMinCollectability(ItemId));

    [JsonIgnore]
    public readonly uint QuantityNeeded => QuantityOwned >= Required ? 0 : Required - QuantityOwned;
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    [JsonProperty] private readonly SortedDictionary<ItemId, List<RouteId>> ItemLookup = [];
    [JsonProperty] private readonly Dictionary<RouteId, GatherPointBase> GPBase = [];
    [JsonProperty] private readonly Dictionary<GatherPointId, HashSet<SingleGatherPointId>> GatherPointObjects = [];
    [JsonProperty] private readonly Dictionary<SingleGatherPointId, GatherPoint> GatherPointObjectsById = [];

    [JsonIgnore] public IEnumerable<(RouteId, GatherPointBase)> AllGatherPointGroups => GPBase.Select(x => (x.Key, x.Value));
    [JsonIgnore] public int GatherPointGroupCount => GPBase.Count;
    [JsonIgnore]
    public IEnumerable<(ItemId, IEnumerable<(RouteId, GatherPointBase)>)> ItemDB
    {
        get
        {
            foreach ((var it, var rtes) in ItemLookup)
                yield return (it, rtes.Select(x => (x, GPBase[x])));
        }
    }

    public List<TodoList> Lists = [];

    public IEnumerable<GatherPointBase> GetGatherPointGroupsForItem(uint itemId)
    {
        if (ItemLookup.TryGetValue(itemId, out var routes))
            return routes.Select(x => GPBase[x]);

        return [];
    }

    public bool Fly = true;

    public bool OverlayOpen = false;

    public string ItemSearchText = "";

    public RouteId SelectedRoute = -1;

    public int Version { get; set; } = 0;

    public void Save()
    {
        Svc.PluginInterface.SavePluginConfig(this);
    }

    public static SingleGatherPointId GetKey(uint dataId, Vector3 pos) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes($"{dataId} {(int)pos.X} {(int)pos.Y} {(int)pos.Z}")), 0, 16);

    private void AddGPBase(GatherPointBase gpb)
    {
        var nextRouteId = GPBase.Count == 0 ? 0 : GPBase.Keys.Max() + 1;
        GPBase.Add(nextRouteId, gpb);
        foreach (var itemId in gpb.Items)
        {
            ItemLookup.TryAdd(itemId, []);
            ItemLookup[itemId].Add(nextRouteId);
        }
    }

    /*
    public void DeleteRoute(RouteId routeId)
    {
        foreach (var itemId in ItemLookup.Keys.ToList())
        {
            ItemLookup[itemId].Remove(routeId);
            if (ItemLookup[itemId].Count == 0)
                ItemLookup.Remove(itemId);
        }
        GPBase.Remove(routeId);
    }
    */

    public bool TryGetGatherPointBase(RouteId routeId, [MaybeNullWhen(false)] out GatherPointBase route) => GPBase.TryGetValue(routeId, out route);

    public IEnumerable<GatherPoint> GetKnownPoints(GatherPointId dataId)
    {
        if (GatherPointObjects.TryGetValue(dataId, out var points))
            foreach (var p in points)
                if (GatherPointObjectsById.TryGetValue(p, out var gobj))
                    yield return gobj;
    }

    public bool GetFloorPoint(IGameObject obj, out Vector3 point) => GetFloorPoint(GetKey(obj.DataId, obj.Position), out point);

    public bool GetFloorPoint(SingleGatherPointId id, out Vector3 point)
    {
        if (GatherPointObjectsById.TryGetValue(id, out var obj) && obj.GatherLocation != null)
        {
            point = obj.GatherLocation.Value;
            return true;
        }
        point = new();
        return false;
    }

    public void SetFloorPoint(IGameObject obj, Vector3 point) => UpdateFloorPoint(obj, _ => point);

    public void ClearFloorPoint(IGameObject obj) => UpdateFloorPoint(obj, _ => null);

    public void UpdateFloorPoint(IGameObject obj, Func<Vector3?, Vector3?> update)
        => UpdateFloorPoint(obj.DataId, obj.Position, update);

    public void UpdateFloorPoint(uint dataId, Vector3 position, Func<Vector3?, Vector3?> update)
    {
        var id = GetKey(dataId, position);
        if (GatherPointObjectsById.TryGetValue(id, out var gobj))
            GatherPointObjectsById[id] = gobj with { GatherLocation = update(gobj.GatherLocation) };
    }

    public void RecordPosition(IGameObject obj)
    {
        var key = obj.DataId;
        var objId = GetKey(obj.DataId, obj.Position);
        GatherPointObjects.TryAdd(key, []);
        GatherPointObjects[key].Add(objId);
        if (GatherPointObjectsById.TryAdd(objId, new GatherPoint(obj)))
            Svc.Log.Debug($"found NEW entry for {obj.DataId} - position is {obj.Position.X:F10}, {obj.Position.Y:F10}, {obj.Position.Z:F10}");
    }

    public void RecordPositions(IEnumerable<IGameObject> positions)
    {
        foreach (var p in positions)
            RecordPosition(p);
    }

    [JsonIgnore] private static readonly List<uint> IxalQuestNodes = [170, 181, 206, 208, 243, 244];
    [JsonIgnore] private const uint ObsoleteDiademUnlockQuest = 74;
    [JsonIgnore] private static readonly List<uint> GenericDiademNodes = [794, 795, 786, 793, 790, 788, 789, 787, 796, 792];

    public void RegisterGameItems()
    {
        var allGatherPoints = Svc.ExcelSheet<GatheringPoint>()!;
        foreach (var gatherPointGroup in allGatherPoints.GroupBy(gp => gp.GatheringPointBase.Row))
        {
            // diadem nodes don't function like overworld nodes. custom ordered routes should be created for these instead
            if (GenericDiademNodes.Contains(gatherPointGroup.Key))
                continue;

            var gpBase = Svc.ExcelRow<GatheringPointBase>(gatherPointGroup.Key)!;

            // fishing spots
            //if (gpBase.GatheringType.Row is 4 or 5)
            //    continue;

            // i think these are for ixal quests
            if (IxalQuestNodes.Contains(gpBase.RowId))
                continue;

            // gathering point is not associated with a territory type, meaning it doesn't appear anywhere in the game
            // (for old diadem stuff etc)
            if (gatherPointGroup.First().TerritoryType.Row < 128)
                continue;

            // territorytype is not wrong but placename is, still a bad mat
            if (gatherPointGroup.First().PlaceName.Row == 0)
                continue;

            // more old diadem stuff
            if (gatherPointGroup.First().GatheringSubCategory.Value?.Quest.Row == ObsoleteDiademUnlockQuest)
                continue;

            // already got a route for this
            if (AllGatherPointGroups.Any(r => r.Item2.GatheringPointBaseId == gpBase.RowId))
                continue;

            var ttName = gatherPointGroup.First().TerritoryType.Value!.PlaceName.Value!.Name;

            if (ttName == "")
                throw new Exception($"territory type {gatherPointGroup.First().TerritoryType.Row} is wrong");

            var gatherType = gpBase.GatheringType.Row switch
            {
                4 or 5 => "Spearfishing",
                _ => gpBase.GatheringType.Value!.Name
            };

            var label = $"Level {gpBase.GatheringLevel} {gatherType} @ {gatherPointGroup.First().TerritoryType.Value!.PlaceName.Value!.Name}";

            var newGPB = new GatherPointBase()
            {
                Label = label,
                Zone = gatherPointGroup.First().TerritoryType.Row,
                Nodes = gatherPointGroup.Select(x => x.RowId).ToList(),
                GatheringPointBaseId = gpBase.RowId,
                Class = gpBase.GetRequiredClass(),
                Items = gpBase.Item.Where(x => x > 0).Select(it =>
                {
                    if (it >= 20000)
                    {
                        var spearfishItem = Svc.ExcelRow<SpearfishingItem>((uint)it);
                        if (spearfishItem == null)
                            throw new Exception($"{it} is not valid for a SpearfishingItem (GPBase ID: {gpBase.RowId})");
                        return spearfishItem.Item.Row;
                    }
                    else
                    {
                        var gatherit = Svc.ExcelRow<GatheringItem>((uint)it);
                        if (gatherit == null)
                            throw new Exception($"{it} is not valid for a GatheringItem (GPBase ID: {gpBase.RowId})");
                        return (uint)gatherit.Item;
                    }
                }).ToList()
            };

            AddGPBase(newGPB);
        }
    }
}
