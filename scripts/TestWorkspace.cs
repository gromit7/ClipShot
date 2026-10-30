#if NET10_0_OR_GREATER
#nullable disable
#endif
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace ClipShot.Testing
{
    // Один объект владеет только созданным им GUID-каталогом. Никакого удаления
    // соседних/старых запусков: остановленный процесс разбирается отдельно.
    public sealed class TestWorkspace : IDisposable
    {
        public string Root { get; private set; }
        private readonly string parent;
        private readonly string diagnostics;
        private FileStream lease;
        private bool disposed;

        public TestWorkspace(string diagnostics = null)
        {
            parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ClipShot.Tests"));
            Directory.CreateDirectory(parent);
            AssertAncestors(parent);
            Root = Path.Combine(parent, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            this.diagnostics = diagnostics;
            lease = new FileStream(Path.Combine(Root, ".test-lease"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
        }

        public static void AssertAncestors(string path)
        {
            for (string p = Path.GetFullPath(path); !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
                if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Test path contains a reparse point; retained.");
        }

        private List<string> Walk()
        {
            if (Path.GetDirectoryName(Path.GetFullPath(Root)) != parent) throw new IOException("Unsafe test root.");
            AssertAncestors(Root);
            var paths = new List<string>();
            var pending = new Stack<string>(); pending.Push(Root);
            while (pending.Count > 0)
            {
                foreach (string path in Directory.EnumerateFileSystemEntries(pending.Pop()))
                {
                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Test link retained for inspection.");
                    paths.Add(path);
                    if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
                }
            }
            return paths;
        }

        public static void DeleteKnownTree(string root, IEnumerable<string> knownPaths)
        {
            root = Path.GetFullPath(root);
            string allowed = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ClipShot.Tests"));
            if (Path.GetDirectoryName(root) != allowed) throw new IOException("Unsafe cleanup boundary.");
            AssertAncestors(root);
            var paths = knownPaths.Select(Path.GetFullPath).OrderByDescending(p => p.Length).ToArray();
            foreach (string path in paths)
                if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Cleanup entry escaped its owner.");
            // Только известные файлы; новый файл оставит непустой каталог и
            // приведёт к отказу, но не попадёт под рекурсивное удаление.
            foreach (string path in paths)
            {
                AssertAncestors(path);
                if (Directory.Exists(path)) Directory.Delete(path);
                else File.Delete(path);
            }
            Directory.Delete(root);
        }

        public void Dispose()
        {
            if (disposed) return;
            try
            {
                var paths = Walk();
                var files = paths.Where(File.Exists).Where(p => Path.GetFileName(p) != ".test-lease").ToArray();
                // Не стираем даже часть дерева, если в нём остался открытый файл.
                foreach (string file in files)
                    using (File.Open(file, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                if (diagnostics != null)
                {
                    string destination = Path.GetFullPath(Path.Combine(diagnostics, Path.GetFileName(Root)));
                    if (destination.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Diagnostics must be outside temporary test data.");
                    AssertAncestors(destination);
                    foreach (string file in files)
                    {
                        // Сохраняем отчёты и причины отказа, но не копии программы,
                        // базу или изображения. Тесты сами создают синтетические данные.
                        string ext = Path.GetExtension(file).ToLowerInvariant();
                        if (!new[] { ".txt", ".log", ".jsonl", ".nsi" }.Contains(ext)) continue;
                        if (new FileInfo(file).Length > 4 * 1024 * 1024) throw new IOException("Oversized diagnostic retained in test root.");
                        string copy = Path.Combine(destination, file.Substring(Root.Length + 1));
                        Directory.CreateDirectory(Path.GetDirectoryName(copy));
                        File.Copy(file, copy, true);
                        using (var a = File.OpenRead(file))
                        using (var b = File.OpenRead(copy))
                        using (var hash = SHA256.Create())
                            if (!hash.ComputeHash(a).SequenceEqual(hash.ComputeHash(b))) throw new IOException("Diagnostic copy failed verification.");
                    }
                }
                if (lease != null) { lease.Dispose(); lease = null; }
                DeleteKnownTree(Root, paths); disposed = true;
            }
            finally { if (lease != null) { lease.Dispose(); lease = null; } }
        }
        public void ReleaseLease() { if (lease != null) { lease.Dispose(); lease = null; } }
    }
}
