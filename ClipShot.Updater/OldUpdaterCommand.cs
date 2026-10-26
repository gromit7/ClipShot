using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

// Запрашиваем только уже подтверждённый родительский updater, не сканируем
// командные строки посторонних процессов и не используем реестр как fallback.
// ProcessCommandLineInformation=60 — native interface целевых Windows 10/11.
// Его недоступность или отказ доступа прекращают bridge до Ready (fail-closed).
internal static class OldUpdaterCommand
{
    internal static string AppPath(Process parent,string installer,string sha256,string? testRoot,string[] expectedOptions)
    {
        string[] args=Read(parent.SafeHandle);
        if(args.Length is <4 or >7||!Path.GetFileName(args[0]).Equals("ClipShot.Updater.exe",StringComparison.OrdinalIgnoreCase)||
            !Path.GetFullPath(args[1]).Equals(installer,StringComparison.OrdinalIgnoreCase)||
            !Regex.IsMatch(args[2],@"^[a-fA-F0-9]{64}$")||!args[2].Equals(sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Parent update does not match bridge");
        string[] options=args.Skip(4).ToArray();
        if(options.Distinct().Count()!=options.Length||options.Any(a=>a!="--automatic"&&a!="--startup"&&a!="--test-health"&&!a.StartsWith("--test-root="))||
            options.Count(a=>a.StartsWith("--test-root="))>1||options.Contains("--automatic")!=expectedOptions.Contains("--automatic")||
            options.Contains("--startup")!=expectedOptions.Contains("--startup"))throw new InvalidDataException("Parent update options do not match bridge");
        string? rootOption=options.SingleOrDefault(a=>a.StartsWith("--test-root="));
        string? oldRoot=rootOption is null?null:Path.GetFullPath(rootOption[12..]);
        if(!string.Equals(oldRoot,testRoot,StringComparison.OrdinalIgnoreCase)||options.Contains("--test-health")&&testRoot is null)throw new InvalidDataException("Parent test root does not match bridge");
        string app=Path.GetFullPath(args[3]);
        string? appRoot=testRoot is null?null:Path.GetFullPath(Path.Combine(testRoot,"App"))+Path.DirectorySeparatorChar;
        if(Path.GetFileName(app)!="ClipShot.exe"||!File.Exists(app)||appRoot is not null&&
            !app.StartsWith(appRoot,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Invalid parent app path");
        return app;
    }
    private static string[] Read(SafeProcessHandle process)
    {
        const int commandLineInformation=60;
        int status=NtQueryInformationProcess(process,commandLineInformation,0,0,out uint required);
        if(status<0&&status!=unchecked((int)0xc0000004)&&status!=unchecked((int)0xc0000023))throw NativeFailure(status);
        if(required<Marshal.SizeOf<UnicodeString>()||required>128*1024)throw new InvalidDataException("Invalid command line buffer size");
        nint buffer=Marshal.AllocHGlobal(checked((int)required));
        try
        {
            status=NtQueryInformationProcess(process,commandLineInformation,buffer,required,out uint returned);
            if(status<0)throw NativeFailure(status);
            var text=Marshal.PtrToStructure<UnicodeString>(buffer);
            long start=buffer.ToInt64(),address=text.Buffer.ToInt64();
            if(returned>required||text.Length==0||(text.Length&1)!=0||text.MaximumLength<text.Length||
                address<start+Marshal.SizeOf<UnicodeString>()||address>start+required-text.Length)throw new InvalidDataException("Invalid command line response");
            string command=Marshal.PtrToStringUni(text.Buffer,text.Length/2)??throw new InvalidDataException();
            if(command.Contains('\0'))throw new InvalidDataException("Invalid command line terminator");
            nint vector=CommandLineToArgv(command,out int count);
            if(vector==0)throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                if(count is <4 or >7)throw new InvalidDataException("Invalid parent argument count");
                var args=new string[count];
                for(int i=0;i<count;i++)args[i]=Marshal.PtrToStringUni(Marshal.ReadIntPtr(vector,i*IntPtr.Size))??throw new InvalidDataException();
                return args;
            }
            finally{LocalFree(vector);}
        }
        finally{Marshal.FreeHGlobal(buffer);}
    }
    private static Win32Exception NativeFailure(int status)=>new(checked((int)RtlNtStatusToDosError(status)));
    [StructLayout(LayoutKind.Sequential)]private struct UnicodeString{public ushort Length,MaximumLength;public nint Buffer;}
    [DllImport("ntdll.dll")]private static extern int NtQueryInformationProcess(SafeProcessHandle process,int informationClass,nint buffer,uint length,out uint returned);
    [DllImport("ntdll.dll")]private static extern uint RtlNtStatusToDosError(int status);
    [DllImport("shell32.dll",EntryPoint="CommandLineToArgvW",CharSet=CharSet.Unicode,SetLastError=true)]private static extern nint CommandLineToArgv(string command,out int count);
    [DllImport("kernel32.dll")]private static extern nint LocalFree(nint memory);
}
