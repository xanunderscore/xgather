using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using System;

namespace xgather.Windows;

public class DebugView : Window
{
    private IGameObject? _tmptarget;
    private byte _previousDiademWeather = 0;
    private byte _currentDiademWeather = 0;
    private DateTime _diademWeatherSwap = DateTime.MinValue;

    public DebugView() : base("xgather", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoResize) { }

    public override unsafe void Draw()
    {
        /*
        var record = Svc.Plugin.RecordMode;
        if (ImGui.Checkbox("Record gathering point locations", ref record))
            Svc.Plugin.RecordMode = record;
        */

        var rte = Svc.Route;

        if (rte._currentRoute == null)
        {
            ImGui.Text("Current route: none");
            return;
        }
        if (Svc.Route.CurrentState == RouteExec.State.Stopped)
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Play))
                Svc.Route.Start(rte._currentRoute);
        }
        else if (ImGuiComponents.IconButton(FontAwesomeIcon.Stop))
            Svc.Route.Stop();

        ImGui.SameLine();
        ImGui.Text(rte._currentRoute.Label);

        var ty = rte.CurrentState;

        var color = ty switch
        {
            RouteExec.State.Teleport => ImGuiColors.TankBlue,
            RouteExec.State.Mount => ImGuiColors.ParsedPink,
            RouteExec.State.Dismount => ImGuiColors.DalamudViolet,
            RouteExec.State.Gathering => ImGuiColors.HealerGreen,
            RouteExec.State.Gearset => ImGuiColors.DalamudYellow,
            _ => ImGuiColors.DalamudWhite
        };

        string text = ty.ToString();

        if (ty == RouteExec.State.Running)
        {
            text = "Idle";

            if (IPCHelper.PathIsRunning())
            {
                text = "Move";
                color = ImGuiColors.ParsedGold;
            }

            if (IPCHelper.PathfindInProgress())
            {
                text = "Pathfind";
                color = ImGuiColors.DalamudOrange;
            }
        }

        ImGui.TextColored(color, text);

        ImGui.Text($"Nodes to skip: {string.Join(", ", rte._skippedPoints)}");

        if (rte._destination is IWaypoint pt)
        {
            ImGui.Text($"Destination: {pt}");
            ImGui.Text($"Distance to target: {pt.Pos().DistanceFromPlayer():F2} ({pt.Pos().DistanceFromPlayerXZ():F2} horizontally)");
        }

        if (Svc.Player?.TargetObject is IGameObject tar)
        {
            ImGui.Text($"Target: {tar.Name} ({tar.GameObjectId:X})");
            ImGui.Text($"Distance to target: {tar.Position.DistanceFromPlayer():F2} ({tar.Position.DistanceFromPlayerXZ():F2} horizontally)");
        }

        if (UiMessage.LastError != "")
            ImGui.Text($"Last error: {UiMessage.LastError}");

        //var wm = WeatherManager.Instance();

        //var wt = Svc.ExcelRow<Weather>(wm->WeatherId);
        //if (wt != null)
        //{
        //    ImGui.Text("Current weather: ");
        //    ImGui.SameLine();

        //    var wtIcon = Svc.TextureProvider.GetFromGameIcon((uint)wt.Icon)?.GetWrapOrEmpty();
        //    if (wtIcon != null)
        //    {
        //        ImGui.Image(wtIcon.ImGuiHandle, new(32, 32));
        //        ImGui.SameLine();
        //    }

        //    ImGui.Text(wt.Name);
        //}

        //if (Svc.ClientState.TerritoryType == 939)
        //{
        //    _currentDiademWeather = wm->WeatherId;

        //    if (_previousDiademWeather > 0 && _currentDiademWeather > 0 && _previousDiademWeather != _currentDiademWeather)
        //        _diademWeatherSwap = DateTime.UtcNow;

        //    _previousDiademWeather = _currentDiademWeather;

        //    ImGui.Text($"Last weather swap (Diadem): {_diademWeatherSwap}");
        //}
        //else
        //{
        //    _previousDiademWeather = 0;
        //    _currentDiademWeather = 0;
        //}

        //if (_diademWeatherSwap != DateTime.MinValue)
        //{
        //    var nextSwap = CalcNextSwap(_diademWeatherSwap);
        //    var timeUntil = nextSwap - DateTime.UtcNow;
        //    ImGui.Text($"Next Diadem weather change in: {timeUntil:mm\\:ss}");
        //}

        //ImGui.Checkbox("Diadem farm mode", ref Svc.Route._diademMode);

        //if (ImGui.Button("Target Aurvael"))
        //    RouteExec.EnterDiadem();
    }

    private float MarkerToMap(float x, float scale) => (int)((2 * x / scale) + 100.9);

    private DateTime CalcNextSwap(DateTime lastSwap)
    {
        var nextSwap = lastSwap;
        var dt = DateTime.UtcNow;
        while (nextSwap < dt)
        {
            nextSwap = nextSwap.AddMinutes(10);
        }
        return nextSwap;
    }
}
