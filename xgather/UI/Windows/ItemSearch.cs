using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using Lumina.Excel.Sheets;
using System.Linq;
using xgather.Tasks;

namespace xgather.UI.Windows;

internal class ItemSearch(Automation auto, string initialSearchText)
{
    private string _searchText = initialSearchText;
    private readonly Automation _auto = auto;

    public void Draw()
    {
        if (ImGui.InputText("###isearch", ref _searchText, 256))
            Svc.Config.ItemSearchText = _searchText;

        ImGui.BeginTable("items", 4, ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV);
        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthFixed, 250);
        ImGui.TableSetupColumn("Zones");
        ImGui.TableSetupColumn("Class");
        ImGui.TableSetupColumn("###buttons", ImGuiTableColumnFlags.WidthFixed, 64);
        ImGui.TableHeadersRow();

        if (_searchText.Length >= 3)
        {
            foreach ((var itemId, var routes) in Svc.ItemDB.EnumerateItems())
            {
                if (itemId >= 2000000) // event/quest items, todo should these be displayed normally?
                    continue;

                var it = Svc.ExcelRow<Item>(itemId);

                if (!it.Name.ToString().Contains(_searchText, System.StringComparison.InvariantCultureIgnoreCase))
                    continue;

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                Helpers.DrawItem(it);

                ImGui.TableNextColumn();
                ImGui.Text(string.Join(", ", routes.Select(r => r.Label)));

                ImGui.TableNextColumn();
                ImGui.Text(routes.FirstOrDefault()?.Class.ToString() ?? "unknown");

                ImGui.TableNextColumn();
                using (ImRaii.Disabled(routes.Count == 0))
                    if (ImGuiComponents.IconButton((int)itemId, Dalamud.Interface.FontAwesomeIcon.Play))
                        _auto.Start(new GatherItem(itemId, 999));
            }
        }

        ImGui.EndTable();
    }
}
