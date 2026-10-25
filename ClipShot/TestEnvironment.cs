namespace ClipShot;

public static class TestEnvironment
{
    private static readonly string[] TestFlags = ["--smoke-test", "--demo", "--capture-self-test", "--capture-demo", "--shell-test", "--interaction-self-test", "--upgrade-self-test"];
    public static bool IsIsolated(IEnumerable<string> args) => args.Any(TestFlags.Contains);
    public static string DataFolder(bool isolated, string? testRoot = null)
    {
        if (!isolated) return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipNest");
        if (string.IsNullOrEmpty(testRoot)) return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipNest.Smoke");
        string safeRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ClipShot.Tests")) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(testRoot);
        if (!path.StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase) || path.Length <= safeRoot.Length)
            throw new ArgumentException("Test data must be in a dedicated child of TEMP/ClipShot.Tests.", nameof(testRoot));
        return path;
    }
    public static string MessageName(bool isolated, string action) => $"ClipNest.{(isolated ? "Test." : "")}{action}.v1";
}
