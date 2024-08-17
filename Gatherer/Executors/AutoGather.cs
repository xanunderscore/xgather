using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace xgather.Executors;

public sealed unsafe class AutoGather : IDisposable
{
    public delegate void ErrorHandler(object sender, string message);
    public event ErrorHandler OnError = delegate { };

    private AddonGathering* Addon;
    public HashSet<uint> DesiredItems = [];

    public AutoGather()
    {
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "Gathering", PostSetup);
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "Gathering", PreFinalize);
        Svc.Framework.Update += Tick;
    }

    public void Dispose()
    {
        Svc.AddonLifecycle.UnregisterListener(PostSetup);
        Svc.AddonLifecycle.UnregisterListener(PreFinalize);
        Svc.Framework.Update -= Tick;
    }

    private void PreFinalize(AddonEvent type, AddonArgs args)
    {
        Addon = null;
    }

    private void PostSetup(AddonEvent type, AddonArgs args)
    {
        Addon = (AddonGathering*)args.Addon;
    }

    private void Tick(IFramework fw)
    {
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
            OnError.Invoke(this, "No desired items exist on this node");
            Addon = null;
            return;
        }

        var checkbox = Addon->GetNodeById(17 + (uint)availableIndex)->GetAsAtkComponentCheckBox();
        if (checkbox == null)
        {
            OnError.Invoke(this, $"Internal error: node {availableIndex} is invalid");
            Addon = null;
            return;
        }
        checkbox->AtkComponentButton.IsChecked = true;
        Addon->FireCallbackInt(availableIndex);
    }
}
