using Microsoft.Data.Sqlite;
using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Drawing;
using System.Drawing.Imaging;

namespace ClipShot;

public sealed record ClipdiaryImportedClip(string Kind, string Text, byte[]? Image, bool Template, string Name, DateTime? SeenLocal);
public enum ClipdiaryImportSkipReason { InvalidDate, Malformed, Unsupported, TooLarge }
public sealed record ClipdiaryImportResult(long SourceId, ClipdiaryImportedClip? Clip, ClipdiaryImportSkipReason? SkipReason);
public sealed record ClipdiaryInstallation(string Folder, string? HistoryPath, string? TemplatesPath);

/// <summary>
/// Reads only full source payloads, never title/description previews. The composite format
/// was recovered statically from Clipdiary 5.7; tests use synthetic fixtures, not an
/// independently captured Clipdiary runtime sample. Unknown formats are skipped explicitly.
/// </summary>
public static class ClipdiaryImport
{
    public const int MaximumPayloadBytes = 64 * 1024 * 1024;
    private const int MaximumPixels = 16 * 1024 * 1024;
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
    private static readonly Encoding StrictUnicode = new UnicodeEncoding(false, false, true);
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static ClipdiaryInstallation? DetectInstallation() => DetectInstallation(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    // Injectable roots keep discovery tests entirely away from personal profiles.
    public static ClipdiaryInstallation? DetectInstallation(string programFilesX86, string applicationData)
    {
        string folder = Path.Combine(programFilesX86, "Clipdiary");
        if (!File.Exists(Path.Combine(folder, "clipdiary.exe"))) return null;
        string history = Path.Combine(applicationData, "Clipdiary", "diary.db");
        string templates = Path.Combine(applicationData, "Clipdiary", "snippets.sdb");
        return new(folder, File.Exists(history) ? history : null, File.Exists(templates) ? templates : null);
    }

    public static IEnumerable<ClipdiaryImportResult> ReadHistory(string path, CancellationToken cancellationToken = default)
        => Read(path, false, cancellationToken);
    public static IEnumerable<ClipdiaryImportResult> ReadTemplates(string path, CancellationToken cancellationToken = default)
        => Read(path, true, cancellationToken);

    private static IEnumerable<ClipdiaryImportResult> Read(string path, bool templates, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = Path.GetFullPath(path), Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 5 }.ToString());
        db.Open();
        using (var safety = db.CreateCommand())
        {
            safety.CommandText = "PRAGMA query_only=ON; PRAGMA trusted_schema=OFF;";
            safety.ExecuteNonQuery();
        }
        using var command = db.CreateCommand();
        using var cancellation = token.Register(command.Cancel);
        // CASE prevents materialising an oversized SQLite cell into managed memory.
        command.CommandText = templates
            ? "SELECT id,NULL,type,name,typeof(data),length(CAST(data AS BLOB)),CASE WHEN length(CAST(data AS BLOB))<=$limit THEN data END FROM common ORDER BY id"
            : "SELECT id,created,type,'',typeof(data),length(CAST(data AS BLOB)),CASE WHEN length(CAST(data AS BLOB))<=$limit THEN data END FROM log ORDER BY id";
        command.Parameters.AddWithValue("$limit", MaximumPayloadBytes);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested();
            long id = reader.GetInt64(0);
            DateTime? seen = null;
            if (!templates)
            {
                if (reader.IsDBNull(1) || !DateTime.TryParseExact(reader.GetString(1),
                    ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss.FFFFFFF"], CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed))
                { yield return new(id, null, ClipdiaryImportSkipReason.InvalidDate); continue; }
                seen = DateTime.SpecifyKind(parsed, DateTimeKind.Local);
            }
            if (!reader.IsDBNull(5) && reader.GetInt64(5) > MaximumPayloadBytes)
            { yield return new(id, null, ClipdiaryImportSkipReason.TooLarge); continue; }
            ClipdiaryImportedClip? clip = null;
            ClipdiaryImportSkipReason? skip = null;
            try
            {
                int type = reader.GetInt32(2);
                string storage = reader.GetString(4);
                if (templates && type == 2 && storage == "text")
                {
                    string text = reader.GetString(6);
                    if (text.Length != 0) clip = new("text", text, null, true, reader.GetString(3), null);
                }
                else if ((!templates || type == 4) && storage == "blob")
                    clip = DecodeComposite((byte[])reader.GetValue(6), token) is { } decoded
                        ? decoded with { Template = templates, Name = templates ? reader.GetString(3) : "", SeenLocal = seen } : null;
                skip = clip is null ? ClipdiaryImportSkipReason.Unsupported : null;
            }
            catch (PayloadTooLargeException) { skip = ClipdiaryImportSkipReason.TooLarge; }
            catch (Exception ex) when (ex is InvalidDataException or DecoderFallbackException or ArgumentException or OverflowException)
            { skip = ClipdiaryImportSkipReason.Malformed; }
            yield return new(id, clip, skip);
        }
    }

