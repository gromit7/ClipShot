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
    private static bool validated,installerRunning,snapshotCompleted,automatic,startup;
    private static string UpdatesRoot=>testRoot is null?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ClipNest","Updates"):Path.Combine(testRoot,"Updates");
    [STAThread]private static async Task<int> Main(string[] args)
    {
        try
        {
            if(args.Length==1&&args[0]=="--self-test")return 0;
            if(args.Length==2&&args[0]=="--verify-install")return InstallationIntegrity.Verify(args[1])?0:3;
            if(args.Length is <3 or >5)return 2;
            var options=args.Skip(3).ToArray();
            if(options.Distinct().Count()!=options.Length||options.Any(a=>a!="--automatic"&&a!="--startup"&&!a.StartsWith("--test-root=")))return 2;
            automatic=options.Contains("--automatic");
            startup=options.Contains("--startup");if(automatic&&startup)return 2;
            string? rootOption=options.SingleOrDefault(a=>a.StartsWith("--test-root="));
            if(rootOption is not null)
            {
                testRoot=Path.GetFullPath(rootOption[12..]);
                string allowed=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"ClipShot.Tests"))+Path.DirectorySeparatorChar;
                if(!testRoot.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)||testRoot.Length<=allowed.Length)return 2;
                Environment.SetEnvironmentVariable("CLIPSHOT_TEST_ROOT",Path.Combine(testRoot,"Data"));
            }
            app=Path.GetFullPath(args[2]);if(Path.GetFileName(app)!="ClipShot.exe"||!File.Exists(app))return 2;
            if(testRoot is not null&&!Inside(app,Path.Combine(testRoot,"App")))return 2;
            // Один обновлятор на каталог данных; FileStream не привязан к потоку после await.
            Directory.CreateDirectory(UpdatesRoot);
            using var lease=AcquireLease();if(lease is null)return 4;
            using var installLease=AcquireInstallerLease();if(installLease is null)return 4;
            if(args[0]=="--rollback")
            {
                if(automatic||startup)return 2;
                backup=Path.GetFullPath(args[1]);if(!Inside(backup,UpdatesRoot)||!File.Exists(Path.Combine(backup,"FileInventory.json")))return 2;
                validated=true;string skipped=FileVersionInfo.GetVersionInfo(app).ProductVersion?.Split('+')[0]??"";
                StopApp();Restore();await Restart("--background --install-result=rollback --skip-update="+skipped);return 0;
            }
            string installer=Path.GetFullPath(args[0]),hash=args[1];
            if(!Inside(installer,UpdatesRoot)||!Regex.IsMatch(Path.GetFileName(installer),@"^ClipShot-Setup-\d+\.\d+\.\d+\.exe$")||!Regex.IsMatch(hash,@"^[a-fA-F0-9]{64}$"))return 2;
            // Держим файл открытым без разрешения записи/удаления между проверкой и запуском.
            using var image=new FileStream(installer,FileMode.Open,FileAccess.Read,FileShare.Read);
            if(!Convert.ToHexString(SHA256.HashData(image)).Equals(hash,StringComparison.OrdinalIgnoreCase))return 3;
            failedVersion=Path.GetFileNameWithoutExtension(installer)["ClipShot-Setup-".Length..];
            string former=FileVersionInfo.GetVersionInfo(app).ProductVersion?.Split('+')[0]??"";
            bool downgrade=Version.TryParse(former,out var oldVersion)&&Version.Parse(failedVersion)<oldVersion;
            if(automatic&&!IdleEnough())return 4;
            // Главное приложение само ещё раз проверит окно, редактор и ввод перед завершением.
            if(!StopApp(automatic||startup))return 4;
            validated=true;Snapshot();snapshotCompleted=true;
            if(automatic&&!IdleEnough()){await Restart("--background");return 4;}
            // Держим общую блокировку до завершения транзакции. Дочерний NSIS допускается
            // только по одноразовому имени объекта; ручной установщик не начнётся параллельно.
            string handoffName="Local\\ClipShot.Setup.Handoff."+Guid.NewGuid().ToString("N");
            using var handoff=new Mutex(false,handoffName);
            var setup=new ProcessStartInfo(installer){UseShellExecute=false,CreateNoWindow=true};setup.ArgumentList.Add("/S");if(testRoot is not null)setup.ArgumentList.Add("/TESTROOT="+testRoot);
            setup.ArgumentList.Add("/UPDATERTOKEN="+handoffName);if(automatic)setup.ArgumentList.Add("/AUTOMATIC");
            if(startup)setup.ArgumentList.Add("/STARTUP");
            using var process=Process.Start(setup)??throw new IOException("Cannot start installer");
            installerRunning=true;using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(10));await process.WaitForExitAsync(timeout.Token);installerRunning=false;
            if(process.ExitCode==4){await Restart("--background");return 4;}
            if(process.ExitCode!=0)throw new IOException("Installer returned an error");
            string arguments="--background --install-result=updated"+(downgrade?" --skip-update="+former:"");
            if(!await Restart(arguments,true))throw new IOException("New app did not confirm startup");
            return 0;
        }
        catch
        {
            // В тестовом режиме Restart всегда использует --upgrade-self-test. Ни один fallback
            // не должен случайно запустить приложение с личной базой на рабочем ПК.
            if(validated&&!installerRunning&&app is not null&&!InstallerBusy())
                // Проверка простоя ограничивает начало установки. Возврат целого комплекта
                // после ошибки нельзя отменять из-за движения мыши. При startup сохраняется
                // защищённая команда: вновь открытый пользователем редактор не закрываем.
                try{if(!StopApp(startup))return 1;if(snapshotCompleted)Restore();}catch{}
                finally{try{await Restart("--background --update-failed --skip-update="+failedVersion);}catch{}}
            return 1;
        }
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
        if(testRoot is not null)arguments="--upgrade-self-test --background";
        else if(checkHealth){marker=Path.Combine(UpdatesRoot,"health-"+Guid.NewGuid().ToString("N")+".txt");arguments+=" --update-health-file=\""+marker+"\"";}
        using var process=Process.Start(new ProcessStartInfo(app!){UseShellExecute=false,Arguments=arguments,WorkingDirectory=Path.GetDirectoryName(app!)!,CreateNoWindow=true});
        if(process is null)return false;
        if(testRoot is not null){await process.WaitForExitAsync();return process.ExitCode==0;}
        if(marker is null)return true;
        for(int i=0;i<150;i++){if(File.Exists(marker))return true;if(process.HasExited)return false;await Task.Delay(100);}
        return false;
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
        using(mutex){bool acquired=false;try{acquired=mutex.WaitOne(10000);}catch(AbandonedMutexException){acquired=true;}finally{if(acquired)mutex.ReleaseMutex();}if(!acquired&&!guarded)throw new IOException("App is still busy");return acquired;}
    }
    private static string TestEnvironmentName(string action)=>$"ClipNest.{(testRoot is null?"":"Test.")}{action}.v1";
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")]private static extern bool PostMessage(nint window,uint message,nint w,nint l);
}
