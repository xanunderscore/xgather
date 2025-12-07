using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using xgather.Utils;
using BaseId = uint;
using DataId = uint;
using ItemId = uint;

namespace xgather.GameData;

internal class ItemDatabaseManager(IDalamudPluginInterface pluginInterface)
{
    private readonly IDalamudPluginInterface _plugin = pluginInterface;
    private readonly string _gameVersion = File.ReadAllText("ffxivgame.ver");

    private string GetDatabaseFile()
    {
        if (!_plugin.ConfigDirectory.Exists)
            _plugin.ConfigDirectory.Create();
        return Path.Join(_plugin.ConfigDirectory.FullName, $"items.{_gameVersion}.json");
    }

    public void Save(ItemDatabase db)
    {
        Json.Serialize(GetDatabaseFile(), db);
    }

    public ItemDatabase OpenOrCreate()
    {
        ItemDatabase db;
        try
        {
            db = Json.Deserialize<ItemDatabase>(GetDatabaseFile());
        }
        catch (FileNotFoundException)
        {
            db = Create();
        }
        db.Initialize();
        return db;
    }

    public ItemDatabase Create()
    {
        var db = new ItemDatabase();
        db.FillFromGameData();
        Save(db);
        return db;
    }
}

public record class GatheringPointBase(
    uint Row,
    uint Zone,
    string Label,
    List<uint> Nodes,
    List<uint> Items,
    GatherClass Class,
    Vector2 WorldPos,
    float Radius,
    bool IsValid
)
{
    public bool ContainsPoint(Vector2 point) => (point - WorldPos).LengthSquared() < Radius * Radius * 1.1f;
}

public record class NodeLocation(
    DataId DataId,
    Vector3 Node,
    Vector3? Floor = null
);

public record class IslandGatherPoint(
    uint NameId,
    string Name,
    uint LayoutId,
    Vector3 Position
);

public class ItemDatabase
{
    public readonly Dictionary<BaseId, GatheringPointBase> Groups = [];
    public readonly Dictionary<ItemId, HashSet<BaseId>> ItemIdGroupLookup = [];
    public readonly Dictionary<string, NodeLocation> KnownNodes = [];

    [JsonIgnore]
    public readonly Dictionary<uint, List<IslandGatherPoint>> IslandNodesByNameId = [];
    [JsonIgnore]
    public readonly Dictionary<uint, IslandGatherPoint> IslandNodesByLayoutId = [];

    // there are two clusters of rock salt nodes about 300y apart, we don't want to waste time flying across the whole map to gather from both of them, so we ignore the smaller cluster entirely
    private static readonly uint[] _unwantedIslandNodes = [
        0x8ECE74,
        0x8ECE75,
        0x8ECE76,
        0x8ECE77,
    ];

    private static readonly (uint NameId, int[] Items)[] _islandNodeMaterials = [
        (0x1EB7F3, [35]), // Lightly Gnawed Pumpkin -> seeds
        (0x1EB7F4, [33]), // Partially Consumed Cabbage -> seeds
        (0x1EB7F5, [2, 9, 67]), // Tualong Tree -> branch, log, resin
        (0x1EB7F6, [0, 10, 68]), // Palm Tree -> palm leaf, palm log, coconut
        (0x1EB7F7, [1, 11, 69]), // Island Apple Tree -> vine, apple, beehive chip
        (0x1EB7F8, [9, 12, 70]), // Mahogany Tree -> sap, log, wood opal
        (0x1EB7F9, [3, 13, 80]), // Bluish Rock -> stone, copper ore, mythril ore
        (0x1EB7FA, [3, 14, 79]), // Smooth White Rock -> stone, limestone, marble
        (0x1EB7FB, [3, 15]), // Crystal-banded Rock -> stone, salt
        (0x1EB7FC, [8, 19]), // Mound of Dirt -> sand, clay
        (0x1EB7FD, [7, 32]), // Wild Popoto -> islewort, popoto
        (0x1EB7FE, [7, 40]), // Wild Parsnip -> islewort, parsnip
        (0x1EB7FF, [8, 20]), // Submerged Sand -> sand, tinsand
        (0x1EB800, [11, 16]), // Sugarcane -> vine, sugarcane
        (0x1EB801, [7, 17]), // Cotton Plant -> islewort, cotton
        (0x1EB802, [7, 18]), // Agave Plant -> islewort, hemp
        (0x1EB803, [4, 24]), // Large Shell -> clam, islefish
        (0x1EB804, [5, 25]), // Seaweed Tangle -> laver, squid
        (0x1EB805, [6, 26]), // Coral Formation -> coral, jellyfish
        (0x1EB806, [3, 21, 92]), // Rough Black Rock -> stone, iron ore, durium sand
        (0x1EB807, [3, 22]), // Quartz Formation -> stone, quartz
        (0x1EB808, [3, 23]), // Speckled Rock -> stone, leucogranite
        (0x1EB874, [71]), // Multicolored Isleblooms
        (0x1EB8BF, [78]), // Glowing Fungus -> glimshroom
        (0x1EB8C0, [3, 76, 77]), // Composite Rock -> stone, coal, shale
        (0x1EB8C1, [3, 81, 82]), // Stalagmite -> stone, water, spectrine
        (0x1EB953, [3, 93, 94]), // Yellowish Rock -> stone, yellow copper, gold
        (0x1EB954, [95, 96]), // Island Crystal Cluster -> hawk's eye sand, crystal formation
    ];

