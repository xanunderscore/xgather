using Lumina.Excel.Sheets;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GameAetheryte = Lumina.Excel.Sheets.Aetheryte;

namespace xgather.GameData;

internal class Aetheryte
{
    public GameAetheryte GameAetheryte;
    public TerritoryType Territory;
    public string Name;
    public float WorldX;
    public float WorldY;

    internal Aetheryte(GameAetheryte aetheryte)
    {
        var map = aetheryte.Territory.Value!.Map.Value!;

        var marker = Svc.Data
            .GetSubrowExcelSheet<MapMarker>()!
            .Flatten()
            .FirstOrDefault(m => m.DataType == 3 && m.DataKey.RowId == aetheryte.RowId);

        WorldX = ConvertMapMarkerToRawPosition(marker.X, map.SizeFactor);
        WorldY = ConvertMapMarkerToRawPosition(marker.Y, map.SizeFactor);
        Territory = aetheryte.Territory.Value;
        Name = aetheryte.PlaceName.Value!.Name.ToString();
        GameAetheryte = aetheryte;
    }

    public float DistanceToRoute(GatherPointBase rte)
    {
        // badly behaved aetherytes: amaurot, tamamizu
        if (GameAetheryte.RowId is 105 or 148)
            return float.MaxValue;

        if (rte.TerritoryType.RowId == Territory.RowId && rte.Location is Vector3 pos)
            return (new Vector2(WorldX, WorldY) - new Vector2(pos.X, pos.Z)).Length();

        return float.MaxValue;
    }

    public float DistanceToPoint(uint territory, Vector3 pos)
    {
        if (Territory.RowId == territory)
            return (new Vector2(WorldX, WorldY) - new Vector2(pos.X, pos.Z)).Length();

        return float.MaxValue;
    }

    public static float ConvertMapMarkerToRawPosition(int pos, float scale)
    {
        float num = scale / 100f;
        var rawPosition = ((float)(pos - 1024.0) / num);
        return rawPosition;
    }

    public static IEnumerable<Aetheryte> LoadAetherytes()
    {
        return Svc.Data
            .GetExcelSheet<GameAetheryte>()!
            .Where(x => x.IsAetheryte && x.AethernetName.RowId == 0 && x.RowId > 1)
            .Select(x => new Aetheryte(x));
    }
}
