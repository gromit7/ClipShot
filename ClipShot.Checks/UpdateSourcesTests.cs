using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Xunit;
namespace ClipShot;
public class UpdateSourcesTests
{
    private sealed class Network(byte[] bytes,bool failMirror=false,bool ignoreRange=false):HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            bool mirror=request.RequestUri!.Host=="mirror.test";
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
        byte[] bytes=new byte[131072];using var http=new HttpClient(new Network(bytes,ignoreRange:ignoreRange));
        var ranked=await UpdateSources.RankAsync(http,Update(bytes),[new("https://mirror.test/clipshot/")],CancellationToken.None);
        Assert.Equal("mirror.test",ranked[0].Host);Assert.Equal("github.com",ranked[1].Host);
    }
    [Fact]public async Task FailedMirrorDoesNotPreventGithubDownload()
    {
        byte[] bytes=new byte[2048];using var http=new HttpClient(new Network(bytes,failMirror:true));
        var ranked=await UpdateSources.RankAsync(http,Update(bytes),[new("https://mirror.test/clipshot/")],CancellationToken.None);
        Assert.Equal("github.com",ranked[0].Host);
    }
    [Fact]public async Task SingleSourceDoesNotIssueExtraProbeRequest()
    {
        byte[] bytes=new byte[2048];using var http=new HttpClient(new Network(bytes));
        var ranked=await UpdateSources.RankAsync(http,Update(bytes),[],CancellationToken.None);Assert.Equal(Update(bytes).Download,Assert.Single(ranked));
    }
    [Theory][InlineData("http://mirror.test/")][InlineData("https://user:pass@mirror.test/")][InlineData("https://mirror.test:444/")][InlineData("https://mirror.test/path")][InlineData("https://mirror.test/?x=1")]
    public void InvalidMirrorRootIsRejected(string root)=>Assert.Throws<InvalidDataException>(()=>UpdateSources.Package(new(root),Update(new byte[2048])));
    [Fact]public async Task RankedMirrorIsUsedByDownloader()
    {
        byte[] bytes=new byte[2048];string root=Path.Combine(Path.GetTempPath(),"ClipShot.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        using var client=new GitHubUpdates(new Network(bytes),[new("https://mirror.test/clipshot/")]);string path=Path.Combine(root,"setup.exe");
        await client.DownloadAsync(Update(bytes),path,new Progress<int>(),CancellationToken.None);Assert.True(UpdateProtocol.Verify(path,Update(bytes)));
    }
}
