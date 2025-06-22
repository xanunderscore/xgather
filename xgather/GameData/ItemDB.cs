using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using BaseId = uint;
using DataId = uint;
using ItemId = uint;

namespace xgather.GameData;

public static class Json
{
    public static T Deserialize<T>(string path)
    {
        using var fstream = File.OpenRead(path);
        using var reader = new StreamReader(fstream);
        using var js = new JsonTextReader(reader);
        return new JsonSerializer().Deserialize<T>(js) ?? throw new InvalidDataException("malformed json");
    }

    public static void Serialize<T>(string path, T val)
    {
        using var fstream = File.OpenWrite(path);
        using var writer = new StreamWriter(fstream);
        using var js = new JsonTextWriter(writer);
        JsonSerializer.Create(new JsonSerializerSettings()
        {
            Formatting = Formatting.Indented,
        }).Serialize(writer, val);
    }
}

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
    ulong ObjectId,
    Vector3 Position
);

public class ItemDatabase
{
    public readonly Dictionary<BaseId, GatheringPointBase> Groups = [];
    public readonly Dictionary<ItemId, HashSet<BaseId>> ItemIdGroupLookup = [];
    public readonly Dictionary<string, NodeLocation> KnownNodes = [];
    public readonly Dictionary<ulong, IslandGatherPoint> IslandGatherPoints = [];
    public readonly Dictionary<uint, List<IslandGatherPoint>> IslandGatherPointsByType = [];

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
        var key = $"0x{obj.DataId:X4} {px} {py} {pz}";
        KnownNodes.TryAdd(key, new(obj.DataId, obj.Position, null));
    }

    public void FillFromGameData()
    {
        Groups.Clear();
        ItemIdGroupLookup.Clear();

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
        IslandGatherPointsByType.Clear();

        foreach (var (_, point) in IslandGatherPoints)
        {
            IslandGatherPointsByType.TryAdd(point.NameId, []);
            IslandGatherPointsByType[point.NameId].Add(point);
        }
    }
}
