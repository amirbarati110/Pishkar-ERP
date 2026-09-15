namespace ERP.Presentation.Features.Sales;

/// <summary>A keyboard binding: a key name as Windows calls it (F2, N, Escape) and its modifiers.</summary>
public sealed record SalesShortcut(string Action, string Key, bool Control, string Label);

/// <summary>
/// Every key the sales screen answers to, in one place — source-of-truth §3.14:
/// shortcuts are not scattered over buttons, and each is shown on its button.
/// This is the seed of the Keyboard &amp; Shortcut Manager: when per-user
/// profiles arrive they replace this table, not the page.
///
/// Choices against §3.14's criteria:
/// <list type="bullet">
/// <item>F1 is left to «راهنمای صفحه», as §3.14 reserves it. The approved design
/// had put «نقدی» on F1 inside the payment window; payment methods use Alt+number
/// there instead.</item>
/// <item>Removing an invoice has no key at all and always asks first — it is
/// the destructive action §3.14 forbids a single key for.</item>
/// <item>F2/F3/F4/F5/F6 are as in the approved design.</item>
/// </list>
/// </summary>
public static class SalesShortcuts
{
    public static readonly SalesShortcut InvoiceList = new("invoice-list", "F2", false, "F2");
    public static readonly SalesShortcut FocusSearch = new("focus-search", "F3", false, "F3");
    public static readonly SalesShortcut CompleteAndPrint = new("complete-print", "F4", false, "F4");
    public static readonly SalesShortcut CompleteAndNext = new("complete-next", "F5", false, "F5");
    public static readonly SalesShortcut ReceivePayment = new("receive-payment", "F6", false, "F6");
    public static readonly SalesShortcut NewInvoice = new("new-invoice", "N", true, "Ctrl+N");
    public static readonly SalesShortcut CloseDialog = new("close", "Escape", false, "Esc");

    public static IReadOnlyList<SalesShortcut> All { get; } =
        [InvoiceList, FocusSearch, CompleteAndPrint, CompleteAndNext, ReceivePayment, NewInvoice, CloseDialog];
}
