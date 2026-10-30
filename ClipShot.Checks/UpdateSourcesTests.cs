using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Xunit;
namespace ClipShot;
public class UpdateSourcesTests : TempWorkspaceOwner
{
    private sealed class Network(byte[] bytes,bool failMirror=false,bool ignoreRange=false):HttpMessageHandler
    {
        internal readonly ConcurrentQueue<(string Host,bool IsProbe)> Requests=[];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Requests.Enqueue((request.RequestUri!.Host,request.Headers.Range?.Ranges.Single().To is not null));
            bool mirror=request.RequestUri!.Host=="tg.data-clouds.ru";
            await Task.Delay(mirror?5:120,token);
            if(mirror&&failMirror)throw new HttpRequestException();
            long from=request.Headers.Range?.Ranges.Single().From??0;
            long to=request.Headers.Range?.Ranges.Single().To??bytes.Length-1;
            var content=new ByteArrayContent(ignoreRange?bytes:bytes[(int)from..(int)(to+1)]);
            if(!ignoreRange)content.Headers.ContentRange=new ContentRangeHeaderValue(from,to,bytes.Length);
            return new HttpResponseMessage(ignoreRange?HttpStatusCode.OK:HttpStatusCode.PartialContent){Content=content};
        }
    }
    private static AppUpdate Update(byte[] bytes)=>new(new(9,0,0),new("https://github.com/gromit7/ClipShot/releases/download/v9.0.0/ClipShot-Setup-9.0.0.exe"),bytes.Length,Convert.ToHexString(SHA256.HashData(bytes)));
    [Theory][InlineData(false)][InlineData(true)]public async Task ActualSampleChoosesFasterMirrorWithoutVpnAssumptions(bool ignoreRange)
    {
        byte[] bytes=new byte[131072];var network=new Network(bytes,ignoreRange:ignoreRange);using var http=new HttpClient(network);
        var ranked=await UpdateSources.RankAsync(http,Update(bytes),UpdateSources.MirrorRoots,CancellationToken.None);
        Assert.Equal(new[]{"tg.data-clouds.ru","bot.data-clouds.ru","github.com"},ranked.Select(uri=>uri.Host));
        Assert.DoesNotContain(network.Requests,request=>request.Host=="github.com");
    }
    [Fact]public async Task FailedRelayDoesNotPreventOriginDownload()
    {
        byte[] bytes=new byte[2048];var network=new Network(bytes,failMirror:true);using var client=new GitHubUpdates(network);
        string root=NewTempRoot();Directory.CreateDirectory(root);string path=Path.Combine(root,"setup.exe");
        try
        {
            await client.DownloadAsync(Update(bytes),path,new Progress<int>(),TestContext.Current.CancellationToken);
            Assert.True(UpdateProtocol.Verify(path,Update(bytes)));Assert.Equal("bot.data-clouds.ru",Assert.Single(network.Requests,request=>!request.IsProbe).Host);
            Assert.DoesNotContain(network.Requests,request=>request.Host=="github.com");
        }
        finally{DeleteTempRoot(root);}
    }
    [Fact]public async Task SingleSourceDoesNotIssueExtraProbeRequest()
    {
        byte[] bytes=new byte[2048];var network=new Network(bytes);using var http=new HttpClient(network);
        var ranked=await UpdateSources.RankAsync(http,Update(bytes),[],CancellationToken.None);Assert.Equal(Update(bytes).Download,Assert.Single(ranked));
        Assert.Empty(network.Requests);
    }
    [Theory][InlineData("http://mirror.test/")][InlineData("https://user:pass@mirror.test/")][InlineData("https://mirror.test:444/")][InlineData("https://mirror.test/path")][InlineData("https://mirror.test/?x=1")]
    public void InvalidMirrorRootIsRejected(string root)=>Assert.Throws<InvalidDataException>(()=>UpdateSources.Package(new(root),Update(new byte[2048])));
    [Fact]public async Task RankedMirrorIsUsedByDownloader()
    {
        byte[] bytes=new byte[2048];string root=NewTempRoot();Directory.CreateDirectory(root);
        var network=new Network(bytes);using var client=new GitHubUpdates(network);string path=Path.Combine(root,"setup.exe");
        await client.DownloadAsync(Update(bytes),path,new Progress<int>(),CancellationToken.None);Assert.True(UpdateProtocol.Verify(path,Update(bytes)));
        Assert.Equal("tg.data-clouds.ru",Assert.Single(network.Requests,request=>!request.IsProbe).Host);Assert.DoesNotContain(network.Requests,request=>request.Host=="github.com");
    }
    [Fact]public async Task OriginMetadataUsesOnlyTheTwoVdsRoutes()
    {
        byte[] bytes=new byte[2048];var update=Update(bytes) with{Download=new("https://bot.data-clouds.ru/clipshot/v9.0.0/ClipShot-Setup-9.0.0.exe")};
        var network=new Network(bytes);using var http=new HttpClient(network);
        var ranked=await UpdateSources.RankAsync(http,update,UpdateSources.MirrorRoots,TestContext.Current.CancellationToken);
        Assert.Equal(2,ranked.Count);Assert.DoesNotContain(ranked,uri=>uri.Host=="github.com");Assert.DoesNotContain(network.Requests,request=>request.Host=="github.com");
    }
    private sealed class StalledOrigin(byte[] bytes):HttpMessageHandler
    {
        internal bool OriginCancelled;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            if(request.RequestUri!.Host=="bot.data-clouds.ru")
            {
                try{await Task.Delay(Timeout.Infinite,token);}catch(OperationCanceledException){OriginCancelled=true;throw;}
            }
            Assert.Equal("tg.data-clouds.ru",request.RequestUri.Host);
            long to=request.Headers.Range!.Ranges.Single().To!.Value;
            var content=new ByteArrayContent(bytes[..((int)to+1)]);content.Headers.ContentRange=new ContentRangeHeaderValue(0,to,bytes.Length);
            return new(HttpStatusCode.PartialContent){Content=content};
        }
    }
    [Fact]public async Task SuccessfulRelayProbeCancelsStalledOriginWithoutWaitingForItsTimeout()
    {
        byte[] bytes=new byte[65536];var network=new StalledOrigin(bytes);using var http=new HttpClient(network);
        var ranking=UpdateSources.RankAsync(http,Update(bytes),UpdateSources.MirrorRoots,TestContext.Current.CancellationToken);
        var ranked=await ranking.WaitAsync(TimeSpan.FromSeconds(1),TestContext.Current.CancellationToken);
        Assert.True(network.OriginCancelled);Assert.Equal(new[]{"tg.data-clouds.ru","bot.data-clouds.ru","github.com"},ranked.Select(uri=>uri.Host));
    }
}
