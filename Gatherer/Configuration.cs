using Dalamud.Configuration;
using Dalamud.Game.ClientState.Objects.Types;
using Lumina.Excel.GeneratedSheets;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using GatherPointId = uint;
using ItemId = uint;
using RouteId = int;
using SingleGatherPointId = int;

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
public record struct GatherPointObject(Vector3 Position, Vector3? GatherLocation)
{
    public GatherPointObject(IGameObject obj) : this(obj.Position, null) { }

    [JsonIgnore] public readonly Vector3 NaviPosition => GatherLocation ?? Position;
}

[Serializable]
public class GatherRoute
{
    public required uint Zone;
    public required string Label;
    public required List<GatherPointId> Nodes;
    public List<ItemId> Items = [];
    public required bool Fly;
    public required GatherClass Class;

    // if null, this is a user-created ordered gathering route (i.e. diadem)
    public uint? GatheringPointBaseId;

    [JsonIgnore] public bool IsUnderwater => Items.Any(x => x >= 20000);
    [JsonIgnore] public bool Ordered => GatheringPointBaseId == null;

    [JsonIgnore] public TerritoryType TerritoryType => Svc.ExcelRow<TerritoryType>(Zone)!;

    public bool MissingPoints() => Nodes.Any(x => !Svc.Config.GetKnownPoints(x).Any());
    public bool Contains(uint dataId) => Nodes.Contains(dataId);
    public Vector3? GatherAreaCenter()
    {
        if (GatheringPointBaseId is uint id)
        {
            var exp = Svc.ExcelRow<ExportedGatheringPoint>(id);
            return new(exp.X, 0, exp.Y);
        }

        return null;
    }
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    [JsonProperty] private readonly SortedDictionary<ItemId, List<RouteId>> ItemLookup = [];
    [JsonProperty] private readonly Dictionary<RouteId, GatherRoute> Routes = [];
    [JsonProperty] private readonly Dictionary<GatherPointId, HashSet<SingleGatherPointId>> GatherPointObjects = [];
    [JsonProperty] private readonly Dictionary<SingleGatherPointId, GatherPointObject> GatherPointObjectsById = [];

    [JsonIgnore] public IEnumerable<(RouteId, GatherRoute)> AllRoutes => Routes.Select(x => (x.Key, x.Value));
    [JsonIgnore] public int RouteCount => Routes.Count;
    [JsonIgnore]
    public IEnumerable<(ItemId, IEnumerable<(RouteId, GatherRoute)>)> ItemDB
    {
        get
        {
            foreach ((var it, var rtes) in ItemLookup)
                yield return (it, rtes.Select(x => (x, Routes[x])));
        }
    }

    public string ItemSearchText = "";

    public RouteId SelectedRoute = -1;

    public int Version { get; set; } = 0;

    public void Save()
    {
        Svc.PluginInterface.SavePluginConfig(this);
    }

    public void AddRoute(GatherRoute rte)
    {
        var nextRouteId = Routes.Count == 0 ? 0 : Routes.Keys.Max() + 1;
        Routes.Add(nextRouteId, rte);
        foreach (var itemId in rte.Items)
        {
            ItemLookup.TryAdd(itemId, []);
            ItemLookup[itemId].Add(nextRouteId);
        }
    }

    public void DeleteRoute(RouteId routeId)
    {
        foreach (var itemId in ItemLookup.Keys.ToList())
        {
            ItemLookup[itemId].Remove(routeId);
            if (ItemLookup[itemId].Count == 0)
                ItemLookup.Remove(itemId);
        }
        Routes.Remove(routeId);
    }

    public bool TryGetRoute(RouteId routeId, [MaybeNullWhen(false)] out GatherRoute route) => Routes.TryGetValue(routeId, out route);

    public IEnumerable<GatherPointObject> GetKnownPoints(GatherPointId dataId)
    {
        if (GatherPointObjects.TryGetValue(dataId, out var points))
            foreach (var p in points)
                if (GatherPointObjectsById.TryGetValue(p, out var gobj))
                    yield return gobj;
    }

    public bool GetFloorPoint(IGameObject obj, out Vector3 point) => GetFloorPoint((obj.DataId, obj.Position).GetHashCode(), out point);

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
    {
        var id = (obj.DataId, obj.Position).GetHashCode();
        if (GatherPointObjectsById.TryGetValue(id, out var gobj))
            GatherPointObjectsById[id] = gobj with { GatherLocation = update(gobj.GatherLocation) };
    }

    public void RecordPosition(IGameObject obj)
    {
        var key = obj.DataId;
        var objId = (obj.DataId, obj.Position).GetHashCode();
        GatherPointObjects.TryAdd(key, []);
        GatherPointObjects[key].Add(objId);
        GatherPointObjectsById.TryAdd(objId, new GatherPointObject(obj));
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
            if (AllRoutes.Any(r => r.Item2.GatheringPointBaseId == gpBase.RowId))
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

            var newRoute = new GatherRoute()
            {
                Label = label,
                Zone = gatherPointGroup.First().TerritoryType.Row,
                Nodes = gatherPointGroup.Select(x => (GatherPointId)x.RowId).ToList(),
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
                }).ToList(),
                Fly = true
            };

            AddRoute(newRoute);
        }
    }
}
