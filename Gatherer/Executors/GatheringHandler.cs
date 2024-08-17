using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace xgather.Executors;

public sealed unsafe class GatheringHandler : IDisposable
{
    private AddonGathering* Addon;

    public GatheringHandler()
    {
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "Gathering", PostSetup);
        Svc.Condition.ConditionChange += ConditionChange;
        Svc.Framework.Update += Tick;
    }

    public void Dispose()
    {
        Svc.AddonLifecycle.UnregisterListener(PostSetup);
        Svc.Condition.ConditionChange -= ConditionChange;
        Svc.Framework.Update -= Tick;
    }

    private void PostSetup(AddonEvent type, AddonArgs args)
    {
        Addon = (AddonGathering*)args.Addon;
    }

    public void ConditionChange(ConditionFlag flag, bool active)
    {
        if ((uint)flag == 85 && !active)
            Addon = null;
    }

    private void Tick(IFramework fw)
    {
        var DesiredItems = Svc.Executor.Gather?.Planner.DesiredItems().ToList() ?? [];

        if (Addon == null || Svc.Condition[ConditionFlag.Gathering42] || DesiredItems.Count == 0)
            return;

        // 110 is usually the node integrity, but it can be null if Revisit procs
        if (Addon->AtkValuesCount < 111)
            return;

        var ptIntegrity = Addon->AtkValues[110].UInt;
        if (ptIntegrity == 0)
            return;

        List<uint> available = [];
        for (var i = 7; i <= (11 * 8) + 7; i += 11)
            available.Add(Addon->AtkValues[i].UInt);

        var availableIndex = available.FindIndex(DesiredItems.Contains);
        if (availableIndex < 0)
        {
            UI.Alerts.Error("No desired items exist on this node");
            Addon = null;
            return;
        }

        var checkbox = Addon->GetNodeById(17 + (uint)availableIndex)->GetAsAtkComponentCheckBox();
        if (checkbox == null)
        {
            UI.Alerts.Error($"Internal error: node {availableIndex} is invalid");
            Addon = null;
            return;
        }
        checkbox->AtkComponentButton.IsChecked = true;
        Addon->FireCallbackInt(availableIndex);
    }
}
