using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;
using System;

namespace xgather.Windows;

public class DebugView
{
    private IGameObject? _tmptarget;
    private byte _previousDiademWeather = 0;
    private byte _currentDiademWeather = 0;
    private DateTime _diademWeatherSwap = DateTime.MinValue;

    public unsafe void Draw()
    {
        var rte = Svc.Route;
        if (rte._currentRoute != null)
            ImGui.Text($"Current route: {rte._currentRoute.Label} ({Svc.Config.SelectedRoute})");

        ImGui.Text($"Nodes to skip: {string.Join(", ", rte._skippedPoints)}");

        if (rte._destination != null)
        {
            ImGui.Text($"Dest: {rte._destination.Position} ({rte._destination.Position.DistanceFromPlayer():F2}y away)");
            ImGui.Text($"Destination data IDs: {string.Join(", ", rte._destination.TargetIDs)}");
            ImGui.Text($"Dest in range? {(rte._destination.IsLoaded ? "true" : "false")}");
        }

        if (rte._nearbyTarget != null)
        {
            ImGui.Text($"Target is nearby: {rte._nearbyTarget}");
        }

        if (ImGui.Button("Remember current target"))
        {
            if (Svc.Player!.TargetObject != null)
                _tmptarget = Svc.Player!.TargetObject;
        }

        if (_tmptarget != null)
        {
            ImGui.Text($"Target: {_tmptarget.Name} ({_tmptarget.GameObjectId:X})");
            ImGui.Text($"Distance to target: {_tmptarget.Position.DistanceFromPlayer()}");
        }

        var record = Svc.Plugin.RecordMode;
        if (ImGui.Checkbox("Record gathering point locations", ref record))
            Svc.Plugin.RecordMode = record;

        var wm = WeatherManager.Instance();

        var wt = Svc.ExcelRow<Weather>(wm->WeatherId);
        if (wt != null)
        {
            ImGui.Text("Current weather: ");
            ImGui.SameLine();

            var wtIcon = Svc.TextureProvider.GetFromGameIcon((uint)wt.Icon)?.GetWrapOrEmpty();
            if (wtIcon != null)
            {
                ImGui.Image(wtIcon.ImGuiHandle, new(32, 32));
                ImGui.SameLine();
            }

            ImGui.Text(wt.Name);
        }

        if (Svc.ClientState.TerritoryType == 939)
        {
            _currentDiademWeather = wm->WeatherId;

            if (_previousDiademWeather > 0 && _currentDiademWeather > 0 && _previousDiademWeather != _currentDiademWeather)
                _diademWeatherSwap = DateTime.UtcNow;

            _previousDiademWeather = _currentDiademWeather;

            ImGui.Text($"Last weather swap (Diadem): {_diademWeatherSwap}");
        }
        else
        {
            _previousDiademWeather = 0;
            _currentDiademWeather = 0;
        }

        if (_diademWeatherSwap != DateTime.MinValue)
        {
            var nextSwap = CalcNextSwap(_diademWeatherSwap);
            var timeUntil = nextSwap - DateTime.UtcNow;
            ImGui.Text($"Next Diadem weather change in: {timeUntil:mm\\:ss}");
        }

        ImGui.Checkbox("Diadem farm mode", ref Svc.Route._diademMode);

        if (ImGui.Button("Target Aurvael"))
        {
            RouteExec.EnterDiadem();
        }
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
