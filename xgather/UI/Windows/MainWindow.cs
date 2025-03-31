using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using System;

namespace xgather.UI.Windows;

internal class MainWindow(Routes routes, ItemSearch items, Lists lists) : Window("xgather browser"), IDisposable
{
    public override bool DrawConditions() => !(Svc.GameGui.GameUiHidden || Svc.Condition[ConditionFlag.OccupiedInCutSceneEvent] || Svc.Condition[ConditionFlag.WatchingCutscene78] || Svc.Condition[ConditionFlag.WatchingCutscene]);

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

    public override void OnClose()
    {
        Svc.Config.MainWindowOpen = false;
        base.OnClose();
    }

    public override void OnOpen()
    {
        Svc.Config.MainWindowOpen = true;
        base.OnOpen();
    }

    public void Dispose()
    {
        items.Dispose();
    }
}
