using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using System.Linq;
using System.Threading.Tasks;


namespace xgather.Tasks;

public class MoonFate : AutoTask
{
    protected override async Task Execute()
    {
        while (Util.PlayerHasStatus(StatusID.FATEParticipant))
        {
            var obj = Svc.ObjectTable.Where(x => x.ObjectKind is ObjectKind.EventObj && x.IsTargetable && x.DataId < 0x1EBDB0).MinBy(obj => obj.Position.DistanceFromPlayerXZ());
            if (obj == null)
                return;

            await MoveTo(obj.Position, 3.5f, false, false, false);
            Util.InteractWithObject(obj);
            await WaitWhile(() => !Svc.Condition[ConditionFlag.OccupiedInEvent], "InteractStart");
            await WaitWhile(() => Svc.Condition[ConditionFlag.OccupiedInEvent], "InteractEnd");
        }
    }
}
