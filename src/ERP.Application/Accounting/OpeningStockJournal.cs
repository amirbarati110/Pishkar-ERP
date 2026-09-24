using ERP.Domain.Inventory;

namespace ERP.Application.Accounting;

/// <summary>Posts the opening entry for one opening-stock layer — shared by the opening-stock screen and the import, so both book it the same way.</summary>
internal static class OpeningStockJournal
{
    public static async Task PostAsync(
        IJournalEntryRepository journal,
        InventoryLayer layer,
        DateTimeOffset postedAtUtc,
        CancellationToken cancellationToken)
    {
        var value = layer.UnitCost.Multiply(layer.OriginalQuantity.Value);
        if (OperationalJournalEntryFactory.OpeningStock(layer.Id.ToString(), value, postedAtUtc) is { } entry)
        {
            await journal.SaveAsync(entry, cancellationToken).ConfigureAwait(false);
        }
    }
}
