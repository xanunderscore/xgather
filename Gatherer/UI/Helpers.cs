using ImGuiNET;
using Lumina.Excel.Sheets;

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
        ImGui.Text(it.Name.ToString());
    }

    internal static bool DrawItem(uint itemId, int iconSize = 32)
    {
        DrawItem(Svc.ExcelRow<Item>(itemId), iconSize);
        return true;
    }
}
