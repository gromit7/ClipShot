using Microsoft.UI.Xaml;
using System.Diagnostics;
namespace ClipShot;
public sealed partial class MainWindow
{
    private readonly DispatcherTimer updateTimer=new(){Interval=TimeSpan.FromSeconds(2)};
    private readonly DispatcherTimer updateFeedbackTimer=new(){Interval=TimeSpan.FromSeconds(3)};
    private readonly CancellationTokenSource updateStop=new();
    private MicrophoneNotice? installationNotice;
    private GitHubUpdates? updateClient;
    private AppUpdate? availableUpdate;
    private bool updateChecking,updateDownloading,updateInstallRequested,updateInstalling,requestedAutomatically,updateApplying,updateMetadataValid;
    private AppUpdate? preparedUpdate;
    private DateTime nextUpdateAttempt;
    private DateTime nextUpdateCheckAttempt;
    private string? preparedInstaller;
    private bool startupUpdatePending,updateLaunchRequested,manualUpdateRequested,checkAfterLaunch=true;
    private readonly DateTime startupInstallUntil=DateTime.UtcNow.AddMinutes(2);
    private bool CanStartupInstall=>!isolated&&!exiting&&!AppWindow.IsVisible&&!UpdateOperationBusy&&DateTime.UtcNow<=startupInstallUntil;
    private string updateDownloadText="";
    private string PendingUpdateFile=>Path.Combine(dataFolder,"Updates","pending.json");
    private bool AutomaticUpdates=>true;
    private string NoUpdatesVersionDetail=>$"{T("Текущая версия")}: {typeof(App).Assembly.GetName().Version!.ToString(3)}";
    private bool UpdateOperationBusy=>clipdiaryImportRunning||dialogOpen||takingScreenshot||editor is not null||capturing||captureAgain;
    private bool CanAutoDownload()=>!isolated&&!exiting&&AutomaticUpdates&&!UpdateOperationBusy;
    // Фоновая установка использует guarded UpdateQuit и ждёт простоя.
    // Явная ручная команда разрешает перезапуск, но также защищает открытый редактор.
    private bool CanAutoInstall()=>!exiting&&UpdateIdlePolicy.CanInstall(isolated,
        AutomaticUpdates||(updateInstallRequested&&!requestedAutomatically),
        AppWindow.IsVisible,UpdateOperationBusy,UserInactivity.Read());
    private void StartUpdates()
    {
        if(isolated)return;
        store.SetMany(new Dictionary<string,string>{{"updatecheck","1"},{"updateinstall","auto"}});
        var skip=Environment.GetCommandLineArgs().FirstOrDefault(a=>a.StartsWith("--skip-update="));
        if(skip is not null)store.Set("updateskip",skip["--skip-update=".Length..]);
        ResumePendingUpdate();
        updateFeedbackTimer.Tick+=(_,_)=>{updateFeedbackTimer.Stop();installationNotice?.Close();installationNotice=null;};
        updateClient=new();updateTimer.Tick+=async(_,_) =>
        {
            updateTimer.Interval=TimeSpan.FromSeconds(15);
            if(exiting||updateInstalling||updateDownloading||updateChecking||updateApplying)return;
            var slot=!UpdateOperationBusy&&DateTime.UtcNow>=nextUpdateCheckAttempt
                ?UpdateSchedule.GetDueSlot(DateTime.Now,store.Get("updatecheckslot",""),UserInactivity.Read()):null;
            if(checkAfterLaunch||slot is not null)
            {
                // Успех сохраняется для всех входов в CheckUpdates. После сетевой
                // ошибки повтор в простое отложен, чтобы не создавать петлю GET.
                bool resuming=startupUpdatePending;checkAfterLaunch=false;await CheckUpdates(false);
                startupUpdatePending=false;
                if(resuming&&(!updateMetadataValid||preparedInstaller is null)&&!Environment.GetCommandLineArgs().Contains("--background"))ShowNearCursor();
            }
            if(exiting)return;
            if(preparedInstaller is not null&&updateInstallRequested){TryInstallUpdate(startup:CanStartupInstall);return;}
            // Пакет готовим заранее; пяти минут простоя ждёт только установка.
            if(updateMetadataValid&&availableUpdate is not null&&CanAutoDownload()&&DateTime.UtcNow>=nextUpdateAttempt)await DownloadUpdate(true);
        };updateTimer.Start();
        var install=Environment.GetCommandLineArgs().FirstOrDefault(a=>a.StartsWith("--install-result="));
        if(install is not null)
        {
            var noticeTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(700)};
            noticeTimer.Tick+=(_,_) =>
            {
                noticeTimer.Stop();if(exiting)return;
                ShowInstalledHistory(install.EndsWith("=installed",StringComparison.Ordinal));
            };noticeTimer.Start();
        }
    }
    // Общий вход ручной проверки из трея и настроек. Он не открывает главное окно.
    private async Task CheckUpdatesFromTray()
    {
        activeDialog?.Hide();
        for(int attempt=0;attempt<40&&dialogOpen&&!exiting;attempt++)await Task.Delay(50);
        if(!dialogOpen&&!exiting)
        {
            manualUpdateRequested=true;
            if(updateDownloading){ShowNearCursor();return;}
            if(updateChecking)return; // Присоединяемся к текущей проверке, не создавая второй GET.
            if(preparedInstaller is not null&&updateMetadataValid){manualUpdateRequested=false;await InstallPreparedNow();return;}
            await CheckUpdates(true);
        }
    }
    private Task OfferImmediateUpdate()
    {
        if(!exiting&&preparedInstaller is not null){ShowNearCursor();ShowUpdateOffer();}
        return Task.CompletedTask;
    }
    private readonly Microsoft.UI.Xaml.Media.Animation.Storyboard updatePulse=new();
    private bool updatePulseActive;
    private void ShowUpdateOffer()
    {
        if(ManualUpdateLink is null)return;
        bool ready=preparedInstaller is not null&&updateMetadataValid&&!updateInstalling&&!updateLaunchRequested;
        ManualUpdateLink.IsEnabled=!updateChecking&&!updateDownloading&&!updateInstalling&&!updateLaunchRequested;
        ManualUpdateLink.Content=updateLaunchRequested||updateInstalling?T("Устанавливаем обновление…"):updateChecking?T("Проверяем обновления…"):updateDownloading?updateDownloadText:T(ready?"Обновить сейчас":"Проверить обновление");
        if(ready&&preparedUpdate is not null)ManualUpdateLink.Content=$"{T("Обновить сейчас")} · {preparedUpdate.Version}";
        if(ready==updatePulseActive)return;
        updatePulseActive=ready;updatePulse.Stop();updatePulse.Children.Clear();
        if(ready)
        {
            var fade=new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation{From=.5,To=1,AutoReverse=true,RepeatBehavior=Microsoft.UI.Xaml.Media.Animation.RepeatBehavior.Forever,Duration=new Duration(TimeSpan.FromMilliseconds(1200))};
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fade,ManualUpdateLink);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fade,"Opacity");updatePulse.Children.Add(fade);updatePulse.Begin();
        }
        else ManualUpdateLink.Opacity=.6;
    }
    private async void ManualUpdateClick(object sender,RoutedEventArgs e)
    {
        if(updateLaunchRequested||updateInstalling||updateDownloading||exiting)return;
        if(preparedInstaller is null||!updateMetadataValid){await CheckUpdatesFromTray();return;}
        await InstallPreparedNow();
    }
    private async Task InstallPreparedNow()
    {
        if(updateLaunchRequested||updateInstalling||exiting||preparedInstaller is null)return;
        requestedAutomatically=false;updateLaunchRequested=true;ShowUpdateOffer();
        try
        {
            activeDialog?.Hide();
            for(int attempt=0;attempt<40&&dialogOpen&&!exiting;attempt++)await Task.Delay(50);
            if(exiting)return;
            if(UpdateOperationBusy){ShowUpdateMessage("Закройте редактор перед обновлением");return;}
            await Task.Delay(180);
            if(exiting)return;
            Hide();nextUpdateAttempt=DateTime.MinValue;TryInstallUpdate(immediate:true);
            if(updateInstalling)ShowUpdateMessage("Устанавливаем обновление…","Программа перезапустится сама.");
        }
        finally{updateLaunchRequested=false;if(!exiting)ShowUpdateOffer();}
    }
    // Неудачный пакет не запускаем по кругу в фоне. Явная команда пользователя
    // разрешает новую попытку; иначе она ошибочно получала «Обновлений нет».
    private void ApplyCheckedUpdate(AppUpdate? candidate,bool explicitRequest)
    {
        availableUpdate=candidate;
        if(!explicitRequest&&availableUpdate is {IsRollback:false}&&availableUpdate.Version.ToString()==store.Get("updateskip",""))availableUpdate=null;
    }
    private async Task CheckUpdates(bool manual)
    {
        if(isolated||exiting||updateChecking||updateDownloading||updateInstalling||updateApplying||updateClient is null)return;
        updateChecking=true;updateMetadataValid=false;bool download=false;ShowUpdateOffer();
        try
        {
            var checkedUpdate=await updateClient.CheckAsync(typeof(App).Assembly.GetName().Version!,updateStop.Token);
            if(exiting)return;ApplyCheckedUpdate(checkedUpdate,manual||manualUpdateRequested);updateMetadataValid=true;nextUpdateCheckAttempt=DateTime.MinValue;
            store.Set("updatecheckslot",UpdateSchedule.DayKey(DateTime.Now));
            if(preparedUpdate is not null&&preparedUpdate!=availableUpdate)
            {
                preparedUpdate=null;preparedInstaller=null;updateInstallRequested=false;ClearPendingUpdate();
            }
            if(manual||manualUpdateRequested)
            {
                if(availableUpdate is null)ShowUpdateMessage("Обновлений нет",NoUpdatesVersionDetail);
                else
                {
                    ShowUpdateMessage(availableUpdate.IsRollback?"Восстановление версии":"Новая версия",availableUpdate.Version.ToString());
                    download=true;
                }
            }
        }
        catch(OperationCanceledException){nextUpdateCheckAttempt=DateTime.UtcNow.AddMinutes(30);if(!exiting&&(manual||manualUpdateRequested))ShowUpdateMessage("Не удалось проверить обновления","Повторите позже.");}
        catch{nextUpdateCheckAttempt=DateTime.UtcNow.AddMinutes(30);if(!exiting&&(manual||manualUpdateRequested))ShowUpdateMessage("Не удалось проверить обновления","Повторите позже.");}
        finally{updateChecking=false;ShowUpdateOffer();}
        if(download&&updateMetadataValid&&!exiting)await DownloadUpdate(false);
        else if(!updateDownloading)manualUpdateRequested=false;
    }
    // Совместимость с вызовами из других partial-файлов. В основном окне обновления не показываем.

    private void UpdateFooterVisibility()=>FooterSurface.Visibility=Status.Visibility==Visibility.Visible?Visibility.Visible:Visibility.Collapsed;
    private void DismissUpdate(object sender,RoutedEventArgs e){ }
    private async void InstallUpdateClick(object sender,RoutedEventArgs e)=>await CheckUpdatesFromTray();
    private void ShowUpdateMessage(string title,string detail="",bool persistent=false)
    {
        if(isolated||exiting)return;
        updateFeedbackTimer.Stop();installationNotice?.Close();
        installationNotice=new(T(title),T(detail),MicrophoneState.Enabled,appNotice:true);
        if(!persistent)updateFeedbackTimer.Start();
    }
    private async Task DownloadUpdate(bool automatic)
    {
        if(isolated||exiting||!updateMetadataValid||updateDownloading||updateInstalling||updateApplying||availableUpdate is null||updateClient is null)return;
        if(automatic&&!CanAutoDownload())return;
        if(!automatic)nextUpdateAttempt=DateTime.MinValue;
        if(preparedInstaller is not null)
        {
            requestedAutomatically=automatic;updateInstallRequested=true;
            if(manualUpdateRequested){manualUpdateRequested=false;await InstallPreparedNow();return;}
            TryInstallUpdate();
            if(!automatic&&!updateInstalling)await OfferImmediateUpdate();
            return;
        }
        // Захватываем метаданные выпуска до await: файл и хеш относятся к одному кандидату.
        var candidate=availableUpdate;
        updateDownloading=true;updateInstallRequested=true;requestedAutomatically=automatic;updateDownloadText=T("Загрузка обновления");ShowUpdateOffer();
        try
        {
            string folder=Path.Combine(dataFolder,"Updates",candidate.Version+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            string path=Path.Combine(folder,UpdatePackages.FileName(candidate));
            await updateClient.DownloadAsync(candidate,path,new Progress<int>(percent=>{if(exiting)return;updateDownloadText=$"{T("Загрузка обновления")} · {percent}%";ShowUpdateOffer();}),updateStop.Token,attempt=>{updateDownloadText=T("Связь прервана · повторяем…");ShowUpdateOffer();});
            if(exiting)return;preparedInstaller=path;preparedUpdate=candidate;SavePendingUpdate();TryInstallUpdate(startup:automatic&&CanStartupInstall);
            if(!automatic&&!updateInstalling)await OfferImmediateUpdate();
        }
        catch
        {
            nextUpdateAttempt=DateTime.UtcNow.AddMinutes(30);updateInstallRequested=false;
            if(!exiting&&(!automatic||manualUpdateRequested))ShowUpdateMessage("Не удалось скачать обновление","Повторите позже.");
        }
        finally
        {
            updateDownloading=false;ShowUpdateOffer();
            // Ручная проверка — одно действие: настройки уже закрыты, проверенный пакет
            // устанавливаем сразу. Открытый редактор остаётся защищён InstallPreparedNow.
            bool install=manualUpdateRequested&&preparedInstaller is not null&&updateMetadataValid;
            manualUpdateRequested=false;
            if(install&&!exiting)await InstallPreparedNow();
        }
    }
    private void TryInstallUpdate(bool immediate=false,bool startup=false)
    {
        if(isolated||exiting||!updateMetadataValid||updateInstalling||updateApplying||preparedInstaller is null||preparedUpdate is null||updateClient is null||DateTime.UtcNow<nextUpdateAttempt)return;
        if(startup?!CanStartupInstall:immediate?UpdateOperationBusy:!CanAutoInstall())return;
        updateApplying=true;
        try
        {
            // Повторная сверка с уже полученной лентой без лишнего HTTP.
            // Сам пакет перед запуском повторно проверяем по SHA.
            if(availableUpdate!=preparedUpdate)
            {
                preparedInstaller=null;preparedUpdate=null;updateInstallRequested=false;return;
            }
            if(!UpdateProtocol.Verify(preparedInstaller,preparedUpdate))throw new InvalidDataException();
            string runnerFolder=Path.Combine(dataFolder,"Updates","runner-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(runnerFolder);
            string runner=Path.Combine(runnerFolder,"ClipShot.Updater.exe");File.Copy(Path.Combine(AppContext.BaseDirectory,"Updater","ClipShot.Updater.exe"),runner);
            if(startup?!CanStartupInstall:immediate?UpdateOperationBusy:!CanAutoInstall())return;
            var start=new ProcessStartInfo(runner){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=runnerFolder};
            start.ArgumentList.Add(preparedInstaller);start.ArgumentList.Add(preparedUpdate.Sha256);start.ArgumentList.Add(Environment.ProcessPath!);
            // Явное «Обновить сейчас» разрешает немедленную установку; фон ждёт guarded UpdateQuit.
            if(startup)start.ArgumentList.Add("--startup");else if(!immediate)start.ArgumentList.Add("--automatic");updateInstalling=true;
            if(File.Exists(PendingUpdateFile))File.Delete(PendingUpdateFile);
            var process=Process.Start(start)??throw new IOException();_=ObserveUpdater(process);
        }
        catch
        {
            updateInstalling=false;nextUpdateAttempt=DateTime.UtcNow.AddMinutes(30);
            if(!requestedAutomatically)ShowUpdateMessage("Не удалось установить обновление","Повторите позже.");
        }
        finally{updateApplying=false;ShowUpdateOffer();}
    }
    private async Task ObserveUpdater(Process process)
    {
        using(process)try
        {
            await process.WaitForExitAsync(updateStop.Token);
            if(!exiting)
            {
                updateInstalling=false;
                if(process.ExitCode==4){nextUpdateAttempt=DateTime.UtcNow.AddMinutes(1);SavePendingUpdate();}
                else
                {
                    preparedInstaller=null;preparedUpdate=null;updateInstallRequested=false;nextUpdateAttempt=DateTime.UtcNow.AddMinutes(30);
                    if(!requestedAutomatically&&process.ExitCode!=0)ShowUpdateMessage("Не удалось установить обновление","Повторите позже.");
                }
            }
        }catch(OperationCanceledException){}
    }
    private void SavePendingUpdate()
    {
        // Только уже скачанный и проверенный пакет переживает завершение приложения.
        if(preparedInstaller is null||preparedUpdate is null)return;
        var json=System.Text.Json.JsonSerializer.Serialize(new PendingPackage(preparedInstaller,preparedUpdate));
        File.WriteAllText(PendingUpdateFile+".tmp",json);File.Move(PendingUpdateFile+".tmp",PendingUpdateFile,true);
    }
    private void ClearPendingUpdate(){try{File.Delete(PendingUpdateFile);}catch{}}
    private void ResumePendingUpdate()
    {
        try
        {
            if(!File.Exists(PendingUpdateFile))return;
            if(new FileInfo(PendingUpdateFile).Length>16384)throw new InvalidDataException();
            var pending=PendingUpdate.Read(File.ReadAllText(PendingUpdateFile),Path.Combine(dataFolder,"Updates"),typeof(App).Assembly.GetName().Version!);
            if(pending is null||pending.Update.IsRollback||pending.Update.Version.ToString()==store.Get("updateskip","")){ClearPendingUpdate();return;}
            string path=pending.FilePath;
            preparedInstaller=path;preparedUpdate=availableUpdate=pending.Update;updateMetadataValid=false;updateInstallRequested=true;requestedAutomatically=false;startupUpdatePending=true;

        }
        catch{startupUpdatePending=false;try{File.Delete(PendingUpdateFile);}catch{}}
    }
    private void StopUpdates(){updatePulse.Stop();installationNotice?.Close();updateFeedbackTimer.Stop();updateTimer.Stop();updateStop.Cancel();updateClient?.Dispose();}
}
