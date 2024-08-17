using System;

namespace xgather.GameData;

public class Timestamp(long seconds)
{
    public const int MsPerSec = 1000;
    public const int SecPerMin = 60;
    public const int MinPerHr = 60;
    public const int HrPerDay = 24;

    public const int MsPerMin = MsPerSec * SecPerMin;
    public const int MsPerHr = MsPerMin * MinPerHr;
    public const int MsPerDay = MsPerHr * HrPerDay;

    public const int MinPerDay = MinPerHr * HrPerDay;
    public const int SecPerDay = MinPerDay * SecPerMin;

    public const int MsPerEoHr = 175000;

    public long Time { get; private set; } = seconds;

    public static Timestamp Zero => new(0);

    public static Timestamp Now => new(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    public static TimeSpan operator -(Timestamp self, Timestamp other) => TimeSpan.FromMilliseconds(self.Time - other.Time);
    public static explicit operator TimeSpan(Timestamp self) => TimeSpan.FromMilliseconds(self.Time);

    public DateTime AsDateTime => DateTimeOffset.FromUnixTimeMilliseconds(Time).LocalDateTime;

    public Timestamp AddEorzeaSeconds(long value) => new(Time + value * 875 / 18);
    public Timestamp AddEorzeaMinutes(long value) => new(Time + value * 8750 / 3);

    public long TotalEorzeaSeconds => Time * 144 / 7 / MsPerSec;
    public long TotalEorzeaMinutes => Time * 144 / 7 / MsPerMin;
    public long TotalEorzeaHours => Time * 144 / 7 / MsPerHr;
    public long TotalEorzeaDays => Time * 144 / 7 / MsPerDay;

    public int CurrentEorzeaMinute => (int)(TotalEorzeaMinutes % MinPerHr);
    public int CurrentEorzeaSecondOfDay => (int)(TotalEorzeaSeconds % SecPerDay);
    public int CurrentEorzeaMinuteOfDay => (int)(TotalEorzeaMinutes % MinPerDay);
    public int CurrentEorzeaHour => (int)(TotalEorzeaHours % HrPerDay);
    public (int Hours, int Minutes) CurrentEorzeaToD(int eorzeaMinuteOffset = 0)
    {
        var min = TotalEorzeaMinutes + eorzeaMinuteOffset;
        return ((int)(min / MinPerHr) % HrPerDay, (int)min % MinPerHr);
    }

    public Timestamp SyncToEorzeaHour() => new(Time - Time % MsPerEoHr);
}
