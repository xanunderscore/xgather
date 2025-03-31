using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using xgather.GameData;

namespace xgather;

internal static unsafe class Utils
{
    internal class Chat
    {
        private static class Signatures
        {
            internal const string SendChat = "48 89 5C 24 ?? 57 48 83 EC 20 48 8B FA 48 8B D9 45 84 C9";
            internal const string SanitiseString = "E8 ?? ?? ?? ?? EB 0A 48 8D 4C 24 ?? E8 ?? ?? ?? ?? 48 8D 8D";
        }

        private delegate void ProcessChatBoxDelegate(IntPtr uiModule, IntPtr message, IntPtr unused, byte a4);
        private ProcessChatBoxDelegate ProcessChatBox { get; }

        internal static Chat instance;
        public static Chat Instance
        {
            get
            {
                instance ??= new();
                return instance;
            }
        }

        public Chat()
        {
            if (Svc.SigScanner.TryScanText(Signatures.SendChat, out var processChatBoxPtr))
            {
                ProcessChatBox = Marshal.GetDelegateForFunctionPointer<ProcessChatBoxDelegate>(processChatBoxPtr);
            }
        }

        public unsafe void Send(string message)
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            if (bytes.Length == 0)
                throw new ArgumentException("message is empty", nameof(message));

            if (ProcessChatBox == null)
                throw new InvalidOperationException("Could not find signature for ProcessChat");

