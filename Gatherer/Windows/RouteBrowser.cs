using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;
using System.Linq;
using System.Numerics;

namespace xgather.Windows;

public class RouteBrowser
{
    private string _manualDataId = "";

    public void Draw()
    {
        if (ImGui.BeginChild("Left", new Vector2(150 * ImGuiHelpers.GlobalScale, -1), false))
        {
            DrawSidebar();
            ImGui.EndChild();
        }
        ImGui.SameLine();
        if (ImGui.BeginChild("Right", new Vector2(-1, -1), false, ImGuiWindowFlags.NoSavedSettings))
        {
            var b = Svc.Config.Fly;
            if (ImGui.Checkbox("Allow flight", ref b))
                Svc.Config.Fly = b;

            ImGui.SameLine();
            {
                using var scope = ImRaii.PushFont(UiBuilder.IconFont);
                ImGui.TextUnformatted(FontAwesomeIcon.InfoCircle.ToIconString());
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.Text("Flight will only be used to reach a node once a walkable point has been recorded for that node.");
                ImGui.EndTooltip();
            }

            DrawEditor();
            ImGui.EndChild();
        }
    }

    private static void DrawSidebar()
    {
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Route))
        {
            var tt = Svc.Data.GetExcelSheet<TerritoryType>()!.GetRow(Svc.ClientState.TerritoryType)!.PlaceName.Value!.Name;
            var newRoute = new GatherRoute
            {
                Label = $"Unnamed Route ({tt})",
                Nodes = [],
                Zone = Svc.ClientState.TerritoryType,
                Class = GatherClass.None,
            };
            Svc.Config.AddRoute(newRoute);
            Svc.Config.SelectedRoute = Svc.Config.RouteCount - 1;
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Create new (empty) ordered route");

        ImGui.Separator();


        if (ImGui.BeginChild("Routes"))
        {
            foreach ((var rteId, var rte) in Svc.Config.AllRoutes.Where(x => x.Item2.MissingPoints()).OrderBy(x => x.Item2.TerritoryType.RowId == Svc.ClientState.TerritoryType ? x.Item2.GatherAreaCenter()?.DistanceFromPlayer() ?? float.MaxValue : float.MaxValue))
            {
                if (ImGui.Selectable($"{rte.Label}###route{rteId}", rteId == Svc.Config.SelectedRoute))
                {
                    Svc.Config.SelectedRoute = rteId;
                }
            }
            ImGui.EndChild();
        }
    }

    private void DrawEditor()
    {
        if (!Svc.Config.TryGetRoute(Svc.Config.SelectedRoute, out var selectedRoute))
            return;

        if (Svc.Route.CurrentState == RouteExec.State.Stopped)
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Play))
                Svc.Route.Start(selectedRoute);
        }
        else
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Stop))
                Svc.Route.Stop();
        }

        ImGui.SameLine();
        var ctrlHeld = ImGui.GetIO().KeyCtrl;

        if (!ctrlHeld)
            ImGui.BeginDisabled();

        if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash))
        {
            Svc.Route.Stop();
            Svc.Config.DeleteRoute(Svc.Config.SelectedRoute);
            Svc.Config.SelectedRoute = -1;
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Delete Route (Hold CTRL)");

        if (!ctrlHeld)
            ImGui.EndDisabled();

        var label = selectedRoute.Label;

        ImGui.Text("Name: ");
        ImGui.SameLine();
        if (ImGui.InputText($"###route{Svc.Config.SelectedRoute}", ref label, 256))
        {
            selectedRoute.Label = label;
        }

        if (selectedRoute.Ordered)
        {
            ImGui.SameLine();

            var tar = Svc.TargetManager.Target;

            if (tar == null)
                ImGui.BeginDisabled();

            if (ImGuiComponents.IconButton("###newtargetbtn", FontAwesomeIcon.Plus) && tar != null)
            {
                var newNode = RouteExec.CreateNodeFromObject(tar);
                if (newNode is (var newId, var newPoints))
                {
                    selectedRoute.Nodes.Add(newId);
                    Svc.Config.RecordPositions(newPoints);
                }
            }

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip("Add waypoint: current target");

            if (tar == null)
                ImGui.EndDisabled();

            ImGui.SetNextItemWidth(125);
            ImGui.InputTextWithHint("###waypointnewmanual", "DataID", ref _manualDataId, 24);
            ImGui.SameLine();
            if (ImGuiComponents.IconButton("###newmanualbtn", FontAwesomeIcon.Plus) && _manualDataId != "")
            {
                if (uint.TryParse(_manualDataId, System.Globalization.NumberStyles.HexNumber, null, out var did))
                {
                    var nearby = RouteExec.FindGatherPoints(obj => obj.DataId == did);
                    selectedRoute.Nodes.Add(did);
                    Svc.Config.RecordPositions(nearby);
                    _manualDataId = "";
                }
            }
        }

        if (!selectedRoute.Ordered)
        {
            foreach (var itemId in selectedRoute.Items)
                Ui.DrawItem(itemId);

            foreach (var did in selectedRoute.Nodes)
            {
                ImGui.Text($"{did:X}");
                foreach (var gobj in Svc.Config.GetKnownPoints(did))
                {
                    ImGui.Text($"  {gobj.Position}");
                    if (gobj.GatherLocation != null)
                    {
                        ImGui.SameLine();
                        ImGui.Text($" ({gobj.GatherLocation})");
                        ImGui.SameLine();
                        var k = Configuration.GetKey(gobj.DataId, gobj.Position);
                        if (ImGui.Button($"Forget###forget{k}"))
                            Svc.Config.UpdateFloorPoint(gobj.DataId, gobj.Position, _ => null);
                    }
                }
            }
        }

        if (selectedRoute.Ordered)
            DrawWaypoints(selectedRoute);
    }

    private static void DrawWaypoints(GatherRoute route)
    {
        var i = 0;
        foreach (var pt in route.Nodes.ToList())
        {
            var changeColor = Svc.Route._loopIndex == i && Svc.Route.IsActive && route.Ordered;
            if (changeColor)
                ImGui.PushStyleColor(ImGuiCol.Text, 0xff00ff00);
            if (ImGui.TreeNodeEx($"0x{pt:X}###{i}", ImGuiTreeNodeFlags.Leaf))
            {
                if (ImGui.BeginPopupContextItem())
                {
                    if (ImGui.MenuItem("Start from this step"))
                    {
                        Svc.Route.Start(route, i);
                    }
                    if (ImGui.MenuItem("Move up"))
                    {
                        var node = route.Nodes[i];
                        route.Nodes.RemoveAt(i);
                        route.Nodes.Insert(i - 1, node);
                    }
                    if (ImGui.MenuItem("Delete step"))
                    {
                        route.Nodes.RemoveAt(i);
                    }
                    ImGui.EndPopup();
                }
                ImGui.TreePop();
            }
            if (changeColor)
                ImGui.PopStyleColor();
            i += 1;
        }
    }
}
