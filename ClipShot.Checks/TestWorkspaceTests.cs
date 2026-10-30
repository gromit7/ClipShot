using ClipShot.Testing;
using Xunit;
namespace ClipShot;

public class TestWorkspaceTests
{
    [Fact]
    public void UnknownNewFileAndForeignRootAreNeverDeleted()
    {
        using var workspace = new TestWorkspace();
        using var other = new TestWorkspace();
        string foreign = Path.Combine(other.Root, "keep.bin"); File.WriteAllBytes(foreign, [7]);
        Assert.Throws<IOException>(() => TestWorkspace.DeleteKnownTree(workspace.Root, [foreign]));
        Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(foreign));
        workspace.ReleaseLease();
        string[] known = Directory.GetFileSystemEntries(workspace.Root);
        string appeared = Path.Combine(workspace.Root, "appeared-after-inventory.txt"); File.WriteAllText(appeared, "retain");
        Assert.Throws<IOException>(() => TestWorkspace.DeleteKnownTree(workspace.Root, known));
        Assert.Equal("retain", File.ReadAllText(appeared));
    }

    [Fact]
    public void ExceptionStillCleansPayloadAndNestedBackups()
    {
        string root = "";
        Action failingTest = () =>
        {
            using var workspace = new TestWorkspace(); root = workspace.Root;
            string backup = Path.Combine(root, "App.ClipShot-backup-test", "nested");
            Directory.CreateDirectory(backup); File.WriteAllBytes(Path.Combine(backup, "synthetic.dll"), new byte[8192]);
            throw new InvalidOperationException("synthetic test failure");
        };
        Assert.Throws<InvalidOperationException>(failingTest);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task ParallelOwnersNeverRemoveAnotherOwnersData()
    {
        using var stillRunning = new TestWorkspace();
        string sentinel = Path.Combine(stillRunning.Root, "keep.txt"); File.WriteAllText(sentinel, "running");
        var roots = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            using var workspace = new TestWorkspace();
            File.WriteAllBytes(Path.Combine(workspace.Root, "data.bin"), new byte[128]);
            return workspace.Root;
        })));
        Assert.Equal(8, roots.Distinct().Count());
        Assert.All(roots, root => Assert.False(Directory.Exists(root)));
        Assert.Equal("running", File.ReadAllText(sentinel));
        Assert.Throws<IOException>(() => File.Open(Path.Combine(stillRunning.Root, ".test-lease"), FileMode.Open, FileAccess.ReadWrite, FileShare.None));
    }

    [Fact]
    public void OpenFileRefusesWholeCleanupAndRetrySucceeds()
    {
        var workspace = new TestWorkspace();
        string file = Path.Combine(workspace.Root, "busy.bin"); File.WriteAllBytes(file, [1, 2]);
        using (File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Throws<IOException>(() => workspace.Dispose());
        Assert.True(File.Exists(file));
        workspace.Dispose(); Assert.False(Directory.Exists(workspace.Root));
    }

    [Fact]
    public void UnsafeArchiveRetainsSource()
    {
        using var outside = new TestWorkspace();
        string blocked = Path.Combine(outside.Root, "file-instead-of-directory"); File.WriteAllText(blocked, "sentinel");
        var workspace = new TestWorkspace(blocked);
        File.WriteAllText(Path.Combine(workspace.Root, "test-error.txt"), "synthetic diagnostic");
        Assert.Throws<IOException>(() => workspace.Dispose());
        Assert.True(File.Exists(Path.Combine(workspace.Root, "test-error.txt")));
        // В этом сценарии архив намеренно недоступен. Удаление только собственной
        // фикстуры после проверки сохранности, без обращения к соседним данным.
        File.Delete(Path.Combine(workspace.Root, "test-error.txt"));
        File.Delete(Path.Combine(workspace.Root, ".test-lease")); Directory.Delete(workspace.Root);
        Assert.Equal("sentinel", File.ReadAllText(blocked));
    }
}
