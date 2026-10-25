using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace ClipShot;
public sealed record AppUpdate(Version Version,Uri Download,long Bytes,string Sha256,bool IsRollback=false,string Reason="");
public sealed record RollbackPolicy(Version Target,string Reason)
{
    // Понижение разрешает только явный список затронутых версий. До 0.7 откатывать нельзя: там нет обновлятора.
    public static RollbackPolicy? Parse(string json,Version current)
    {
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
        if(root.GetProperty("schema").GetInt32()!=1)throw new InvalidDataException("Unknown update policy");
        if(root.GetProperty("action").GetString()!="rollback")return null;
        if(root.GetProperty("channel").GetString()!="stable")return null;
        string installed=$"{current.Major}.{current.Minor}.{Math.Max(0,current.Build)}";
        if(!root.GetProperty("affectedVersions").EnumerateArray().Any(v=>v.GetString()==installed))return null;
        string target=root.GetProperty("targetVersion").GetString()??"";
        if(!Regex.IsMatch(target,@"^\d+\.\d+\.\d+$")||!Version.TryParse(target,out var version)||version<new Version(0,7,0)||version>=Version.Parse(installed))throw new InvalidDataException("Unsafe rollback target");
        string reason=root.GetProperty("reason").GetString()??"";
        if(reason.Length>300)throw new InvalidDataException("Rollback reason too long");
        return new(version,reason);
    }
}
public static class UpdateProtocol
{
    public const long MaximumBytes=250L*1024*1024;
    public static AppUpdate? Parse(string json,Version current,bool allowRollback=false)
    {
        using var document=JsonDocument.Parse(json);var release=document.RootElement;
        if(release.GetProperty("draft").GetBoolean()||release.GetProperty("prerelease").GetBoolean())return null;
        string tag=release.GetProperty("tag_name").GetString()??"";
        if(!Regex.IsMatch(tag,@"^v\d+\.\d+\.\d+$")||!Version.TryParse(tag[1..],out var version))return null;
        var installed=new Version(current.Major,current.Minor,Math.Max(0,current.Build));
        if(version==installed||(!allowRollback&&version<installed))return null;
        string name=$"ClipShot-Setup-{version}.exe";
        foreach(var asset in release.GetProperty("assets").EnumerateArray())
        {
            if(asset.GetProperty("name").GetString()!=name)continue;
            long size=asset.GetProperty("size").GetInt64();string digest=asset.GetProperty("digest").GetString()??"";
            if(size<1024||size>MaximumBytes||!Regex.IsMatch(digest,@"^sha256:[0-9a-fA-F]{64}$"))throw new InvalidDataException("Invalid update metadata");
            var uri=new Uri(asset.GetProperty("browser_download_url").GetString()!);
            string expected=$"/gromit7/ClipShot/releases/download/{tag}/{name}";
            if(uri.Scheme!="https"||uri.Host!="github.com"||!uri.IsDefaultPort||uri.AbsolutePath!=expected||uri.Query.Length!=0||uri.Fragment.Length!=0||uri.UserInfo.Length!=0)throw new InvalidDataException("Unexpected update location");
            return new(version,uri,size,digest[7..].ToLowerInvariant());
        }
        return null;
    }
    public static bool Verify(string path,AppUpdate update)
    {
        using var file=File.OpenRead(path);
        return file.Length==update.Bytes&&Convert.ToHexString(SHA256.HashData(file)).Equals(update.Sha256,StringComparison.OrdinalIgnoreCase);
    }
}
internal sealed class GitHubUpdates : IDisposable
{
    private readonly HttpClient http;
    private readonly HttpClient feedHttp;
    internal GitHubUpdates(HttpMessageHandler? handler=null)
    {
        http=handler is null?new HttpClient():new HttpClient(handler);
        http.Timeout=TimeSpan.FromMinutes(5);http.DefaultRequestHeaders.UserAgent.ParseAdd("ClipShot/1.0");
        // Для ленты запрещены редиректы: одна проверка — ровно один GET без API/fallback.
        // Загрузка установщика использует отдельный клиент, поскольку GitHub перенаправляет assets.
        feedHttp=handler is null?new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}):new HttpClient(handler,disposeHandler:false);
        feedHttp.Timeout=TimeSpan.FromSeconds(10);feedHttp.DefaultRequestHeaders.UserAgent.ParseAdd("ClipShot/1.0");feedHttp.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        feedHttp.DefaultRequestHeaders.CacheControl=new(){NoCache=true};
    }
    internal async Task<AppUpdate?> CheckAsync(Version version,CancellationToken token)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(10));
        string json=await ReadFeed(timeout.Token);
        return UpdateFeed.Parse(json,version);
    }
    private async Task<string> ReadFeed(CancellationToken token)
    {
        using var response=await feedHttp.GetAsync(UpdateFeed.Url,HttpCompletionOption.ResponseHeadersRead,token);
        response.EnsureSuccessStatusCode();
        if(response.Content.Headers.ContentLength is long length&&length>UpdateFeed.MaximumBytes)throw new InvalidDataException("Update feed is too large");
        await using var input=await response.Content.ReadAsStreamAsync(token);using var bytes=new MemoryStream();var buffer=new byte[8192];int read;
        while((read=await input.ReadAsync(buffer,token))>0){if(bytes.Length+read>UpdateFeed.MaximumBytes)throw new InvalidDataException("Update feed is too large");bytes.Write(buffer,0,read);}
        return System.Text.Encoding.UTF8.GetString(bytes.ToArray());
    }
    internal async Task DownloadAsync(AppUpdate update,string path,IProgress<int> progress,CancellationToken token)
    {
        string partial=path+".partial";
        try
        {
            using var response=await http.GetAsync(update.Download,HttpCompletionOption.ResponseHeadersRead,token);response.EnsureSuccessStatusCode();
            if(response.Content.Headers.ContentLength is long size&&size!=update.Bytes)throw new InvalidDataException("Download size mismatch");
            await using(var input=await response.Content.ReadAsStreamAsync(token))
            await using(var output=new FileStream(partial,FileMode.Create,FileAccess.Write,FileShare.None,81920,true))
            {
                var buffer=new byte[81920];long total=0;int read;int previous=-1;
                while((read=await input.ReadAsync(buffer,token))>0)
                {
                    total+=read;if(total>update.Bytes)throw new InvalidDataException("Download exceeds expected size");await output.WriteAsync(buffer.AsMemory(0,read),token);
                    int percent=(int)(total*100/update.Bytes);if(percent!=previous){previous=percent;progress.Report(percent);}
                }
                if(total!=update.Bytes)throw new InvalidDataException("Incomplete update");
            }
            if(!UpdateProtocol.Verify(partial,update))throw new InvalidDataException("Update checksum mismatch");File.Move(partial,path,true);
        }
        finally{if(File.Exists(partial))File.Delete(partial);}
    }
    public void Dispose(){feedHttp.Dispose();http.Dispose();}
}
