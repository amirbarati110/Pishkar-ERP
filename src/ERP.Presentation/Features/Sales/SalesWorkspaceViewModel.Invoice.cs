using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERP.Application.Sales;
using ERP.Domain.Common;
using ERP.Domain.Sales;

namespace ERP.Presentation.Features.Sales;

/// <summary>What happens after «دریافت وجه»: the invoice can be completed and followed by a summary, or by a fresh invoice straight away.</summary>
public enum CompletionFollowUp
{
    /// <summary>«دریافت وجه» (F6) — show the completed invoice (§6.21/§6.22) before starting the next.</summary>
    ShowSummary = 1,

    /// <summary>«ثبت و بعدی» (F5) — a short notice and the next invoice at once (§3.14 Fast-entry).</summary>
    NextInvoice = 2,
}

/// <summary>Row editing, payment, completion, removal and the invoice list.</summary>
public sealed partial class SalesWorkspaceViewModel
{
    private CartLineRow? _editingRow;
    private bool _syncingEditDiscount;

    public IReadOnlyList<PaymentMethod> PaymentMethods { get; } =
        [PaymentMethod.Cash, PaymentMethod.Card, PaymentMethod.Credit, PaymentMethod.Cheque];

    public ObservableCollection<InvoiceListRow> HeldInvoices { get; } = [];

    public ObservableCollection<InvoiceListRow> TodayInvoices { get; } = [];

    // ───── edit row (§6.10 / §6.11) ─────

    [ObservableProperty]
    public partial bool IsEditLineOpen { get; set; }

    [ObservableProperty]
    public partial string EditProductName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditQuantityText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditUnitPriceText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditDiscountText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditDiscountPercentText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool EditUpdateCatalogPrice { get; set; }

    [ObservableProperty]
    public partial string? EditError { get; set; }

    // ───── the numbers next to the price field (§6.10) ─────

    [ObservableProperty]
    public partial string EditStockText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditLastPurchaseText { get; set; } = "—";

    [ObservableProperty]
    public partial string EditCurrentCostText { get; set; } = "—";

    [ObservableProperty]
    public partial string EditLastPriceToCustomerText { get; set; } = "—";

    /// <summary>«حاشیه سود» (§5.14 Gross Margin) — (قیمت − بها) ÷ قیمت.</summary>
    [ObservableProperty]
    public partial string EditMarginText { get; set; } = "—";

    /// <summary>«درصد افزایش نسبت به بهای تمام‌شده» (§5.14 Markup) — (قیمت − بها) ÷ بها.</summary>
    [ObservableProperty]
    public partial string EditMarkupText { get; set; } = "—";

    /// <summary>§5.17 Below-cost Detection: non-empty once the typed price is under <see cref="_editCurrentCost"/>.</summary>
    [ObservableProperty]
    public partial string? EditBelowCostWarning { get; set; }

    public bool HasEditBelowCostWarning => EditBelowCostWarning is not null;

    partial void OnEditBelowCostWarningChanged(string? value) => OnPropertyChanged(nameof(HasEditBelowCostWarning));

    /// <summary>
    /// FIFO's next-to-consume unit cost for the product being edited — what
    /// actually gets charged against this sale — kept aside so margin/markup
    /// and the below-cost warning can recompute as the cashier types a new
    /// price, without another round trip per keystroke.
    /// </summary>
    private Money? _editCurrentCost;

