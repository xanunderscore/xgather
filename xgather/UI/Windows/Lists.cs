using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using Lumina.Excel.Sheets;
using System;
using System.Numerics;

namespace xgather.UI.Windows;

internal class Lists
{
    private int selectedList = -1;
    private string newItemInput = "";

    public void Draw()
    {
        if (ImGui.BeginChild("Left", new Vector2(150 * ImGuiHelpers.GlobalScale, -1), false))
        {
            DrawSidebar();
            ImGui.EndChild();
        }

        ImGui.SameLine();

        if (ImGui.BeginChild("Right", new Vector2(-1, -1), false, ImGuiWindowFlags.NoSavedSettings))
        {
            DrawEditor();
            ImGui.EndChild();
        }
    }

    private void DrawSidebar()
    {
        var lists = Svc.Config.Lists;

        if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus))
        {
            var newIndex = lists.Count;
            var l = new TodoList($"new list #{newIndex}", []);
            Svc.Config.Lists.Add(l);
            selectedList = newIndex;
            return;
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Create new TODO list");

        ImGui.Separator();

        for (var i = 0; i < lists.Count; i++)
        {
            if (ImGui.Selectable($"{lists[i].Name}", i == selectedList))
                selectedList = i;
        }
    }

    private void DrawEditor()
    {
        if (selectedList < 0)
            return;

        var list = Svc.Config.Lists[selectedList];

        //if (ImGuiComponents.IconButton(FontAwesomeIcon.Play))
        //    Svc.Executor.StartList(list);

        ImGui.SameLine();

        var ctrlHeld = ImGui.GetIO().KeyCtrl;

        if (!ctrlHeld)
            ImGui.BeginDisabled();

        if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash))
        {
            Svc.Config.Lists.RemoveAt(selectedList);
            selectedList = -1;
            return;
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Delete list (hold CTRL)");

        if (!ctrlHeld)
            ImGui.EndDisabled();

        ImGui.Text($"{list.Name}");

        ImGui.Separator();

        DrawItemSelector(selectedList);

        ImGui.BeginTable("items", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV);
        ImGui.TableSetupColumn("Item name", ImGuiTableColumnFlags.WidthFixed, 250);
        ImGui.TableSetupColumn("Required");

        foreach (var (itemId, item) in list.Items)
        {
            var it = Svc.ExcelRow<Item>(itemId);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            Helpers.DrawItem(it);
            DrawFishWarning(it);

            ImGui.TableNextColumn();
            using (var color = ImRaii.PushColor(ImGuiCol.Text, item.QuantityNeeded > 0 ? ImGuiColors.DalamudWhite : ImGuiColors.ParsedGreen))
            {
                ImGui.AlignTextToFramePadding();
                ImGui.Text($" {item.QuantityOwned} /");
                ImGui.SameLine();
            }

            var q = (int)item.Required;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputInt($"###quant{itemId}", ref q))
                Svc.Config.Lists[selectedList].UpdateRequired(itemId, (uint)Math.Max(0, q));
        }

        ImGui.EndTable();
    }

    private static void DrawFishWarning(Item it)
    {
        if (it.ItemSearchCategory.RowId == 46)
        {
            ImGui.SameLine();
            using (ImRaii.PushFont(UiBuilder.IconFont))
                ImGui.TextUnformatted(FontAwesomeIcon.InfoCircle.ToIconString());

            if (ImGui.IsItemHovered())
                using (ImRaii.Tooltip())
                    ImGui.TextUnformatted("AutoHook is required for auto-spearfishing. (You can hide this warning in the Configuration tab.)");
        }
    }

    private void DrawItemSelector(int selectedList)
    {
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("###itemsearch", "Add item"))
        {
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("###newitem", "Item name", ref newItemInput, 255);
            ImGui.Separator();

            if (newItemInput.Length >= 3)
            {
                foreach (var (itemId, _) in Svc.ItemDB.ItemIdGroupLookup)
                {
                    var it = Svc.ExcelRow<Item>(itemId);

                    if (!it.Name.ToString().Contains(newItemInput, StringComparison.InvariantCultureIgnoreCase))
                        continue;

                    if (ImGui.Selectable(it.Name.ToString()))
                    {
                        ImGui.CloseCurrentPopup();
                        Svc.Config.Lists[selectedList].Add(new TodoItem(itemId, 1));
                        newItemInput = "";
                    }
                }
            }
            ImGui.EndCombo();
        }
    }
}
