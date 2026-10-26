using System.ComponentModel;
using System.Text;
using System.Text.Json;

// Только технические коды этапов: исключения могут содержать личные пути,
// поэтому Message, StackTrace и аргументы запуска сюда никогда не попадают.
internal static class UpdaterTrace
{
    private static string? file;
    private static string mode="normal",stage="arguments";
    internal static int? InstallerExit {get;set;}
    internal static void Initialize(string directory,string operation)
    {
        file=Path.Combine(directory,"updater-log.jsonl");mode=operation;Write("begin");
    }
    internal static void Write(string next,int? result=null,Exception? error=null,string? expectedVersion=null,string? actualVersion=null)
    {
        string failedStage=stage;stage=next;
        if(file is null)return;
        int? native=error is Win32Exception win32?win32.NativeErrorCode:
            error is not null&&((uint)error.HResult&0xffff0000)==0x80070000?error.HResult&0xffff:null;
        try
        {
            string json=JsonSerializer.Serialize(new{schema=1,utc=DateTime.UtcNow.ToString("O"),processId=Environment.ProcessId,mode,stage=next,
                failedStage=error is null?null:failedStage,result,errorType=error?.GetType().FullName,hresult=error?.HResult,nativeCode=native,installerExit=InstallerExit,expectedVersion,actualVersion});
            File.AppendAllText(file,json+Environment.NewLine,new UTF8Encoding(false));
        }
        catch { /* Отказ журнала не должен менять транзакцию или её откат. */ }
    }
    internal static int Complete(int result){Write("complete",result);return result;}
}