    [RelayCommand]
    private Task OpenEditLineAsync(CartLineRow? row) => row is null ? Task.CompletedTask : RunAsync(async () =>
    {
        var tab = RequireTab();

        _editingRow = row;
        _editCurrentCost = null; // cleared until the real cost for this product arrives below
        EditProductName = row.Name;
        _syncingEditDiscount = true;
        EditQuantityText = SalesText.Quantity(row.Line.Quantity);
        EditUnitPriceText = SalesText.Tomans(row.Line.UnitPrice);
        EditDiscountText = SalesText.Tomans(row.Line.Discount);
        var gross = row.Line.UnitPrice.Multiply(row.Line.Quantity);
        EditDiscountPercentText = gross.Rials == 0 ? "۰" : SalesText.Percent(row.Line.Discount.Rials * 100m / gross.Rials);
        _syncingEditDiscount = false;
        EditUpdateCatalogPrice = false;
        EditError = null;
        EditStockText = SalesText.Quantity(row.Line.Available.Value);
        EditLastPurchaseText = EditCurrentCostText = EditLastPriceToCustomerText = EditMarginText = EditMarkupText = "—";
        EditBelowCostWarning = null;
        IsEditLineOpen = true;

        var info = await _backend.LineEditInfo.ExecuteAsync(
            new GetLineEditInfoQuery(_warehouseId, row.ProductId, tab.CustomerId),
            CancellationToken.None);

        // The cashier may have already closed the window, or opened a
        // different row, before this reply arrives — do not overwrite it.
        if (_editingRow != row)
        {
            return;
        }

        _editCurrentCost = info.CurrentCost;
        EditLastPurchaseText = info.LastPurchaseCost is { } lastPurchase ? SalesText.Tomans(lastPurchase) : "—";
        EditCurrentCostText = info.CurrentCost is { } currentCost ? SalesText.Tomans(currentCost) : "—";
        EditLastPriceToCustomerText = tab.CustomerId is null
            ? "مشتری نقدی"
            : info.LastSalePriceToCustomer is { } lastPrice ? SalesText.Tomans(lastPrice) : "بدون سابقه";
        UpdateMarginPreview();
    });

    partial void OnEditUnitPriceTextChanged(string value) => UpdateMarginPreview();

    /// <summary>Recomputes «حاشیه سود» / «درصد افزایش نسبت به بهای تمام‌شده» / the below-cost warning from the price box the cashier is typing into.</summary>
    private void UpdateMarginPreview()
    {
        if (_editCurrentCost is not { } cost || SalesText.ParseTomansToRials(EditUnitPriceText) is not { } priceRials)
        {
            EditMarginText = EditMarkupText = "—";
            EditBelowCostWarning = null;
            return;
        }

        var costRials = cost.Rials;
        EditMarginText = priceRials > 0 ? SalesText.Percent((priceRials - costRials) * 100m / priceRials) : "—";
        EditMarkupText = costRials > 0 ? SalesText.Percent((priceRials - costRials) * 100m / costRials) : "—";

        // §5.17 Below-cost Detection — Warning policy (Manager Approval/Block
        // need the permission system of checklist ب#11, not built yet).
        EditBelowCostWarning = priceRials < costRials
            ? $"این قیمت زیر بهای تمام‌شده است (بهای تمام‌شده: {SalesText.Tomans(cost)} تومان)."
            : null;
    }

    partial void OnEditDiscountPercentTextChanged(string value)
    {
        if (_syncingEditDiscount || EditGross() is not { } gross || SalesText.ParseDecimal(value) is not { } percent)
        {
            return;
        }

        _syncingEditDiscount = true;
        EditDiscountText = SalesText.Tomans(gross.Multiply(Math.Min(percent, 100m) / 100m));
        _syncingEditDiscount = false;
    }

    partial void OnEditDiscountTextChanged(string value)
    {
        if (_syncingEditDiscount || EditGross() is not { } gross || SalesText.ParseTomansToRials(value) is not { } rials)
        {
            return;
        }

        _syncingEditDiscount = true;
        EditDiscountPercentText = gross.Rials == 0 ? "۰" : SalesText.Percent(rials * 100m / gross.Rials);
        _syncingEditDiscount = false;
    }

    [RelayCommand]
    private void CancelEditLine() => IsEditLineOpen = false;

