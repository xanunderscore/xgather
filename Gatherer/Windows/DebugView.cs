using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Colors;
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

    public DebugView() : base("xgather debug")
    {
    }

    public override void OnClose()
    {
        Svc.Config.DebugOpen = false;
        base.OnClose();
    }

    public override void OnOpen()
    {
        Svc.Config.DebugOpen = true;
        base.OnOpen();
    }

    public override unsafe void Draw()
    {
        var record = Svc.Plugin.RecordMode;
        if (ImGui.Checkbox("Record gathering point locations", ref record))
            Svc.Plugin.RecordMode = record;

        var rte = Svc.Route;
        if (rte._currentRoute != null)
        {

            ImGui.Text($"Current route: {rte._currentRoute.Label} ({Svc.Config.SelectedRoute})");

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
        }

        if (rte._destination != null)
            ImGui.Text($"Destination: {rte._destination.ShowDebug()}");

        if (Svc.Player?.TargetObject is IGameObject tar)
        {
            ImGui.Text($"Target: {tar.Name} ({tar.GameObjectId:X})");
            ImGui.Text($"Distance to target: {tar.Position.DistanceFromPlayer():F2} ({tar.Position.DistanceFromPlayerXZ():F2} horizontally)");
        }

        ImGui.Text($"Nodes to skip: {string.Join(", ", rte._skippedPoints)}");

        if (rte._lasterr != "")
            ImGui.Text($"Last error: {rte._lasterr}");

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
