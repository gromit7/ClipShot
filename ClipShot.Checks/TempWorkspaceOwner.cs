namespace ClipShot;

// xUnit создаёт отдельный экземпляр класса на каждый тест и вызывает Dispose
// при его ошибке. Владение каталогами не зависит от успешного Assert.
public abstract class TempWorkspaceOwner : IDisposable
{
    private readonly List<Testing.TestWorkspace> workspaces = [];
    protected string NewTempRoot()
    {
        var workspace = new Testing.TestWorkspace();
        workspaces.Add(workspace);
        return workspace.Root;
    }
    protected void DeleteTempRoot(string root) => workspaces.Single(w => w.Root == root).Dispose();
    public void Dispose()
    {
        List<Exception> errors = [];
        foreach (var workspace in workspaces)
            try { workspace.Dispose(); } catch (Exception error) { errors.Add(error); }
        if (errors.Count != 0) throw new AggregateException("Test roots retained; cleanup failed.", errors);
    }
}