    [RelayCommand]
    private Task SaveEditLineAsync() => RunAsync(async () =>
    {
        var tab = RequireTab();
        if (_editingRow is null)
        {
            return;
        }

        var quantity = SalesText.ParseDecimal(EditQuantityText);
        var price = SalesText.ParseTomansToRials(EditUnitPriceText);
        var discount = SalesText.ParseTomansToRials(EditDiscountText);
        if (quantity is not > 0 || price is null || discount is null)
        {
            EditError = "مقدار باید بیشتر از صفر باشد و قیمت و تخفیف فقط عدد.";
            return;
        }

        var result = await _backend.ChangeLine.ExecuteAsync(
            new ChangeSaleLineCommand(tab.SaleId, _editingRow.ProductId, quantity.Value, price.Value, discount.Value, EditUpdateCatalogPrice),
            CancellationToken.None);
        if (!result.IsSuccess)
        {
            EditError = result.Error?.Message ?? "تغییر ثبت نشد.";
            return;
        }

        IsEditLineOpen = false;
        await RefreshInvoiceAsync(tab, CancellationToken.None);
        if (EditUpdateCatalogPrice)
        {
            ShowNotice("قیمت اصلی کالا هم به‌روز شد.");
            await LoadProductsAsync(CancellationToken.None);
        }
    });

    private Money? EditGross()
    {
        var quantity = SalesText.ParseDecimal(EditQuantityText);
        var price = SalesText.ParseTomansToRials(EditUnitPriceText);
        return quantity is > 0 && price is { } rials ? Money.FromRials(rials).Multiply(quantity.Value) : null;
    }

    // ───── payment and completion ─────

    [ObservableProperty]
    public partial bool IsPaymentOpen { get; set; }

    [ObservableProperty]
    public partial PaymentMethod SelectedPaymentMethod { get; set; } = PaymentMethod.Cash;

    [ObservableProperty]
    public partial string PaymentDueText { get; set; } = "۰";

    [ObservableProperty]
    public partial string CashReceivedText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CashChangeText { get; set; } = "۰";

    [ObservableProperty]
    public partial bool AllowNegativeStock { get; set; }

    [ObservableProperty]
    public partial string? PaymentError { get; set; }

    /// <summary>Set when completion was refused because a نسیه sale passes the credit limit (§6.7); the window then offers approval.</summary>
    [ObservableProperty]
    public partial bool NeedsCreditApproval { get; set; }

    [ObservableProperty]
    public partial bool IsCompletedSummaryOpen { get; set; }

    [ObservableProperty]
    public partial string CompletedNumberText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CompletedTotalText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CompletedMethodText { get; set; } = string.Empty;

    /// <summary>§6.21 «مشتری» in the confirmation summary — «مشتری نقدی» included, same as everywhere else on screen.</summary>
    [ObservableProperty]
    public partial string CompletedCustomerText { get; set; } = string.Empty;

    /// <summary>§6.21 «اقلام» — every line, name × quantity, so the cashier can double check what was actually sold before waving the customer off.</summary>
    [ObservableProperty]
    public partial string CompletedItemsText { get; set; } = string.Empty;

    /// <summary>§6.21 «تخفیف» — «بدون تخفیف» rather than «۰ تومان» when there was none, so the common case reads as a plain statement, not a zero to parse.</summary>
    [ObservableProperty]
    public partial string CompletedDiscountText { get; set; } = string.Empty;

    public CompletionFollowUp PendingFollowUp { get; private set; } = CompletionFollowUp.ShowSummary;

    public bool IsCashSelected => SelectedPaymentMethod == PaymentMethod.Cash;

    partial void OnSelectedPaymentMethodChanged(PaymentMethod value)
    {
        OnPropertyChanged(nameof(IsCashSelected));
        NeedsCreditApproval = false;
        PaymentError = null;
    }

    partial void OnCashReceivedTextChanged(string value)
    {
        var due = CurrentTab?.Payable.Rials ?? 0;
        CashChangeText = SalesText.ParseTomansToRials(value) is { } received && received > due
            ? SalesText.Tomans(received - due)
            : "۰";
    }

