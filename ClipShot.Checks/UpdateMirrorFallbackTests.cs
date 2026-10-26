using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Xunit;
namespace ClipShot;

public class UpdateMirrorFallbackTests
{
    private sealed class Network(byte[] bytes,string mirrorError):HttpMessageHandler
    {
        internal readonly ConcurrentQueue<(string Host,long? Offset)> Downloads=[];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            bool mirror=request.RequestUri!.Host=="mirror.test";
            var range=request.Headers.Range?.Ranges.Single();
            if(range?.To is {} to)
            {
                // Оба источника отвечают на пробу; зеркало заведомо быстрее GitHub.
                if(!mirror)await Task.Delay(100,token);
                var sample=new ByteArrayContent(bytes[..((int)to+1)]);sample.Headers.ContentRange=new ContentRangeHeaderValue(0,to,bytes.Length);
                return new(HttpStatusCode.PartialContent){Content=sample};
            }
            Downloads.Enqueue((request.RequestUri.Host,range?.From));
            if(!mirror)return new(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)};
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
    public async Task CorruptFastMirrorFallsBackToVerifiedGithubWithoutBadPrefix(string error)
    {
        byte[] bytes=new byte[131072];Random.Shared.NextBytes(bytes);var network=new Network(bytes,error);
        string root=Path.Combine(Path.GetTempPath(),"ClipShot.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string path=Path.Combine(root,"setup.exe");
        var update=Update(bytes);var retries=new List<int>();
        using var client=new GitHubUpdates(network,[new("https://mirror.test/clipshot/")]){DownloadRetryDelay=TimeSpan.Zero};
        await client.DownloadAsync(update,path,new Progress<int>(),CancellationToken.None,retries.Add);
        Assert.Equal(new[]{("mirror.test",(long?)null),("github.com",(long?)null)},network.Downloads.ToArray());
        Assert.Equal(new[]{1},retries);Assert.True(UpdateProtocol.Verify(path,update));Assert.Equal(bytes,File.ReadAllBytes(path));Assert.False(File.Exists(path+".partial"));
    }
}
