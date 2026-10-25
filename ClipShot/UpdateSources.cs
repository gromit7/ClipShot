using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
namespace ClipShot;

public static class UpdateSources
{
    // Зеркало поставляет только пакет: ожидаемые размер и SHA-256 берём из ленты GitHub.
    public static readonly Uri[] MirrorRoots=[new("https://bot.data-clouds.ru/clipshot/")];
    public static Uri Package(Uri root,AppUpdate update)
    {
        if(root.Scheme!="https"||root.UserInfo.Length!=0||!root.IsDefaultPort||root.Query.Length!=0||root.Fragment.Length!=0||!root.AbsolutePath.EndsWith('/'))throw new InvalidDataException("Invalid mirror root");
        return new Uri(root,$"v{update.Version}/ClipShot-Setup-{update.Version}.exe");
    }
    public static async Task<IReadOnlyList<Uri>> RankAsync(HttpClient http,AppUpdate update,IEnumerable<Uri> mirrors,CancellationToken token)
    {
        var sources=new[]{update.Download}.Concat(mirrors.Take(2).Select(root=>Package(root,update))).Distinct().ToArray();
        if(sources.Length==1)return sources;
        async Task<(Uri Uri,double Time)> Probe(Uri uri)
        {
            using var limit=CancellationTokenSource.CreateLinkedTokenSource(token);limit.CancelAfter(TimeSpan.FromSeconds(4));
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
        var results=await Task.WhenAll(sources.Select(Probe));token.ThrowIfCancellationRequested();
        return results.OrderBy(item=>item.Time).Select(item=>item.Uri).ToArray();
    }
}
