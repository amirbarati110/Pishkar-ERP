using System.Data;
using System.Globalization;
using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Domain.Catalog;
using ERP.Persistence.Database;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Catalog;

public sealed class FirebirdCategoryRepository : ICategoryRepository
{
    private const string RootParentKey = "00000000-0000-0000-0000-000000000000";
    private readonly FirebirdUnitOfWork _unitOfWork;

    public FirebirdCategoryRepository(FirebirdUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Category?> GetByIdAsync(
        CategoryId categoryId,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            """
            SELECT ID, NAME, PARENT_ID, SORT_ORDER, STATUS,
                   VIS_POS, VIS_ONLINE, VIS_PURCHASING
            FROM CATEGORY
            WHERE ID = @ID
            """);
        command.Parameters.Add("@ID", FbDbType.Char).Value = categoryId.ToString();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var parentId = reader.IsDBNull(2)
            ? (CategoryId?)null
            : CategoryId.From(Guid.Parse(reader.GetString(2)));

        return Category.Rehydrate(
            CategoryId.From(Guid.Parse(reader.GetString(0))),
            reader.GetString(1),
            parentId,
            reader.GetInt32(3),
            (CategoryStatus)reader.GetInt16(4),
            new CategoryVisibility(
                reader.GetBoolean(5),
                reader.GetBoolean(6),
                reader.GetBoolean(7)));
    }

    public async Task<bool> SiblingNameExistsAsync(
        string name,
        CategoryId? parentId,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            """
            SELECT COUNT(*)
            FROM CATEGORY
            WHERE PARENT_KEY = @PARENT_KEY
              AND UPPER(NAME) = UPPER(@NAME)
            """);
        command.Parameters.Add("@PARENT_KEY", FbDbType.Char).Value = GetParentKey(parentId);
        command.Parameters.Add("@NAME", FbDbType.VarChar).Value = name.Trim();

        var count = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(count, CultureInfo.InvariantCulture) > 0;
    }

    public async Task AddAsync(Category category, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(category);

        await using var command = CreateCommand(
            """
            INSERT INTO CATEGORY (
                ID, PARENT_ID, PARENT_KEY, NAME, SORT_ORDER, STATUS,
                VIS_POS, VIS_ONLINE, VIS_PURCHASING)
            VALUES (
                @ID, @PARENT_ID, @PARENT_KEY, @NAME, @SORT_ORDER, @STATUS,
                @VIS_POS, @VIS_ONLINE, @VIS_PURCHASING)
            """);
        command.Parameters.Add("@ID", FbDbType.Char).Value = category.Id.ToString();
        command.Parameters.Add("@PARENT_ID", FbDbType.Char).Value = category.ParentId is { } parentId
            ? parentId.ToString()
            : DBNull.Value;
        command.Parameters.Add("@PARENT_KEY", FbDbType.Char).Value = GetParentKey(category.ParentId);
        command.Parameters.Add("@NAME", FbDbType.VarChar).Value = category.Name;
        command.Parameters.Add("@SORT_ORDER", FbDbType.Integer).Value = category.SortOrder;
        command.Parameters.Add("@STATUS", FbDbType.SmallInt).Value = (short)category.Status;
        command.Parameters.Add("@VIS_POS", FbDbType.Boolean).Value = category.Visibility.IsVisibleInPos;
        command.Parameters.Add("@VIS_ONLINE", FbDbType.Boolean).Value = category.Visibility.IsVisibleOnline;
        command.Parameters.Add("@VIS_PURCHASING", FbDbType.Boolean).Value = category.Visibility.IsVisibleInPurchasing;

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (FbException exception) when (
            FirebirdConstraint.IsViolation(exception, "UQ_CATEGORY_LEVEL_NAME"))
        {
            throw new DataConflictException(
                "catalog.category.duplicate-name",
                "در این سطح، دسته‌بندی دیگری با همین نام وجود دارد.",
                exception);
        }
    }

    private static string GetParentKey(CategoryId? parentId)
    {
        return parentId?.ToString() ?? RootParentKey;
    }

    private FbCommand CreateCommand(string commandText)
    {
        return new FbCommand(commandText, _unitOfWork.Connection, _unitOfWork.Transaction)
        {
            CommandType = CommandType.Text,
        };
    }
}
