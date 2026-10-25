using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;
namespace ClipShot;
public sealed partial class MainWindow
{
    private readonly DispatcherTimer updateTimer=new(){Interval=TimeSpan.FromSeconds(45)};
    private readonly CancellationTokenSource updateStop=new();
    private MicrophoneNotice? installationNotice;
    private GitHubUpdates? updateClient;
    private AppUpdate? availableUpdate;
    private bool updateChecking,updateDownloading,updateInstallRequested,updateInstalling,requestedAutomatically,updateApplying;
    private AppUpdate? preparedUpdate;
    private DateTime nextUpdateAttempt;
    private bool AutomaticUpdates=>store.Get("updatecheck","1")=="1"&&store.Get("updateinstall","auto")!="manual";
    private bool CanAutoInstall()=>UpdateIdlePolicy.CanInstall(isolated,AutomaticUpdates,AppWindow.IsVisible,dialogOpen||takingScreenshot||editor is not null||capturing||captureAgain,UserInactivity.Read());
    private DateTime nextUpdateCheck=DateTime.UtcNow;
    private DateTime updateDismissedUntil;
    private string? preparedInstaller;
    private void StartUpdates()
    {
        if(isolated)return;
        var skip=Environment.GetCommandLineArgs().FirstOrDefault(a=>a.StartsWith("--skip-update="));if(skip is not null)store.Set("updateskip",skip["--skip-update=".Length..]);
        updateClient=new();updateTimer.Tick+=async(_,_)=>
        {
            updateTimer.Interval=TimeSpan.FromSeconds(15);
            if(updateInstalling||updateDownloading||updateChecking||updateApplying)return;
            if(preparedInstaller is not null&&updateInstallRequested){await TryInstallUpdate();return;}
            if(DateTime.UtcNow>=nextUpdateCheck&&store.Get("updatecheck","1")=="1")await CheckUpdates(false);
            if(availableUpdate is not null&&CanAutoInstall()&&DateTime.UtcNow>=nextUpdateAttempt)await DownloadUpdate(true);
        };updateTimer.Start();
        var install=Environment.GetCommandLineArgs().FirstOrDefault(a=>a.StartsWith("--install-result="));
        if(install is not null)
        {
            var noticeTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(700)};
            noticeTimer.Tick+=(_,_)=>{noticeTimer.Stop();if(exiting)return;installationNotice?.Close();installationNotice=new(T(install.EndsWith("=updated")?"ClipShot обновлён":"ClipShot установлен"),T("Работает в трее · Alt+F1 — история"),MicrophoneState.Enabled,appNotice:true);};noticeTimer.Start();
        }
        if(Environment.GetCommandLineArgs().Contains("--update-failed"))Status.Text=T("Обновление не установлено. Предыдущая версия продолжает работать.");
    }
    private async Task CheckUpdates(bool manual)
    {
        if(isolated||exiting||updateChecking||updateDownloading||updateInstalling||updateApplying||preparedInstaller is not null||updateClient is null)return;
        updateChecking=true;nextUpdateCheck=DateTime.UtcNow.AddHours(6);
        if(manual)Status.Text=T("Проверяем обновления…");
        try
        {
            availableUpdate=await updateClient.CheckAsync(typeof(App).Assembly.GetName().Version!,updateStop.Token);
            if(exiting)return;
            if(availableUpdate is {IsRollback:false}&&availableUpdate.Version.ToString()==store.Get("updateskip",""))availableUpdate=null;
            if(manual)updateDismissedUntil=DateTime.MinValue;
            ShowUpdateOffer();if(manual)Status.Text=T(availableUpdate is null?"Установлена актуальная версия.":AutomaticUpdates?"Обновление установится автоматически во время простоя.":"Есть новая версия. Нажмите «Обновить сейчас».");
        }
        catch(OperationCanceledException){if(!exiting&&manual)Status.Text=T("Не удалось проверить обновления. Повторите позже.");}
        catch{nextUpdateCheck=DateTime.UtcNow.AddMinutes(30);if(!exiting&&manual)Status.Text=T("Не удалось проверить обновления. Повторите позже.");}
        finally{updateChecking=false;}
    }
    private void ShowUpdateOffer()
    {
        UpdateNotice.Visibility=!AutomaticUpdates&&availableUpdate is not null&&DateTime.UtcNow>=updateDismissedUntil?Visibility.Visible:Visibility.Collapsed;
        if(availableUpdate is not null)
        {
            UpdateText.Text=$"{T(availableUpdate.IsRollback?"Восстановление версии":"Новая версия")} {availableUpdate.Version}";
            ToolTipService.SetToolTip(UpdateText,availableUpdate.Reason);
            UpdateInstall.Content=T(availableUpdate.IsRollback?"Восстановить":"Обновить сейчас");
        }
        UpdateFooterVisibility();
    }
    private void UpdateFooterVisibility()=>FooterSurface.Visibility=Status.Visibility==Visibility.Visible||UpdateNotice.Visibility==Visibility.Visible?Visibility.Visible:Visibility.Collapsed;
    private void DismissUpdate(object sender,RoutedEventArgs e){updateDismissedUntil=DateTime.UtcNow.AddHours(2);ShowUpdateOffer();}
    private async void InstallUpdateClick(object sender,RoutedEventArgs e)=>await DownloadUpdate(false);
    private async Task DownloadUpdate(bool automatic)
    {
        if(isolated||exiting||updateDownloading||updateInstalling||updateApplying||availableUpdate is null||updateClient is null)return;
        if(automatic&&!CanAutoInstall())return;
        if(preparedInstaller is not null){requestedAutomatically=automatic;await TryInstallUpdate();return;}
        // Файл и метаданные всегда относятся к одному снимку выпуска, даже после await.
        var candidate=availableUpdate;
        updateDownloading=true;UpdateInstall.IsEnabled=false;updateInstallRequested=true;requestedAutomatically=automatic;
        try
        {
            string folder=Path.Combine(dataFolder,"Updates",candidate.Version+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            string path=Path.Combine(folder,$"ClipShot-Setup-{candidate.Version}.exe");
            await updateClient.DownloadAsync(candidate,path,new Progress<int>(percent=>{if(!exiting&&!automatic)UpdateText.Text=$"{T("Загрузка обновления")} · {percent}%";}),updateStop.Token);
            if(exiting)return;preparedInstaller=path;preparedUpdate=candidate;await TryInstallUpdate();
        }
        catch{nextUpdateAttempt=DateTime.UtcNow.AddMinutes(30);updateInstallRequested=false;if(!exiting){if(!automatic)UpdateText.Text=T("Не удалось скачать обновление. Повторить?");UpdateInstall.IsEnabled=true;}}
        finally{updateDownloading=false;if(!exiting&&!updateInstalling)UpdateInstall.IsEnabled=true;}
    }
    private async Task TryInstallUpdate()
    {
        if(isolated||exiting||updateInstalling||updateApplying||preparedInstaller is null||preparedUpdate is null||updateClient is null||DateTime.UtcNow<nextUpdateAttempt)return;
        if(dialogOpen||takingScreenshot||editor is not null||capturing||captureAgain)return;
        if(requestedAutomatically&&!CanAutoInstall())return;
        updateApplying=true;
        try
        {
            // Пока пользователь отсутствовал, выпуск могли отозвать или заменить аварийным откатом.
            var current=await updateClient.CheckAsync(typeof(App).Assembly.GetName().Version!,updateStop.Token);
            if(exiting)return;
            if(current!=preparedUpdate){availableUpdate=current;preparedInstaller=null;preparedUpdate=null;updateInstallRequested=false;UpdateInstall.IsEnabled=true;ShowUpdateOffer();return;}
            if(dialogOpen||takingScreenshot||editor is not null||capturing||captureAgain||(requestedAutomatically&&!CanAutoInstall()))return;
            if(!UpdateProtocol.Verify(preparedInstaller,preparedUpdate))throw new InvalidDataException();
            string runnerFolder=Path.Combine(dataFolder,"Updates","runner-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(runnerFolder);
            string runner=Path.Combine(runnerFolder,"ClipShot.Updater.exe");File.Copy(Path.Combine(AppContext.BaseDirectory,"Updater","ClipShot.Updater.exe"),runner);
            if(requestedAutomatically&&!CanAutoInstall())return;
            var start=new ProcessStartInfo(runner){UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add(preparedInstaller);start.ArgumentList.Add(preparedUpdate.Sha256);start.ArgumentList.Add(Environment.ProcessPath!);if(requestedAutomatically)start.ArgumentList.Add("--automatic");
            updateInstalling=true;
            var process=Process.Start(start)??throw new IOException();_=ObserveUpdater(process);if(!requestedAutomatically)UpdateText.Text=T("Устанавливаем обновление…");
        }
        catch{updateInstalling=false;nextUpdateAttempt=DateTime.UtcNow.AddMinutes(30);if(!requestedAutomatically)UpdateText.Text=T("Не удалось установить обновление. Повторите позже.");UpdateInstall.IsEnabled=true;}
        finally{updateApplying=false;}
    }
    private async Task ObserveUpdater(Process process)
    {
        using(process)try
        {
            await process.WaitForExitAsync(updateStop.Token);
            if(!exiting)
            {
                updateInstalling=false;UpdateInstall.IsEnabled=true;
                if(process.ExitCode==4)nextUpdateAttempt=DateTime.UtcNow.AddMinutes(1);
                else{preparedInstaller=null;preparedUpdate=null;updateInstallRequested=false;nextUpdateAttempt=DateTime.UtcNow.AddMinutes(30);if(!requestedAutomatically&&process.ExitCode!=0)UpdateText.Text=T("Не удалось установить обновление. Повторите позже.");}
            }
        }catch(OperationCanceledException){}
    }
    private void StopUpdates(){installationNotice?.Close();updateTimer.Stop();updateStop.Cancel();updateClient?.Dispose();}
}
