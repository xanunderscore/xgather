using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using Lumina.Excel.Sheets;
using System.Linq;
using xgather.Tasks;

namespace xgather.UI.Windows;

internal class ItemSearch(string initialSearchText)
{
    private string _searchText = initialSearchText;
    private Automation _auto = new();

    public void Draw()
    {
        using (ImRaii.Disabled(_auto.Running))
            if (ImGui.Button("Stop"))
                _auto.Stop();
        ImGui.SameLine();
        ImGui.TextUnformatted($"Status: {_auto.CurrentTask?.Status ?? "idle"}");

        if (ImGui.InputText("###isearch", ref _searchText, 256))
            Svc.Config.ItemSearchText = _searchText;

        ImGui.BeginTable("items", 4, ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV);
        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthFixed, 250);
        ImGui.TableSetupColumn("Zones");
        ImGui.TableSetupColumn("Misc");
        ImGui.TableSetupColumn("###buttons", ImGuiTableColumnFlags.WidthFixed, 64);
        ImGui.TableHeadersRow();

        if (_searchText.Length >= 3)
        {
            foreach ((var itemId, var routes) in Svc.Config.ItemDB)
            {
                var it = Svc.ExcelRow<Item>(itemId);

                if (!it.Name.ToString().Contains(_searchText, System.StringComparison.InvariantCultureIgnoreCase))
                    continue;

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                Helpers.DrawItem(it);

                ImGui.TableNextColumn();
                ImGui.Text(string.Join(", ", routes.Select(r => r.Item2.Label)));

                ImGui.TableNextColumn();
                ImGui.Text(routes.First().Item2.Class.ToString());

                ImGui.TableNextColumn();
                if (ImGuiComponents.IconButton((int)itemId, Dalamud.Interface.FontAwesomeIcon.Play))
                    _auto.Start(new GatherItem(itemId, 10, false));
            }
        }

        ImGui.EndTable();
    }

    private void DrawStartRouteButton(uint itemId, GatherPointBase route, bool isSameZone)
    {
        /*
        if (Svc.Executor.IsActive)
        {
            if (ImGuiComponents.IconButton($"###stopbutton{itemId}", FontAwesomeIcon.Stop))
                Svc.Executor.Stop();
        }
        else
        {
            if (ImGuiComponents.IconButton($"###playbutton{itemId}", FontAwesomeIcon.Play))
            {
                Svc.Executor.Start(route);
            }
        }
        */
    }
}
