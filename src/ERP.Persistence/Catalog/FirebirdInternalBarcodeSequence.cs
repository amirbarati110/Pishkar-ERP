using System.Data;
using System.Globalization;
using ERP.Application.Catalog;
using ERP.Persistence.Database;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Catalog;

public sealed class FirebirdInternalBarcodeSequence : IInternalBarcodeSequence
{
    private readonly FirebirdUnitOfWork _unitOfWork;

    public FirebirdInternalBarcodeSequence(FirebirdUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<long> NextAsync(CancellationToken cancellationToken)
    {
        await using var command = new FbCommand(
            "SELECT NEXT VALUE FOR SEQ_INTERNAL_BARCODE FROM RDB$DATABASE",
            _unitOfWork.Connection,
            _unitOfWork.Transaction)
        {
            CommandType = CommandType.Text,
        };

        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }
}
