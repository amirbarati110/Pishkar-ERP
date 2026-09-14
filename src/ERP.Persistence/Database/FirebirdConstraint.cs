using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Database;

internal static class FirebirdConstraint
{
    public static bool IsViolation(FbException exception, string constraintName)
    {
        return exception.ToString().Contains(constraintName, StringComparison.OrdinalIgnoreCase);
    }
}
