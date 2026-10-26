using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;

namespace ClipShot;
public sealed record Clip(long Id, string Kind, string Text, string Html, string Rtf, byte[]? Image, bool Template, string Name, DateTime Seen)
{
    public string Label => Name.Length > 0 ? Name : Kind == "image" ? "Изображение" : Text.Replace('\r', ' ').Replace('\n', ' ');
}
public sealed record ImportSummary(int Imported,int Existing,int Skipped);
public sealed partial class Store : IDisposable
{
    private readonly SqliteConnection db;
    public Store(string path)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        try
        {
        db.Open();
        using var c = db.CreateCommand();
        c.CommandText = """
        PRAGMA journal_mode=WAL;
        CREATE TABLE IF NOT EXISTS clips(id INTEGER PRIMARY KEY, hash TEXT NOT NULL UNIQUE, kind TEXT NOT NULL, text TEXT NOT NULL DEFAULT '', html TEXT NOT NULL DEFAULT '', rtf TEXT NOT NULL DEFAULT '', image BLOB, template INTEGER NOT NULL DEFAULT 0, name TEXT NOT NULL DEFAULT '', seen TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY,value TEXT NOT NULL);
        """; c.ExecuteNonQuery();
        InitializeTemplateFolders();
        }
        catch { db.Dispose(); throw; }
    }
    public string Get(string key, string fallback)
    { using var c = db.CreateCommand(); c.CommandText = "SELECT value FROM settings WHERE key=$k"; c.Parameters.AddWithValue("$k",key); return c.ExecuteScalar() as string ?? fallback; }
    public void Set(string key, string value)
    { using var c = db.CreateCommand(); c.CommandText = "INSERT INTO settings VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=$v"; c.Parameters.AddWithValue("$k",key); c.Parameters.AddWithValue("$v",value); c.ExecuteNonQuery(); }
    public void SetMany(IEnumerable<KeyValuePair<string, string>> values)
    {
        using var transaction = db.BeginTransaction();
        foreach (var pair in values) Set(pair.Key, pair.Value);
        transaction.Commit();
    }
    public void Add(string kind, string text, string html = "", string rtf = "", byte[]? image = null)
    {
        // Text deduplication intentionally ignores source formatting; images are canonical PNG.
        string hash = Convert.ToHexString(SHA256.HashData(image ?? Encoding.UTF8.GetBytes(text)));
        using var c = db.CreateCommand();
        c.CommandText = """
        INSERT INTO clips(hash,kind,text,html,rtf,image,seen) VALUES($hash,$kind,$text,$html,$rtf,$image,$seen)
        ON CONFLICT(hash) DO UPDATE SET seen=$seen,html=$html,rtf=$rtf;
        """;
        c.Parameters.AddWithValue("$hash",kind + hash); c.Parameters.AddWithValue("$kind",kind);
        c.Parameters.AddWithValue("$text",text); c.Parameters.AddWithValue("$html",html); c.Parameters.AddWithValue("$rtf",rtf);
        c.Parameters.AddWithValue("$image",(object?)image ?? DBNull.Value); c.Parameters.AddWithValue("$seen",DateTime.UtcNow.ToString("O")); c.ExecuteNonQuery();
    }
    public ImportSummary Import(IEnumerable<ClipdiaryImportResult> items,bool keepAllHistory,CancellationToken token=default,IProgress<int>? progress=null)
    {
        ArgumentNullException.ThrowIfNull(items);token.ThrowIfCancellationRequested();
        string temporaryRoot=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"ClipShot.Import"));
        string folder=Path.Combine(temporaryRoot,Guid.NewGuid().ToString("N")),staging=Path.Combine(folder,"import.db");
        Directory.CreateDirectory(folder);bool attached=false;int processed=0,valid=0,skipped=0;
        const string alias="clipshot_import",seenFunction="clipshot_import_seen",aliveFunction="clipshot_import_alive";
        try
        {
            // Декодер может читать большой источник несколько минут. Его перечисление
            // пишет только во временную БД, не удерживая writer lock рабочей истории.
            using(var stage=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=staging,Pooling=false}.ToString()))
            {
                stage.Open();using var create=stage.CreateCommand();
                create.CommandText="CREATE TABLE imported(hash TEXT PRIMARY KEY,kind TEXT NOT NULL,text TEXT NOT NULL,image BLOB,template INTEGER NOT NULL,name TEXT NOT NULL,seen TEXT NOT NULL,dated INTEGER NOT NULL)";create.ExecuteNonQuery();
                using var transaction=stage.BeginTransaction();using var write=stage.CreateCommand();write.Transaction=transaction;
                write.CommandText="""
                INSERT INTO imported(hash,kind,text,image,template,name,seen,dated) VALUES($hash,$kind,$text,$image,$template,$name,$seen,$dated)
                ON CONFLICT(hash) DO UPDATE SET template=MAX(imported.template,excluded.template),
                  name=CASE WHEN imported.name<>'' THEN imported.name ELSE excluded.name END,
                  seen=CASE WHEN imported.dated>excluded.dated THEN imported.seen WHEN imported.dated<excluded.dated THEN excluded.seen ELSE MAX(imported.seen,excluded.seen) END,
                  dated=MAX(imported.dated,excluded.dated);
                """;
                write.Parameters.Add("$hash",SqliteType.Text);write.Parameters.Add("$kind",SqliteType.Text);write.Parameters.Add("$text",SqliteType.Text);
                write.Parameters.Add("$image",SqliteType.Blob);write.Parameters.Add("$template",SqliteType.Integer);write.Parameters.Add("$name",SqliteType.Text);write.Parameters.Add("$seen",SqliteType.Text);write.Parameters.Add("$dated",SqliteType.Integer);
                write.Prepare();DateTime undatedUtc=DateTime.UtcNow;
                foreach(var result in items)
                {
                    token.ThrowIfCancellationRequested();
                    if(result is null||(result.Clip is null)==(result.SkipReason is null))throw new InvalidDataException("Invalid import result");
                    if(result.Clip is not {} clip)skipped=checked(skipped+1);
                    else
                    {
                        if(clip.Kind is not "text" and not "image"||clip.Text is null||clip.Name is null||
                            (clip.Kind=="image"?(clip.Image is null||clip.Image.Length==0):clip.Image is not null))throw new InvalidDataException("Invalid imported clip");
                        DateTime seen=clip.SeenLocal is {} source?source.Kind==DateTimeKind.Utc?source:source.Kind==DateTimeKind.Local?source.ToUniversalTime():DateTime.SpecifyKind(source,DateTimeKind.Local).ToUniversalTime():undatedUtc;
                        string hash=clip.Kind+Convert.ToHexString(SHA256.HashData(clip.Image??Encoding.UTF8.GetBytes(clip.Text)));
                        write.Parameters["$hash"].Value=hash;write.Parameters["$kind"].Value=clip.Kind;write.Parameters["$text"].Value=clip.Text;
                        write.Parameters["$image"].Value=(object?)clip.Image??DBNull.Value;write.Parameters["$template"].Value=clip.Template?1:0;
                        write.Parameters["$name"].Value=clip.Name;write.Parameters["$seen"].Value=seen.ToString("O",CultureInfo.InvariantCulture);write.Parameters["$dated"].Value=clip.SeenLocal.HasValue?1:0;
                        write.ExecuteNonQuery();valid=checked(valid+1);
                    }
                    processed=checked(processed+1);if(processed==1||processed%128==0)progress?.Report(processed);
                }
                token.ThrowIfCancellationRequested();transaction.Commit();
            }
            token.ThrowIfCancellationRequested();
            using(var attach=db.CreateCommand()){attach.CommandText=$"ATTACH DATABASE $file AS {alias}";attach.Parameters.AddWithValue("$file",staging);attach.ExecuteNonQuery();attached=true;}
            // Канонический UTC нужен и для старых значений с часовым смещением:
            // строковый MAX двух разных смещений не означает более поздний момент.
            db.CreateFunction<string,string,string>(seenFunction,(existing,incoming)=>
            {
                DateTime left=StoredUtc(existing),right=StoredUtc(incoming);return (left>right?left:right).ToString("O",CultureInfo.InvariantCulture);
            });
            db.CreateFunction<int>(aliveFunction,()=>{token.ThrowIfCancellationRequested();return 1;});
            using(var transaction=db.BeginTransaction())
            {
                int imported;
                using(var count=db.CreateCommand())
                {
                    count.Transaction=transaction;count.CommandText=$"SELECT COUNT(*) FROM {alias}.imported s LEFT JOIN main.clips c ON c.hash=s.hash WHERE c.id IS NULL";
                    imported=checked(Convert.ToInt32(count.ExecuteScalar(),CultureInfo.InvariantCulture));
                }
                using(var merge=db.CreateCommand())
                {
                    merge.Transaction=transaction;merge.CommandText=$"""
                    INSERT INTO main.clips(hash,kind,text,image,template,name,seen)
                    SELECT hash,kind,text,image,template,name,seen FROM {alias}.imported WHERE {aliveFunction}()=1
                    ON CONFLICT(hash) DO UPDATE SET
                      template=CASE WHEN clips.template<>0 OR excluded.template<>0 THEN 1 ELSE 0 END,
                      name=CASE WHEN clips.name<>'' THEN clips.name ELSE excluded.name END,
                      seen=CASE WHEN (SELECT s.dated FROM {alias}.imported s WHERE s.hash=excluded.hash)=0 THEN clips.seen ELSE {seenFunction}(clips.seen,excluded.seen) END;
                    """;
                    merge.ExecuteNonQuery();
                }
                if(keepAllHistory)
                {
                    using var retention=db.CreateCommand();retention.Transaction=transaction;
                    retention.CommandText="INSERT INTO settings(key,value) VALUES('days','0') ON CONFLICT(key) DO UPDATE SET value=excluded.value";retention.ExecuteNonQuery();
                }
                // Повтор N обозначает окончание merge. Синхронный получатель может
                // отменить операцию здесь; ни clips, ни days ещё не закоммичены.
                progress?.Report(processed);token.ThrowIfCancellationRequested();transaction.Commit();
                return new(imported,valid-imported,skipped);
            }
        }
        catch(SqliteException)when(token.IsCancellationRequested){throw new OperationCanceledException(token);}
        finally
        {
            try
            {
                db.CreateFunction<string,string,string>(seenFunction,(Func<string,string,string>?)null);db.CreateFunction<int>(aliveFunction,(Func<int>?)null);
                if(attached){using var detach=db.CreateCommand();detach.CommandText=$"DETACH DATABASE {alias}";detach.ExecuteNonQuery();}
            }
            catch(SqliteException){}catch(InvalidOperationException){}
            // Папка создана только для этого вызова. Закрытые SQLite connections не
            // оставляют pool/WAL handles; уборка не обращается к исходной базе.
            try{if(Path.GetFullPath(folder).StartsWith(temporaryRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))Directory.Delete(folder,true);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
    private static DateTime StoredUtc(string value)
    {
        if(!DateTimeOffset.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var result))throw new InvalidDataException("Invalid stored timestamp");
        return result.UtcDateTime;
    }
    public List<Clip> All()
    {
        using var c = db.CreateCommand(); c.CommandText="SELECT id,kind,text,html,rtf,image,template,name,seen FROM clips ORDER BY seen DESC,id DESC";
        using var r=c.ExecuteReader(); var result=new List<Clip>();
        while(r.Read()) result.Add(new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.IsDBNull(5)?null:(byte[])r[5],r.GetInt64(6)!=0,r.GetString(7),DateTime.Parse(r.GetString(8),null,System.Globalization.DateTimeStyles.RoundtripKind).ToLocalTime()));
        return result;
    }
    public void Template(long id, bool value) => Change("UPDATE clips SET template=$v WHERE id=$id",id,value?1:0);
    public void Edit(long id,string name) => Change("UPDATE clips SET name=$v WHERE id=$id",id,name);
    public void EditText(long id,string name,string text)
    {
        string hash="text"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        using var c=db.CreateCommand();c.CommandText="UPDATE clips SET name=$name,text=$text,hash=$hash,html='',rtf='' WHERE id=$id";
        c.Parameters.AddWithValue("$name",name);c.Parameters.AddWithValue("$text",text);c.Parameters.AddWithValue("$hash",hash);c.Parameters.AddWithValue("$id",id);c.ExecuteNonQuery();
    }
    public void EditImage(long id,byte[] image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if(image.Length==0)throw new ArgumentException("Image cannot be empty.",nameof(image));
        string hash="image"+Convert.ToHexString(SHA256.HashData(image));
        // Одно UPDATE атомарно заменяет содержимое существующего снимка, сохраняя его id,
        // имя и признак шаблона. UNIQUE-конфликт отменяет всё изменение; другой клип не удаляем.
        using var c=db.CreateCommand();c.CommandText="UPDATE clips SET image=$image,hash=$hash,seen=$seen WHERE id=$id AND kind='image'";
        c.Parameters.AddWithValue("$image",image);c.Parameters.AddWithValue("$hash",hash);
        c.Parameters.AddWithValue("$seen",DateTime.UtcNow.ToString("O"));c.Parameters.AddWithValue("$id",id);
        if(c.ExecuteNonQuery()!=1)throw new InvalidOperationException("The clip does not exist or is not an image.");
    }
    public void Delete(long id) => Change("DELETE FROM clips WHERE id=$id",id,0);
    private void Change(string sql,long id,object value)
    { using var c=db.CreateCommand(); c.CommandText=sql;c.Parameters.AddWithValue("$id",id); if(sql.Contains("$v")) c.Parameters.AddWithValue("$v",value); c.ExecuteNonQuery(); }
    public void Clear() { using var c=db.CreateCommand(); c.CommandText="DELETE FROM clips WHERE template=0";c.ExecuteNonQuery(); }
    public void Prune(int days)
    { if(days<=0)return;using var c=db.CreateCommand();c.CommandText="DELETE FROM clips WHERE template=0 AND seen < $date"; c.Parameters.AddWithValue("$date",DateTime.UtcNow.AddDays(-days).ToString("O"));c.ExecuteNonQuery(); }
    public void Dispose()=>db.Dispose();
}