    public static ClipdiaryImportedClip? DecodeComposite(byte[] payload, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (payload.Length > MaximumPayloadBytes) throw new PayloadTooLargeException();
        using var input = new MemoryStream(payload, false);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var expanded = new MemoryStream();
        byte[] buffer = new byte[81920];
        int read;
        while ((read = gzip.Read(buffer)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (expanded.Length + read > MaximumPayloadBytes) throw new PayloadTooLargeException();
            expanded.Write(buffer, 0, read);
        }
        byte[] raw = expanded.ToArray();
        int offset = 0;
        ushort version = Read16(raw, ref offset), count = Read16(raw, ref offset);
        if (version != 1) return null;
        if (count == 0 || count > 4096) throw new InvalidDataException("Invalid format count.");
        string? text = null;
        byte[]? image = null;
        for (int index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            uint format = Read32(raw, ref offset), size = Read32(raw, ref offset), nameSize = Read32(raw, ref offset);
            if (nameSize > 4096) throw new InvalidDataException("Invalid format name length.");
            string name = StrictUtf8.GetString(Take(raw, ref offset, nameSize));
            var data = Take(raw, ref offset, size);
            if (format == 13)
            {
                if ((data.Length & 1) != 0) throw new InvalidDataException("Invalid Unicode payload.");
                string candidate = StrictUnicode.GetString(data);
                int nul = candidate.IndexOf('\0');
                text = nul >= 0 ? candidate[..nul] : candidate;
            }
            else if (image is null && (name.Equals("PNG", StringComparison.OrdinalIgnoreCase)
                || name.Equals("image/png", StringComparison.OrdinalIgnoreCase) || data.StartsWith(PngSignature)))
                image = NormalizePng(data);
            else if (image is null && format is 8 or 17) image = ConvertDib(data);
        }
        if (offset != raw.Length) throw new InvalidDataException("Unexpected trailing composite data.");
        // Match ordinary clipboard capture: screenshot plus an alternative text
        // representation remains an image, rather than silently losing its pixels.
        return image is not null ? new("image", "", image, false, "", null)
            : text is { Length: > 0 } ? new("text", text, null, false, "", null) : null;
    }

    private static ReadOnlySpan<byte> Take(byte[] bytes, ref int offset, uint length)
    {
        if (length > (uint)(bytes.Length - offset)) throw new InvalidDataException("Truncated composite.");
        var result = bytes.AsSpan(offset, (int)length); offset += (int)length; return result;
    }
    private static ushort Read16(byte[] bytes, ref int offset) => BinaryPrimitives.ReadUInt16LittleEndian(Take(bytes, ref offset, 2));
    private static uint Read32(byte[] bytes, ref int offset) => BinaryPrimitives.ReadUInt32LittleEndian(Take(bytes, ref offset, 4));

    private static byte[] NormalizePng(ReadOnlySpan<byte> data)
    {
        if (data.Length < 33 || !data.StartsWith(PngSignature) || !data.Slice(12, 4).SequenceEqual("IHDR"u8))
            throw new InvalidDataException("Invalid PNG header.");
        ValidateDimensions(BinaryPrimitives.ReadInt32BigEndian(data.Slice(16, 4)), BinaryPrimitives.ReadInt32BigEndian(data.Slice(20, 4)));
        return NormalizeBitmap(data.ToArray());
    }
    private static byte[] ConvertDib(ReadOnlySpan<byte> data)
    {
        if (data.Length < 40) throw new InvalidDataException("Truncated DIB.");
        uint header = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (header is not (40 or 52 or 56 or 108 or 124) || header > data.Length) throw new InvalidDataException("Unknown DIB header.");
        int width = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(4)), height = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(8));
        if (height == int.MinValue) throw new InvalidDataException("Invalid DIB height.");
        ValidateDimensions(width, Math.Abs(height));
        int bits = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(14));
        uint compression = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(16));
        if (BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(12)) != 1 || bits is not (1 or 4 or 8 or 16 or 24 or 32)
            || compression is not (0 or 3)) throw new InvalidDataException("Unsupported DIB encoding.");
        uint colors = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(32));
        if (colors == 0 && bits <= 8) colors = 1u << bits;
        long pixels = header + (compression == 3 && header == 40 ? 12 : 0) + colors * 4L;
        long stride = ((width * (long)bits + 31) / 32) * 4;
        if (pixels > data.Length || stride * Math.Abs(height) > data.Length - pixels) throw new InvalidDataException("Truncated DIB pixels.");
        byte[] bmp = new byte[checked(data.Length + 14)];
        bmp[0] = (byte)'B'; bmp[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(2), bmp.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(10), checked((int)pixels + 14));
        data.CopyTo(bmp.AsSpan(14));
        return NormalizeBitmap(bmp);
    }
    private static void ValidateDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0 || (long)width * height > MaximumPixels) throw new InvalidDataException("Invalid image dimensions.");
    }
    private static byte[] NormalizeBitmap(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, false);
            using var bitmap = new Bitmap(stream);
            using var output = new MemoryStream(); bitmap.Save(output, ImageFormat.Png);
            return output.ToArray();
        }
        catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.ExternalException)
        { throw new InvalidDataException("Invalid image payload.", ex); }
    }
    private sealed class PayloadTooLargeException : IOException { }
}
