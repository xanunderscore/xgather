using Dalamud.Configuration;
using FFXIVClientStructs.FFXIV.Client.Game;
using ImGuiNET;
using Lumina.Excel.Sheets;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using RouteId = int;

namespace xgather;

public enum GatherClass : uint
{
    None = 0,
    BTN = 1,
    MIN = 2,
    FSH = 3
}

internal static class GCExt
{
    public static ClassJob? GetClassJob(this GatherClass gc) => gc switch
    {
        GatherClass.BTN => Svc.ExcelRow<ClassJob>(17),
        GatherClass.MIN => Svc.ExcelRow<ClassJob>(16),
        GatherClass.FSH => Svc.ExcelRow<ClassJob>(18),
        _ => null
    };
}

public record struct TodoList(string? Name, Dictionary<uint, TodoItem> Items, bool Ephemeral = false)
{
    public void Add(TodoItem item)
    {
        if (Items.TryGetValue(item.ItemId, out var entry))
            Items[item.ItemId] = entry with { Required = entry.Required + item.Required };
        else
            Items.Add(item.ItemId, item);
    }

    public void UpdateRequired(uint itemId, uint quantity)
    {
        if (quantity == 0)
        {
            Items.Remove(itemId);
            return;
        }

        if (Items.TryGetValue(itemId, out var entry))
            Items[itemId] = entry with { Required = Math.Max(1u, quantity) };
    }

    public void Debug()
    {
        ImGui.Text($"Todo list: {Name}");
        foreach (var it in Items)
        {
            UI.Helpers.DrawItem(it.Key);
            ImGui.SameLine();
            ImGui.Text($" - {it.Value.Required}");
        }
    }
}

[Serializable]
public record struct TodoItem(uint ItemId, uint Required)
{
    [JsonIgnore]
    public readonly unsafe uint QuantityOwned => (uint)InventoryManager.Instance()->GetInventoryItemCount(ItemId, minCollectability: (short)Util.GetMinCollectability(ItemId));

    [JsonIgnore]
    public readonly uint QuantityNeeded => QuantityOwned >= Required ? 0 : Required - QuantityOwned;
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    public List<TodoList> Lists = [];
    public bool Fly = true;

    public bool OverlayOpen = false;
    public bool MainWindowOpen = false;

    public string ItemSearchText = "";

    public RouteId SelectedRoute = -1;

    public int Version { get; set; } = 0;

    public void Save()
    {
        Svc.PluginInterface.SavePluginConfig(this);
    }
}
