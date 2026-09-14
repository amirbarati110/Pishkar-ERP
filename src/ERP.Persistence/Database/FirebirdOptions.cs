using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Database;

public sealed record FirebirdOptions(
    string DatabasePath,
    string ClientLibraryPath,
    string UserName,
    string Password)
{
    public string BuildConnectionString()
    {
        if (!Path.IsPathFullyQualified(DatabasePath))
        {
            throw new ArgumentException("مسیر دیتابیس باید کامل باشد.", nameof(DatabasePath));
        }

        if (!File.Exists(ClientLibraryPath))
        {
            throw new FileNotFoundException("کتابخانه Firebird پیدا نشد.", ClientLibraryPath);
        }

        var builder = new FbConnectionStringBuilder
        {
            Database = DatabasePath,
            UserID = UserName,
            Password = Password,
            ServerType = FbServerType.Embedded,
            ClientLibrary = ClientLibraryPath,
            Charset = "UTF8",
            Dialect = 3,
            Pooling = false,
        };

        return builder.ConnectionString;
    }
}

