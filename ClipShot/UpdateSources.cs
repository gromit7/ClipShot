using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
namespace ClipShot;

public static class UpdateSources
{
    // RF — origin, DE — caching relay. Размер и SHA берём только из RF-ленты
    // или её некешируемого DE relay. GitHub остаётся последним legacy fallback.
    public static readonly Uri[] MirrorRoots=[new("https://bot.data-clouds.ru/clipshot/"),new("https://tg.data-clouds.ru/clipshot/")];
    public static Uri Package(Uri root,AppUpdate update)
    {
        if(root.Scheme!="https"||root.UserInfo.Length!=0||!root.IsDefaultPort||root.Query.Length!=0||root.Fragment.Length!=0||!root.AbsolutePath.EndsWith('/'))throw new InvalidDataException("Invalid mirror root");
        return new Uri(root,$"v{update.Version}/{UpdatePackages.FileName(update)}");
    }
    public static async Task<IReadOnlyList<Uri>> RankAsync(HttpClient http,AppUpdate update,IEnumerable<Uri> mirrors,CancellationToken token)
    {
        string name=UpdatePackages.FileName(update);
        if(!UpdateProtocol.IsAllowedLocation(update.Version,name,update.Download))throw new InvalidDataException("Unexpected update location");
        bool legacy=update.Download.Host=="github.com";
        var sources=mirrors.Take(2).Select(root=>Package(root,update)).Concat(legacy?[]:new[]{update.Download}).Distinct().ToArray();
        if(sources.Length==0)return [update.Download];
        // Последний GitHub URL не участвует в гонке: быстрый GitHub не должен
        // обойти оба VDS и снова стать обычным путём загрузки.
        Uri[] AppendLegacy(IEnumerable<Uri> ranked)=>legacy?ranked.Append(update.Download).ToArray():ranked.ToArray();
        if(sources.Length==1)return AppendLegacy(sources);
        using var race=CancellationTokenSource.CreateLinkedTokenSource(token);
        async Task<(Uri Uri,double Time)> Probe(Uri uri)
        {
            using var limit=CancellationTokenSource.CreateLinkedTokenSource(race.Token);limit.CancelAfter(TimeSpan.FromSeconds(4));
            try
            {
                int count=(int)Math.Min(65536,update.Bytes);var watch=Stopwatch.StartNew();
                using var request=new HttpRequestMessage(HttpMethod.Get,uri);request.Headers.Range=new RangeHeaderValue(0,count-1);
                using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,limit.Token);response.EnsureSuccessStatusCode();
                if(response.StatusCode==HttpStatusCode.PartialContent&&(response.Content.Headers.ContentRange?.From!=0||response.Content.Headers.ContentRange.Length!=update.Bytes))throw new InvalidDataException();
                if(response.StatusCode is not HttpStatusCode.OK and not HttpStatusCode.PartialContent)throw new InvalidDataException();
                using var stream=await response.Content.ReadAsStreamAsync(limit.Token);var bytes=new byte[count];int read=0;
                while(read<count){int part=await stream.ReadAsync(bytes.AsMemory(read),limit.Token);if(part==0)throw new IOException();read+=part;}
                return(uri,watch.Elapsed.TotalMilliseconds);
            }
            catch when(!token.IsCancellationRequested){return(uri,double.PositiveInfinity);}
        }
        var pending=sources.Select(Probe).ToList();var results=new List<(Uri Uri,double Time)>();
        try
        {
            while(pending.Count>0)
            {
                var completed=await Task.WhenAny(pending);pending.Remove(completed);
                var result=await completed;results.Add(result);
                if(double.IsFinite(result.Time))
                {
                    // Все пробы читают одинаковое число байтов. Первый полный ответ
                    // уже даёт быстрый путь; отменённый соперник остаётся fallback.
                    race.Cancel();break;
                }
            }
        }
        catch
        {
            race.Cancel();try{await Task.WhenAll(pending);}catch{}
            throw;
        }
        // Дожидаемся Dispose response/stream у отменённых проб, не оставляя
        // фоновых запросов после возврата выбранного пути.
        results.AddRange(await Task.WhenAll(pending));token.ThrowIfCancellationRequested();
        return AppendLegacy(results.OrderBy(item=>item.Time).Select(item=>item.Uri));
    }
}
