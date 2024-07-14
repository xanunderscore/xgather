using Lumina.Excel.GeneratedSheets;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GameAetheryte = Lumina.Excel.GeneratedSheets.Aetheryte;

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

        var marker = Svc.Data.GetExcelSheet<MapMarker>()!.FirstOrDefault(m => m.DataType == 3 && m.DataKey == aetheryte.RowId) ?? throw new System.Exception($"No aetheryte found for {aetheryte}");

        WorldX = ConvertMapMarkerToRawPosition(marker.X, map.SizeFactor);
        WorldY = ConvertMapMarkerToRawPosition(marker.Y, map.SizeFactor);
        Territory = aetheryte.Territory.Value;
        Name = aetheryte.PlaceName.Value!.Name.ToString();
        GameAetheryte = aetheryte;
    }

    public float DistanceToRoute(GatherRoute rte)
    {
        if (rte.TerritoryType == Territory && rte.GatherAreaCenter() is Vector3 pos)
            return (new Vector2(WorldX, WorldY) - new Vector2(pos.X, pos.Z)).Length();

        return float.MaxValue;
    }

    private static float ConvertMapMarkerToRawPosition(int pos, float scale)
    {
        float num = scale / 100f;
        var rawPosition = ((float)(pos - 1024.0) / num);
        return rawPosition;
    }

    public static IEnumerable<Aetheryte> LoadAetherytes()
    {
        return Svc.Data.GetExcelSheet<GameAetheryte>()!.Where(x => x.IsAetheryte && x.AethernetName.Row == 0 && x.RowId > 1).Select(x => new Aetheryte(x));
    }
}
