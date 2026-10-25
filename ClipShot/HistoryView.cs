namespace ClipShot;

public sealed record ClipGroup(string Title, List<Clip> Clips);
public static class HistoryView
{
    public static List<ClipGroup> Groups(IEnumerable<Clip> clips, string query, string templateOrder, DateTime today)
    {
        query = query.Trim();
        var all = clips.Where(c => query.Length == 0 || c.Kind == "text" &&
            (c.Text.Contains(query, StringComparison.OrdinalIgnoreCase) || c.Name.Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();
        var order = new Dictionary<long, int>();
        foreach (string value in templateOrder.Split(','))
            if (long.TryParse(value, out long id)) order.TryAdd(id, order.Count);
        today = today.Date;
        DateTime yesterday = today.AddDays(-1), monday = today.AddDays(-((int)today.DayOfWeek + 6) % 7);
        var history = all.Where(c => !c.Template).ToList();
        return [
            new("Шаблоны", all.Where(c => c.Template).OrderBy(c => order.GetValueOrDefault(c.Id, int.MaxValue)).ToList()),
            new("Сегодня", history.Where(c => c.Seen.Date >= today).ToList()),
            new("Вчера", history.Where(c => c.Seen.Date == yesterday).ToList()),
            new("Ранее на этой неделе", history.Where(c => c.Seen.Date < yesterday && c.Seen.Date >= monday).ToList()),
            new("Более старые", history.Where(c => c.Seen.Date < yesterday && c.Seen.Date < monday).ToList())
        ];
    }
}
