using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClipShot;

// Отдельный процесс нужен потому, что запущенный ClipShot не может заменить собственные файлы.
// Он работает без повышения прав и никогда не читает историю буфера: данные лежат вне папки программы.
internal static class Program
{
    private static string? testRoot,app,backup,failedVersion;
    private static bool validated,installerRunning,snapshotCompleted,automatic,startup,testHealth;
    private static string UpdatesRoot=>testRoot is null?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ClipNest","Updates"):Path.Combine(testRoot,"Updates");
    [STAThread]private static async Task<int> Main(string[] args)
    {
        UpdaterBridge? bridge=null;
        try
        {
            if(args.Length==1&&args[0]=="--self-test")return 0;
            if(args.Length==2&&args[0]=="--verify-install")return InstallationIntegrity.Verify(args[1])?0:3;
            if(args.Length>0&&args[0]=="--bridge")
            {
                if(args.Length is <6 or >9||!ConfigureOptions(args.Skip(6).ToArray()))return 2;
                // args[2] от legacy NSIS — только hint из старого InstallDir.
                // Фактический app берём из подтверждённого родительского updater.
                string bridgePackage=ValidatedInstaller(args[1]);
                PrepareWorkingDirectory("bridge");
                bridge=await UpdaterBridge.PrepareAsync(bridgePackage,args[3],args[4],args[5],UpdatesRoot,testRoot,args.Skip(6).ToArray());
                app=ValidatedApp(bridge.AppPath);
                args=new[]{bridgePackage,bridge.Sha256,app!}.Concat(args.Skip(6)).ToArray();
            }
            if(args.Length is <3 or >6)return UpdaterTrace.Complete(2);
            var options=args.Skip(3).ToArray();
            if(!ConfigureOptions(options))return UpdaterTrace.Complete(2);
            app=ValidatedApp(args[2]);
            if(bridge is null)PrepareWorkingDirectory(args[0]=="--rollback"?"rollback":"normal");
            // Один обновлятор на каталог данных; FileStream не привязан к потоку после await.
            UpdaterTrace.Write("acquire-lease");
            using var lease=bridge?.TakeLease()??AcquireLease();if(lease is null)return UpdaterTrace.Complete(4);
            using var installLease=bridge?.TakeReservation()??AcquireInstallerLease();if(installLease is null)return UpdaterTrace.Complete(4);
            if(args[0]=="--rollback")
            {
                if(automatic||startup)return UpdaterTrace.Complete(2);
                backup=Path.GetFullPath(args[1]);if(!Inside(backup,UpdatesRoot)||!File.Exists(Path.Combine(backup,"FileInventory.json")))return UpdaterTrace.Complete(2);
                validated=true;string skipped=FileVersionInfo.GetVersionInfo(app).ProductVersion?.Split('+')[0]??"";
                UpdaterTrace.Write("rollback.stop-app");StopApp();UpdaterTrace.Write("rollback.restore");Restore();await Restart("--background --install-result=rollback --skip-update="+skipped);return UpdaterTrace.Complete(0);
            }
            string installer=ValidatedInstaller(args[0]),hash=args[1];
            var package=Regex.Match(Path.GetFileName(installer),@"^ClipShot-(?:(Win10|Win11)-)?Setup-(?<version>\d+\.\d+\.\d+)\.exe$");
            if(!Regex.IsMatch(hash,@"^[a-fA-F0-9]{64}$"))return UpdaterTrace.Complete(2);
            // Держим файл открытым без разрешения записи/удаления между проверкой и запуском.
            UpdaterTrace.Write("verify-installer");using var image=bridge?.TakeImage()??new FileStream(installer,FileMode.Open,FileAccess.Read,FileShare.Read);
            image.Position=0;if(!Convert.ToHexString(SHA256.HashData(image)).Equals(hash,StringComparison.OrdinalIgnoreCase))return UpdaterTrace.Complete(3);
            failedVersion=package.Groups["version"].Value;
            string former=FileVersionInfo.GetVersionInfo(app).ProductVersion?.Split('+')[0]??"";
            bool downgrade=Version.TryParse(former,out var oldVersion)&&Version.Parse(failedVersion)<oldVersion;
            if(automatic&&!IdleEnough())return UpdaterTrace.Complete(4);
            // Главное приложение само ещё раз проверит окно, редактор и ввод перед завершением.
            UpdaterTrace.Write("stop-app");if(!StopApp(automatic||startup))return UpdaterTrace.Complete(4);
            validated=true;UpdaterTrace.Write("snapshot");Snapshot();snapshotCompleted=true;
            if(automatic&&!IdleEnough()){await Restart("--background");return UpdaterTrace.Complete(4);}
            // Держим общую блокировку до завершения транзакции. Дочерний NSIS допускается
            // только по одноразовому имени объекта; ручной установщик не начнётся параллельно.
            string handoffName="Local\\ClipShot.Setup.Handoff."+Guid.NewGuid().ToString("N");
            using var handoff=new Mutex(false,handoffName);
            var setup=new ProcessStartInfo(installer){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=UpdatesRoot};setup.ArgumentList.Add("/S");if(testRoot is not null)setup.ArgumentList.Add("/TESTROOT="+testRoot);
            setup.ArgumentList.Add("/UPDATERTOKEN="+handoffName);setup.ArgumentList.Add("/UPDATERREADY");setup.ArgumentList.Add("/APPDIR="+Path.GetDirectoryName(app));if(automatic)setup.ArgumentList.Add("/AUTOMATIC");
            if(startup)setup.ArgumentList.Add("/STARTUP");
            UpdaterTrace.Write("installer.start");
            using var process=Process.Start(setup)??throw new IOException("Cannot start installer");
            installerRunning=true;using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(10));await process.WaitForExitAsync(timeout.Token);installerRunning=false;
            UpdaterTrace.InstallerExit=process.ExitCode;UpdaterTrace.Write("installer.exited");
            if(process.ExitCode==4){await Restart("--background");return UpdaterTrace.Complete(4);}
            if(process.ExitCode!=0)throw new IOException("Installer returned an error");
            UpdaterTrace.Write("verify-installed-version");
            string installedText=FileVersionInfo.GetVersionInfo(app).ProductVersion?.Split('+')[0]??"";
            Version? installed=Version.TryParse(installedText,out var actualVersion)?new Version(actualVersion.Major,actualVersion.Minor,Math.Max(0,actualVersion.Build)):null;
            UpdaterTrace.Write("verify-installed-version",expectedVersion:failedVersion,actualVersion:installed?.ToString());
            // Успешный чужой INSTDIR или health старого EXE не подтверждают обновление.
            // Проверяем именно фактический путь из вызывающего приложения.
            if(installed!=Version.Parse(failedVersion)||!InstallationIntegrity.Verify(Path.GetDirectoryName(app)!))throw new InvalidDataException("Installed app does not match target release");
            string arguments="--background --install-result=updated"+(downgrade?" --skip-update="+former:"");
            UpdaterTrace.Write("restart-health");
            if(!await Restart(arguments,true))throw new IOException("New app did not confirm startup");
            return UpdaterTrace.Complete(0);
        }
        catch(Exception error)
        {
            UpdaterTrace.Write("error",error:error);
            // В тестовом режиме Restart всегда использует --upgrade-self-test. Ни один fallback
            // не должен случайно запустить приложение с личной базой на рабочем ПК.
            if(validated&&!installerRunning&&app is not null&&!InstallerBusy())
                // Проверка простоя ограничивает начало установки. Возврат целого комплекта
                // после ошибки нельзя отменять из-за движения мыши. При startup сохраняется
                // защищённая команда: вновь открытый пользователем редактор не закрываем.
                try{UpdaterTrace.Write("recovery.stop-app");if(!StopApp(startup))return UpdaterTrace.Complete(1);if(snapshotCompleted){UpdaterTrace.Write("recovery.restore");Restore();}}catch(Exception recovery){UpdaterTrace.Write("recovery.error",error:recovery);}
                finally{try{await Restart("--background --update-failed --skip-update="+failedVersion);}catch(Exception restart){UpdaterTrace.Write("recovery.restart-error",error:restart);}}
            return UpdaterTrace.Complete(1);
        }
        finally{bridge?.Dispose();}
    }
    private static bool ConfigureOptions(string[] options)
    {
        if(options.Distinct().Count()!=options.Length||options.Any(a=>a!="--automatic"&&a!="--startup"&&a!="--test-health"&&!a.StartsWith("--test-root="))||options.Count(a=>a.StartsWith("--test-root="))>1)return false;
        automatic=options.Contains("--automatic");startup=options.Contains("--startup");testHealth=options.Contains("--test-health");
        if(automatic&&startup)return false;
        string? rootOption=options.SingleOrDefault(a=>a.StartsWith("--test-root="));
        testRoot=null;
        if(rootOption is not null)
        {
            testRoot=Path.GetFullPath(rootOption[12..]);string allowed=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"ClipShot.Tests"))+Path.DirectorySeparatorChar;
            if(!testRoot.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)||testRoot.Length<=allowed.Length)return false;
            Environment.SetEnvironmentVariable("CLIPSHOT_TEST_ROOT",Path.Combine(testRoot,"Data"));
        }
        return !testHealth||testRoot is not null;
    }
    private static string ValidatedApp(string path)
    {
        path=Path.GetFullPath(path);
        if(Path.GetFileName(path)!="ClipShot.exe"||!File.Exists(path)||testRoot is not null&&!Inside(path,Path.Combine(testRoot,"App")))throw new InvalidDataException("Invalid app path");
        return path;
    }
    private static string ValidatedInstaller(string path)
    {
        path=Path.GetFullPath(path);var match=Regex.Match(Path.GetFileName(path),@"^ClipShot-(?:(Win10|Win11)-)?Setup-(?<version>\d+\.\d+\.\d+)\.exe$");
        if(!Inside(path,UpdatesRoot)||!match.Success||!Version.TryParse(match.Groups["version"].Value,out var version)||version.ToString()!=match.Groups["version"].Value)throw new InvalidDataException("Invalid installer path");
        int build=Environment.OSVersion.Version.Build;string flavor=match.Groups[1].Value;
        if(build<17763||flavor=="Win10"&&build>=22000||flavor=="Win11"&&build<22000)throw new InvalidDataException("Unsupported Windows build");
        return path;
    }
    private static void PrepareWorkingDirectory(string mode)
    {
        Directory.CreateDirectory(UpdatesRoot);Directory.SetCurrentDirectory(UpdatesRoot);UpdaterTrace.Initialize(UpdatesRoot,mode);
    }
    private static bool Inside(string path,string directory)=>Path.GetFullPath(path).StartsWith(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    private static string[] Inventory(string directory)
    {
        var names=JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(directory,"FileInventory.json")))??throw new InvalidDataException();
        if(names.Length==0||names.Length>5000)throw new InvalidDataException();
        foreach(string name in names)if(Path.IsPathRooted(name)||!Inside(Path.Combine(directory,name),directory))throw new InvalidDataException("Unsafe inventory path");
        return names;
    }
    private static void Snapshot()
    {
        string directory=Path.GetDirectoryName(app!)!;var files=Inventory(directory);
        backup=Path.Combine(UpdatesRoot,"backup-"+DateTime.UtcNow.ToString("yyyyMMddHHmmss")+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(backup);
        foreach(string name in files){string target=Path.Combine(backup,name);Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(Path.Combine(directory,name),target,true);}
        // Метаданные привязывают снимок к конкретной папке установки. Посторонние файлы не копируются.
        File.WriteAllText(Path.Combine(backup,"Rollback.json"),JsonSerializer.Serialize(new{App=app,Version=FileVersionInfo.GetVersionInfo(app!).ProductVersion}));
    }
    private static void Restore()
    {
        string directory=Path.GetDirectoryName(app!)!;
        using var info=JsonDocument.Parse(File.ReadAllText(Path.Combine(backup!,"Rollback.json")));
        if(!string.Equals(info.RootElement.GetProperty("App").GetString(),app,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Snapshot belongs to another installation");
        foreach(string name in Inventory(backup!)){string target=Path.Combine(directory,name);Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(Path.Combine(backup!,name),target,true);}
        if(testRoot is null){using var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ClipNest",true);key?.SetValue("DisplayVersion",FileVersionInfo.GetVersionInfo(app!).ProductVersion?.Split('+')[0]??"");}
    }
    private static async Task<bool> Restart(string arguments,bool checkHealth=false)
    {
        string? marker=null;
        if(testRoot is not null)arguments=testHealth&&checkHealth?"--upgrade-self-test --wait-for-upgrade --background --update-health-test":"--upgrade-self-test --background";
        if(checkHealth&&(testRoot is null||testHealth)){marker=Path.Combine(UpdatesRoot,"health-"+Guid.NewGuid().ToString("N")+".txt");arguments+=" --update-health-file=\""+marker+"\"";}
        UpdaterTrace.Write(marker is null?"restart":"health.start");
        using var process=Process.Start(new ProcessStartInfo(app!){UseShellExecute=false,Arguments=arguments,WorkingDirectory=UpdatesRoot,CreateNoWindow=true});
        if(process is null)return false;
        if(testRoot is not null&&marker is null){using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(60));await process.WaitForExitAsync(limit.Token);return process.ExitCode==0;}
        if(marker is null)return true;
        var clock=Stopwatch.StartNew();
        bool healthy=await UpdateStartupHealth.WaitAsync(()=>File.Exists(marker),()=>process.HasExited,()=>clock.Elapsed,()=>Task.Delay(100));
        UpdaterTrace.Write(healthy?"health.ready":process.HasExited?"health.exited":"health.timeout");
        if(process.HasExited)UpdaterTrace.Write("health.exit-code",result:process.ExitCode);
        return healthy;
    }
    private static FileStream? AcquireLease(){try{return new FileStream(Path.Combine(UpdatesRoot,"updater.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}catch(IOException){return null;}}
    private static Mutex? AcquireInstallerLease(){var mutex=new Mutex(false,testRoot is null?"Local\\ClipShot.Setup.v1":"Local\\ClipShot.Test.Setup.v1",out bool created);if(created)return mutex;mutex.Dispose();return null;}
    private static bool IdleEnough()=>UserInactivity.Read() is {} idle&&idle>=UpdateIdlePolicy.MinimumIdle;
    private static bool InstallerBusy(){if(!Mutex.TryOpenExisting(testRoot is null?"Local\\ClipShot.Setup.v1":"Local\\ClipShot.Test.Setup.v1",out var mutex))return false;mutex.Dispose();return true;}
    private static bool StopApp(bool guarded=false)
    {
        if(guarded&&automatic&&!IdleEnough())return false;
        uint message=RegisterWindowMessage(TestEnvironmentName(guarded?"UpdateQuit":"Quit"));
        nint deadline=guarded?(nint)unchecked((uint)Environment.TickCount+5000):0;
        PostMessage((nint)0xffff,message,deadline,guarded&&startup?1:0);
        if(!Mutex.TryOpenExisting(testRoot is null?"Local\\ClipNest.Local.v1":"Local\\ClipNest.Test.v1",out var mutex))return true;
        using(mutex)
        {
            bool acquired=false;
            try
            {
                // В bridge старый Restart мог только что создать процесс: первый
                // broadcast бывает раньше WindowMessage. Повторяем тот же запрос,
                // не продлевая пятисекундный guarded deadline.
                for(int i=0;i<100&&!acquired;i++)
                {
                    PostMessage((nint)0xffff,message,deadline,guarded&&startup?1:0);
                    acquired=mutex.WaitOne(100);
                }
            }
            catch(AbandonedMutexException){acquired=true;}
            finally{if(acquired)mutex.ReleaseMutex();}
            if(!acquired&&!guarded)throw new IOException("App is still busy");return acquired;
        }
    }
    private static string TestEnvironmentName(string action)=>$"ClipNest.{(testRoot is null?"":"Test.")}{action}.v1";
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")]private static extern bool PostMessage(nint window,uint message,nint w,nint l);
}