    [RelayCommand]
    private void OpenPayment(CompletionFollowUp followUp)
    {
        var tab = CurrentTab;
        if (tab is null || tab.IsEmpty || IsBusy)
        {
            ShowNotice("اول کالایی به فاکتور اضافه کنید.", isError: true);
            return;
        }

        PendingFollowUp = followUp;
        SelectedPaymentMethod = PaymentMethod.Cash;
        PaymentDueText = tab.TotalText;
        CashReceivedText = tab.TotalText;
        AllowNegativeStock = false;
        NeedsCreditApproval = false;
        PaymentError = null;
        IsPaymentOpen = true;
    }

    [RelayCommand]
    private void SelectPaymentMethod(PaymentMethod method) => SelectedPaymentMethod = method;

    [RelayCommand]
    private void CancelPayment() => IsPaymentOpen = false;

    [RelayCommand]
    private Task ConfirmPaymentAsync() => CompleteCoreAsync(approveCreditOverLimit: false);

    /// <summary>Completes after the over-limit warning was shown and confirmed.</summary>
    [RelayCommand]
    private Task ApproveCreditAndCompleteAsync() => CompleteCoreAsync(approveCreditOverLimit: true);

    [RelayCommand]
    private Task StartNextAfterSummaryAsync() => RunAsync(async () =>
    {
        IsCompletedSummaryOpen = false;
        await ResetCurrentTabAsync();
    });

    private Task CompleteCoreAsync(bool approveCreditOverLimit) => RunAsync(async () =>
    {
        var tab = RequireTab();
        if (IsCashSelected
            && SalesText.ParseTomansToRials(CashReceivedText) is { } received
            && received > 0
            && received < tab.Payable.Rials - 9)
        {
            // Cash short of the total is not a partial payment — split payment
            // (§6.16) does not exist yet — so it would silently record a debt.
            PaymentError = "مبلغ دریافتی نقدی کمتر از مبلغ فاکتور است. کل مبلغ را بگیرید یا روش «نسیه» را انتخاب کنید.";
            return;
        }

        var result = await _backend.Complete.ExecuteAsync(
            new CompleteSaleCommand(
                tab.SaleId,
                SelectedPaymentMethod,
                DiscountRials: 0,
                tab.TaxRatePercent,
                AllowNegativeStock,
                approveCreditOverLimit),
            CancellationToken.None);

        if (!result.IsSuccess || result.Value is null)
        {
            NeedsCreditApproval = result.Error?.Code == "sales.credit.requires-approval";
            PaymentError = result.Error?.Code == "sales.sale.insufficient-stock"
                ? $"{result.Error.Message}\nاگر کالا واقعاً در فروشگاه هست و فقط هنوز در سیستم ثبت نشده، «اجازه فروش با کسری موجودی» را بزنید."
                : result.Error?.Message ?? "فاکتور ثبت نشد.";
            return;
        }

        var completed = result.Value;
        IsPaymentOpen = false;
        CompletedNumberText = completed.Number.ToPersianString();
        CompletedTotalText = SalesText.Tomans(completed.Totals.Total);
        CompletedMethodText = SalesText.PaymentMethodName(SelectedPaymentMethod);
        // Read from the tab's own lines before it is reset below — CompletedSale
        // itself does not carry them, only the number and the fixed totals.
        CompletedCustomerText = tab.CustomerName;
        CompletedItemsText = string.Join(" · ", tab.Lines.Select(line => $"{line.Name} ×{line.QuantityText}"));
        CompletedDiscountText = completed.Totals.Discount.Rials > 0
            ? $"{SalesText.Tomans(completed.Totals.Discount)} تومان"
            : "بدون تخفیف";

        if (PendingFollowUp == CompletionFollowUp.NextInvoice)
        {
            await ResetCurrentTabAsync();
            ShowNotice($"فاکتور {CompletedNumberText} ثبت شد — {CompletedTotalText} تومان");
        }
        else
        {
            IsCompletedSummaryOpen = true;
        }

        await LoadProductsAsync(CancellationToken.None); // stock just changed
    });

    private async Task ResetCurrentTabAsync()
    {
        var tab = RequireTab();
        var started = await _backend.StartSale.ExecuteAsync(new StartSaleCommand(_warehouseId), CancellationToken.None);
        if (!started.IsSuccess)
        {
            throw new SalesScreenException(started.Error?.Message ?? "فاکتور جدید باز نشد.");
        }

        tab.Reset(started.Value);
        await RefreshInvoiceAsync(tab, CancellationToken.None);
    }

