using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;

namespace ClipShot;
public sealed record Clip(long Id, string Kind, string Text, string Html, string Rtf, byte[]? Image, bool Template, string Name, DateTime Seen)
{
    public string Label => Name.Length > 0 ? Name : Kind == "image" ? "Изображение" : Text.Replace('\r', ' ').Replace('\n', ' ');
}
public sealed class Store : IDisposable
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
