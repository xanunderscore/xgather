using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using xgather.Tasks;
using xgather.Tasks.Debug;

namespace xgather.UI.Windows;

public class Overlay : Window
{
    private readonly Automation _auto;
    private readonly Debug? _debugHelper;

    public Overlay(Automation auto, Debug? debugHelper) : base("xgather", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse)
    {
        _auto = auto;
        _debugHelper = debugHelper;
        SizeConstraints = new()
        {
            MinimumSize = new(200, 100)
        };
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

    public override void Draw()
    {
        ImGui.TextUnformatted($"Status: {_auto.CurrentTask?.Status ?? "idle"}");
        using (ImRaii.Disabled(!_auto.Running))
            if (ImGui.Button("Stop"))
                _auto.Stop();
        if (_debugHelper != null)
            DrawDebug();
    }

    private void DrawDebug()
    {
        ImGui.SameLine();
        var hovermoon = false;
        var hoverlist = false;
        using (ImRaii.Disabled(_auto.Running))
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Moon))
                _auto.Start(new GatherMoon());
            hovermoon = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);
            ImGui.SameLine();
            if (ImGuiComponents.IconButton(FontAwesomeIcon.ListUl))
            {
                var missing = IPCHelper._atoolsGetMissingItems.InvokeFunc();
                _auto.Start(new GatherMulti(missing));
            }
            hoverlist = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);
            ImGui.SameLine();
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Leaf))
                _auto.Start(new GatherIsland());
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Bug))
                _auto.Start(new TeleportMount());
        }
        if (hovermoon)
            ImGui.SetTooltip("Run current Cosmic Exploration mission");
        if (hoverlist)
            ImGui.SetTooltip("Collect all missing items from active Inventory Tools crafting list");
        _debugHelper?.Draw();
    }
}
