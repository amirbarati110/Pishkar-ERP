using System.Data;
using System.Globalization;
using ERP.Application.Sales;
using ERP.Domain.Sales;
using ERP.Persistence.Database;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Sales;

/// <summary>
/// Draws from SEQ_SALE_NUMBER (migration V004). Running on the unit of work's
/// connection is safe: a Firebird sequence increment is not undone when the
/// surrounding transaction rolls back, which is exactly the "never reissue"
/// guarantee <see cref="ISaleNumberGenerator"/> promises.
/// </summary>
public sealed class FirebirdSaleNumberGenerator : ISaleNumberGenerator
{
    private readonly FirebirdUnitOfWork _unitOfWork;

    public FirebirdSaleNumberGenerator(FirebirdUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<SaleNumber> NextAsync(CancellationToken cancellationToken)
    {
        await using var command = new FbCommand(
            "SELECT NEXT VALUE FOR SEQ_SALE_NUMBER FROM RDB$DATABASE",
            _unitOfWork.Connection,
            _unitOfWork.Transaction)
        {
            CommandType = CommandType.Text,
        };

        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return SaleNumber.From(Convert.ToInt64(value, CultureInfo.InvariantCulture));
    }
}
