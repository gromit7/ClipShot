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
    [Theory][InlineData("https://evil.test/setup.exe")][InlineData("http://github.com/gromit7/ClipShot/releases/download/v9.0.0/ClipShot-Setup-9.0.0.exe")][InlineData("https://github.com/other/ClipShot/releases/download/v9.0.0/ClipShot-Setup-9.0.0.exe")][InlineData("https://github.com:444/gromit7/ClipShot/releases/download/v9.0.0/ClipShot-Setup-9.0.0.exe")][InlineData("https://github.com/gromit7/ClipShot/releases/download/v9.0.0/ClipShot-Setup-9.0.0.exe#changed")]
    public void ForeignDownloadLocationIsRejected(string url)=>Assert.Throws<InvalidDataException>(()=>UpdateProtocol.Parse(Feed(url:url),new Version(0,7,0)));
    [Theory][InlineData("bot.data-clouds.ru")][InlineData("tg.data-clouds.ru")]
    public void ExactTrustedOriginAndRelayPackageUrlsAreAccepted(string host)
        =>Assert.Equal(host,UpdateProtocol.Parse(Feed(url:$"https://{host}/clipshot/v9.0.0/ClipShot-Setup-9.0.0.exe"),new(0,7,0))!.Download.Host);
    [Theory][InlineData("http://bot.data-clouds.ru/clipshot/v9.0.0/ClipShot-Setup-9.0.0.exe")]
    [InlineData("https://bot.data-clouds.ru:444/clipshot/v9.0.0/ClipShot-Setup-9.0.0.exe")]
    [InlineData("https://user@tg.data-clouds.ru/clipshot/v9.0.0/ClipShot-Setup-9.0.0.exe")]
    [InlineData("https://tg.data-clouds.ru/clipshot/v8.0.0/ClipShot-Setup-9.0.0.exe")]
    [InlineData("https://bot.data-clouds.ru/clipshot/v9.0.0/ClipShot-Win10-Setup-9.0.0.exe")]
    [InlineData("https://bot.data-clouds.ru/clipshot/v9.0.0/ClipShot-Setup-9.0.0.exe?changed=1")]
    [InlineData("https://tg.data-clouds.ru/clipshot/v9.0.0/ClipShot-Setup-9.0.0.exe#changed")]
    [InlineData("https://other.data-clouds.ru/clipshot/v9.0.0/ClipShot-Setup-9.0.0.exe")]
    public void TrustedHostDoesNotBypassExactPackageLocation(string url)=>Assert.Throws<InvalidDataException>(()=>UpdateProtocol.Parse(Feed(url:url),new(0,7,0)));
    [Theory][InlineData("md5:abc")][InlineData("sha256:abc")][InlineData("sha256:xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    public void MissingOrInvalidDigestIsRejected(string digest)=>Assert.Throws<InvalidDataException>(()=>UpdateProtocol.Parse(Feed(digest:digest),new Version(0,7,0)));
    [Theory][InlineData(0)][InlineData(999999999)]public void UnreasonablePackageSizeIsRejected(long size)=>Assert.Throws<InvalidDataException>(()=>UpdateProtocol.Parse(Feed(size:size),new Version(0,7,0)));
    private sealed class Handler(byte[] data):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(data)});}
    private static string Document(string? stable,string? policy=null,string? rollbackRelease=null)=>JsonSerializer.Serialize(new
    {
        schema=1,
        stable=stable is null?(JsonElement?)null:JsonSerializer.Deserialize<JsonElement>(stable),
        rollback=policy is null?null:new{policy=JsonSerializer.Deserialize<JsonElement>(policy),release=JsonSerializer.Deserialize<JsonElement>(rollbackRelease??Feed("0.7.0"))}
    });
    private sealed class UnknownLengthContent(byte[] data):HttpContent
    {
        protected override bool TryComputeLength(out long length){length=0;return false;}
        protected override Task SerializeToStreamAsync(Stream stream,TransportContext? context)=>stream.WriteAsync(data).AsTask();
        protected override Task<Stream> CreateContentReadStreamAsync()=>Task.FromResult<Stream>(new MemoryStream(data));
    }
    private sealed class FeedHandler(string json,HttpStatusCode status=HttpStatusCode.OK,bool unknownLength=false):HttpMessageHandler
    {
        internal readonly List<string> Requests=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Assert.Equal(HttpMethod.Get,request.Method);
            Requests.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(status){Content=unknownLength?new UnknownLengthContent(System.Text.Encoding.UTF8.GetBytes(json)):new StringContent(json)});
        }
    }
    private static void AssertSingleFeedRequest(FeedHandler handler)=>Assert.Equal(UpdateFeed.Url,Assert.Single(handler.Requests));
    [Fact]public async Task StableUpdateNeedsOnlyOneShortFeedRequest()
    {
        var handler=new FeedHandler(Document(Feed()));using var client=new GitHubUpdates(handler);
        var update=await client.CheckAsync(new Version(0,7,0),CancellationToken.None);
        Assert.Equal(new Version(9,0,0),update!.Version);Assert.False(update.IsRollback);AssertSingleFeedRequest(handler);
    }
    [Fact]public async Task RemoteRollbackComesFromSameSingleFeedAndMarksRecovery()
    {
        var handler=new FeedHandler(Document(Feed(),Policy(),Feed("0.7.0")));using var client=new GitHubUpdates(handler);
        var update=await client.CheckAsync(new Version(0,8,0),CancellationToken.None);
        Assert.True(update!.IsRollback);Assert.Equal(new Version(0,7,0),update.Version);
        Assert.Equal("Исправление ошибки",update.Reason);AssertSingleFeedRequest(handler);
    }
    [Fact]public async Task WrongReleaseCannotSatisfyRollbackPolicy()
    {
        var handler=new FeedHandler(Document(Feed(),Policy(),Feed("0.7.1")));using var client=new GitHubUpdates(handler);
        await Assert.ThrowsAsync<InvalidDataException>(()=>client.CheckAsync(new Version(0,8,0),CancellationToken.None));
        AssertSingleFeedRequest(handler);
    }
    [Fact]public async Task RollbackForAnotherVersionDoesNotOverrideStableUpdate()
    {
        var handler=new FeedHandler(Document(Feed(),Policy()));using var client=new GitHubUpdates(handler);
        var update=await client.CheckAsync(new Version(0,7,2),CancellationToken.None);
        Assert.Equal(new Version(9,0,0),update!.Version);Assert.False(update.IsRollback);AssertSingleFeedRequest(handler);
    }
    [Fact]public async Task RevokedStableOfferDoesNotUseFallback()
    {
        var handler=new FeedHandler(Document(null));using var client=new GitHubUpdates(handler);
        Assert.Null(await client.CheckAsync(new Version(0,7,0),CancellationToken.None));AssertSingleFeedRequest(handler);
    }
    [Theory][InlineData(HttpStatusCode.NotFound)][InlineData(HttpStatusCode.ServiceUnavailable)][InlineData(HttpStatusCode.Redirect)]
    public async Task FeedHttpFailureNeverFallsBackToReleaseApi(HttpStatusCode status)
    {
        var handler=new FeedHandler(Document(Feed()),status);using var client=new GitHubUpdates(handler);
        await Assert.ThrowsAsync<HttpRequestException>(()=>client.CheckAsync(new Version(0,7,0),CancellationToken.None));Assert.Equal(new[]{UpdateFeed.Url,UpdateFeed.FallbackUrl},handler.Requests);
    }
    [Fact]public async Task InvalidFeedNeverFallsBackToReleaseApi()
    {
        var handler=new FeedHandler("not json");using var client=new GitHubUpdates(handler);
        await Assert.ThrowsAnyAsync<JsonException>(()=>client.CheckAsync(new Version(0,7,0),CancellationToken.None));AssertSingleFeedRequest(handler);
    }
    private sealed class RoutedFeedHandler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> respond):HttpMessageHandler
    {
        internal readonly List<string> Requests=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Assert.Equal(HttpMethod.Get,request.Method);Assert.True(request.Headers.CacheControl?.NoCache);Requests.Add(request.RequestUri!.AbsoluteUri);return respond(request,token);
        }
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task RfTransportErrorOrTimeoutUsesFreshDeDeadline(bool timeout)
    {
        var handler=new RoutedFeedHandler(async(request,token)=>
        {
            if(request.RequestUri!.AbsoluteUri==UpdateFeed.Url){if(timeout)await Task.Delay(Timeout.Infinite,token);throw new HttpRequestException("synthetic unavailable origin");}
            return new(HttpStatusCode.OK){Content=new StringContent(Document(Feed()))};
        });
        using var client=new GitHubUpdates(handler){FeedAttemptTimeout=TimeSpan.FromMilliseconds(50)};
        Assert.Equal(new Version(9,0,0),(await client.CheckAsync(new(0,7,0),TestContext.Current.CancellationToken))!.Version);
        Assert.Equal(new[]{UpdateFeed.Url,UpdateFeed.FallbackUrl},handler.Requests);
    }
    [Theory][InlineData(HttpStatusCode.NotFound)][InlineData(HttpStatusCode.ServiceUnavailable)][InlineData(HttpStatusCode.Redirect)]
    public async Task RfHttpErrorUsesDeFeedWithoutGithubRequests(HttpStatusCode status)
    {
        var handler=new RoutedFeedHandler((request,_)=>Task.FromResult(new HttpResponseMessage(request.RequestUri!.AbsoluteUri==UpdateFeed.Url?status:HttpStatusCode.OK){Content=new StringContent(Document(Feed()))}));
        using var client=new GitHubUpdates(handler);Assert.NotNull(await client.CheckAsync(new(0,7,0),TestContext.Current.CancellationToken));
        Assert.Equal(new[]{UpdateFeed.Url,UpdateFeed.FallbackUrl},handler.Requests);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task SuccessfulRfResponseWithInvalidSchemaOrHashNeverUsesDe(bool badHash)
    {
        string json=badHash?Document(Feed(digest:"bad")):"{\"schema\":2,\"stable\":null,\"rollback\":null}";
        var handler=new FeedHandler(json);using var client=new GitHubUpdates(handler);
        await Assert.ThrowsAsync<InvalidDataException>(()=>client.CheckAsync(new(0,7,0),TestContext.Current.CancellationToken));AssertSingleFeedRequest(handler);
    }
    [Fact]public async Task CallerCancellationNeverStartsTheRelayRequest()
    {
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler=new RoutedFeedHandler(async(_,token)=>{entered.TrySetResult();await Task.Delay(Timeout.Infinite,token);return new(HttpStatusCode.OK);});
        using var client=new GitHubUpdates(handler);using var stop=CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var checking=client.CheckAsync(new(0,7,0),stop.Token);await entered.Task.WaitAsync(TimeSpan.FromSeconds(5),TestContext.Current.CancellationToken);stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>checking);Assert.Equal(UpdateFeed.Url,Assert.Single(handler.Requests));
    }
    [Fact]public void FeedRejectsUnknownSchema()=>Assert.Throws<InvalidDataException>(()=>UpdateFeed.Parse("{\"schema\":2,\"stable\":null,\"rollback\":null}",new(0,7,0)));
    [Fact]public void FeedNeverDowngradesWithoutMatchingPolicy()=>Assert.Null(UpdateFeed.Parse(Document(Feed("0.7.0")),new(0,8,0)));
    [Fact]public void FeedStillRejectsForeignRollbackInstaller()=>Assert.Throws<InvalidDataException>(()=>UpdateFeed.Parse(Document(Feed(),Policy(),Feed("0.7.0",url:"https://evil.test/setup.exe")),new(0,8,0)));
    [Fact]public void FeedStillRequiresSha256()=>Assert.Throws<InvalidDataException>(()=>UpdateFeed.Parse(Document(Feed(digest:"bad")),new(0,7,0)));
    [Fact]public void FeedAcceptsExactlyThirtyTwoKiB()
    {
        string json=Document(Feed());json=json.PadRight(UpdateFeed.MaximumBytes);
        Assert.NotNull(UpdateFeed.Parse(json,new(0,7,0)));
        Assert.Throws<InvalidDataException>(()=>UpdateFeed.Parse(json+" ",new(0,7,0)));
    }
    [Fact]public void FeedLimitCountsUtf8BytesRatherThanCharacters()
    {
        string json="{\"schema\":1,\"stable\":null,\"rollback\":null,\"note\":\""+new string('я',17000)+"\"}";
        Assert.True(json.Length<UpdateFeed.MaximumBytes);Assert.Throws<InvalidDataException>(()=>UpdateFeed.Parse(json,new(0,7,0)));
    }
    [Theory][InlineData(false)][InlineData(true)]public async Task OversizedFeedIsRejectedAfterOneRequest(bool unknownLength)
    {
        var handler=new FeedHandler(new string(' ',UpdateFeed.MaximumBytes+1),unknownLength:unknownLength);using var client=new GitHubUpdates(handler);
        await Assert.ThrowsAsync<InvalidDataException>(()=>client.CheckAsync(new Version(0,7,0),CancellationToken.None));AssertSingleFeedRequest(handler);
    }
    [Fact]public async Task DownloadVerifiesHashBeforePublishingInstaller()
    {
        byte[] data=Enumerable.Range(0,2048).Select(i=>(byte)i).ToArray();string folder=Path.Combine(Path.GetTempPath(),"ClipShot.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);string path=Path.Combine(folder,"setup.exe");
        var update=new AppUpdate(new(9,0,0),new("https://github.com/gromit7/ClipShot/releases/download/v9.0.0/ClipShot-Setup-9.0.0.exe"),data.Length,Convert.ToHexString(SHA256.HashData(data)));
        using var client=new GitHubUpdates(new Handler(data),[]){DownloadRetryDelay=TimeSpan.Zero};await client.DownloadAsync(update,path,new Progress<int>(),CancellationToken.None);Assert.True(UpdateProtocol.Verify(path,update));Assert.False(File.Exists(path+".partial"));
        data[0]^=1;File.WriteAllBytes(path,data);Assert.False(UpdateProtocol.Verify(path,update));
    }
    [Fact]public async Task CorruptedDownloadIsRemovedAndNeverBecomesInstaller()
    {
        byte[] data=new byte[2048];string folder=Path.Combine(Path.GetTempPath(),"ClipShot.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);string path=Path.Combine(folder,"setup.exe");
        var update=new AppUpdate(new(9,0,0),new("https://github.com/gromit7/ClipShot/releases/download/v9.0.0/ClipShot-Setup-9.0.0.exe"),data.Length,new string('a',64));
        using var client=new GitHubUpdates(new Handler(data),[]){DownloadRetryDelay=TimeSpan.Zero};await Assert.ThrowsAsync<InvalidDataException>(()=>client.DownloadAsync(update,path,new Progress<int>(),CancellationToken.None));Assert.False(File.Exists(path));Assert.False(File.Exists(path+".partial"));
    }
}
