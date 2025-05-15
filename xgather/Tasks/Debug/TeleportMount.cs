using System.Threading.Tasks;

namespace xgather.Tasks.Debug;

internal class TeleportMount : AutoTask
{
    protected override async Task Execute()
    {
        Status = "Testing delayed mount";
        await TeleportToZone(134, default, force: true);
        await Mount();
    }
}
