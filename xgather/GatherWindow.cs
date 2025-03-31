using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.Interop;

namespace xgather;

public class GatherWindow
{
    private readonly Pointer<AddonGathering> _ptr;

    private GatherWindow(Pointer<AddonGathering> p)
    {
        _ptr = p;
    }

    public unsafe bool Ready => _ptr.Value->IsVisible && _ptr.Value->IsReady && _ptr.Value->GatherStatus == 1;
    public unsafe int Integrity => _ptr.Value->IntegrityGaugeBar->Values[0].ValueInt;

    public static GatherWindow? Instance()
    {
        unsafe
        {
            var gat = (AddonGathering*)RaptureAtkUnitManager.Instance()->GetAddonByName("Gathering");
            return gat == null ? null : new(gat);
        }
    }
}
