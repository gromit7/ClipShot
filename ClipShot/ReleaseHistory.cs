using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace ClipShot;

public sealed record ReleaseHistoryEntry(Version Version,IReadOnlyList<string> Changes);

// История поставляется вместе с проверенным приложением. Сеть, настройки и
// история буфера для уведомления не нужны; повреждённый файл не мешает запуску.
public static class ReleaseHistory
{
    public const int MaximumBytes=32*1024,MaximumReleases=5,MaximumChanges=3,MaximumChangeLength=160;
    public static IReadOnlyList<ReleaseHistoryEntry> Load(string path,Version current)
    {
        try
        {
            if(new FileInfo(path).Length>MaximumBytes)return [];
            return Parse(File.ReadAllText(path,new UTF8Encoding(false,true)),current);
        }
        catch{return [];}
    }
    public static IReadOnlyList<ReleaseHistoryEntry> Parse(string json,Version current)
    {
        try
        {
            if(Encoding.UTF8.GetByteCount(json)>MaximumBytes)throw new InvalidDataException("Release history is too large");
            using var document=JsonDocument.Parse(json,new JsonDocumentOptions{MaxDepth=8});var root=document.RootElement;
            if(root.GetProperty("schema").GetInt32()!=1)throw new InvalidDataException("Unknown release history schema");
            var releases=root.GetProperty("releases");
            if(releases.ValueKind!=JsonValueKind.Array||releases.GetArrayLength()>128)throw new InvalidDataException("Invalid release list");
            var entries=new List<ReleaseHistoryEntry>();var seen=new HashSet<Version>();
            foreach(var release in releases.EnumerateArray())
            {
                string text=release.GetProperty("version").GetString()??"";
                if(!Regex.IsMatch(text,@"^\d+\.\d+\.\d+$")||!Version.TryParse(text,out var version)||version.ToString()!=text||!seen.Add(version))throw new InvalidDataException("Invalid release version");
                var changes=release.GetProperty("changes");
                if(changes.ValueKind!=JsonValueKind.Array||changes.GetArrayLength() is <1 or >MaximumChanges)throw new InvalidDataException("Invalid release changes");
                var lines=new List<string>();
                foreach(var change in changes.EnumerateArray())
                {
                    string line=change.GetString()?.Trim()??"";
                    if(line.Length is <1 or >MaximumChangeLength||line.Any(char.IsControl))throw new InvalidDataException("Invalid release change text");
                    lines.Add(line);
                }
                entries.Add(new(version,lines));
            }
            var installed=new Version(current.Major,current.Minor,Math.Max(0,current.Build));
            return entries.Where(entry=>entry.Version<=installed).OrderByDescending(entry=>entry.Version).Take(MaximumReleases).ToArray();
        }
        catch(Exception error)when(error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException)
        {throw new InvalidDataException("Invalid release history",error);}
    }
}

public static class ReleaseHistoryLayout
{
    // Высота в DIP: на обычном экране до392, на маленьком — около половины
    // рабочей области. Оставшийся текст прокручивается внутри уведомления.
    public static double HeightForWorkArea(int height,double dpiScale)
    {
        if(!double.IsFinite(dpiScale)||dpiScale<=0)dpiScale=1;
        double available=Math.Max(1,height-24)/dpiScale;
        return Math.Min(available,Math.Min(392,Math.Max(200,available*.55)));
    }
}
