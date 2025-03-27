using FFXIVClientStructs.FFXIV.Client.Game;
using System.Linq;
using System.Threading.Tasks;

namespace xgather.Tasks;

public class GatherItem(uint itemId, uint quantity, bool includeInventory) : AutoTask
{
    protected override async Task Execute()
    {
        using var scope = BeginScope("GatherItem");
        var needed = GetQuantityNeeded();
        Log($"Gathering {needed}x {Utils.ItemName(itemId)}");

        if (needed == 0)
            return;

        var route = Svc.Config.GetGatherPointGroupsForItem(itemId).FirstOrDefault();
        ErrorIf(route == null, $"No route found for item {itemId}");

        while (GetQuantityNeeded() > 0)
            await GatherNext(route);
    }

    private async Task GatherNext(GatherPointBase gpBase)
    {
        await TeleportToZone(gpBase.Zone, gpBase.GatherAreaCenter());

        var dest = IPCHelper.PointOnFloor(gpBase.GatherAreaCenter() with { Y = 1024 }, true, 10);
        ErrorIf(dest == null, "Unable to find point near gathering area");

        await MoveTo(dest.Value, 10, true, true);
    }

    private unsafe uint GetQuantityNeeded()
    {
        var owned = includeInventory ? (uint)InventoryManager.Instance()->GetInventoryItemCount(itemId, minCollectability: (short)Utils.GetMinCollectability(itemId)) : 0;
        return owned >= quantity ? 0 : quantity - owned;
    }
}
