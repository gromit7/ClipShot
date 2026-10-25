using System.Runtime.InteropServices;
namespace ClipShot;

// Не считаем скрытое окно достаточным: пользователь может работать в другой программе.
public static class UpdateIdlePolicy
{
    public static readonly TimeSpan MinimumIdle=TimeSpan.FromMinutes(5);
    public static bool CanInstall(bool isolated,bool enabled,bool windowVisible,bool busy,TimeSpan? idle)=>
        !isolated&&enabled&&!windowVisible&&!busy&&idle is { } elapsed&&elapsed>=MinimumIdle;
    public static TimeSpan? Elapsed(uint now,uint lastInput)
    {
        uint milliseconds=unchecked(now-lastInput);
        // DWORD переполняется каждые 49 дней. Неправдоподобное время не разрешает установку.
        return milliseconds>int.MaxValue?null:TimeSpan.FromMilliseconds(milliseconds);
    }
    public static bool QuitRequestValid(uint now,uint deadline)=>unchecked((int)(deadline-now)) is >=0 and <=5000;
}
internal static class UserInactivity
{
    [StructLayout(LayoutKind.Sequential)]private struct LastInput{public uint Size,Time;}
    [DllImport("user32.dll")]private static extern bool GetLastInputInfo(ref LastInput info);
    internal static TimeSpan? Read()
    {
        var info=new LastInput{Size=(uint)Marshal.SizeOf<LastInput>()};
        return GetLastInputInfo(ref info)?UpdateIdlePolicy.Elapsed(unchecked((uint)Environment.TickCount),info.Time):null;
    }
}
