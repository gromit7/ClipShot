using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ClipShot;

public sealed record TemplateFolder(long Id, string Name, IReadOnlyList<long> ClipIds);
public sealed record TemplateLayout(IReadOnlyList<string> RootOrder, IReadOnlyList<TemplateFolder> Folders);

public sealed partial class Store
{
    private const string TemplateOrderMarker = "templateorder.layout-sha256";

    private void InitializeTemplateFolders()
    {
        using var command = db.CreateCommand();
        command.CommandText = """
        CREATE TABLE IF NOT EXISTS template_folders(id INTEGER PRIMARY KEY AUTOINCREMENT,name TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS template_layout(entry_key TEXT PRIMARY KEY,clip_id INTEGER UNIQUE,parent_folder INTEGER,position INTEGER NOT NULL);
        CREATE TRIGGER IF NOT EXISTS clipshot_template_deleted AFTER DELETE ON clips BEGIN
          DELETE FROM template_layout WHERE clip_id=OLD.id;
        END;
        CREATE TRIGGER IF NOT EXISTS clipshot_template_unset AFTER UPDATE OF template ON clips WHEN NEW.template=0 BEGIN
          DELETE FROM template_layout WHERE clip_id=OLD.id;
        END;
        """;
        command.ExecuteNonQuery();
    }

    public TemplateLayout GetTemplateLayout()
    {
        using var transaction = db.BeginTransaction(deferred: true);
        var result = ReadTemplateLayout(transaction).Public(); transaction.Commit(); return result;
    }

    public long CreateTemplateFolder(string name, long targetClipId, long sourceClipId)
    {
        name = ValidateTemplateFolderName(name);
        using var transaction = db.BeginTransaction();
        var layout = ReadTemplateLayout(transaction);
        string target = ClipKey(targetClipId), source = ClipKey(sourceClipId);
        int targetPosition = layout.Root.IndexOf(target);
        if (targetClipId == sourceClipId || targetPosition < 0 || !layout.ContainsClip(sourceClipId))
            throw new ArgumentException("Folder requires two distinct templates and a root target.");
        using var create = db.CreateCommand(); create.Transaction = transaction;
        create.CommandText = "INSERT INTO template_folders(name) VALUES($name) RETURNING id";
        create.Parameters.AddWithValue("$name", name);
        long id = Convert.ToInt64(create.ExecuteScalar(), CultureInfo.InvariantCulture);
        // Replace target before removing source: its place stays stable even when
        // the dragged source was an earlier root entry.
        layout.Root[targetPosition] = FolderKey(id);
        layout.RemoveClip(sourceClipId);
        layout.Folders[id] = new FolderState(id, name, [targetClipId, sourceClipId]);
        SaveTemplateLayout(layout, transaction);
        transaction.Commit(); return id;
    }

    public void MoveTemplateEntry(string sourceKey, long? destinationFolderId = null, string? anchorKey = null, bool after = false)
    {
        ParseTemplateKey(sourceKey, out bool folder, out long sourceId);
        using var transaction = db.BeginTransaction();
        var layout = ReadTemplateLayout(transaction);
        List<string> destination;
        FolderState? destinationFolder = null;
        if (destinationFolderId is long id)
        {
            if (folder || !layout.Folders.TryGetValue(id, out destinationFolder)) throw new ArgumentException("Invalid folder destination.");
            destination = destinationFolder.Clips.Select(ClipKey).ToList();
        }
        else destination = layout.Root;
        if (folder ? !layout.Root.Contains(sourceKey) : !layout.ContainsClip(sourceId)) throw new ArgumentException("Unknown template entry.");
        if (anchorKey is not null)
        {
            ParseTemplateKey(anchorKey, out _, out _);
            if (!destination.Contains(anchorKey)) throw new ArgumentException("Anchor is not in destination.");
            if (anchorKey == sourceKey) { transaction.Commit(); return; }
        }
        if (folder) layout.Root.Remove(sourceKey); else layout.RemoveClip(sourceId);
        if (destinationFolder is not null) destination = destinationFolder.Clips.Select(ClipKey).ToList();
        int position = anchorKey is null ? destination.Count : destination.IndexOf(anchorKey) + (after ? 1 : 0);
        destination.Insert(position, sourceKey);
        if (destinationFolder is not null) destinationFolder.Clips = destination.Select(key => { ParseTemplateKey(key, out _, out long clipId); return clipId; }).ToList();
        SaveTemplateLayout(layout, transaction); transaction.Commit();
    }

    public void RenameTemplateFolder(long id, string name)
    {
        name = ValidateTemplateFolderName(name);
        using var transaction = db.BeginTransaction();
        var layout = ReadTemplateLayout(transaction);
        if (!layout.Folders.TryGetValue(id, out var folder)) throw new ArgumentException("Unknown visible folder.");
        using var command = db.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "UPDATE template_folders SET name=$name WHERE id=$id";
        command.Parameters.AddWithValue("$name", name); command.Parameters.AddWithValue("$id", id); command.ExecuteNonQuery();
        folder.Name = name;
        SaveTemplateLayout(layout, transaction); transaction.Commit();
    }

