using Dalamud.Plugin;
using System.Collections.Generic;
using System.Reflection;

namespace xgather.Util;

public static class Reflection
{
    private static IDalamudPlugin? GetDalamudPlugin(string internalName)
    {
        var dalamudAssembly = Svc.PluginInterface.GetType().Assembly;
        if (dalamudAssembly.GetType("Dalamud.Plugin.Internal.PluginManager", true) is not { } pm)
            return null;

        var manager = dalamudAssembly.GetType("Dalamud.Service`1", true)?.MakeGenericType(pm).GetMethod("Get")?.Invoke(null, BindingFlags.Default, null, [], null);

        if (manager?.GetType().GetProperty("InstalledPlugins")?.GetValue(manager) is not System.Collections.IList installed)
            return null;

        foreach (var plugin in installed)
        {
            var name = plugin.GetType().GetProperty("InternalName")?.GetValue(plugin) as string;
            if (name == internalName)
            {
                return plugin.GetType().GetField("instance", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(plugin) as IDalamudPlugin;
            }
        }
        return null;
    }

    public static Dictionary<uint, uint> GetMissingMaterialsList()
    {
        var plugin = GetDalamudPlugin("InventoryTools");
        var host = plugin?.GetType().GetProperty("Host")?.GetValue(plugin);
        var services = host?.GetType().GetProperty("Services")?.GetValue(host);

        var listService = services?.GetType().GetMethod("GetRequiredService")?.Invoke(services, [plugin?.GetType().Assembly.GetType("InventoryTools.Lists.ListService", true)]);

        var activeList = listService?.GetType().GetMethod("GetActiveCraftList")?.Invoke(listService, []);
        var craftList = activeList?.GetType().GetProperty("CraftList")?.GetValue(activeList);
        var materials = craftList?.GetType().GetMethod("GetMissingMaterialsList")?.Invoke(craftList, []);

        return materials as Dictionary<uint, uint> ?? [];
    }
}
