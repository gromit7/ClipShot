using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Xunit;
namespace ClipShot;

public class UpdateMirrorFallbackTests
{
    private sealed class Network(byte[] bytes,string mirrorError,bool bothCorrupt=false):HttpMessageHandler
    {
        internal readonly ConcurrentQueue<(string Host,long? Offset)> Downloads=[];
        internal readonly ConcurrentQueue<string> Probes=[];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            bool mirror=request.RequestUri!.Host=="bot.data-clouds.ru";
            var range=request.Headers.Range?.Ranges.Single();
            if(range?.To is {} to)
            {
                // RF заведомо быстрее DE; GitHub не должен участвовать в пробах.
                Probes.Enqueue(request.RequestUri.Host);
                if(!mirror)await Task.Delay(100,token);
                var sample=new ByteArrayContent(bytes[..((int)to+1)]);sample.Headers.ContentRange=new ContentRangeHeaderValue(0,to,bytes.Length);
                return new(HttpStatusCode.PartialContent){Content=sample};
            }
            Downloads.Enqueue((request.RequestUri.Host,range?.From));
            if(request.RequestUri.Host=="github.com"||!mirror&&!bothCorrupt)return new(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)};
            byte[] content=bytes.ToArray();
            if(mirrorError=="checksum")content[0]^=1;
            if(mirrorError=="size")content=content[..^1];
            var response=new HttpResponseMessage(mirrorError=="range"?HttpStatusCode.PartialContent:HttpStatusCode.OK){Content=new ByteArrayContent(content)};
            if(mirrorError=="range")response.Content.Headers.ContentRange=new ContentRangeHeaderValue(1,bytes.Length-1,bytes.Length);
            return response;
        }
    }
    private static AppUpdate Update(byte[] bytes)=>new(new(9,0,0),
        new("https://github.com/gromit7/ClipShot/releases/download/v9.0.0/ClipShot-Setup-9.0.0.exe"),bytes.Length,Convert.ToHexString(SHA256.HashData(bytes)));

    [Theory][InlineData("checksum")][InlineData("range")][InlineData("size")]
    public async Task CorruptFastOriginFallsBackToVerifiedRelayWithoutBadPrefix(string error)
    {
        byte[] bytes=new byte[131072];Random.Shared.NextBytes(bytes);var network=new Network(bytes,error);
        string root=Path.Combine(Path.GetTempPath(),"ClipShot.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string path=Path.Combine(root,"setup.exe");
        var update=Update(bytes);var retries=new List<int>();
        using var client=new GitHubUpdates(network){DownloadRetryDelay=TimeSpan.Zero};
        await client.DownloadAsync(update,path,new Progress<int>(),CancellationToken.None,retries.Add);
        Assert.Equal(new[]{("bot.data-clouds.ru",(long?)null),("tg.data-clouds.ru",(long?)null)},network.Downloads.ToArray());
        Assert.Equal(new[]{1},retries);Assert.True(UpdateProtocol.Verify(path,update));Assert.Equal(bytes,File.ReadAllBytes(path));Assert.False(File.Exists(path+".partial"));
    }
    [Fact]public async Task LegacyGithubIsTriedOnlyAfterBothVdsDownloadsFail()
    {
        byte[] bytes=new byte[131072];Random.Shared.NextBytes(bytes);var network=new Network(bytes,"checksum",bothCorrupt:true);
        string root=Path.Combine(Path.GetTempPath(),"ClipShot.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string path=Path.Combine(root,"setup.exe");
        try
        {
            using var client=new GitHubUpdates(network){DownloadRetryDelay=TimeSpan.Zero};
            await client.DownloadAsync(Update(bytes),path,new Progress<int>(),TestContext.Current.CancellationToken);
            Assert.Equal(new[]{("bot.data-clouds.ru",(long?)null),("tg.data-clouds.ru",(long?)null),("github.com",(long?)null)},network.Downloads.ToArray());
            Assert.Equal(2,network.Probes.Count);Assert.DoesNotContain("github.com",network.Probes);Assert.True(UpdateProtocol.Verify(path,Update(bytes)));Assert.False(File.Exists(path+".partial"));
        }
        finally{Directory.Delete(root,true);}
    }
}
