using Microsoft.UI.Xaml;
using System.Diagnostics;
namespace ClipShot;
public sealed partial class MainWindow
{
    private readonly DispatcherTimer updateTimer=new(){Interval=TimeSpan.FromSeconds(45)};
    private readonly DispatcherTimer updateFeedbackTimer=new(){Interval=TimeSpan.FromSeconds(3)};
    private readonly CancellationTokenSource updateStop=new();
    private MicrophoneNotice? installationNotice;
    private GitHubUpdates? updateClient;
    private AppUpdate? availableUpdate;
    private bool updateChecking,updateDownloading,updateInstallRequested,updateInstalling,requestedAutomatically,updateApplying,updateMetadataValid;
    private AppUpdate? preparedUpdate;
    private DateTime nextUpdateAttempt;
    private string? preparedInstaller;
    private bool AutomaticUpdates=>store.Get("updatecheck","1")=="1"&&store.Get("updateinstall","auto")!="manual";
    private bool UpdateOperationBusy=>dialogOpen||takingScreenshot||editor is not null||capturing||captureAgain;
    private bool CanAutoDownload()=>!isolated&&!exiting&&AutomaticUpdates&&!UpdateOperationBusy;
    // Guarded UpdateQuit нужен и явному запросу из трея, когда автообновление выключено.
    // Ни один запрос не закрывает открытое окно/редактор и не прерывает работу за ПК.
    private bool CanAutoInstall()=>!exiting&&UpdateIdlePolicy.CanInstall(isolated,
        AutomaticUpdates||(updateInstallRequested&&!requestedAutomatically),
        AppWindow.IsVisible,UpdateOperationBusy,UserInactivity.Read());
    private void StartUpdates()
    {
        if(isolated)return;
        var skip=Environment.GetCommandLineArgs().FirstOrDefault(a=>a.StartsWith("--skip-update="));
        if(skip is not null)store.Set("updateskip",skip["--skip-update=".Length..]);
        updateFeedbackTimer.Tick+=(_,_)=>{updateFeedbackTimer.Stop();installationNotice?.Close();installationNotice=null;};
        updateClient=new();updateTimer.Tick+=async(_,_) =>
        {
            updateTimer.Interval=TimeSpan.FromSeconds(15);
            if(exiting||updateInstalling||updateDownloading||updateChecking||updateApplying)return;
            if(store.Get("updatecheck","1")=="1"&&UpdateSchedule.GetDueSlot(DateTime.Now,store.Get("updatecheckslot","")) is {} slot)
            {
                // Слот записываем до HTTP. Неудачная попытка и перезапуск не создают лишние GET.
                store.Set("updatecheckslot",slot);await CheckUpdates(false);
            }
            if(exiting)return;
            if(preparedInstaller is not null&&updateInstallRequested){TryInstallUpdate();return;}
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
                ShowUpdateMessage(install.EndsWith("=installed",StringComparison.Ordinal)?"Программа установлена":"Программа обновлена",
                    "Работает в трее · Alt+F1 — история",persistent:true);
            };noticeTimer.Start();
        }
    }
    // Общий вход ручной проверки из трея и настроек. Он не открывает главное окно.
    private async Task CheckUpdatesFromTray()=>await CheckUpdates(true);
    private async Task CheckUpdates(bool manual)
    {
        if(isolated||exiting||updateChecking||updateDownloading||updateInstalling||updateApplying||updateClient is null)return;
        updateChecking=true;updateMetadataValid=false;bool download=false;
        try
        {
            availableUpdate=await updateClient.CheckAsync(typeof(App).Assembly.GetName().Version!,updateStop.Token);
            if(exiting)return;updateMetadataValid=true;
            if(availableUpdate is {IsRollback:false}&&availableUpdate.Version.ToString()==store.Get("updateskip",""))availableUpdate=null;
            if(preparedUpdate is not null&&preparedUpdate!=availableUpdate)
            {
                preparedUpdate=null;preparedInstaller=null;updateInstallRequested=false;
            }
            if(manual)
            {
                if(availableUpdate is null)ShowUpdateMessage("Обновлений нет");
                else download=true;
            }
        }
        catch(OperationCanceledException){if(!exiting&&manual)ShowUpdateMessage("Не удалось проверить обновления","Повторите позже.");}
        catch{if(!exiting&&manual)ShowUpdateMessage("Не удалось проверить обновления","Повторите позже.");}
        finally{updateChecking=false;}
        if(download&&!exiting)await DownloadUpdate(false);
    }
    // Совместимость с вызовами из других partial-файлов. В основном окне обновления не показываем.
    private void ShowUpdateOffer(){ }
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
            TryInstallUpdate();
            if(!automatic&&!updateInstalling)ShowUpdateMessage("Обновление скачано","Установится во время перерыва.");
            return;
        }
        // Захватываем метаданные выпуска до await: файл и хеш относятся к одному кандидату.
        var candidate=availableUpdate;
        updateDownloading=true;updateInstallRequested=true;requestedAutomatically=automatic;
        try
        {
            string folder=Path.Combine(dataFolder,"Updates",candidate.Version+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            string path=Path.Combine(folder,$"ClipShot-Setup-{candidate.Version}.exe");
            await updateClient.DownloadAsync(candidate,path,new Progress<int>(),updateStop.Token);
            if(exiting)return;preparedInstaller=path;preparedUpdate=candidate;TryInstallUpdate();
            if(!automatic&&!updateInstalling)ShowUpdateMessage("Обновление скачано","Установится во время перерыва.");
        }
        catch
        {
            nextUpdateAttempt=DateTime.UtcNow.AddMinutes(30);updateInstallRequested=false;
            if(!exiting&&!automatic)ShowUpdateMessage("Не удалось скачать обновление","Повторите позже.");
        }
        finally{updateDownloading=false;}
    }
    private void TryInstallUpdate()
    {
        if(isolated||exiting||!updateMetadataValid||updateInstalling||updateApplying||preparedInstaller is null||preparedUpdate is null||updateClient is null||DateTime.UtcNow<nextUpdateAttempt)return;
        if(!CanAutoInstall())return;
        updateApplying=true;
        try
        {
            // Повторная сверка с последней лентой без HTTP: запросы только в двух будних
            // слотах или по явной команде. Сам пакет перед запуском повторно проверяем по SHA.
            if(availableUpdate!=preparedUpdate)
            {
                preparedInstaller=null;preparedUpdate=null;updateInstallRequested=false;return;
            }
            if(!UpdateProtocol.Verify(preparedInstaller,preparedUpdate))throw new InvalidDataException();
            string runnerFolder=Path.Combine(dataFolder,"Updates","runner-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(runnerFolder);
            string runner=Path.Combine(runnerFolder,"ClipShot.Updater.exe");File.Copy(Path.Combine(AppContext.BaseDirectory,"Updater","ClipShot.Updater.exe"),runner);
            if(!CanAutoInstall())return;
            var start=new ProcessStartInfo(runner){UseShellExecute=false,CreateNoWindow=true};
            start.ArgumentList.Add(preparedInstaller);start.ArgumentList.Add(preparedUpdate.Sha256);start.ArgumentList.Add(Environment.ProcessPath!);
            // Даже ручная проверка лишь готовит обновление; закрытие проходит через guarded UpdateQuit.
            start.ArgumentList.Add("--automatic");updateInstalling=true;
            var process=Process.Start(start)??throw new IOException();_=ObserveUpdater(process);
        }
        catch
        {
            updateInstalling=false;nextUpdateAttempt=DateTime.UtcNow.AddMinutes(30);
            if(!requestedAutomatically)ShowUpdateMessage("Не удалось установить обновление","Повторите позже.");
        }
        finally{updateApplying=false;}
    }
    private async Task ObserveUpdater(Process process)
    {
        using(process)try
        {
            await process.WaitForExitAsync(updateStop.Token);
            if(!exiting)
            {
                updateInstalling=false;
                if(process.ExitCode==4)nextUpdateAttempt=DateTime.UtcNow.AddMinutes(1);
                else
                {
                    preparedInstaller=null;preparedUpdate=null;updateInstallRequested=false;nextUpdateAttempt=DateTime.UtcNow.AddMinutes(30);
                    if(!requestedAutomatically&&process.ExitCode!=0)ShowUpdateMessage("Не удалось установить обновление","Повторите позже.");
                }
            }
        }catch(OperationCanceledException){}
    }
    private void StopUpdates(){installationNotice?.Close();updateFeedbackTimer.Stop();updateTimer.Stop();updateStop.Cancel();updateClient?.Dispose();}
}