    // ───── remove invoice (asks first — §3.10, §3.14) ─────

    [ObservableProperty]
    public partial bool IsCancelConfirmOpen { get; set; }

    [RelayCommand]
    private void AskCancelInvoice()
    {
        if (CurrentTab is { IsEmpty: false })
        {
            IsCancelConfirmOpen = true;
        }
    }

    [RelayCommand]
    private void KeepInvoice() => IsCancelConfirmOpen = false;

    [RelayCommand]
    private Task ConfirmCancelInvoiceAsync() => RunAsync(async () =>
    {
        var tab = RequireTab();
        IsCancelConfirmOpen = false;
        Check(await _backend.Cancel.ExecuteAsync(new CancelSaleCommand(tab.SaleId), CancellationToken.None));
        await ResetCurrentTabAsync();
        ShowNotice("فاکتور حذف شد.");
    });

    // ───── invoice list (F2) ─────

    [ObservableProperty]
    public partial bool IsInvoiceListOpen { get; set; }

    [ObservableProperty]
    public partial string TodaySummaryText { get; set; } = string.Empty;

    [RelayCommand]
    private Task OpenInvoiceListAsync() => RunAsync(async () =>
    {
        var held = await _backend.HeldSales.ExecuteAsync(new ListHeldSalesQuery(), CancellationToken.None);
        HeldInvoices.Clear();
        foreach (var item in held)
        {
            HeldInvoices.Add(new InvoiceListRow(item));
        }

        var today = PersianDate.FromUtc(_backend.Clock.UtcNow);
        var day = await _backend.SalesOfDay.ExecuteAsync(new ListSalesOfDayQuery(today), CancellationToken.None);
        TodayInvoices.Clear();
        foreach (var item in day.Sales)
        {
            TodayInvoices.Add(new InvoiceListRow(item));
        }

        var byMethod = string.Join(
            " · ",
            day.TotalsByPaymentMethod.OrderBy(pair => pair.Key).Select(pair => $"{SalesText.PaymentMethodName(pair.Key)} {SalesText.Tomans(pair.Value)}"));
        TodaySummaryText = $"{today} · {PersianNumber.FormatGrouped(day.Sales.Count)} فاکتور · جمع {SalesText.Tomans(day.Total)} تومان"
            + (byMethod.Length > 0 ? $" ({byMethod})" : string.Empty);

        IsInvoiceListOpen = true;
    });

    [RelayCommand]
    private void CloseInvoiceList() => IsInvoiceListOpen = false;

    /// <summary>Brings a held invoice back into a tab (or to the front, if it is already open).</summary>
    [RelayCommand]
    private Task ResumeHeldInvoiceAsync(InvoiceListRow? row) => row is null ? Task.CompletedTask : RunAsync(async () =>
    {
        var open = Tabs.FirstOrDefault(tab => tab.SaleId == row.Item.SaleId);
        if (open is null)
        {
            open = new InvoiceTab(row.Item.SaleId);
            Tabs.Add(open);
            await RefreshInvoiceAsync(open, CancellationToken.None);
        }

        CurrentTab = open;
        OnPropertyChanged(nameof(TabPositionText));
        IsInvoiceListOpen = false;
    });

    /// <summary>Esc: closes whichever window is open, innermost first.</summary>
    [RelayCommand]
    private void CloseTopDialog()
    {
        if (IsCancelConfirmOpen) { IsCancelConfirmOpen = false; }
        else if (IsNewCustomerOpen) { IsNewCustomerOpen = false; }
        else if (IsReceivePaymentOpen) { IsReceivePaymentOpen = false; }
        else if (IsEditLineOpen) { IsEditLineOpen = false; }
        else if (IsPaymentOpen) { IsPaymentOpen = false; }
        else if (IsInvoiceListOpen) { IsInvoiceListOpen = false; }
    }
}
