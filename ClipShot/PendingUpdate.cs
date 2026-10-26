using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace ClipShot;

// Сохраняется только описание уже проверенного пакета, без данных буфера.
public sealed record PendingPackage(string FilePath,AppUpdate Update);
public static class PendingUpdate
{
    public static PendingPackage? Read(string json,string updatesRoot,Version current,int? osBuild=null)
    {
        if(Encoding.UTF8.GetByteCount(json)>16384)throw new InvalidDataException();
        var pending=JsonSerializer.Deserialize<PendingPackage>(json)??throw new InvalidDataException();
        var update=pending.Update??throw new InvalidDataException();
        // Сохранённый флаг не заменяет свежую аварийную политику: после перезапуска
        // установленная версия или разрешение отката могли измениться.
        if(update.IsRollback)return null;
        var installed=new Version(current.Major,current.Minor,Math.Max(0,current.Build));
        if(update.Version<=installed)return null;
        string version=update.Version.ToString();
        string name=UpdatePackages.FileName(update);
        if(!UpdatePackages.TryParseName(name,out _,out string flavor)||!UpdatePackages.Supports(flavor,osBuild??Environment.OSVersion.Version.Build))throw new InvalidDataException("Package does not support this Windows build");
        string root=Path.GetFullPath(updatesRoot)+Path.DirectorySeparatorChar;
        string path=Path.GetFullPath(pending.FilePath);
        if(!Regex.IsMatch(version,@"^\d+\.\d+\.\d+$")||update.Version<new Version(0,7,0)||!UpdateProtocol.IsAllowedLocation(update.Version,name,update.Download)||
            !path.StartsWith(root,StringComparison.OrdinalIgnoreCase)||Path.GetFileName(path)!=name||
            update.Bytes is <1024 or >262144000||!Regex.IsMatch(update.Sha256??"",@"^[a-fA-F0-9]{64}$")||!UpdateProtocol.Verify(path,update))throw new InvalidDataException();
        return pending;
    }
}
