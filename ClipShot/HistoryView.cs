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
        DateTime yesterday = today.AddDays(-1), monday = today.AddDays(-((int)today.DayOfWeek + 6) % 7), month = new(today.Year,today.Month,1);
        var history = all.Where(c => !c.Template).ToList();
        var byId=history.ToDictionary(c=>c.Id);
        var positions=history.Select((clip,index)=>(clip.Id,index)).ToDictionary(item=>item.Id,item=>item.index);
        // Правки и исходник образуют одну соседнюю группу по дате последней
        // версии, но фактические Seen и байты исходника не переписываются.
        var dates=new Dictionary<long,DateTime>();var arranged=new List<Clip>();
        var families=history.GroupBy(c=>c.Kind=="image"&&c.ImageSourceId is long source&&byId.TryGetValue(source,out var original)&&original.Kind=="image"&&original.ImageSourceId is null?source:c.Id)
            .OrderBy(group=>group.Min(c=>positions[c.Id]));
        foreach(var family in families)
        {
            var members=family.OrderByDescending(c=>c.Seen).ThenByDescending(c=>c.Id).ToList();
            var original=members.FirstOrDefault(c=>c.Id==family.Key);
            var edits=members.Where(c=>c.Id!=family.Key).ToList();
            if(original is not null&&edits.Count>0){arranged.Add(edits[0]);arranged.Add(original);arranged.AddRange(edits.Skip(1));}
            else arranged.AddRange(members);
            DateTime familyDate=members.Max(c=>c.Seen).Date;
            foreach(var member in members)dates[member.Id]=familyDate;
        }
        history=arranged;
        return [
            new("Шаблоны", all.Where(c => c.Template).OrderBy(c => order.GetValueOrDefault(c.Id, int.MaxValue)).ToList()),
            new("Сегодня", history.Where(c => dates[c.Id] >= today).ToList()),
            new("Вчера", history.Where(c => dates[c.Id] == yesterday).ToList()),
            new("На неделе", history.Where(c => dates[c.Id] < yesterday && dates[c.Id] >= monday).ToList()),
            new("Месяц", history.Where(c => dates[c.Id] < yesterday && dates[c.Id] < monday && dates[c.Id] >= month).ToList()),
            new("Ранее", history.Where(c => dates[c.Id] < yesterday && dates[c.Id] < monday && dates[c.Id] < month).ToList())
        ];
    }
}
