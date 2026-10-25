using System.Globalization;
namespace ClipShot;

public static class UpdateSchedule
{
    // Передаётся местное время. После пропуска берём только последний слот сегодняшнего дня,
    // а не проигрываем очередь вчерашних проверок. Возвращённый слот сохраняется до HTTP-запроса.
    public static string? GetDueSlot(DateTime localNow,string? lastCheckedSlot)
    {
        if(localNow.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday||localNow.Hour<9)return null;
        int hour=localNow.Hour>=15?15:9;
        string slot=localNow.ToString("yyyyMMdd",CultureInfo.InvariantCulture)+"-"+hour.ToString("00",CultureInfo.InvariantCulture);
        // Память о последней попытке переживает перезапуск. Будущий слот также блокирует
        // повторную проверку при переводе системных часов назад, включая смену дня.
        if(IsSlot(lastCheckedSlot)&&string.CompareOrdinal(lastCheckedSlot,slot)>=0)return null;
        return slot;
    }

    private static bool IsSlot(string? value)=>value is not null&&
        DateTime.TryParseExact(value,"yyyyMMdd-HH",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)&&
        date.Hour is 9 or 15&&date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
}
