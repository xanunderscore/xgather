using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Diagnostics;

namespace xgather;
internal unsafe class Debug : IDisposable
{
    private readonly long moduleStart;

    private delegate AtkValue* CallHandlerDelegate(AtkExternalInterface* thisPtr, AtkValue* result, uint handlerIndex, uint valueCount, AtkValue* values);
    private readonly Hook<CallHandlerDelegate> _callHandler;

    public Debug()
    {
        moduleStart = Process.GetCurrentProcess().MainModule!.BaseAddress.ToInt64();

        _callHandler = Svc.Hook.HookFromSignature<CallHandlerDelegate>("40 53 48 83 EC 40 48 8B 81 ?? ?? ?? ?? 48 8B DA", CallHandlerDetour);
        //_callHandler.Enable();
    }

    private unsafe AtkValue* CallHandlerDetour(AtkExternalInterface* thisPtr, AtkValue* result, uint handlerIndex, uint valueCount, AtkValue* values)
    {
        if (handlerIndex == 51)
            return null;

        var handlersStart = thisPtr[3663].VirtualTable;
        var handlersEnd = thisPtr[3664].VirtualTable;
        var handlersLen = handlersEnd - handlersStart;
        if (handlerIndex < handlersLen)
        {
            var off = (nint)handlersStart + (handlerIndex * sizeof(nint));
            var addr = (ulong*)off;
            Svc.Log.Debug($"CallHandler({handlerIndex}) => {(uint)*addr:X}");
        }

        var res = _callHandler.Original(thisPtr, result, handlerIndex, valueCount, values);
        return res;
    }

    public void Dispose()
    {
        _callHandler?.Dispose();
    }
}
