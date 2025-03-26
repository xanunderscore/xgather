using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace xgather.Tasks;

public class Single(uint itemId, uint quantity, bool includeInventory) : AutoTask
{
    protected override async Task Execute()
    {
        if (GetQuantityNeeded() == 0)
            return;

        var route = Svc.Config.GetGatherPointGroupsForItem(itemId).FirstOrDefault() ?? throw new Exception($"No route found for item {itemId}");

        await TeleportToZone(route.Zone, route.GatherAreaCenter());
    }

    private unsafe uint GetQuantityNeeded()
    {
        var owned = includeInventory ? (uint)InventoryManager.Instance()->GetInventoryItemCount(itemId, minCollectability: (short)Utils.GetMinCollectability(itemId)) : 0;
        return owned >= quantity ? 0 : quantity - owned;
    }

    protected async Task TeleportToZone(uint territoryId, Vector3 destination)
    {
        using var scope = BeginScope("Teleport");
        var currentZone = Svc.ClientState.TerritoryType;
        var goalZone = territoryId;
        if (goalZone == currentZone || (goalZone == 901 && currentZone == 939))
            return;

        var closest = Svc.Plugin.Aetherytes.MinBy(a => a.DistanceToPoint(goalZone, destination)) ?? throw new Exception($"No aetheryte near zone {goalZone}");

        Status = "Teleporting";

        bool success;
        unsafe
        {
            success = UIState.Instance()->Telepo.Teleport(closest.GameAetheryte.RowId, 0);
        }
        ErrorIf(!success, $"Failed to teleport to {closest.GameAetheryte.RowId}");
        await WaitWhile(() => !Utils.PlayerIsBusy(), "TeleportStart");
        await WaitWhile(Utils.PlayerIsBusy, "TeleportFinish");
    }
}
