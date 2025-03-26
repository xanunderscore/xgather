using Dalamud.Interface.Windowing;
using ImGuiNET;

namespace xgather.UI.Windows;

internal class MainWindow(Routes routes, ItemSearch items, Lists lists) : Window("xgather browser")
{
    // public override bool DrawConditions() => Utils.IsGatherer;

    public override void Draw()
    {
        if (ImGui.BeginTabBar("tabs"))
        {
            if (ImGui.BeginTabItem("Lists"))
            {
                lists.Draw();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Routes"))
            {
                routes.Draw();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Items"))
            {
                items.Draw();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
    }
}
