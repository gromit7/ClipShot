using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;
namespace ClipShot;

public class UnitPlatformPackagesTests
{
    private static readonly Version Current=new(0,7,3),Next=new(0,7,4);
    private static string Release(string flavor="legacy",string version="0.7.4",string? name=null,string? url=null,string? digest=null)=>JsonSerializer.Serialize(new
    {
        draft=false,prerelease=false,tag_name="v"+version,
        assets=new[]{new{name=name??UpdatePackages.Name(Version.Parse(version),flavor),size=2048,
            digest=digest??"sha256:"+new string('a',64),browser_download_url=url??$"https://github.com/gromit7/ClipShot/releases/download/v{version}/{UpdatePackages.Name(Version.Parse(version),flavor)}"}}
    });
    private static JsonElement Element(string json)=>JsonSerializer.Deserialize<JsonElement>(json);
    private static string Feed(string? win10=null,string? win11=null,bool legacyOnly=false,object? rollback=null,string? stable=null)=>JsonSerializer.Serialize(new Dictionary<string,object?>
    {
        ["schema"]=1,["stable"]=Element(stable??Release()),["rollback"]=rollback,
        // У старой ленты поле variants отсутствует, а не null.
    }.Concat(legacyOnly?[]:new Dictionary<string,object?>{["variants"]=new Dictionary<string,object?>
        {["win10"]=win10 is null?null:Element(win10),["win11"]=win11 is null?null:Element(win11)}}).ToDictionary(p=>p.Key,p=>p.Value));
    [Theory][InlineData(17762,null)][InlineData(17763,"win10")][InlineData(19041,"win10")][InlineData(21999,"win10")][InlineData(22000,"win11")][InlineData(26100,"win11")]
    public void OsBuildSelectsExactSupportedFlavor(int build,string? expected)=>Assert.Equal(expected,UpdatePackages.FlavorForBuild(build));
    [Theory][InlineData("legacy",17762,false)][InlineData("legacy",17763,true)][InlineData("legacy",22000,true)]
    [InlineData("win10",17762,false)][InlineData("win10",17763,true)][InlineData("win10",21999,true)][InlineData("win10",22000,false)]
    [InlineData("win11",21999,false)][InlineData("win11",22000,true)][InlineData("win11",26100,true)]
    public void NativeGuardContractHasSameBoundaries(string flavor,int build,bool expected)=>Assert.Equal(expected,UpdatePackages.Supports(flavor,build));
    [Theory][InlineData("legacy","ClipShot-Setup-0.7.4.exe")][InlineData("win10","ClipShot-Win10-Setup-0.7.4.exe")][InlineData("win11","ClipShot-Win11-Setup-0.7.4.exe")]
    public void NamesRoundTripAndKeepVersionForFailedUpdate(string flavor,string name)
    {
        Assert.Equal(name,UpdatePackages.Name(Next,flavor));Assert.True(UpdatePackages.TryParseName(name,out var version,out var actual));Assert.Equal(Next,version);Assert.Equal(flavor,actual);
    }
    [Theory][InlineData("ClipShot-Win12-Setup-0.7.4.exe")][InlineData("clipshot-Win10-Setup-0.7.4.exe")]
    [InlineData("ClipShot-Win10-Setup-00.7.4.exe")][InlineData("ClipShot-Win10-Setup-0.7.4.0.exe")]
    [InlineData("../ClipShot-Win10-Setup-0.7.4.exe")][InlineData("ClipShot-Win10-Setup-0.7.4.exe.bak")]
    public void UnapprovedPackageNamesAreRejected(string name)=>Assert.False(UpdatePackages.TryParseName(name,out _,out _));
    [Theory][InlineData(17763,"win10")][InlineData(19041,"win10")][InlineData(21999,"win10")][InlineData(22000,"win11")][InlineData(26100,"win11")]
    public void FeedChoosesOnlyPackageForCurrentWindows(int build,string flavor)
    {
        var update=UpdateFeed.Parse(Feed(Release("win10"),Release("win11")),Current,build)!;
        Assert.Equal(UpdatePackages.Name(Next,flavor),update.PackageName);Assert.EndsWith("/"+update.PackageName,update.Download.AbsoluteUri);
    }
    [Fact]public void UnsupportedWindowsNeverDownloads()=>Assert.Null(UpdateFeed.Parse(Feed(Release("win10"),Release("win11")),Current,17762));
    [Fact]public void OldFeedRemainsUsableByBothOsFamilies()
    {
        foreach(int build in new[]{17763,22000})Assert.Equal(UpdatePackages.Name(Next),UpdateFeed.Parse(Feed(legacyOnly:true),Current,build)!.PackageName);
    }
    [Fact]public void RevokedOsPackageDoesNotFallBackToLegacyOrOtherOs()=>Assert.Null(UpdateFeed.Parse(Feed(win11:Release("win11")),Current,17763));
    [Fact]public void MissingOsPackageDoesNotFallBack()
    {
        string json=JsonSerializer.Serialize(new{schema=1,stable=Element(Release()),rollback=(object?)null,variants=new{win11=Element(Release("win11"))}});
        Assert.Throws<InvalidDataException>(()=>UpdateFeed.Parse(json,Current,17763));
    }
    [Fact]public void GlobalRevocationOverridesAnyStaleVariant()
    {
        string json=JsonSerializer.Serialize(new{schema=1,stable=(object?)null,rollback=(object?)null,variants=new{win10=Element(Release("win10"))}});
        Assert.Null(UpdateFeed.Parse(json,Current,17763));
    }
    [Fact]public void VariantCannotChangeReleaseIdentity()=>Assert.Throws<InvalidDataException>(()=>UpdateFeed.Parse(Feed(Release("win10","0.7.5"),Release("win11")),Current,17763));
    [Fact]public void VariantMustContainExactFlavorFilename()=>Assert.Null(UpdateFeed.Parse(Feed(Release(),Release("win11")),Current,17763));
    [Fact]public void VariantMustKeepOfficialUrlAndSha()
    {
        Assert.Throws<InvalidDataException>(()=>UpdateFeed.Parse(Feed(Release("win10",url:"https://evil.test/ClipShot-Win10-Setup-0.7.4.exe")),Current,17763));
        Assert.Throws<InvalidDataException>(()=>UpdateFeed.Parse(Feed(Release("win10",digest:"bad")),Current,17763));
        Assert.Throws<InvalidDataException>(()=>UpdateProtocol.Parse(Release("win10"),Current,flavor:"win10",osBuild:22000));
    }
    [Theory][InlineData(17763,"win10")][InlineData(22000,"win11")]
    public void AffectedRollbackSelectsMatchingOsPackage(int build,string flavor)
    {
        var policy=new{schema=1,channel="stable",action="rollback",affectedVersions=new[]{"0.7.5"},targetVersion="0.7.4",reason="Исправление"};
        var rollback=new{policy,release=Element(Release()),variants=new{win10=Element(Release("win10")),win11=Element(Release("win11"))}};
        var update=UpdateFeed.Parse(Feed(Release("win10"),Release("win11"),rollback:rollback),new(0,7,5),build)!;
        Assert.True(update.IsRollback);Assert.Equal(UpdatePackages.Name(Next,flavor),update.PackageName);
        Assert.Null(UpdateFeed.Parse(Feed(Release("win10"),Release("win11"),rollback:rollback),new(0,7,6),build));
    }
    [Fact]public void LegacyRollbackTargetStillWorksForOldReleases()
    {
        var rollback=new{policy=new{schema=1,channel="stable",action="rollback",affectedVersions=new[]{"0.7.5"},targetVersion="0.7.3",reason="Исправление"},release=Element(Release(version:"0.7.3"))};
        Assert.Equal("ClipShot-Setup-0.7.3.exe",UpdateFeed.Parse(Feed(Release("win10"),Release("win11"),rollback:rollback),new(0,7,5),17763)!.PackageName);
    }
    [Fact]public void MirrorKeepsSelectedPackageName()
    {
        var update=UpdateProtocol.Parse(Release("win10"),Current,flavor:"win10")!;
        Assert.Equal("https://mirror.test/clipshot/v0.7.4/ClipShot-Win10-Setup-0.7.4.exe",UpdateSources.Package(new("https://mirror.test/clipshot/"),update).AbsoluteUri);
        Assert.Throws<InvalidDataException>(()=>UpdateSources.Package(new("https://mirror.test/"),update with{PackageName="../setup.exe"}));
    }
    [Fact]public void PendingPackageKeepsFlavorAndRefusesWrongWindowsAfterRestart()
    {
        string root=Path.Combine(Path.GetTempPath(),"ClipShot.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var update=UpdateProtocol.Parse(Release("win10"),Current,flavor:"win10")!;byte[] bytes=new byte[2048];Random.Shared.NextBytes(bytes);
            update=update with{Sha256=Convert.ToHexString(SHA256.HashData(bytes))};string file=Path.Combine(root,UpdatePackages.FileName(update));File.WriteAllBytes(file,bytes);
            string json=JsonSerializer.Serialize(new PendingPackage(file,update));
            Assert.Equal(update,PendingUpdate.Read(json,root,Current,17763)!.Update);
            Assert.Throws<InvalidDataException>(()=>PendingUpdate.Read(json,root,Current,22000));
            Assert.Throws<InvalidDataException>(()=>PendingUpdate.Read(JsonSerializer.Serialize(new PendingPackage(file,update with{PackageName="ClipShot-Win11-Setup-0.7.4.exe"})),root,Current,22000));
        }
        finally{Directory.Delete(root,true);}
    }
    private sealed class OneFeedHandler(string json):HttpMessageHandler
    {
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {Requests++;Assert.Equal(UpdateFeed.Url,request.RequestUri!.AbsoluteUri);return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json)});}
    }
    [Fact]public async Task FlavorSelectionStillUsesExactlyOneFeedGet()
    {
        var handler=new OneFeedHandler(Feed(Release("win10"),Release("win11")));using var client=new GitHubUpdates(handler);
        Assert.Equal("ClipShot-Win10-Setup-0.7.4.exe",(await client.CheckAsync(Current,CancellationToken.None,17763))!.PackageName);Assert.Equal(1,handler.Requests);
    }
}