            var uiModule = (IntPtr)Framework.Instance()->GetUIModule();
            using var payload = new ChatPayload(bytes);
            var mem1 = Marshal.AllocHGlobal(400);
            Marshal.StructureToPtr(payload, mem1, false);
            ProcessChatBox(uiModule, mem1, IntPtr.Zero, 0);
            Marshal.FreeHGlobal(mem1);
        }
    }

    public static bool IsGatherer => Svc.Player?.ClassJob.RowId is 16 or 17 or 18;

    [StructLayout(LayoutKind.Explicit)]
    private readonly struct ChatPayload : IDisposable
    {
        [FieldOffset(0)]
        private readonly IntPtr textPtr;

        [FieldOffset(16)]
        private readonly ulong textLen;

        [FieldOffset(8)]
        private readonly ulong unk1;

        [FieldOffset(24)]
        private readonly ulong unk2;

        internal ChatPayload(byte[] stringBytes)
        {
            textPtr = Marshal.AllocHGlobal(stringBytes.Length + 30);
            Marshal.Copy(stringBytes, 0, textPtr, stringBytes.Length);
            Marshal.WriteByte(textPtr + stringBytes.Length, 0);
            textLen = (ulong)(stringBytes.Length + 1);
            unk1 = 64;
            unk2 = 0;
        }

        public void Dispose()
        {
            Marshal.FreeHGlobal(textPtr);
        }
    }

    internal static string ShowV3(Vector3 vec) => $"[{vec.X:F2}, {vec.Y:F2}, {vec.Z:F2}]";

    public static uint GetMinCollectability(uint itemId)
    {
        foreach (var group in Svc.SubrowExcelSheet<CollectablesShopItem>())
        {
            foreach (var item in group)
            {
                if (item.Item.RowId == itemId)
                    return item.CollectablesShopRefine.Value.LowCollectability;
            }
        }

        return 0;
    }

    public static bool PlayerIsFalling => ((Character*)Player())->IsJumping();

    public static (DateTime Start, DateTime End) GetNextAvailable(uint itemId)
    {
        var gpt2 = Svc.ExcelRowMaybe<GatheringPointTransient>(itemId);
        if (gpt2 == null)
            return (DateTime.MinValue, DateTime.MaxValue);

        var gpt = gpt2.Value;

        if (gpt.EphemeralStartTime != 65535 && gpt.EphemeralEndTime != 65535)
            return CalcAvailability(gpt.EphemeralStartTime, gpt.EphemeralEndTime);

        if (gpt.GatheringRarePopTimeTable.Value is GatheringRarePopTimeTable gptt && gptt.RowId > 0)
            return CalcAvailability(gptt).MinBy(x => x.Start);

        return (DateTime.MinValue, DateTime.MaxValue);
    }

    public static IEnumerable<(DateTime Start, DateTime End)> CalcAvailability(GatheringRarePopTimeTable obj)
    {
        foreach (var (start, dur) in obj.StartTime.Zip(obj.Duration))
        {
            if (start == 65535)
                yield return (DateTime.MaxValue, DateTime.MaxValue);

            yield return CalcAvailability(start, start + dur);
        }
    }

    public static (DateTime Start, DateTime End) CalcAvailability(int EorzeaMinStart, int EorzeaMinEnd)
    {
        var currentTime = Timestamp.Now;
        var (startHr, startMin) = (EorzeaMinStart / 100, EorzeaMinStart % 100);
        var (endHr, endMin) = (EorzeaMinEnd / 100, EorzeaMinEnd % 100);

        var realStartMin = (startHr * 60) + startMin;
        var realEndMin = (endHr * 60) + endMin;

        if (realEndMin < realStartMin)
            realEndMin += Timestamp.MinPerDay;

        var realStartSec = realStartMin * 60;
        var realEndSec = realEndMin * 60;

        var curSec = currentTime.CurrentEorzeaSecondOfDay;

        if (curSec >= realEndSec)
            realStartSec += Timestamp.SecPerDay;

        var secondsToWait = realStartSec - curSec;
        var ts = currentTime.AddEorzeaSeconds(secondsToWait);
        var tsend = ts.AddEorzeaMinutes(realEndMin - realStartMin);
        return (ts.AsDateTime, tsend.AsDateTime);
    }

    public static (DateTime Start, DateTime End) GetNextAvailable(GatherPointBase b) => GetNextAvailable(b.Nodes[0]);
    public static unsafe bool PlayerIsBusy() => Svc.Condition[ConditionFlag.BetweenAreas] || Svc.Condition[ConditionFlag.Casting] || ActionManager.Instance()->AnimationLock > 0;

    public static Item Item(uint itemId) => Svc.ExcelRow<Item>(itemId);
    public static string ItemName(uint itemId) => Item(itemId).Name.ToString();

    public static GameObject* Player() => GameObjectManager.Instance()->Objects.IndexSorted[0].Value;

    public static bool PlayerInRange(Vector3 dest, float dist)
    {
        var d = dest - (Vector3)Player()->Position;
        return d.LengthSquared() <= dist * dist;
    }

    public static unsafe bool UseAction(ActionType actionType, uint actionId) => ActionManager.Instance()->UseAction(actionType, actionId);

    public static unsafe void InteractWithObject(IGameObject obj) => TargetSystem.Instance()->OpenObjectInteraction((GameObject*)obj.Address);

    public static unsafe AddonGathering* GetAddonGathering() => (AddonGathering*)RaptureAtkUnitManager.Instance()->GetAddonByName("Gathering");

    public static unsafe bool GatheringAddonReady()
    {
        var gat = GetAddonGathering();
        return gat != null && gat->IsVisible && gat->IsReady && gat->GatherStatus == 1;
    }

    public static unsafe bool AddonReady(string name)
    {
        var sp = RaptureAtkUnitManager.Instance()->GetAddonByName(name);
        return sp != null && sp->IsVisible && sp->IsReady;
    }

    public static unsafe int GatheringIntegrityLeft()
    {
        var gat = GetAddonGathering();
        if (gat == null || gat->IntegrityGaugeBar == null)
            return 0;

        return gat->IntegrityGaugeBar->Values[0].ValueInt;
    }

    public static unsafe void GatheringSelectItem(uint itemId)
    {
        var gat = GetAddonGathering();
        if (gat == null)
            throw new Exception("Addon is null");

        var items = gat->ItemIds.ToArray();
        var index = Array.IndexOf(items, itemId);
        if (index < 0)
            throw new Exception($"{itemId} not found at gathering point");

        gat->GatheredItemComponentCheckbox[index].Value->AtkComponentButton.IsChecked = true;
        gat->FireCallbackInt(index);
    }

    public static unsafe bool PlayerHasStatus(int statusId)
    {
        foreach (var stat in ((Character*)Player())->GetStatusManager()->Status)
        {
            if (stat.StatusId == statusId)
                return true;
        }

        return false;
    }

    public const float Cos120 = -0.5f;
    public const float Sin120 = 0.8660254f;

    public static Vector2 Rotate120Degrees(Vector2 input)
    {
        return new((input.X * Cos120) - (input.Y * Sin120), (input.X * Sin120) + (input.Y * Cos120));
    }
}

internal static class VectorExt
{
    public static float DistanceFromPlayer(this Vector3 vec) =>
        Svc.Player == null ? float.MaxValue : (vec - Svc.Player.Position).Length();

    public static float DistanceFromPlayerXZ(this Vector3 vec) =>
        Svc.Player == null ? float.MaxValue : (vec.XZ() - Svc.Player.Position.XZ()).Length();

    public static Vector2 XZ(this Vector3 vec) => new(vec.X, vec.Z);
}

internal static class GPBaseExt
{
    public static GatherClass GetRequiredClass(this GatheringPointBase gpBase) =>
        gpBase.GatheringType.RowId switch
        {
            0 or 1 => GatherClass.MIN,
            2 or 3 => GatherClass.BTN,
            4 or 5 => GatherClass.FSH,
            _ => GatherClass.None
        };
}

internal static class EnumerableExt
{
    public static bool TryFirst<T>(
        this IEnumerable<T> list,
        Func<T, bool> condition,
        [MaybeNullWhen(false)] out T result
    )
    {
        foreach (var item in list)
        {
            if (condition(item))
            {
                result = item;
                return true;
            }
        }

        result = default;
        return false;
    }
}

internal readonly record struct OnDispose(System.Action a) : IDisposable
{
    public void Dispose() => a();
}
