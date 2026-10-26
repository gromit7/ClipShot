using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ClipShot.Checks;

// Artificial fixtures encode the statically recovered 5.7 layout. These are not
// captures from a running Clipdiary; interoperability remains a separate check.
public sealed class ClipdiaryImportTests
{
    private static ClipdiaryImportedClip? Decode(byte[] payload) => ClipdiaryImport.DecodeComposite(payload, TestContext.Current.CancellationToken);
    private static byte[] Pack(params (uint Format, string Name, byte[] Bytes)[] formats)
    {
        using var raw = new MemoryStream();
        using (var writer = new BinaryWriter(raw, Encoding.UTF8, true))
        {
            writer.Write((ushort)1); writer.Write((ushort)formats.Length);
            foreach (var (format, name, bytes) in formats)
            {
                byte[] encoded = Encoding.UTF8.GetBytes(name);
                writer.Write(format); writer.Write((uint)bytes.Length); writer.Write((uint)encoded.Length);
                writer.Write(encoded); writer.Write(bytes);
            }
        }
        return Zip(raw.ToArray());
    }
    private static byte[] Zip(byte[] data)
    {
        using var result = new MemoryStream();
        using (var gzip = new GZipStream(result, CompressionLevel.Fastest, true)) gzip.Write(data);
        return result.ToArray();
    }
    private static byte[] Text(string text) => Pack((13, "CF_UNICODETEXT", Encoding.Unicode.GetBytes(text + "\0")));

    [Fact] public void Full_unicode_payload_wins_over_other_formats()
    {
        string text = "Полный текст\n" + new string('Ж', 500) + " 🐈";
        var clip = Decode(Pack((1, "CF_TEXT", "preview"u8.ToArray()),
            (13, "CF_UNICODETEXT", Encoding.Unicode.GetBytes(text + "\0"))));
        Assert.Equal(text, clip!.Text); Assert.Equal("text", clip.Kind);
    }

    [Fact] public void Unsupported_format_and_version_are_not_preview_text()
    {
        Assert.Null(Decode(Pack((1, "CF_TEXT", "preview"u8.ToArray()))));
        Assert.Null(Decode(Zip([2, 0, 1, 0])));
    }

    [Theory]
    [InlineData(new byte[] { 1, 0, 0, 0 })]
    [InlineData(new byte[] { 1, 0, 1, 0, 13, 0, 0, 0 })]
    [InlineData(new byte[] { 1, 0, 1, 0, 13, 0, 0, 0, 255, 255, 255, 255, 0, 0, 0, 0 })]
    public void Malformed_counts_and_lengths_fail_closed(byte[] raw)
        => Assert.Throws<InvalidDataException>(() => Decode(Zip(raw)));

    [Fact] public void Invalid_utf16_and_trailing_payload_fail_closed()
    {
        Assert.Throws<InvalidDataException>(() => Decode(Pack((13, "CF_UNICODETEXT", [65]))));
        Assert.Throws<DecoderFallbackException>(() => Decode(Pack((13, "CF_UNICODETEXT", [0, 216]))));
        Assert.Throws<InvalidDataException>(() => Decode(Zip([1, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 99])));
    }

