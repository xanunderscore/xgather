using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using xgather.Tasks;

namespace xgather.UI.Windows;

public class Overlay : Window
{
    private Automation _auto;

    public Overlay(Automation auto) : base("xgather", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse)
    {
        _auto = auto;
        TitleBarButtons.Add(new TitleBarButton()
        {
            Icon = FontAwesomeIcon.Cog,
            Priority = -1,
            IconOffset = new(2, 1),
            Click = (_) => Svc.Plugin.MainWindow.IsOpen = true
        });
    }

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

    public override unsafe void Draw()
    {
        using (ImRaii.Disabled(!_auto.Running))
            if (ImGui.Button("Stop"))
                _auto.Stop();
        ImGui.SameLine();
        using (ImRaii.Disabled(_auto.Running))
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Moon))
                _auto.Start(new GatherMoon());
        ImGui.SameLine();
        ImGui.TextUnformatted($"Status: {_auto.CurrentTask?.Status ?? "idle"}");
    }
}
