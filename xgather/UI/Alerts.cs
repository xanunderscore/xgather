using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace xgather.UI;

internal static class Alerts
{
    public static string LastError { get; private set; } = "";

    public static void ClearError() => LastError = "";

    public static void Error(SeString message)
    {
        Svc.Chat.Print(Wrap(message, iconColor: 17));
        UIGlobals.PlayChatSoundEffect(4);
        LastError = message.TextValue;
    }

    public static void Error(string message) => Error((SeString)message);

    public static void Success(SeString message)
    {
        Svc.Chat.Print(Wrap(message, iconColor: 45));
        UIGlobals.PlayChatSoundEffect(1);
    }
    public static void Success(string message) => Success((SeString)message);

    public static void Info(SeString message) => Svc.Chat.Print(Wrap(message));

    public static void Info(string message) => Info((SeString)message);

    private static SeString Wrap(SeString msg, ushort iconColor = 12) => new SeString(new UIForegroundPayload(iconColor)).Append(SeIconChar.CrossWorld.ToIconString()).Append(new UIForegroundPayload(0)).Append(" [xgather] ").Append(msg);
}