    [Fact] public void Cancellation_is_observed_before_decoding_or_opening_source()
    {
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => ClipdiaryImport.DecodeComposite(Text("test"), cancel.Token));
        Assert.Throws<OperationCanceledException>(() => ClipdiaryImport.ReadHistory("does-not-exist.db", cancel.Token).ToList());
    }

    [Fact] public void Gzip_bomb_is_bounded()
        => Assert.ThrowsAny<IOException>(() => Decode(Zip(new byte[ClipdiaryImport.MaximumPayloadBytes + 1])));

    [Fact] public void Dib_without_file_header_becomes_valid_png()
    {
        using var source = new MemoryStream();
        using (var writer = new BinaryWriter(source, Encoding.UTF8, true))
        {
            writer.Write(40); writer.Write(2); writer.Write(2); writer.Write((ushort)1); writer.Write((ushort)24);
            writer.Write(0); writer.Write(16); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
            writer.Write(Convert.FromHexString("0000FF00FF000000FF0000FFFFFF0000"));
        }
        var image = Decode(Pack((8, "CF_DIB", source.ToArray())))!;
        Assert.Equal("image", image.Kind);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, image.Image![..8]);
        var png = Decode(Pack((49199, "PNG", image.Image)))!;
        Assert.Equal("image", png.Kind);
        var screenshotWithText = Decode(Pack((13, "CF_UNICODETEXT", Encoding.Unicode.GetBytes("Alternative text\0")),
            (49199, "PNG", image.Image)))!;
        Assert.Equal("image", screenshotWithText.Kind);
        Assert.NotNull(screenshotWithText.Image);
        Assert.Throws<InvalidDataException>(() => Decode(Pack((8, "CF_DIB", source.ToArray()[..42]))));
    }

    [Fact] public void Synthetic_sqlite_is_read_only_and_reports_each_skip()
    {
        using var fixture = new DatabaseFixture();
        using (var db = fixture.Open())
        {
            using var command = db.CreateCommand();
            command.CommandText = "CREATE TABLE log(id INTEGER,created TEXT,type INTEGER,title TEXT,description TEXT,data BLOB); INSERT INTO log VALUES(1,'2026-10-26 10:30:00',0,'preview','preview',$data),(2,'invalid',0,'preview','preview',$data),(3,'2026-10-26 10:30:00',0,'preview','preview',X'00'),(4,'2026-10-26 10:30:00',0,'preview','preview',$unknown)";
            command.Parameters.AddWithValue("$data", Text("Полный импортируемый текст"));
            command.Parameters.AddWithValue("$unknown", Pack((1, "CF_TEXT", "unknown"u8.ToArray())));
            command.ExecuteNonQuery();
        }
        byte[] before = File.ReadAllBytes(fixture.Path);
        File.SetAttributes(fixture.Path, FileAttributes.ReadOnly);
        try
        {
            var results = ClipdiaryImport.ReadHistory(fixture.Path, TestContext.Current.CancellationToken).ToList();
            Assert.Equal(4, results.Count);
            Assert.Equal("Полный импортируемый текст", results[0].Clip!.Text);
            Assert.Equal(DateTimeKind.Local, results[0].Clip!.SeenLocal!.Value.Kind);
            Assert.Equal(ClipdiaryImportSkipReason.InvalidDate, results[1].SkipReason);
            Assert.Equal(ClipdiaryImportSkipReason.Malformed, results[2].SkipReason);
            Assert.Equal(ClipdiaryImportSkipReason.Unsupported, results[3].SkipReason);
            Assert.Equal(before, File.ReadAllBytes(fixture.Path));
            Assert.Single(Directory.GetFiles(fixture.Directory));
        }
        finally { File.SetAttributes(fixture.Path, FileAttributes.Normal); }
    }

    [Fact] public void Plain_and_composite_templates_preserve_data_without_inventing_dates()
    {
        using var fixture = new DatabaseFixture();
        using (var db = fixture.Open())
        {
            using var command = db.CreateCommand();
            command.CommandText = "CREATE TABLE common(id INTEGER,type INTEGER,name TEXT,data); INSERT INTO common VALUES(1,2,'Имя','Реквизиты'),(2,4,'Снимок',$data),(3,1,'Папка','не импортировать')";
            command.Parameters.AddWithValue("$data", Text("Composite")); command.ExecuteNonQuery();
        }
        var result = ClipdiaryImport.ReadTemplates(fixture.Path, TestContext.Current.CancellationToken).ToList();
        Assert.Equal("Реквизиты", result[0].Clip!.Text); Assert.Equal("Имя", result[0].Clip!.Name);
        Assert.All(result.Take(2), item => { Assert.True(item.Clip!.Template); Assert.Null(item.Clip.SeenLocal); });
        Assert.Equal("Composite", result[1].Clip!.Text);
        Assert.Equal(ClipdiaryImportSkipReason.Unsupported, result[2].SkipReason);
    }

    [Fact] public void Oversized_sqlite_cell_is_skipped_and_streaming_can_be_cancelled()
    {
        using var fixture = new DatabaseFixture();
        using (var db = fixture.Open())
        {
            using var command = db.CreateCommand();
            command.CommandText = "CREATE TABLE log(id INTEGER,created TEXT,type INTEGER,data BLOB); INSERT INTO log VALUES(1,'2026-10-26 10:30:00',0,zeroblob($size)),(2,'2026-10-26 10:30:00',0,$data)";
            command.Parameters.AddWithValue("$size", ClipdiaryImport.MaximumPayloadBytes + 1);
            command.Parameters.AddWithValue("$data", Text("Next row")); command.ExecuteNonQuery();
        }
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var rows = ClipdiaryImport.ReadHistory(fixture.Path, cancel.Token).GetEnumerator();
        Assert.True(rows.MoveNext()); Assert.Equal(ClipdiaryImportSkipReason.TooLarge, rows.Current.SkipReason);
        cancel.Cancel(); Assert.Throws<OperationCanceledException>(() => rows.MoveNext());
    }

    [Fact] public void Missing_source_is_never_created()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".db");
        Assert.Throws<SqliteException>(() => ClipdiaryImport.ReadHistory(path, TestContext.Current.CancellationToken).ToList()); Assert.False(File.Exists(path));
    }

    [Fact] public void Detection_finds_installed_application_even_when_custom_data_folder_is_unknown()
    {
        using var fixture = new DatabaseFixture();
        string programs = System.IO.Path.Combine(fixture.Directory, "programs"), profile = System.IO.Path.Combine(fixture.Directory, "profile");
        Assert.Null(ClipdiaryImport.DetectInstallation(programs, profile));
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(programs, "Clipdiary"));
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(profile, "Clipdiary"));
        File.WriteAllBytes(System.IO.Path.Combine(programs, "Clipdiary", "clipdiary.exe"), []);
        var custom=ClipdiaryImport.DetectInstallation(programs, profile);
        Assert.NotNull(custom);Assert.Null(custom.HistoryPath);Assert.Null(custom.TemplatesPath);
        string source = System.IO.Path.Combine(profile, "Clipdiary", "snippets.sdb"); File.WriteAllBytes(source, []);
        var detected = ClipdiaryImport.DetectInstallation(programs, profile);
        Assert.Equal(source, detected!.TemplatesPath); Assert.Null(detected.HistoryPath);
    }

    private sealed class DatabaseFixture : IDisposable
    {
        public string Directory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ClipShot-import-test-" + Guid.NewGuid());
        public string Path => System.IO.Path.Combine(Directory, "fixture.db");
        public DatabaseFixture() => System.IO.Directory.CreateDirectory(Directory);
        public SqliteConnection Open()
        { var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path, Pooling = false }.ToString()); db.Open(); return db; }
        public void Dispose() => System.IO.Directory.Delete(Directory, true);
    }
}
