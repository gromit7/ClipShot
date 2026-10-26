using Microsoft.Data.Sqlite;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Media;

namespace ClipShot;
public sealed partial class MainWindow
{
    private async Task RunClipdiaryChecks(List<string> results)
    {
        void Check(bool ok,string name)=>results.Add($"{(ok?"PASS":"FAIL")} {name}");
        string folder=Path.Combine(dataFolder,"SyntheticClipdiary");Directory.CreateDirectory(folder);
        string path=Path.Combine(folder,"snippets.sdb"),offer=store.Get("clipdiaryoffer","1"),days=store.Get("days","90");
        var ids=store.All().Select(c=>c.Id).ToHashSet();
        Task? task=null;
        try
        {
            using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false}.ToString()))
            {
                db.Open();using var sql=db.CreateCommand();
                sql.CommandText="CREATE TABLE common(id INTEGER,type INTEGER,name TEXT,data TEXT); INSERT INTO common VALUES(1,2,'Тест импорта','Искусственный шаблон Clipdiary UI');";sql.ExecuteNonQuery();
            }
            using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.Combine(folder,"diary.db"),Pooling=false}.ToString()))
            {
                db.Open();using var raw=new MemoryStream();
                using(var writer=new BinaryWriter(raw,System.Text.Encoding.UTF8,true))
                {
                    byte[] text=System.Text.Encoding.Unicode.GetBytes("Давний искусственный клип UI\0");
                    writer.Write((ushort)1);writer.Write((ushort)1);writer.Write(13u);writer.Write((uint)text.Length);writer.Write(0u);writer.Write(text);
                }
                using var packed=new MemoryStream();
                using(var zip=new System.IO.Compression.GZipStream(packed,System.IO.Compression.CompressionMode.Compress,true))zip.Write(raw.ToArray());
                using var sql=db.CreateCommand();sql.CommandText="CREATE TABLE log(id INTEGER,created TEXT,type INTEGER,data BLOB); INSERT INTO log VALUES(1,'2020-01-15 14:25:36',1,$data)";
                sql.Parameters.AddWithValue("$data",packed.ToArray());sql.ExecuteNonQuery();
            }
            var before=System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path));
            Check(!clipdiaryOfferTimer.IsEnabled,"isolated mode does not inspect installed Clipdiary or offer personal import");
            task=ShowClipdiaryImport(folder);
            for(int i=0;i<50&&activeDialog?.IsLoaded!=true;i++)await Task.Delay(40);
            Check(dialogOpen&&activeDialog?.Title?.ToString()=="Импорт из Clipdiary"&&UpdateOperationBusy,"import opens a real dialog and protects it from automatic installation");
            Button? Find(DependencyObject node)
            {
                if(node is Button b&&b.Content?.ToString()=="Импортировать")return b;
                for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)if(Find(VisualTreeHelper.GetChild(node,i)) is {} found)return found;
                return null;
            }
            var button=activeDialog is null?null:Find(activeDialog);
            var provider=button is null?null:FrameworkElementAutomationPeer.CreatePeerForElement(button)?.GetPattern(PatternInterface.Invoke) as IInvokeProvider;
            Check(provider is not null,"import primary action is available to mouse, keyboard and automation");
            provider?.Invoke();
            for(int i=0;i<250;i++)
            {
                await Task.Delay(40);
                if(!clipdiaryImportRunning&&store.Get("clipdiaryoffer","1")=="0")break;
            }
            var imported=store.All().SingleOrDefault(c=>!ids.Contains(c.Id)&&c.Template);
            Check(imported is {Template:true,Text:"Искусственный шаблон Clipdiary UI",Name:"Тест импорта"},"one click imports a complete synthetic snippet through the real dialog and worker store");
            var old=store.All().SingleOrDefault(c=>!ids.Contains(c.Id)&&!c.Template);
            Check(old?.Seen==new DateTime(2020,1,15,14,25,36,DateTimeKind.Local)&&store.Get("days","90")=="0","real import preserves the original local history date and the explicit keep-all retention choice");
            Check(before.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))),"real dialog import leaves the synthetic Clipdiary source unchanged");
            Check(!clipdiaryImportRunning&&store.Get("clipdiaryoffer","1")=="0","completed import suppresses further startup offers");
            activeDialog?.Hide();await task;
        }
        finally
        {
            if(activeDialog is not null)activeDialog.Hide();
            if(task is not null)await task;
            foreach(var clip in store.All().Where(c=>!ids.Contains(c.Id)))store.Delete(clip.Id);
            store.Set("clipdiaryoffer",offer);store.Set("days",days);Render();
        }
    }
}
