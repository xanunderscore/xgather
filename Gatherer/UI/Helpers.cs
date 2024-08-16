using ImGuiNET;
using Lumina.Excel.GeneratedSheets;

namespace xgather.UI;

internal class Helpers
{
    internal static void DrawItem(Item it, int iconSize = 32)
    {
        var ic = Svc.TextureProvider.GetFromGameIcon((uint)it.Icon)?.GetWrapOrEmpty();
        if (ic != null)
        {
            ImGui.Image(ic.ImGuiHandle, new(iconSize, iconSize));
            ImGui.SameLine();
        }
        ImGui.Text(it.Name);
    }

    internal static bool DrawItem(uint itemId, int iconSize = 32)
    {
        var it = Svc.ExcelRow<Item>(itemId);
        if (it == null)
            return false;

        DrawItem(it, iconSize);
        return true;
    }
}
