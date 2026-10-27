using System.Text.Json;
namespace ClipShot;

// Только технические события. Не записываем аргументы, тексты исключений,
// содержимое буфера, имена документов или пользовательские настройки.
internal static class LifetimeTrace
{
    private static readonly object Gate=new();
    private static string? file;
    internal static void Initialize(string directory)
    {
        file=Path.Combine(directory,"lifetime-log.jsonl");
        Write("launch");
        AppDomain.CurrentDomain.ProcessExit+=(_,_)=>Write("process.exit",code:Environment.ExitCode);
        AppDomain.CurrentDomain.UnhandledException+=(_,e)=>Write("runtime.unhandled",error:e.ExceptionObject as Exception);
    }
    internal static void Write(string stage,Exception? error=null,int? code=null)
    {
        lock(Gate)try
        {
            if(file is null)return;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            // Ограничиваем объём двумя файлами, не затрагивая другие данные.
            if(File.Exists(file)&&new FileInfo(file).Length>512*1024)File.Move(file,file+".previous",true);
            File.AppendAllText(file,JsonSerializer.Serialize(new{schema=1,utc=DateTime.UtcNow.ToString("O"),processId=Environment.ProcessId,
                version=typeof(LifetimeTrace).Assembly.GetName().Version?.ToString(3),stage,code,errorType=error?.GetType().FullName,hresult=error?.HResult})+Environment.NewLine);
        }
        catch { /* Журнал не должен останавливать приложение или его завершение. */ }
    }
}
