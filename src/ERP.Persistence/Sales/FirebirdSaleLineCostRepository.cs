using System.Data;
using ERP.Application.Sales;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Sales;
using ERP.Persistence.Database;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Sales;

public sealed class FirebirdSaleLineCostRepository : ISaleLineCostRepository
{
    private readonly FirebirdUnitOfWork _unitOfWork;

    public FirebirdSaleLineCostRepository(FirebirdUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task RecordAsync(
        SaleId saleId,
        IReadOnlyCollection<SaleLineCost> costs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(costs);

        foreach (var cost in costs)
        {
            await using var command = CreateCommand(
                """
                UPDATE OR INSERT INTO SALE_LINE_COST (SALE_ID, PRODUCT_ID, COST_RIALS, COSTED_QTY)
                VALUES (@SALE_ID, @PRODUCT_ID, @COST_RIALS, @COSTED_QTY)
                MATCHING (SALE_ID, PRODUCT_ID)
                """);
            command.Parameters.Add("@SALE_ID", FbDbType.Char).Value = saleId.ToString();
            command.Parameters.Add("@PRODUCT_ID", FbDbType.Char).Value = cost.ProductId.ToString();
            command.Parameters.Add("@COST_RIALS", FbDbType.BigInt).Value = cost.TotalCost.Rials;
            command.Parameters.Add("@COSTED_QTY", FbDbType.Decimal).Value = cost.CostedQuantity;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyDictionary<ProductId, Money>> GetUnitCostsAsync(
        SaleId saleId,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<ProductId, Money>();
        await using var command = CreateCommand(
            "SELECT PRODUCT_ID, COST_RIALS, COSTED_QTY FROM SALE_LINE_COST WHERE SALE_ID = @SALE_ID");
        command.Parameters.Add("@SALE_ID", FbDbType.Char).Value = saleId.ToString();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var totalCost = reader.GetInt64(1);
            var costedQuantity = reader.GetDecimal(2);
            result[ProductId.From(Guid.Parse(reader.GetString(0)))] =
                Money.FromRials(totalCost).Multiply(1m / costedQuantity);
        }

        return result;
    }

    private FbCommand CreateCommand(string commandText)
    {
        return new FbCommand(commandText, _unitOfWork.Connection, _unitOfWork.Transaction)
        {
            CommandType = CommandType.Text,
        };
    }
}
