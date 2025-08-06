using Dalamud.Bindings.ImGui;
using Lumina.Excel.Sheets;

namespace xgather.UI;

internal class Helpers
{
    internal static void DrawItem(uint iconId, string itemName, int iconSize = 32)
    {
        var ic = Svc.TextureProvider.GetFromGameIcon(iconId)?.GetWrapOrEmpty();
        if (ic != null)
        {
            ImGui.Image(ic.Handle, new(iconSize, iconSize));
            ImGui.SameLine();
        }
        ImGui.Text(itemName);
    }

    internal static void DrawItem(Item it, int iconSize = 32) => DrawItem(it.Icon, it.Name.ToString(), iconSize);

    internal static void DrawItem(uint itemId, int iconSize = 32) => DrawItem(Svc.ExcelRow<Item>(itemId), iconSize);
}
