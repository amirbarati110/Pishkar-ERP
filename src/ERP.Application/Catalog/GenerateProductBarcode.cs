using ERP.Application.Common;
using ERP.Domain.Catalog;

namespace ERP.Application.Catalog;

/// <summary>The next unused serial for the shop's own barcodes. Never hands the same number out twice.</summary>
public interface IInternalBarcodeSequence
{
    Task<long> NextAsync(CancellationToken cancellationToken);
}

public interface IGenerateProductBarcodeHandler
{
    /// <summary>A new EAN-13 in the shop's own range that no product uses yet.</summary>
    Task<Result<string>> ExecuteAsync(CancellationToken cancellationToken);
}

/// <summary>
/// «ساخت بارکد خودکار»: for a product that has no barcode the shop makes one (checklist «ن-۳»).
/// The sequence already guarantees a new number each time; the check against existing barcodes is
/// for the case where someone typed a code from our own range by hand, so a taken number is skipped
/// instead of failing the save later.
/// </summary>
public sealed class GenerateProductBarcodeHandler : IGenerateProductBarcodeHandler
{
    private const int MaximumAttempts = 20;

    private readonly IInternalBarcodeSequence _sequence;
    private readonly IProductRepository _products;

    public GenerateProductBarcodeHandler(IInternalBarcodeSequence sequence, IProductRepository products)
    {
        _sequence = sequence;
        _products = products;
    }

    public async Task<Result<string>> ExecuteAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            var serial = await _sequence.NextAsync(cancellationToken).ConfigureAwait(false);
            if (serial > InternalBarcode.MaximumSerial)
            {
                return Result.Failure<string>("catalog.barcode.exhausted", "شماره‌های بارکد داخلی تمام شده است؛ با پشتیبانی تماس بگیرید.");
            }

            var barcode = InternalBarcode.FromSerial(serial);
            if (!await _products.BarcodeExistsAsync(barcode, cancellationToken).ConfigureAwait(false))
            {
                return Result.Success(barcode);
            }
        }

        return Result.Failure<string>("catalog.barcode.busy", "ساخت بارکد انجام نشد؛ دوباره امتحان کنید.");
    }
}
