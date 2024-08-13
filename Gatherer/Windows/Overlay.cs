using Dalamud.Interface.Windowing;
using ImGuiNET;

namespace xgather.Windows;

internal class Overlay(RouteBrowser routes, ItemBrowser items) : Window("xgather overlay")
{
    // public override bool DrawConditions() => Utils.IsGatherer;

    public override void OnClose()
    {
        Svc.Config.OverlayOpen = false;
        base.OnClose();
    }

    public override void OnOpen()
    {
        Svc.Config.OverlayOpen = true;
        base.OnOpen();
    }

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
            ImGui.EndTabBar();
        }
    }
}
