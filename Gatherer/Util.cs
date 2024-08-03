using FFXIVClientStructs.FFXIV.Client.System.Framework;
using Lumina.Excel.GeneratedSheets;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace xgather;

internal static class Utils
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

    public static bool IsGatherer => Svc.Player?.ClassJob.Id is 16 or 17 or 18;

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
}

internal static class VectorExt
{
    public static float DistanceFromPlayer(this Vector3 vec) => Svc.Player == null ? float.MaxValue : (vec - Svc.Player.Position).Length();

    public static float DistanceFromPlayerXZ(this Vector3 vec) => Svc.Player == null ? float.MaxValue : (vec.V2() - Svc.Player.Position.V2()).Length();

    public static Vector2 V2(this Vector3 vec) => new(vec.X, vec.Z);
}

internal static class GPBaseExt
{
    public static GatherClass GetRequiredClass(this GatheringPointBase gpBase) => gpBase.GatheringType.Row switch
    {
        0 or 1 => GatherClass.MIN,
        2 or 3 => GatherClass.BTN,
        4 or 5 => GatherClass.FSH,
        _ => GatherClass.None
    };
}

internal static class EnumerableExt
{
    public static bool TryFirst<T>(this IEnumerable<T> list, Func<T, bool> condition, [MaybeNullWhen(false)] out T result)
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
