using System.Data;
using System.Globalization;
using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Persistence.Database;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Catalog;

public sealed class FirebirdProductRepository : IProductRepository
{
    private readonly FirebirdUnitOfWork _unitOfWork;

    public FirebirdProductRepository(FirebirdUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Product?> GetByIdAsync(
        ProductId productId,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            """
            SELECT ID, NAME, SKU, CATEGORY_ID, BASE_UNIT_ID, SALE_PRICE_RIALS, STATUS
            FROM PRODUCT
            WHERE ID = @ID
            """);
        command.Parameters.Add("@ID", FbDbType.Char).Value = productId.ToString();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var id = ProductId.From(Guid.Parse(reader.GetString(0)));
        var name = reader.GetString(1);
        var sku = reader.IsDBNull(2) ? null : reader.GetString(2);
        var categoryId = CategoryId.From(Guid.Parse(reader.GetString(3)));
        var baseUnitId = UnitId.From(Guid.Parse(reader.GetString(4)));
        var salePrice = Money.FromRials(reader.GetInt64(5));
        var status = (ProductStatus)reader.GetInt16(6);
        await reader.DisposeAsync().ConfigureAwait(false);

        var barcodes = await ReadBarcodesAsync(productId, cancellationToken).ConfigureAwait(false);
        return Product.Rehydrate(
            id,
            name,
            sku,
            categoryId,
            baseUnitId,
            salePrice,
            status,
            barcodes);
    }

    public async Task<bool> BarcodeExistsAsync(
        string barcode,
        CancellationToken cancellationToken)
    {
        var normalizedBarcode = ProductBarcode.Create(barcode).Value;
        await using var command = CreateCommand(
            """
            SELECT COUNT(*)
            FROM PRODUCT_BARCODE
            WHERE UPPER(BARCODE) = @BARCODE
            """);
        command.Parameters.Add("@BARCODE", FbDbType.VarChar).Value = normalizedBarcode;

        var count = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(count, CultureInfo.InvariantCulture) > 0;
    }

    public async Task AddAsync(Product product, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(product);

        await using (var productCommand = CreateCommand(
            """
            INSERT INTO PRODUCT (
                ID, NAME, SKU, CATEGORY_ID, BASE_UNIT_ID, SALE_PRICE_RIALS, STATUS)
            VALUES (
                @ID, @NAME, @SKU, @CATEGORY_ID, @BASE_UNIT_ID, @SALE_PRICE_RIALS, @STATUS)
            """))
        {
            productCommand.Parameters.Add("@ID", FbDbType.Char).Value = product.Id.ToString();
            productCommand.Parameters.Add("@NAME", FbDbType.VarChar).Value = product.Name;
            productCommand.Parameters.Add("@SKU", FbDbType.VarChar).Value = product.Sku is { } sku
                ? sku
                : DBNull.Value;
            productCommand.Parameters.Add("@CATEGORY_ID", FbDbType.Char).Value = product.CategoryId.ToString();
            productCommand.Parameters.Add("@BASE_UNIT_ID", FbDbType.Char).Value = product.BaseUnitId.ToString();
            productCommand.Parameters.Add("@SALE_PRICE_RIALS", FbDbType.BigInt).Value = product.SalePrice.Rials;
            productCommand.Parameters.Add("@STATUS", FbDbType.SmallInt).Value = (short)product.Status;
            try
            {
                await productCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (FbException exception) when (
                FirebirdConstraint.IsViolation(exception, "UQ_PRODUCT_SKU"))
            {
                throw new DataConflictException(
                    "catalog.product.duplicate-sku",
                    "این کد کالا قبلاً استفاده شده است.",
                    exception);
            }
        }

        foreach (var barcode in product.Barcodes)
        {
            await using var barcodeCommand = CreateCommand(
                """
                INSERT INTO PRODUCT_BARCODE (PRODUCT_ID, BARCODE)
                VALUES (@PRODUCT_ID, @BARCODE)
                """);
            barcodeCommand.Parameters.Add("@PRODUCT_ID", FbDbType.Char).Value = product.Id.ToString();
            barcodeCommand.Parameters.Add("@BARCODE", FbDbType.VarChar).Value = barcode.Value;
            try
            {
                await barcodeCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (FbException exception) when (
                FirebirdConstraint.IsViolation(exception, "UQ_PRODUCT_BARCODE"))
            {
                throw new DataConflictException(
                    "catalog.product.duplicate-barcode",
                    "این بارکد قبلاً برای کالای دیگری ثبت شده است.",
                    exception);
            }
        }
    }

    private async Task<IReadOnlyList<string>> ReadBarcodesAsync(
        ProductId productId,
        CancellationToken cancellationToken)
    {
        var barcodes = new List<string>();
        await using var command = CreateCommand(
            """
            SELECT BARCODE
            FROM PRODUCT_BARCODE
            WHERE PRODUCT_ID = @PRODUCT_ID
            ORDER BY BARCODE
            """);
        command.Parameters.Add("@PRODUCT_ID", FbDbType.Char).Value = productId.ToString();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            barcodes.Add(reader.GetString(0));
        }

        return barcodes;
    }

    private FbCommand CreateCommand(string commandText)
    {
        return new FbCommand(commandText, _unitOfWork.Connection, _unitOfWork.Transaction)
        {
            CommandType = CommandType.Text,
        };
    }
}
