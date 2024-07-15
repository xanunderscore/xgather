using Dalamud.Interface.Windowing;
using ImGuiNET;

namespace xgather.Windows;

internal class Overlay(RouteBrowser routes, ItemBrowser items, DebugView debug) : Window("xgather")
{
    // public override bool DrawConditions() => Utils.IsGatherer;

    public override void Draw()
    {
        if (ImGui.BeginTabBar("tabs"))
        {
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
            if (ImGui.BeginTabItem("Debug"))
            {
                debug.Draw();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
    }
}