    public void UngroupTemplateFolder(long id)
    {
        using var transaction = db.BeginTransaction();
        var layout = ReadTemplateLayout(transaction);
        if (!layout.Folders.Remove(id, out var folder)) throw new ArgumentException("Unknown visible folder.");
        int position = layout.Root.IndexOf(FolderKey(id));
        layout.Root.RemoveAt(position); layout.Root.InsertRange(position, folder.Clips.Select(ClipKey));
        using var command = db.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "DELETE FROM template_folders WHERE id=$id"; command.Parameters.AddWithValue("$id", id); command.ExecuteNonQuery();
        SaveTemplateLayout(layout, transaction); transaction.Commit();
    }

    private TemplateState ReadTemplateLayout(SqliteTransaction? transaction)
    {
        var live = new List<long>();
        using (var query = db.CreateCommand())
        {
            query.Transaction = transaction; query.CommandText = "SELECT id FROM clips WHERE template<>0 ORDER BY seen DESC,id DESC";
            using var reader = query.ExecuteReader(); while (reader.Read()) live.Add(reader.GetInt64(0));
        }
        var liveIds = live.ToHashSet(); var ordered = new List<long>(); var listed = new HashSet<long>();
        string csv; string? savedOrderHash;
        using (var query = db.CreateCommand())
        {
            query.Transaction = transaction; query.CommandText = "SELECT value FROM settings WHERE key='templateorder'";
            csv = query.ExecuteScalar() as string ?? "";
            query.CommandText = "SELECT value FROM settings WHERE key=$key";
            query.Parameters.AddWithValue("$key", TemplateOrderMarker);
            savedOrderHash = query.ExecuteScalar() as string;
        }
        foreach (string item in csv.Split(','))
            if (long.TryParse(item, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id) && liveIds.Contains(id) && listed.Add(id)) ordered.Add(id);
        var legacyRanks = ordered.Select((id, index) => (id, index)).ToDictionary(item => item.id, item => item.index);
        foreach (long id in live) if (listed.Add(id)) ordered.Add(id);
        var state = new TemplateState();
        using (var query = db.CreateCommand())
        {
            query.Transaction = transaction; query.CommandText = "SELECT id,name FROM template_folders ORDER BY id";
            using var reader = query.ExecuteReader(); while (reader.Read())
            { long id = reader.GetInt64(0); state.Folders[id] = new(id, reader.GetString(1), []); }
        }
        var entries = new List<(string Key, long? Clip, long? Parent)>();
        using (var query = db.CreateCommand())
        {
            query.Transaction = transaction; query.CommandText = "SELECT entry_key,clip_id,parent_folder FROM template_layout ORDER BY position,entry_key";
            using var reader = query.ExecuteReader(); while (reader.Read()) entries.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetInt64(1), reader.IsDBNull(2) ? null : reader.GetInt64(2)));
        }
        var assigned = new HashSet<long>();
        foreach (var entry in entries)
            if (entry.Clip is long clip && liveIds.Contains(clip) && entry.Key == ClipKey(clip) && entry.Parent is long parent &&
                state.Folders.TryGetValue(parent, out var folder) && assigned.Add(clip)) folder.Clips.Add(clip);
        // Empty metadata folders are invisible. They may remain after an older
        // executable removes its last member; no clip content is ever deleted.
        foreach (long id in state.Folders.Where(pair => pair.Value.Clips.Count == 0).Select(pair => pair.Key).ToArray()) state.Folders.Remove(id);
        var rootKeys = new HashSet<string>();
        foreach (var entry in entries.Where(entry => entry.Parent is null))
        {
            if (entry.Clip is long clip && liveIds.Contains(clip) && entry.Key == ClipKey(clip) && assigned.Add(clip))
            { state.Root.Add(entry.Key); rootKeys.Add(entry.Key); }
            else if (entry.Clip is null && TryFolderKey(entry.Key, out long folderId) && state.Folders.ContainsKey(folderId) && rootKeys.Add(entry.Key)) state.Root.Add(entry.Key);
        }
        foreach (long id in state.Folders.Keys) if (rootKeys.Add(FolderKey(id))) state.Root.Add(FolderKey(id));
        foreach (long id in ordered) if (assigned.Add(id)) state.Root.Add(ClipKey(id));
        if (savedOrderHash != TemplateOrderHash(csv))
        {
            // An older executable can only reorder the flattened CSV. Preserve
            // membership while projecting its new order back into whole folders:
            // a folder occupies its first member's place; unlisted entries stay stable.
            int Rank(long id) => legacyRanks.GetValueOrDefault(id, int.MaxValue);
            foreach (var folder in state.Folders.Values) folder.Clips = folder.Clips.OrderBy(Rank).ToList();
            int RootRank(string key)
            {
                ParseTemplateKey(key, out bool folder, out long id);
                return folder ? state.Folders[id].Clips.Min(Rank) : Rank(id);
            }
            state.Root = state.Root.OrderBy(RootRank).ToList();
        }
        return state;
    }

    private void SaveTemplateLayout(TemplateState state, SqliteTransaction transaction)
    {
        using (var clear = db.CreateCommand()) { clear.Transaction = transaction; clear.CommandText = "DELETE FROM template_layout"; clear.ExecuteNonQuery(); }
        using var insert = db.CreateCommand(); insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO template_layout(entry_key,clip_id,parent_folder,position) VALUES($key,$clip,$parent,$position)";
        insert.Parameters.Add("$key", SqliteType.Text); insert.Parameters.Add("$clip", SqliteType.Integer);
        insert.Parameters.Add("$parent", SqliteType.Integer); insert.Parameters.Add("$position", SqliteType.Integer);
        void Entry(string key, long? clip, long? parent, int position)
        {
            insert.Parameters["$key"].Value = key; insert.Parameters["$clip"].Value = (object?)clip ?? DBNull.Value;
            insert.Parameters["$parent"].Value = (object?)parent ?? DBNull.Value; insert.Parameters["$position"].Value = position; insert.ExecuteNonQuery();
        }
        var flattened = new List<long>();
        for (int i = 0; i < state.Root.Count; i++)
        {
            string key = state.Root[i]; ParseTemplateKey(key, out bool folder, out long id);
            if (!folder) { Entry(key, id, null, i); flattened.Add(id); }
            else if (state.Folders[id].Clips.Count > 0)
            {
                Entry(key, null, null, i);
                for (int j = 0; j < state.Folders[id].Clips.Count; j++)
                { long child = state.Folders[id].Clips[j]; Entry(ClipKey(child), child, id, j); flattened.Add(child); }
            }
        }
        using var legacy = db.CreateCommand(); legacy.Transaction = transaction;
        string csv = string.Join(',', flattened);
        legacy.CommandText = "INSERT INTO settings(key,value) VALUES('templateorder',$order),($marker,$hash) ON CONFLICT(key) DO UPDATE SET value=excluded.value";
        legacy.Parameters.AddWithValue("$order", csv); legacy.Parameters.AddWithValue("$marker", TemplateOrderMarker);
        legacy.Parameters.AddWithValue("$hash", TemplateOrderHash(csv)); legacy.ExecuteNonQuery();
    }

    private static string TemplateOrderHash(string csv) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(csv)));

    private static string ValidateTemplateFolderName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Any(char.IsControl)) throw new ArgumentException("Folder name must contain 1–80 characters without controls.");
        name = name.Trim();
        if (name.Length is < 1 or > 80) throw new ArgumentException("Folder name must contain 1–80 characters without controls.");
        return name;
    }
    private static string ClipKey(long id) => "c:" + id.ToString(CultureInfo.InvariantCulture);
    private static string FolderKey(long id) => "f:" + id.ToString(CultureInfo.InvariantCulture);
    private static bool TryFolderKey(string key, out long id)
    {
        id = 0;
        return key.StartsWith("f:", StringComparison.Ordinal) && long.TryParse(key.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0 && key == FolderKey(id);
    }
    private static void ParseTemplateKey(string key, out bool folder, out long id)
    {
        folder = false; id = 0;
        if (key is null || key.Length < 3 || key[1] != ':' || key[0] is not ('c' or 'f') ||
            !long.TryParse(key.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out id) || id <= 0) throw new ArgumentException("Invalid template entry key.");
        folder = key[0] == 'f'; if (key != (folder ? FolderKey(id) : ClipKey(id))) throw new ArgumentException("Noncanonical template entry key.");
    }
    private sealed class FolderState(long id, string name, List<long> clips)
    { public long Id = id; public string Name = name; public List<long> Clips = clips; }
    private sealed class TemplateState
    {
        public List<string> Root = []; public Dictionary<long, FolderState> Folders = [];
        public bool ContainsClip(long id) => Root.Contains(ClipKey(id)) || Folders.Values.Any(folder => folder.Clips.Contains(id));
        public void RemoveClip(long id) { Root.Remove(ClipKey(id)); foreach (var folder in Folders.Values) folder.Clips.Remove(id); }
        public TemplateLayout Public() => new(Root.ToArray(), Root.Where(key => key.StartsWith("f:", StringComparison.Ordinal)).Select(key =>
        { ParseTemplateKey(key, out _, out long id); var folder = Folders[id]; return new TemplateFolder(id, folder.Name, folder.Clips.ToArray()); }).ToArray());
    }
}
