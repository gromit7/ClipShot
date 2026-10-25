using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

// Отдельный процесс нужен потому, что запущенный ClipShot не может заменить собственные файлы.
// Он работает без повышения прав и никогда не читает историю буфера: данные лежат вне папки программы.
internal static class Program
{
    private static string? testRoot,app,backup,failedVersion;
    private static bool validated,installerRunning;
    private static string UpdatesRoot=>testRoot is null?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ClipNest","Updates"):Path.Combine(testRoot,"Updates");
    [STAThread]private static async Task<int> Main(string[] args)
    {
        try
        {
            if(args.Length==1&&args[0]=="--self-test")return 0;
            if(args.Length is not (3 or 4))return 2;
            if(args.Length==4)
            {
                if(!args[3].StartsWith("--test-root="))return 2;testRoot=Path.GetFullPath(args[3][12..]);
                string allowed=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"ClipShot.Tests"))+Path.DirectorySeparatorChar;
                if(!testRoot.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)||testRoot.Length<=allowed.Length)return 2;
                Environment.SetEnvironmentVariable("CLIPSHOT_TEST_ROOT",Path.Combine(testRoot,"Data"));
            }
            app=Path.GetFullPath(args[2]);if(Path.GetFileName(app)!="ClipShot.exe"||!File.Exists(app))return 2;
            if(testRoot is not null&&!Inside(app,Path.Combine(testRoot,"App")))return 2;
            if(args[0]=="--rollback")
            {
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
            validated=true;StopApp();Snapshot();
            var setup=new ProcessStartInfo(installer){UseShellExecute=false,CreateNoWindow=true};setup.ArgumentList.Add("/S");if(testRoot is not null)setup.ArgumentList.Add("/TESTROOT="+testRoot);
            using var process=Process.Start(setup)??throw new IOException("Cannot start installer");
            installerRunning=true;using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(10));await process.WaitForExitAsync(timeout.Token);installerRunning=false;
            if(process.ExitCode!=0)throw new IOException("Installer returned an error");
            string arguments="--background --install-result=updated"+(downgrade?" --skip-update="+former:"");
            if(!await Restart(arguments,true))throw new IOException("New app did not confirm startup");
            return 0;
        }
        catch
        {
            // В тестовом режиме Restart всегда использует --upgrade-self-test. Ни один fallback
            // не должен случайно запустить приложение с личной базой на рабочем ПК.
            if(validated&&!installerRunning&&app is not null)
                try{StopApp();if(backup is not null&&File.Exists(Path.Combine(backup,"FileInventory.json")))Restore();await Restart("--background --update-failed --skip-update="+failedVersion);}catch{}
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
    private static void StopApp()
    {
        uint message=RegisterWindowMessage(testRoot is null?"ClipNest.Quit.v1":"ClipNest.Test.Quit.v1");PostMessage((nint)0xffff,message,0,0);
        if(!Mutex.TryOpenExisting(testRoot is null?"Local\\ClipNest.Local.v1":"Local\\ClipNest.Test.v1",out var mutex))return;
        using(mutex){bool acquired=false;try{acquired=mutex.WaitOne(10000);}catch(AbandonedMutexException){acquired=true;}finally{if(acquired)mutex.ReleaseMutex();}if(!acquired)throw new IOException("App is still busy");}
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")]private static extern bool PostMessage(nint window,uint message,nint w,nint l);
}
