using System.Globalization;
namespace ClipShot;

public static class UpdateSchedule
{
    public static string DayKey(DateTime localNow)=>localNow.ToString("yyyyMMdd",CultureInfo.InvariantCulture);
    // Запуск и ручная команда не ждут простоя. Фоновая проверка нужна,
    // если сегодня ещё не было успешного запроса. Пропущенные дни не догоняем.
    public static string? GetDueSlot(DateTime localNow,string? lastCheckedSlot,TimeSpan? idle)
    {
        if(idle is not {} elapsed||elapsed<UpdateIdlePolicy.MinimumIdle)return null;
        string slot=DayKey(localNow);
        string? previous=CheckedDay(lastCheckedSlot);
        // Поддерживаем старые ключи09/15 и перевод часов назад без лишних запросов.
        if(previous is not null&&string.CompareOrdinal(previous,slot)>=0)return null;
        return slot;
    }

    private static string? CheckedDay(string? value)
    {
        if(value is null)return null;
        string day=value.Length==8?value:value.Length==11&&(value.EndsWith("-09",StringComparison.Ordinal)||value.EndsWith("-15",StringComparison.Ordinal))?value[..8]:"";
        return DateTime.TryParseExact(day,"yyyyMMdd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _)?day:null;
    }
}
