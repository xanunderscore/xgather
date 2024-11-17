using ImGuiNET;
using Lumina.Excel.Sheets;
using System.Linq;

namespace xgather.UI.Windows;

internal class ItemSearch(string initialSearchText)
{
    private string _searchText = initialSearchText;

    public void Draw()
    {
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

                int routeId;
                GatherPointBase route;
                var isNearby = false;

                if (routes.TryFirst(r => r.Item2.Zone == Svc.ClientState.TerritoryType, out var nearbyRoute))
                {
                    isNearby = true;
                    (routeId, route) = nearbyRoute;
                }
                else
                    (routeId, route) = routes.First();

                DrawStartRouteButton(itemId, route, isNearby);
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
