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
    private bool updateChecking,updateDownloading,updateInstallRequested;
    private DateTime nextUpdateCheck=DateTime.UtcNow;
    private DateTime updateDismissedUntil;
    private string? preparedInstaller;
    private void StartUpdates()
    {
        if(isolated)return;
        var skip=Environment.GetCommandLineArgs().FirstOrDefault(a=>a.StartsWith("--skip-update="));if(skip is not null)store.Set("updateskip",skip["--skip-update=".Length..]);
        updateClient=new();updateTimer.Tick+=async(_,_)=>
        {
            if(preparedInstaller is not null&&updateInstallRequested){TryInstallUpdate();return;}
            if(DateTime.UtcNow>=nextUpdateCheck&&store.Get("updatecheck","1")=="1")await CheckUpdates(false);
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
        if(isolated||exiting||updateChecking||updateClient is null)return;
        updateChecking=true;nextUpdateCheck=DateTime.UtcNow.AddHours(6);
        if(manual)Status.Text=T("Проверяем обновления…");
        try
        {
            availableUpdate=await updateClient.CheckAsync(typeof(App).Assembly.GetName().Version!,updateStop.Token);
            if(exiting)return;
            if(!manual&&availableUpdate is {IsRollback:false}&&availableUpdate.Version.ToString()==store.Get("updateskip",""))availableUpdate=null;
            if(manual)updateDismissedUntil=DateTime.MinValue;
            ShowUpdateOffer();if(manual)Status.Text=T(availableUpdate is null?"Установлена актуальная версия.":"Есть новая версия. Нажмите «Обновить сейчас».");
        }
        catch(OperationCanceledException){if(!exiting&&manual)Status.Text=T("Не удалось проверить обновления. Повторите позже.");}
        catch{nextUpdateCheck=DateTime.UtcNow.AddMinutes(30);if(!exiting&&manual)Status.Text=T("Не удалось проверить обновления. Повторите позже.");}
        finally{updateChecking=false;}
    }
    private void ShowUpdateOffer()
    {
        UpdateNotice.Visibility=availableUpdate is not null&&DateTime.UtcNow>=updateDismissedUntil?Visibility.Visible:Visibility.Collapsed;
        if(availableUpdate is not null)
        {
            UpdateText.Text=$"{T(availableUpdate.IsRollback?"Восстановление версии":"Новая версия")} {availableUpdate.Version}";
            ToolTipService.SetToolTip(UpdateText,availableUpdate.Reason);
            UpdateInstall.Content=T(availableUpdate.IsRollback?"Восстановить":"Обновить сейчас");
        }
    }
    private void DismissUpdate(object sender,RoutedEventArgs e){updateDismissedUntil=DateTime.UtcNow.AddHours(2);ShowUpdateOffer();}
    private async void InstallUpdateClick(object sender,RoutedEventArgs e)
    {
        if(isolated||exiting||updateDownloading||availableUpdate is null||updateClient is null)return;
        updateDownloading=true;UpdateInstall.IsEnabled=false;updateInstallRequested=true;
        try
        {
            string folder=Path.Combine(dataFolder,"Updates",availableUpdate.Version+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            string path=Path.Combine(folder,$"ClipShot-Setup-{availableUpdate.Version}.exe");
            await updateClient.DownloadAsync(availableUpdate,path,new Progress<int>(percent=>{if(!exiting)UpdateText.Text=$"{T("Загрузка обновления")} · {percent}%";}),updateStop.Token);
            if(exiting)return;preparedInstaller=path;updateTimer.Interval=TimeSpan.FromSeconds(2);TryInstallUpdate();
        }
        catch{if(!exiting){UpdateText.Text=T("Не удалось скачать обновление. Повторить?");UpdateInstall.IsEnabled=true;}}
        finally{updateDownloading=false;}
    }
    private void TryInstallUpdate()
    {
        if(isolated||exiting||preparedInstaller is null||availableUpdate is null)return;
        if(dialogOpen||takingScreenshot||editor is not null||capturing){UpdateText.Text=T("Обновление готово. Ждём завершения текущей операции.");return;}
        try
        {
            if(!UpdateProtocol.Verify(preparedInstaller,availableUpdate))throw new InvalidDataException();
            string runnerFolder=Path.Combine(dataFolder,"Updates","runner-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(runnerFolder);
            string runner=Path.Combine(runnerFolder,"ClipShot.Updater.exe");File.Copy(Path.Combine(AppContext.BaseDirectory,"Updater","ClipShot.Updater.exe"),runner);
            var start=new ProcessStartInfo(runner){UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add(preparedInstaller);start.ArgumentList.Add(availableUpdate.Sha256);start.ArgumentList.Add(Environment.ProcessPath!);
            var process=Process.Start(start)??throw new IOException();_=ObserveUpdater(process);preparedInstaller=null;updateInstallRequested=false;UpdateText.Text=T("Устанавливаем обновление…");
        }
        catch{UpdateText.Text=T("Не удалось установить обновление. Повторите позже.");UpdateInstall.IsEnabled=true;preparedInstaller=null;updateInstallRequested=false;}
    }
    private async Task ObserveUpdater(Process process)
    {
        using(process)try{await process.WaitForExitAsync(updateStop.Token);if(!exiting&&process.ExitCode!=0){UpdateText.Text=T("Не удалось установить обновление. Повторите позже.");UpdateInstall.IsEnabled=true;}}catch(OperationCanceledException){}
    }
    private void StopUpdates(){installationNotice?.Close();updateTimer.Stop();updateStop.Cancel();updateClient?.Dispose();}
}
