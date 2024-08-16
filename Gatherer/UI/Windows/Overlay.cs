using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using System;
using xgather.Executors;

namespace xgather.UI.Windows;

public class Overlay : Window
{
    //private readonly IGameObject? _tmptarget;
    //private readonly byte _previousDiademWeather = 0;
    //private readonly byte _currentDiademWeather = 0;
    //private readonly DateTime _diademWeatherSwap = DateTime.MinValue;

    public Overlay() : base("xgather", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse)
    {
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
        /*
        var record = Svc.Plugin.RecordMode;
        if (ImGui.Checkbox("Record gathering point locations", ref record))
            Svc.Plugin.RecordMode = record;
        */

        var exec = Svc.Executor;

        if (exec.CurrentRoute == null)
        {
            ImGui.Text("Current route: none");
            return;
        }
        if (exec.CurrentState == ExecutorBase.State.Stopped)
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Play))
                exec.Start();
        }
        else if (ImGuiComponents.IconButton(FontAwesomeIcon.Stop))
        {
            exec.Stop();
            return;
        }

        ImGui.SameLine();
        ImGui.Text(exec.CurrentRoute.Label);

        var ty = exec.CurrentState;

        var color = ty switch
        {
            ExecutorBase.State.Teleport => ImGuiColors.TankBlue,
            ExecutorBase.State.Mount => ImGuiColors.ParsedPink,
            ExecutorBase.State.Dismount => ImGuiColors.DalamudViolet,
            ExecutorBase.State.Gathering => ImGuiColors.HealerGreen,
            ExecutorBase.State.Gearset => ImGuiColors.DalamudYellow,
            _ => ImGuiColors.DalamudWhite
        };

        var text = ty.ToString();

        if (ty == ExecutorBase.State.Idle)
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

        // ImGui.Text($"Nodes to skip: {string.Join(", ", rte._skippedPoints)}");

        if (exec.Destination is IWaypoint pt)
        {
            ImGui.Text($"Destination: {pt}");
            ImGui.Text($"Distance to target: {pt.GetPosition().DistanceFromPlayer():F2} ({pt.GetPosition().DistanceFromPlayerXZ():F2} horizontally)");
        }

        if (Svc.Player?.TargetObject is IGameObject tar)
        {
            ImGui.Text($"Target: {tar.Name} ({tar.GameObjectId:X})");
            ImGui.Text($"Distance to target: {tar.Position.DistanceFromPlayer():F2} ({tar.Position.DistanceFromPlayerXZ():F2} horizontally)");
        }

        if (Alerts.LastError != "")
            ImGui.Text($"Last error: {Alerts.LastError}");

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

    private float MarkerToMap(float x, float scale) => (int)(2 * x / scale + 100.9);

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
