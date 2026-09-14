using FirebirdSql.Data.FirebirdClient;
using ERP.Persistence.Database;

namespace ERP.Persistence.Tests.TestSupport;

internal sealed class FirebirdTestDatabase : IAsyncDisposable
{
    private readonly string _directoryPath;

    private FirebirdTestDatabase(string directoryPath, FirebirdOptions options)
    {
        _directoryPath = directoryPath;
        Options = options;
    }

    public FirebirdOptions Options { get; }

    public static FirebirdTestDatabase Create()
    {
        var repositoryRoot = FindRepositoryRoot();
        var physicalDataRoot = Path.Combine(repositoryRoot, "data");
        var directoryPath = Path.Combine(
            physicalDataRoot,
            "test-databases",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directoryPath);

        var aliasedDirectoryPath = Path.Combine(
            Path.GetPathRoot(repositoryRoot)!,
            "RetailERPData",
            Path.GetRelativePath(physicalDataRoot, directoryPath));

        var options = new FirebirdOptions(
            Path.Combine(aliasedDirectoryPath, "retail-erp-tests.fdb"),
            Path.Combine(Path.GetPathRoot(repositoryRoot)!, "RetailERPTools", "fbclient.dll"),
            "SYSDBA",
            "masterkey");
        FbConnection.CreateDatabase(options.BuildConnectionString(), 16_384, true, false);

        return new FirebirdTestDatabase(directoryPath, options);
    }

    public async ValueTask DisposeAsync()
    {
        FbConnection.ClearAllPools();
        await Task.Yield();
        Directory.Delete(_directoryPath, true);
        GC.SuppressFinalize(this);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "RetailERP.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("ریشه پروژه RetailERP پیدا نشد.");
    }
}
