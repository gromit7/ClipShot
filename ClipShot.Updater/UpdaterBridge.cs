using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

// Старый updater удерживает App как текущий каталог. Installer возвращает ему
// штатный defer=4, а этот helper сохраняет блокировку проверенного EXE и продолжает
// ту же rename-транзакцию только после полного выхода старого updater.
internal sealed class UpdaterBridge : IDisposable
{
    private FileStream? image,lease;
    private Mutex? reservation,token;
    private EventWaitHandle? ready;
    internal string Sha256 {get;private set;}="";
    internal string AppPath {get;private set;}="";
    internal FileStream TakeImage(){var value=image??throw new InvalidOperationException();image=null;return value;}
    internal FileStream TakeLease(){var value=lease??throw new InvalidOperationException();lease=null;return value;}
    internal Mutex TakeReservation(){var value=reservation??throw new InvalidOperationException();reservation=null;return value;}

    internal static async Task<UpdaterBridge> PrepareAsync(string installer,string tokenName,string eventName,string installerPid,string updatesRoot,string? testRoot,string[] options)
    {
        var bridge=new UpdaterBridge();
        try
        {
            var tokenMatch=Regex.Match(tokenName,@"^Local\\ClipShot\.UpdateBridge\.Token\.([0-9a-fA-F]{32})$");
            if(!tokenMatch.Success||eventName!="Local\\ClipShot.UpdateBridge.Ready."+tokenMatch.Groups[1].Value||
                !int.TryParse(installerPid,out int pid)||pid<=0||pid==Environment.ProcessId)throw new InvalidDataException("Invalid bridge capability");
            // Не принимаем произвольный PID: helper должен быть непосредственным
            // дочерним процессом installer, сам installer — дочерним старого updater.
            if(ParentProcess(Environment.ProcessId)!=pid)throw new InvalidDataException("Invalid bridge parent");
            int oldPid=ParentProcess(pid);
            if(oldPid<=0||oldPid==pid||oldPid==Environment.ProcessId)throw new InvalidDataException("Invalid updater parent");
            using var setup=Process.GetProcessById(pid);
            using var oldUpdater=Process.GetProcessById(oldPid);
            // GetProcessById сам по себе не удерживает дескриптор. После ACK
            // родители быстро завершаются; ленивое открытие после их выхода
            // иногда оставляло ExitCode недоступным. Удерживаем оба HANDLE
            // до ACK и проверяем коды именно этих процессов, без повторного PID lookup.
            _=setup.SafeHandle;_=oldUpdater.SafeHandle;
            if(!oldUpdater.ProcessName.Equals("ClipShot.Updater",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Unexpected updater parent");
            bridge.token=Mutex.OpenExisting(tokenName);bridge.ready=EventWaitHandle.OpenExisting(eventName);
            bridge.image=new FileStream(installer,FileMode.Open,FileAccess.Read,FileShare.Read);
            bridge.Sha256=Convert.ToHexString(SHA256.HashData(bridge.image));bridge.image.Position=0;
            UpdaterTrace.Write("bridge.resolve-app");
            bridge.AppPath=OldUpdaterCommand.AppPath(oldUpdater,installer,bridge.Sha256,testRoot,options);
            // Сохраняем существующий объект Setup.v1 даже после выхода родителя.
            // Другие старые worker увидят created=false и не начнут новую установку.
            bridge.reservation=Mutex.OpenExisting(testRoot is not null?"Local\\ClipShot.Test.Setup.v1":"Local\\ClipShot.Setup.v1");
            UpdaterTrace.Write("bridge.ready");
            if(!bridge.ready.Set())throw new IOException("Cannot acknowledge bridge");
            UpdaterTrace.Write("bridge.wait-parent");
            using(var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(2)))
            {
                await setup.WaitForExitAsync(deadline.Token);
                // Регрессия форсирует завершение старого runner до нашего Wait.
                // Производственный запуск без проверенного testRoot это игнорирует.
                if(testRoot is not null&&Environment.GetEnvironmentVariable("CLIPSHOT_TEST_BRIDGE_WAIT_DELAY")=="1")await Task.Delay(1000,deadline.Token);
                await oldUpdater.WaitForExitAsync(deadline.Token);
            }
            if(setup.ExitCode!=4||oldUpdater.ExitCode!=4)throw new InvalidDataException("Bridge was not deferred by parent");
            // Старый Restart не ждёт OnLaunched. Не начинаем замену файлов,
            // пока его уже созданный дочерний ClipShot ещё не дошёл до mutex.
            using(var restarted=RestartedApp(oldPid))
            using(var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            {
                while(restarted is not null&&!restarted.HasExited)
                {
                    if(Mutex.TryOpenExisting(testRoot is not null?"Local\\ClipNest.Test.v1":"Local\\ClipNest.Local.v1",out var appMutex)){appMutex.Dispose();break;}
                    UpdaterTrace.Write("bridge.wait-app-mutex");await Task.Delay(100,deadline.Token);
                }
            }
            UpdaterTrace.Write("bridge.acquire-lease");
            using(var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            {
                while(bridge.lease is null)
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    try{bridge.lease=new FileStream(Path.Combine(updatesRoot,"updater.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
                    catch(IOException){await Task.Delay(100,deadline.Token);}
                }
            }
            return bridge;
        }
        catch{bridge.Dispose();throw;}
    }
    public void Dispose(){image?.Dispose();lease?.Dispose();reservation?.Dispose();ready?.Dispose();token?.Dispose();}

    private static int ParentProcess(int pid)
    {
        nint snapshot=CreateToolhelp32Snapshot(2,0);
        if(snapshot==-1)throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var entry=new ProcessEntry{Size=(uint)Marshal.SizeOf<ProcessEntry>()};
            if(!Process32First(snapshot,ref entry))throw new Win32Exception(Marshal.GetLastWin32Error());
            do{if(entry.ProcessId==(uint)pid)return checked((int)entry.ParentId);}while(Process32Next(snapshot,ref entry));
            throw new InvalidDataException("Bridge process no longer exists");
        }
        finally{CloseHandle(snapshot);}
    }
    private static Process? RestartedApp(int oldPid)
    {
        nint snapshot=CreateToolhelp32Snapshot(2,0);
        if(snapshot==-1)throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var entry=new ProcessEntry{Size=(uint)Marshal.SizeOf<ProcessEntry>()};
            if(!Process32First(snapshot,ref entry))throw new Win32Exception(Marshal.GetLastWin32Error());
            do
            {
                if(entry.ParentId!=(uint)oldPid||!string.Equals(entry.ExeFile,"ClipShot.exe",StringComparison.OrdinalIgnoreCase))continue;
                try{return Process.GetProcessById(checked((int)entry.ProcessId));}catch(ArgumentException){return null;}
            }while(Process32Next(snapshot,ref entry));
            return null;
        }
        finally{CloseHandle(snapshot);}
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct ProcessEntry
    {
        public uint Size,Usage,ProcessId;public nuint DefaultHeap;public uint ModuleId,Threads,ParentId;public int Priority;public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string? ExeFile;
    }
    [DllImport("kernel32.dll",SetLastError=true)]private static extern nint CreateToolhelp32Snapshot(uint flags,uint processId);
    [DllImport("kernel32.dll",EntryPoint="Process32FirstW",CharSet=CharSet.Unicode,SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool Process32First(nint snapshot,ref ProcessEntry entry);
    [DllImport("kernel32.dll",EntryPoint="Process32NextW",CharSet=CharSet.Unicode,SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool Process32Next(nint snapshot,ref ProcessEntry entry);
    [DllImport("kernel32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool CloseHandle(nint handle);
}
