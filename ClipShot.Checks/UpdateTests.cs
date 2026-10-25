using System.Net;
using System.Text.Json;
using System.Security.Cryptography;
using Xunit;
namespace ClipShot;
public class UpdateTests
{
    private static string Policy(string target="0.7.0",string action="rollback",string affected="0.8.0")=>JsonSerializer.Serialize(new{schema=1,channel="stable",action,affectedVersions=new[]{affected},targetVersion=target,reason="Исправление ошибки"});
    [Fact]public void ExplicitPolicyAllowsOnlyAffectedVersion()=>Assert.Equal(new Version(0,7,0),RollbackPolicy.Parse(Policy(),new Version(0,8,0))!.Target);
    [Fact]public void UnaffectedVersionIsNotDowngraded()=>Assert.Null(RollbackPolicy.Parse(Policy(),new Version(0,9,0)));
    [Fact]public void InactivePolicyDoesNothing()=>Assert.Null(RollbackPolicy.Parse(Policy(action:"normal"),new Version(0,8,0)));
    [Theory][InlineData("0.6.2")][InlineData("0.8.0")][InlineData("0.9.0")][InlineData("bad")]
    public void UnsafeRollbackTargetIsRejected(string target)=>Assert.Throws<InvalidDataException>(()=>RollbackPolicy.Parse(Policy(target),new Version(0,8,0)));
    [Fact]public void ExplicitRollbackStillValidatesReleaseHash()=>Assert.Throws<InvalidDataException>(()=>UpdateProtocol.Parse(Feed("0.7.0",digest:"bad"),new Version(0,8,0),true));
    [Fact]public void ExplicitRollbackParsesOlderRelease()=>Assert.Equal(new Version(0,7,0),UpdateProtocol.Parse(Feed("0.7.0"),new Version(0,8,0),true)!.Version);
    private static string Feed(string version="9.0.0",string? url=null,string? digest=null,long size=1024,bool draft=false,bool prerelease=false)=>JsonSerializer.Serialize(new{draft,prerelease,tag_name="v"+version,assets=new[]{new{name="ClipShot-Setup-"+version+".exe",size,digest=digest??"sha256:"+new string('a',64),browser_download_url=url??$"https://github.com/gromit7/ClipShot/releases/download/v{version}/ClipShot-Setup-{version}.exe"}}});
    [Fact]public void StableNewVersionIsDiscovered()=>Assert.Equal(new Version(9,0,0),UpdateProtocol.Parse(Feed(),new Version(0,7,0))!.Version);
    [Theory][InlineData(true,false)][InlineData(false,true)]public void DraftAndPreviewAreSkipped(bool draft,bool preview)=>Assert.Null(UpdateProtocol.Parse(Feed(draft:draft,prerelease:preview),new Version(0,7,0)));
    [Theory][InlineData("0.7.0")][InlineData("0.6.2")]public void SameOrOlderVersionNeverInstalls(string version)=>Assert.Null(UpdateProtocol.Parse(Feed(version),new Version(0,7,0,0)));
    [Theory][InlineData("https://evil.test/setup.exe")][InlineData("http://github.com/gromit7/ClipShot/releases/download/v9.0.0/ClipShot-Setup-9.0.0.exe")][InlineData("https://github.com/other/ClipShot/releases/download/v9.0.0/ClipShot-Setup-9.0.0.exe")]
    public void ForeignDownloadLocationIsRejected(string url)=>Assert.Throws<InvalidDataException>(()=>UpdateProtocol.Parse(Feed(url:url),new Version(0,7,0)));
    [Theory][InlineData("md5:abc")][InlineData("sha256:abc")][InlineData("sha256:xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    public void MissingOrInvalidDigestIsRejected(string digest)=>Assert.Throws<InvalidDataException>(()=>UpdateProtocol.Parse(Feed(digest:digest),new Version(0,7,0)));
    [Theory][InlineData(0)][InlineData(999999999)]public void UnreasonablePackageSizeIsRejected(long size)=>Assert.Throws<InvalidDataException>(()=>UpdateProtocol.Parse(Feed(size:size),new Version(0,7,0)));
    private sealed class Handler(byte[] data):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(data)});}
    private sealed class PolicyHandler(string release):HttpMessageHandler
    {
        internal readonly List<string> Requests=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            string url=request.RequestUri!.AbsoluteUri;Requests.Add(url);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(url.Contains("raw.githubusercontent.com")?Policy():release)});
        }
    }
    [Fact]public async Task RemoteRollbackFetchesExactTargetAndMarksRecovery()
    {
        var handler=new PolicyHandler(Feed("0.7.0"));using var client=new GitHubUpdates(handler);
        var update=await client.CheckAsync(new Version(0,8,0),CancellationToken.None);
        Assert.True(update!.IsRollback);Assert.Equal(new Version(0,7,0),update.Version);
        Assert.EndsWith("/releases/tags/v0.7.0",handler.Requests.Last());
    }
    [Fact]public async Task WrongReleaseCannotSatisfyRollbackPolicy()
    {
        using var client=new GitHubUpdates(new PolicyHandler(Feed("0.7.1")));
        await Assert.ThrowsAsync<InvalidDataException>(()=>client.CheckAsync(new Version(0,8,0),CancellationToken.None));
    }
    [Fact]public async Task DownloadVerifiesHashBeforePublishingInstaller()
    {
        byte[] data=Enumerable.Range(0,2048).Select(i=>(byte)i).ToArray();string folder=Path.Combine(Path.GetTempPath(),"ClipShot.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);string path=Path.Combine(folder,"setup.exe");
        var update=new AppUpdate(new(9,0,0),new("https://github.com/gromit7/ClipShot/releases/download/v9.0.0/ClipShot-Setup-9.0.0.exe"),data.Length,Convert.ToHexString(SHA256.HashData(data)));
        using var client=new GitHubUpdates(new Handler(data));await client.DownloadAsync(update,path,new Progress<int>(),CancellationToken.None);Assert.True(UpdateProtocol.Verify(path,update));Assert.False(File.Exists(path+".partial"));
        data[0]^=1;File.WriteAllBytes(path,data);Assert.False(UpdateProtocol.Verify(path,update));
    }
    [Fact]public async Task CorruptedDownloadIsRemovedAndNeverBecomesInstaller()
    {
        byte[] data=new byte[2048];string folder=Path.Combine(Path.GetTempPath(),"ClipShot.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);string path=Path.Combine(folder,"setup.exe");
        var update=new AppUpdate(new(9,0,0),new("https://github.com/gromit7/ClipShot/releases/download/v9.0.0/ClipShot-Setup-9.0.0.exe"),data.Length,new string('a',64));
        using var client=new GitHubUpdates(new Handler(data));await Assert.ThrowsAsync<InvalidDataException>(()=>client.DownloadAsync(update,path,new Progress<int>(),CancellationToken.None));Assert.False(File.Exists(path));Assert.False(File.Exists(path+".partial"));
    }
}
