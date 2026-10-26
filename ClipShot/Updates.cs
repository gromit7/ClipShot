using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace ClipShot;
public sealed record AppUpdate(Version Version,Uri Download,long Bytes,string Sha256,bool IsRollback=false,string Reason="",string PackageName="");
// Имя пакета — часть проверенных метаданных выпуска, а не свободное имя файла.
// Legacy остаётся для клиентов 0.7.0–0.7.3; новые клиенты выбирают пакет по реальной сборке ОС.
public static class UpdatePackages
{
    public const int MinimumBuild=17763,Windows11Build=22000;
    public static string? FlavorForBuild(int build)=>build<MinimumBuild?null:build<Windows11Build?"win10":"win11";
    public static bool Supports(string flavor,int build)=>flavor switch
    {
        "legacy"=>build>=MinimumBuild,"win10"=>build>=MinimumBuild&&build<Windows11Build,
        "win11"=>build>=Windows11Build,_=>false
    };
    public static string Name(Version version,string flavor="legacy")
    {
        if(!Regex.IsMatch(version.ToString(),@"^\d+\.\d+\.\d+$"))throw new InvalidDataException("Invalid package version");
        string prefix=flavor switch{"legacy"=>"ClipShot-","win10"=>"ClipShot-Win10-","win11"=>"ClipShot-Win11-",_=>throw new InvalidDataException("Unknown package flavor")};
        return prefix+"Setup-"+version+".exe";
    }
    public static bool TryParseName(string name,out Version version,out string flavor)
    {
        version=new Version(0,0,0);flavor="";
        var match=Regex.Match(name,@"^ClipShot-(?:(Win10|Win11)-)?Setup-(\d+\.\d+\.\d+)\.exe$");
        if(!match.Success||!Version.TryParse(match.Groups[2].Value,out var parsed))return false;
        version=parsed;flavor=match.Groups[1].Value switch{"Win10"=>"win10","Win11"=>"win11",_=>"legacy"};
        return name==Name(version,flavor);
    }
    public static string FileName(AppUpdate update)
    {
        string name=string.IsNullOrEmpty(update.PackageName)?Name(update.Version):update.PackageName;
        if(!TryParseName(name,out var version,out _)||version!=update.Version)throw new InvalidDataException("Package name mismatch");
        return name;
    }
}
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
    public static AppUpdate? Parse(string json,Version current,bool allowRollback=false,string flavor="legacy",int? osBuild=null)
    {
        using var document=JsonDocument.Parse(json);var release=document.RootElement;
        if(release.GetProperty("draft").GetBoolean()||release.GetProperty("prerelease").GetBoolean())return null;
        string tag=release.GetProperty("tag_name").GetString()??"";
        if(!Regex.IsMatch(tag,@"^v\d+\.\d+\.\d+$")||!Version.TryParse(tag[1..],out var version))return null;
        var installed=new Version(current.Major,current.Minor,Math.Max(0,current.Build));
        if(version==installed||(!allowRollback&&version<installed))return null;
        string name=UpdatePackages.Name(version,flavor);
        if(osBuild is int build&&!UpdatePackages.Supports(flavor,build))throw new InvalidDataException("Package does not support this Windows build");
        foreach(var asset in release.GetProperty("assets").EnumerateArray())
        {
            if(asset.GetProperty("name").GetString()!=name)continue;
            long size=asset.GetProperty("size").GetInt64();string digest=asset.GetProperty("digest").GetString()??"";
            if(size<1024||size>MaximumBytes||!Regex.IsMatch(digest,@"^sha256:[0-9a-fA-F]{64}$"))throw new InvalidDataException("Invalid update metadata");
            var uri=new Uri(asset.GetProperty("browser_download_url").GetString()!);
            if(!IsAllowedLocation(version,name,uri))throw new InvalidDataException("Unexpected update location");
            return new(version,uri,size,digest[7..].ToLowerInvariant(),PackageName:name);
        }
        return null;
    }
    public static bool IsAllowedLocation(Version version,string name,Uri? uri)
    {
        if(uri is null||!uri.IsAbsoluteUri||uri.Scheme!="https"||!uri.IsDefaultPort||uri.Query.Length!=0||uri.Fragment.Length!=0||uri.UserInfo.Length!=0||
            !UpdatePackages.TryParseName(name,out var packageVersion,out _)||packageVersion!=version)return false;
        return uri.Host switch
        {
            "bot.data-clouds.ru" or "tg.data-clouds.ru"=>uri.AbsolutePath==$"/clipshot/v{version}/{name}",
            "github.com"=>uri.AbsolutePath==$"/gromit7/ClipShot/releases/download/v{version}/{name}",
            _=>false
        };
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
    private readonly HttpClient legacyHttp;
    private readonly HttpClient feedHttp;
    private readonly Uri[] mirrorRoots;
    internal GitHubUpdates(HttpMessageHandler? handler=null,IEnumerable<Uri>? mirrors=null)
    {
        mirrorRoots=(mirrors??UpdateSources.MirrorRoots).ToArray();
        http=handler is null?new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}):new HttpClient(handler);
        http.Timeout=TimeSpan.FromMinutes(5);http.DefaultRequestHeaders.UserAgent.ParseAdd("ClipShot/1.0");
        // Redirect разрешён только для переходного GitHub asset URL. RF/DE
        // отдают package непосредственно и не могут увести запрос на чужой хост.
        legacyHttp=handler is null?new HttpClient():new HttpClient(handler,disposeHandler:false);
        legacyHttp.Timeout=TimeSpan.FromMinutes(5);legacyHttp.DefaultRequestHeaders.UserAgent.ParseAdd("ClipShot/1.0");
        // Метаданные не следуют редиректам и не запрашивают GitHub. DE получает
        // только transport/HTTP fallback, но не отменяет содержание ответа RF.
        feedHttp=handler is null?new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}):new HttpClient(handler,disposeHandler:false);
        feedHttp.Timeout=Timeout.InfiniteTimeSpan;feedHttp.DefaultRequestHeaders.UserAgent.ParseAdd("ClipShot/1.0");feedHttp.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        feedHttp.DefaultRequestHeaders.CacheControl=new(){NoCache=true};
    }
    internal async Task<AppUpdate?> CheckAsync(Version version,CancellationToken token,int? osBuild=null)
    {
        string json;
        try{json=await ReadFeed(UpdateFeed.Url,token);}
        catch(Exception error)when(!token.IsCancellationRequested&&error is not InvalidDataException&&error is HttpRequestException or IOException or OperationCanceledException)
        {json=await ReadFeed(UpdateFeed.FallbackUrl,token);}
        return UpdateFeed.Parse(json,version,osBuild);
    }
    internal TimeSpan FeedAttemptTimeout {get;set;}=TimeSpan.FromSeconds(10);
    private async Task<string> ReadFeed(string endpoint,CancellationToken token)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(FeedAttemptTimeout);
        using var response=await feedHttp.GetAsync(endpoint,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
        response.EnsureSuccessStatusCode();
        if(response.Content.Headers.ContentLength is long length&&length>UpdateFeed.MaximumBytes)throw new InvalidDataException("Update feed is too large");
        await using var input=await response.Content.ReadAsStreamAsync(deadline.Token);using var bytes=new MemoryStream();var buffer=new byte[8192];int read;
        while((read=await input.ReadAsync(buffer,deadline.Token))>0){if(bytes.Length+read>UpdateFeed.MaximumBytes)throw new InvalidDataException("Update feed is too large");bytes.Write(buffer,0,read);}
        return new System.Text.UTF8Encoding(false,true).GetString(bytes.ToArray());
    }
    internal TimeSpan DownloadStallTimeout {get;set;}=TimeSpan.FromSeconds(30);
    internal TimeSpan DownloadRetryDelay {get;set;}=TimeSpan.FromSeconds(2);
    internal async Task DownloadAsync(AppUpdate update,string path,IProgress<int> progress,CancellationToken token,Action<int>? retrying=null)
    {
        string partial=path+".partial";
        var sources=await UpdateSources.RankAsync(http,update,mirrorRoots,token);
        try
        {
            for(int attempt=0;;attempt++)
            {
                try
                {
                    long offset=File.Exists(partial)?new FileInfo(partial).Length:0;
                    if(offset==update.Bytes&&UpdateProtocol.Verify(partial,update)){File.Move(partial,path,true);return;}
                    var source=sources[attempt%sources.Count];using var request=new HttpRequestMessage(HttpMethod.Get,source);
                    if(offset>0)request.Headers.Range=new System.Net.Http.Headers.RangeHeaderValue(offset,null);
                    using var headerTimeout=CancellationTokenSource.CreateLinkedTokenSource(token);headerTimeout.CancelAfter(DownloadStallTimeout);
                    using var response=await (source.Host=="github.com"?legacyHttp:http).SendAsync(request,HttpCompletionOption.ResponseHeadersRead,headerTimeout.Token);
                    headerTimeout.CancelAfter(Timeout.InfiniteTimeSpan);response.EnsureSuccessStatusCode();
                    if(response.StatusCode==System.Net.HttpStatusCode.PartialContent)
                    {
                        var range=response.Content.Headers.ContentRange;
                        if(range?.From!=offset||range.Length!=update.Bytes||range.To!=update.Bytes-1)throw new InvalidDataException("Invalid download range");
                    }
                    else if(response.StatusCode==System.Net.HttpStatusCode.OK)offset=0;
                    else throw new InvalidDataException("Unexpected download response");
                    if(response.Content.Headers.ContentLength is long size&&size!=update.Bytes-offset)throw new InvalidDataException("Download size mismatch");
                    await using(var input=await response.Content.ReadAsStreamAsync(token))
                    await using(var output=new FileStream(partial,offset>0?FileMode.Append:FileMode.Create,FileAccess.Write,FileShare.None,81920,true))
                    {
                        var buffer=new byte[81920];long total=offset;int previous=-1;
                        while(true)
                        {
                            // Таймаут измеряет отсутствие новых байтов, а не длительность всей загрузки.
                            using var stall=CancellationTokenSource.CreateLinkedTokenSource(token);stall.CancelAfter(DownloadStallTimeout);
                            int read=await input.ReadAsync(buffer,stall.Token);if(read==0)break;
                            total+=read;if(total>update.Bytes)throw new InvalidDataException("Download exceeds expected size");
                            await output.WriteAsync(buffer.AsMemory(0,read),token);
                            int percent=(int)(total*100/update.Bytes);if(percent!=previous){previous=percent;progress.Report(percent);}
                        }
                        if(total!=update.Bytes)throw new IOException("Incomplete update");
                    }
                    if(!UpdateProtocol.Verify(partial,update))throw new InvalidDataException("Update checksum mismatch");
                    File.Move(partial,path,true);return;
                }
                catch(Exception error) when(attempt<2&&!token.IsCancellationRequested&&
                    (error is not InvalidDataException||sources.Count>1)&&
                    (error is IOException or HttpRequestException or OperationCanceledException or InvalidDataException))
                {
                    // InvalidDataException не наследует IOException: неверный SHA/Range зеркала
                    // тоже должен переключать источник, начиная файл заново без плохого префикса.
                    if(error is InvalidDataException&&File.Exists(partial))File.Delete(partial);
                    retrying?.Invoke(attempt+1);await Task.Delay(DownloadRetryDelay,token);
                }
            }
        }
        finally{if(File.Exists(partial))File.Delete(partial);}
    }
    public void Dispose(){feedHttp.Dispose();legacyHttp.Dispose();http.Dispose();}
}