    public static IEnumerable<uint> GetIslandNodesForMaterial(int materialId) => _islandNodeMaterials.Where(m => m.Items.Contains(materialId)).Select(n => n.NameId);

    public bool CanGather(ItemId id) => GetGatherPointGroupsForItem(id).Any();

    public IEnumerable<GatheringPointBase> GetGatherPointGroupsForItem(uint itemId)
    {
        if (ItemIdGroupLookup.TryGetValue(itemId, out var groups))
        {
            foreach (var ix in groups)
            {
                var grp = Groups[ix];
                if (grp.IsValid)
                    yield return grp;
                else
                    Svc.Log.Debug($"skipping invalid group {grp.Row}");
            }
        }
    }

    public IEnumerable<(uint ItemId, List<GatheringPointBase> Groups)> EnumerateItems()
    {
        foreach (var (i, b) in ItemIdGroupLookup)
        {
            yield return (i, b.Select(b => Groups[b]).Where(g => g.IsValid).ToList());
        }
    }

    public void RecordPosition(IGameObject obj)
    {
        var px = (int)obj.Position.X;
        var py = (int)obj.Position.Y;
        var pz = (int)obj.Position.Z;
        var key = $"0x{obj.BaseId:X4} {px} {py} {pz}";
        KnownNodes.TryAdd(key, new(obj.BaseId, obj.Position, null));
    }

    public void FillFromGameData()
    {
        Groups.Clear();
        ItemIdGroupLookup.Clear();

        FillNodesFromGameData();
    }

    private void FillNodesFromGameData()
    {
        var allPoints = Svc.ExcelSheet<Lumina.Excel.Sheets.GatheringPoint>();
        foreach (var group in allPoints.GroupBy(gp => gp.GatheringPointBase.RowId))
        {
            // empty rows
            if (group.Key == 0)
                continue;

            var groupLumina = Svc.ExcelRow<Lumina.Excel.Sheets.GatheringPointBase>(group.Key);

            var tt = group.First().TerritoryType;

            var exportedGroup = Svc.ExcelRowMaybe<Lumina.Excel.Sheets.ExportedGatheringPoint>(group.Key);

            var realItemIds = new List<uint>();

            foreach (var item in groupLumina.Item)
            {
                if (item.RowId == 0)
                    continue;

                if (item.TryGetValue<Lumina.Excel.Sheets.GatheringItem>(out var g))
                    realItemIds.Add(g.Item.RowId);
                else if (item.TryGetValue<Lumina.Excel.Sheets.SpearfishingItem>(out var s))
                    realItemIds.Add(s.Item.RowId);
                else
                    Svc.Log.Warning($"group {group.Key}: can't find a real item corresponding with {s.Item.RowId}");
            }

            foreach (var item in realItemIds)
            {
                ItemIdGroupLookup.TryAdd(item, []);
                ItemIdGroupLookup[item].Add(group.Key);
            }

            var gatherTypeStr = groupLumina.GatheringType.RowId is 4 or 5 ? "Spearfishing" : groupLumina.GatheringType.Value.Name;

            var placeName = tt.Value.PlaceName.Value.Name;
            var placeNice = placeName.IsEmpty
                ? $"<invalid {tt.RowId}>"
                : placeName.ToString();

            var groupBase = new GatheringPointBase(
                group.Key,
                tt.RowId,
                $"Level {groupLumina.GatheringLevel} {gatherTypeStr} @ {placeNice}",
                [.. group.Select(g => g.RowId)],
                realItemIds,
                groupLumina.GetRequiredClass(),
                new(exportedGroup?.X ?? 0, exportedGroup?.Y ?? 0),
                exportedGroup?.Radius ?? 0,
                exportedGroup != null && !placeName.IsEmpty
            );

            Groups.Add(group.Key, groupBase);
        }
    }

    public void Initialize()
    {
        var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("xgather.resources.islandObjects.json");
        if (resource == null)
        {
            Svc.Log.Error("Unable to load islandObjects resource, island objects will not be gathered");
            return;
        }

        var points = Json.Deserialize<Dictionary<uint, IslandGatherPoint>>(resource);

        foreach (var (k, v) in points)
        {
            if (_unwantedIslandNodes.Contains(v.LayoutId))
            {
                Svc.Log.Debug($"skipping {v.LayoutId:X} {v.Name} because it has been blacklisted");
                continue;
            }

            IslandNodesByLayoutId[k] = v;
            IslandNodesByNameId.TryAdd(v.NameId, []);
            IslandNodesByNameId[v.NameId].Add(v);
        }
    }
}
