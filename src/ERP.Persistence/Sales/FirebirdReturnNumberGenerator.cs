using System.Data;
using System.Globalization;
using ERP.Application.Sales;
using ERP.Domain.Sales;
using ERP.Persistence.Database;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Sales;

/// <summary>Draws from SEQ_RETURN_NUMBER (migration V014) — same never-reissued guarantee as <see cref="FirebirdSaleNumberGenerator"/>.</summary>
public sealed class FirebirdReturnNumberGenerator : IReturnNumberGenerator
{
    private readonly FirebirdUnitOfWork _unitOfWork;

    public FirebirdReturnNumberGenerator(FirebirdUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<ReturnNumber> NextAsync(CancellationToken cancellationToken)
    {
        await using var command = new FbCommand(
            "SELECT NEXT VALUE FOR SEQ_RETURN_NUMBER FROM RDB$DATABASE",
            _unitOfWork.Connection,
            _unitOfWork.Transaction)
        {
            CommandType = CommandType.Text,
        };

        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return ReturnNumber.From(Convert.ToInt64(value, CultureInfo.InvariantCulture));
    }
}
