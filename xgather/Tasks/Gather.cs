using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System.Numerics;
using System.Threading.Tasks;

namespace xgather.Tasks;

public abstract class GatherBase : AutoTask
{
    protected async Task<Vector2> Survey()
    {
        Status = "Searching for point";

        var (actionId, statusId) = Svc.Player?.ClassJob.RowId switch
        {
            16 => (228, 234), // lay of the land
            17 => (211, 233), // arbor call
            18 => (7904, 1167), // shark eye
            _ => (0, 0)
        };

        ErrorIf(actionId == 0, "Current job has no survey action");
        ErrorIf(!Utils.UseAction(ActionType.Action, (uint)actionId), "Unable to use survey action");

        await WaitWhile(() => !Utils.PlayerHasStatus(statusId), "Survey");

        unsafe
        {
            var map = AgentMap.Instance();
            // i think this is only for temporary markers?
            var mk = map->MiniMapGatheringMarkers[0];
            ErrorIf(mk.MapMarker.IconId == 0, "No valid map marker found");
            return new Vector2(mk.MapMarker.X, mk.MapMarker.Y) / 16f * map->CurrentMapSizeFactorFloat;
        }
    }
}
