using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Xunit;
namespace ClipShot;
public class DownloadRecoveryTests
{
    private sealed class BrokenStream(byte[] bytes,bool stall,TaskCompletionSource? waiting=null):MemoryStream(bytes)
    {
        private bool sent;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default)
        {
            if(!sent){sent=true;return await base.ReadAsync(buffer[..Math.Min(512,buffer.Length)],token);}
            if(stall){waiting?.TrySetResult();await Task.Delay(Timeout.Infinite,token);}
            throw new IOException("synthetic interrupted network");
        }
    }
    private sealed class Network(byte[] bytes,bool ignoreRange=false,bool stall=false,bool wrongRange=false,bool alwaysFail=false):HttpMessageHandler
    {
        public readonly List<long?> Offsets=[];
        internal readonly TaskCompletionSource WaitingForBytes=new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            long? offset=request.Headers.Range?.Ranges.Single().From;Offsets.Add(offset);
            if(Offsets.Count==1||alwaysFail)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(new BrokenStream(bytes,stall,WaitingForBytes))});
            if(ignoreRange)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)});
            var content=new ByteArrayContent(bytes[(int)offset!.Value..]);content.Headers.ContentRange=new ContentRangeHeaderValue(wrongRange?0:offset.Value,bytes.Length-1,bytes.Length);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent){Content=content});
        }
    }
    [Theory][InlineData(false,false)][InlineData(true,false)][InlineData(false,true)]
    public async Task InterruptedDownloadResumesOrRestartsWhenServerIgnoresRange(bool ignoreRange,bool stall)
    {
        byte[] bytes=new byte[2048];Random.Shared.NextBytes(bytes);var network=new Network(bytes,ignoreRange,stall);
        string root=Path.Combine(Path.GetTempPath(),"ClipShot.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string path=Path.Combine(root,"setup.exe");
        var update=new AppUpdate(new(9,0,0),new("https://github.com/gromit7/ClipShot/releases/download/v9.0.0/ClipShot-Setup-9.0.0.exe"),bytes.Length,Convert.ToHexString(SHA256.HashData(bytes)));
        using var client=new GitHubUpdates(network,[]){DownloadStallTimeout=TimeSpan.FromMilliseconds(100),DownloadRetryDelay=TimeSpan.Zero};
        await client.DownloadAsync(update,path,new Progress<int>(),CancellationToken.None);
        Assert.Equal(new long?[]{null,512},network.Offsets);Assert.True(UpdateProtocol.Verify(path,update));Assert.False(File.Exists(path+".partial"));
    }
    [Fact]public async Task CallerCancellationStopsDownloadWithoutRetryOrPartialInstaller()
    {
        byte[] bytes=new byte[2048];var network=new Network(bytes,stall:true);
        string root=Path.Combine(Path.GetTempPath(),"ClipShot.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string path=Path.Combine(root,"setup.exe");
        var update=new AppUpdate(new(9,0,0),new("https://github.com/gromit7/ClipShot/releases/download/v9.0.0/ClipShot-Setup-9.0.0.exe"),bytes.Length,Convert.ToHexString(SHA256.HashData(bytes)));
        using var stop=CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);var retries=new List<int>();
        using var client=new GitHubUpdates(network,[]){DownloadRetryDelay=TimeSpan.Zero};
        try
        {
            var downloading=client.DownloadAsync(update,path,new Progress<int>(),stop.Token,retries.Add);
            await network.WaitingForBytes.Task.WaitAsync(TimeSpan.FromSeconds(5),TestContext.Current.CancellationToken);
            Assert.True(File.Exists(path+".partial"));stop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>downloading);
            Assert.Single(network.Offsets);Assert.Empty(retries);Assert.False(File.Exists(path));Assert.False(File.Exists(path+".partial"));
        }
        finally{stop.Cancel();Directory.Delete(root,true);}
    }
    [Fact]public async Task IncorrectRangeNeverPublishesInstaller()
    {
        byte[] bytes=new byte[2048];var network=new Network(bytes,wrongRange:true);string root=Path.Combine(Path.GetTempPath(),"ClipShot.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string path=Path.Combine(root,"setup.exe");
        var update=new AppUpdate(new(9,0,0),new("https://github.com/gromit7/ClipShot/releases/download/v9.0.0/ClipShot-Setup-9.0.0.exe"),bytes.Length,Convert.ToHexString(SHA256.HashData(bytes)));
        using var client=new GitHubUpdates(network,[]){DownloadRetryDelay=TimeSpan.Zero};await Assert.ThrowsAsync<InvalidDataException>(()=>client.DownloadAsync(update,path,new Progress<int>(),CancellationToken.None));Assert.False(File.Exists(path));Assert.False(File.Exists(path+".partial"));
    }
    [Fact]public async Task RepeatedNetworkFailureStopsAfterThreeAttempts()
    {
        byte[] bytes=new byte[2048];var network=new Network(bytes,alwaysFail:true);string root=Path.Combine(Path.GetTempPath(),"ClipShot.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string path=Path.Combine(root,"setup.exe");
        var update=new AppUpdate(new(9,0,0),new("https://github.com/gromit7/ClipShot/releases/download/v9.0.0/ClipShot-Setup-9.0.0.exe"),bytes.Length,new string('a',64));
        using var client=new GitHubUpdates(network,[]){DownloadRetryDelay=TimeSpan.Zero};await Assert.ThrowsAsync<IOException>(()=>client.DownloadAsync(update,path,new Progress<int>(),CancellationToken.None));Assert.Equal(3,network.Offsets.Count);Assert.False(File.Exists(path));
    }
}
