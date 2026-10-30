using Microsoft.Data.Sqlite;
using System.Text.Json;
using Xunit;

namespace ClipShot.Checks;

public sealed class TemplateFolderTests : IDisposable
{
    private readonly Testing.TestWorkspace workspace = new();
    private string folder => workspace.Root;
    private string Database => Path.Combine(folder, "synthetic.db");
    private Store Open() => new(Database);
    private static string C(long id) => "c:" + id;
    private static string F(long id) => "f:" + id;
    private static long Add(Store store, string text, bool template = true)
    { store.Add("text", text, "<b>synthetic</b>", "{\\rtf1 synthetic}"); long id = store.All().Single(clip => clip.Text == text).Id; store.Template(id, template); return id; }
    private void Sql(string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database, Pooling = false }.ToString());
        connection.Open(); using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
    }
    private long Scalar(string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database, Pooling = false }.ToString());
        connection.Open(); using var command = connection.CreateCommand(); command.CommandText = sql; return Convert.ToInt64(command.ExecuteScalar());
    }
    private static string Snapshot(Store store) => JsonSerializer.Serialize(store.All());

    [Fact] public void Legacy_order_is_projected_without_writing_layout_or_settings()
    {
        using var store = Open(); long a = Add(store, "a"), b = Add(store, "b"), c = Add(store, "c");
        string csv = $"{b},bogus,{b},999,{a}"; store.Set("templateorder", csv);
        var layout = store.GetTemplateLayout();
        Assert.Equal(new[] { C(b), C(a), C(c) }, layout.RootOrder); Assert.Empty(layout.Folders);
        Assert.Equal(csv, store.Get("templateorder", "")); Assert.Equal(0, Scalar("SELECT COUNT(*) FROM template_layout"));
    }

    [Fact] public void Creating_named_folder_reopens_with_exact_order_and_legacy_flattening()
    {
        long a, b, c, id;
        using (var store = Open())
        {
            a = Add(store, "10.0.0.1"); b = Add(store, "10.0.0.2"); c = Add(store, "other");
            store.Set("templateorder", $"{a},{b},{c}");
            id = store.CreateTemplateFolder(" IP-адреса ", b, a);
            Assert.Equal(new[] { F(id), C(c) }, store.GetTemplateLayout().RootOrder);
            Assert.Equal($"{b},{a},{c}", store.Get("templateorder", ""));
        }
        using var reopened = Open(); var folder = Assert.Single(reopened.GetTemplateLayout().Folders);
        Assert.Equal(id, folder.Id); Assert.Equal("IP-адреса", folder.Name); Assert.Equal(new[] { b, a }, folder.ClipIds);
    }

    [Fact] public void Root_and_children_reorder_and_cross_folder_move_keep_each_clip_once()
    {
        using var store = Open(); long a = Add(store, "a"), b = Add(store, "b"), c = Add(store, "c"), d = Add(store, "d");
        store.Set("templateorder", $"{a},{b},{c},{d}"); long id = store.CreateTemplateFolder("Pair", a, b);
        store.MoveTemplateEntry(C(d), null, F(id));
        Assert.Equal(new[] { C(d), F(id), C(c) }, store.GetTemplateLayout().RootOrder);
        store.MoveTemplateEntry(C(c), id, C(a), after: true);
        Assert.Equal(new[] { a, c, b }, Assert.Single(store.GetTemplateLayout().Folders).ClipIds);
        store.MoveTemplateEntry(C(b), id, C(a));
        Assert.Equal(new[] { b, a, c }, Assert.Single(store.GetTemplateLayout().Folders).ClipIds);
        store.MoveTemplateEntry(C(a), null, C(d), after: true);
        Assert.Equal(new[] { C(d), C(a), F(id) }, store.GetTemplateLayout().RootOrder);
        Assert.Equal($"{d},{a},{b},{c}", store.Get("templateorder", ""));
        store.MoveTemplateEntry(F(id), null, C(d));
        Assert.Equal(new[] { F(id), C(d), C(a) }, store.GetTemplateLayout().RootOrder);
    }

    [Fact] public void Reordered_legacy_csv_after_downgrade_preserves_folders_and_is_adopted_by_next_save()
    {
        long a, b, c, d, e, extra, pair, second; string clips, oldCsv;
        using (var store = Open())
        {
            a = Add(store, "a"); b = Add(store, "b"); c = Add(store, "c"); d = Add(store, "d"); e = Add(store, "e");
            store.Set("templateorder", $"{a},{b},{c},{d},{e}");
            pair = store.CreateTemplateFolder("IP-адреса", a, b);
            second = store.CreateTemplateFolder("Другая папка", d, e);
            // Simulate an old executable: it knows only CSV, including an
            // interleaved folder and a new template absent from that CSV.
            oldCsv = $"{c},{e},{b},{d},{a}"; store.Set("templateorder", oldCsv);
            extra = Add(store, "added by older executable"); clips = Snapshot(store);
        }
        using (var reopened = Open())
        {
            var layout = reopened.GetTemplateLayout();
            Assert.Equal(new[] { C(c), F(second), F(pair), C(extra) }, layout.RootOrder);
            Assert.Equal(new[] { e, d }, layout.Folders.Single(folder => folder.Id == second).ClipIds);
            var folder = layout.Folders.Single(folder => folder.Id == pair);
            Assert.Equal("IP-адреса", folder.Name); Assert.Equal(new[] { b, a }, folder.ClipIds);
            Assert.Equal("Другая папка", layout.Folders.Single(item => item.Id == second).Name);
            Assert.Equal(clips, Snapshot(reopened)); Assert.Equal(oldCsv, reopened.Get("templateorder", ""));
            reopened.MoveTemplateEntry(C(extra), null, C(c));
            Assert.Equal($"{extra},{c},{e},{d},{b},{a}", reopened.Get("templateorder", ""));
        }
        using var saved = Open();
        Assert.Equal(new[] { C(extra), C(c), F(second), F(pair) }, saved.GetTemplateLayout().RootOrder);
        Assert.Equal(clips, Snapshot(saved));
    }

    [Fact] public void Rename_and_ungroup_preserve_all_clip_bytes_formats_dates_and_other_settings()
    {
        using var store = Open(); long a = Add(store, "a"), b = Add(store, "b");
        store.Add("image", "", image: [1, 2, 3, 4, 5]); long image = store.All().Single(clip => clip.Kind == "image").Id; store.Template(image, true);
        store.Set("theme", "PinkCats"); store.Set("days", "90"); store.Set("unrelated", "synthetic");
        Sql("UPDATE clips SET seen='2020-01-02T03:04:05.0000000Z'");
        string before = Snapshot(store); long id = store.CreateTemplateFolder("Pair", a, b);
        store.MoveTemplateEntry(C(image), id); store.RenameTemplateFolder(id, "Памятка");
        Assert.Equal("Памятка", Assert.Single(store.GetTemplateLayout().Folders).Name);
        store.UngroupTemplateFolder(id);
        Assert.Empty(store.GetTemplateLayout().Folders); Assert.Equal(new[] { C(a), C(b), C(image) }, store.GetTemplateLayout().RootOrder);
        Assert.Equal(before, Snapshot(store)); Assert.Equal("PinkCats", store.Get("theme", ""));
        Assert.Equal("90", store.Get("days", "")); Assert.Equal("synthetic", store.Get("unrelated", ""));
    }

    [Fact] public void Old_version_delete_and_explicit_id_reuse_cannot_inherit_folder_membership()
    {
        using var store = Open(); long a = Add(store, "a"), b = Add(store, "b"); long id = store.CreateTemplateFolder("Pair", a, b);
        Sql($"DELETE FROM clips WHERE id={b}; INSERT INTO clips(id,hash,kind,text,template,seen) VALUES({b},'synthetic-reused','text','new content',1,'2026-10-26T00:00:00Z')");
        var layout = store.GetTemplateLayout(); Assert.Equal(new[] { a }, Assert.Single(layout.Folders).ClipIds);
        Assert.Contains(C(b), layout.RootOrder);
        Sql($"UPDATE clips SET template=0 WHERE id={a}; UPDATE clips SET template=1 WHERE id={a}");
        layout = store.GetTemplateLayout(); Assert.Empty(layout.Folders); Assert.Contains(C(a), layout.RootOrder);
        Assert.Equal(0, Scalar($"SELECT COUNT(*) FROM template_layout WHERE parent_folder={id}"));
        Assert.Equal(2, store.All().Count);
    }

    [Fact] public void Invalid_names_moves_anchors_and_cycles_are_atomic()
    {
        using var store = Open(); long a = Add(store, "a"), b = Add(store, "b"), c = Add(store, "history", false); long id = store.CreateTemplateFolder("Pair", a, b);
        string state = JsonSerializer.Serialize(store.GetTemplateLayout()), clips = Snapshot(store), csv = store.Get("templateorder", "");
        Action[] invalid = [() => store.CreateTemplateFolder("", a, b), () => store.CreateTemplateFolder("bad\nname", a, b),
            () => store.RenameTemplateFolder(id, new string('x', 81)), () => store.CreateTemplateFolder("nested", a, b),
            () => store.MoveTemplateEntry(C(c), id), () => store.MoveTemplateEntry(F(id), id),
            () => store.MoveTemplateEntry(C(a), null, C(b)), () => store.MoveTemplateEntry(C(a), 999),
            () => store.MoveTemplateEntry("c:01"), () => store.UngroupTemplateFolder(999)];
        foreach (var run in invalid)
        {
            Assert.ThrowsAny<ArgumentException>(run);
            Assert.Equal(state, JsonSerializer.Serialize(store.GetTemplateLayout())); Assert.Equal(clips, Snapshot(store));
            Assert.Equal(csv, store.Get("templateorder", ""));
        }
    }

    [Fact] public void Sql_failure_rolls_back_folder_creation_membership_and_legacy_order()
    {
        using var store = Open(); long a = Add(store, "a"), b = Add(store, "b"); store.Set("templateorder", $"{a},{b}");
        Sql("CREATE TRIGGER synthetic_folder_reject BEFORE INSERT ON template_layout BEGIN SELECT RAISE(ABORT,'synthetic'); END");
        Assert.Throws<SqliteException>(() => store.CreateTemplateFolder("Pair", a, b));
        Assert.Empty(store.GetTemplateLayout().Folders); Assert.Equal(0, Scalar("SELECT COUNT(*) FROM template_folders"));
        Assert.Equal($"{a},{b}", store.Get("templateorder", "")); Assert.Equal(2, store.All().Count);
    }

    public void Dispose() => workspace.Dispose();
}
