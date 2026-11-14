using System.Net;
using System.Text.Json;
namespace ClipShot;
public sealed partial class MainWindow
{
    private sealed class FeedbackFeedHandler(string? json):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
            =>json is null?Task.FromException<HttpResponseMessage>(new HttpRequestException("Synthetic offline")):
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json)});
    }
    private async Task CheckUpdateFeedback(Action<bool,string> check)
    {
        var previous=updateClient;bool valid=updateMetadataValid;var candidate=availableUpdate;
        try
        {
            string version=typeof(App).Assembly.GetName().Version!.ToString(3);
            var feed=JsonSerializer.Serialize(new{schema=1,stable=new{draft=false,prerelease=false,tag_name="v"+version,assets=new[]{new{name="ClipShot-Setup-"+version+".exe",size=1024,digest="sha256:"+new string('a',64),browser_download_url="https://github.com/gromit7/ClipShot/releases/download/v"+version+"/ClipShot-Setup-"+version+".exe"}}},rollback=(object?)null});
            using(var client=new GitHubUpdates(new FeedbackFeedHandler(feed)))
            {
                updateClient=client;await CheckUpdates(true,true);
                check(updateMetadataValid&&availableUpdate is null&&lastUpdateFeedback?.Title=="Обновлений нет","successful same-version check reports no updates, not a network error");
            }
            using(var client=new GitHubUpdates(new FeedbackFeedHandler(null)))
            {
                updateClient=client;await CheckUpdates(true,true);
                check(!updateMetadataValid&&lastUpdateFeedback?.Title=="Не удалось проверить обновления","offline check remains distinguishable from confirmed no updates");
            }
            installationNotice=new MicrophoneNotice("Обновлений нет","Синтетическая проверка",MicrophoneState.Enabled,preview:true,appNotice:true);
            await Task.Delay(100);
            installationNotice.Close();CloseUpdateNotice();CloseUpdateNotice();
            check(installationNotice is null,"a previously dismissed update notice can be replaced safely");
        }
        finally{updateClient=previous;updateMetadataValid=valid;availableUpdate=candidate;}
    }
}
