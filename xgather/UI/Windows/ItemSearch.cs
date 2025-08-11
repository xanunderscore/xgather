using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Lumina.Excel.Sheets;
using System.Collections.Generic;
using System.Linq;
using xgather.Tasks.Gather;

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
            foreach ((var itemId, var groups) in Svc.ItemDB.EnumerateItems())
            {
                if (itemId >= 2000000)
                {
                    var e = Svc.ExcelRow<EventItem>(itemId);
                    ItemRow(itemId, e.Icon, e.Name, groups);
                }
                else
                {
                    var i = Svc.ExcelRow<Item>(itemId);
                    ItemRow(itemId, i.Icon, i.Name, groups);
                }
            }
        }

        ImGui.EndTable();
    }

    private void ItemRow(uint rowId, uint iconId, Lumina.Text.ReadOnly.ReadOnlySeString name, List<GameData.GatheringPointBase> routes)
    {
        if (!name.ToString().Contains(_searchText, System.StringComparison.InvariantCultureIgnoreCase))
            return;

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        Helpers.DrawItem(iconId, name.ToString());

        ImGui.TableNextColumn();
        ImGui.Text(string.Join(", ", routes.Select(r => r.Label)));

        ImGui.TableNextColumn();
        ImGui.Text(routes.FirstOrDefault()?.Class.ToString() ?? "unknown");

        ImGui.TableNextColumn();
        using (ImRaii.Disabled(routes.Count == 0))
        {
            if (ImGuiComponents.IconButton((int)rowId, Dalamud.Interface.FontAwesomeIcon.Play))
                _auto.Start(new OneItem(rowId, 999));
        }
        if (routes.Count == 0 && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("This item has no valid zone associated with it.");
    }
}
