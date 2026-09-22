using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERP.Application.Customers;
using ERP.Domain.Common;
using ERP.Domain.Customers;
using ERP.Presentation.Features.Sales;

namespace ERP.Presentation.Features.Customers;

/// <summary>One row of «لیست مشتریان» as the screen writes it (Persian digits, Tomans).</summary>
public sealed class CustomerListItem
{
    public CustomerListItem(CustomerListRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        Row = row;
        Name = row.Name;
        CodeText = PersianNumber.DigitsToPersian(row.Code.ToString(System.Globalization.CultureInfo.InvariantCulture));
        MobileText = PersianNumber.DigitsToPersian(row.Mobile);
        NationalIdText = row.Profile.NationalId is { } id ? PersianNumber.DigitsToPersian(id) : string.Empty;
        KindText = CustomerListViewModel.KindName(row.Profile.Kind);
        AddressText = row.Address ?? string.Empty;
        CreditLimitText = row.CreditLimit.Rials == 0 ? "بدون سقف" : SalesText.Tomans(row.CreditLimit);
        IsDebtor = row.Debt.Rials > 0;
        BalanceText = IsDebtor
            ? $"{SalesText.Tomans(row.Debt)} بدهکار"
            : row.Advance.Rials > 0 ? $"{SalesText.Tomans(row.Advance)} بستانکار" : "تسویه";
    }

    public CustomerListRow Row { get; }

    public string Name { get; }

    public string CodeText { get; }

    public string MobileText { get; }

    /// <summary>کد ملی · شناسه ملی · کد فراگیر — empty when none was entered.</summary>
    public string NationalIdText { get; }

    /// <summary>«حقیقی» · «حقوقی» · «خارجی».</summary>
    public string KindText { get; }

    public string AddressText { get; }

    public string CreditLimitText { get; }

    /// <summary>«۱٬۵۰۰٬۰۰۰ بدهکار» · «۱۵۰٬۰۰۰ بستانکار» · «تسویه» — words, not just colour, say which way the balance goes.</summary>
    public string BalanceText { get; }

    /// <summary>A debt is drawn in the warning colour, like low stock is on the product list.</summary>
    public bool IsDebtor { get; }

    /// <summary>What a screen reader says for the row (the row is not a control with its own text).</summary>
    public string AccessibleName => $"مشتری شماره {CodeText}، {Name}، موبایل {MobileText}، {BalanceText} تومان";
}

/// <summary>
/// «لیست مشتریان» (checklist «ن» / «ن-۲»): every active customer with search, a debtors-only filter
/// and the totals. New and edit open a form over the list with the fields of the old app «باران»
/// plus what an electronic tax invoice needs (kind, identity numbers, postal code); «حذف» asks
/// first and then archives — refused while the customer still owes money.
/// </summary>
public sealed partial class CustomerListViewModel : ObservableObject
{
    /// <summary>The order of the «نوع مشتری» choices on the form.</summary>
    public static IReadOnlyList<CustomerKind> Kinds { get; } = [CustomerKind.Individual, CustomerKind.Legal, CustomerKind.Foreign];

    private readonly IListCustomersHandler _list;
    private readonly ICreateCustomerHandler _create;
    private readonly IUpdateCustomerHandler _update;
    private readonly IArchiveCustomerHandler _archive;
    private CancellationTokenSource? _searchCancellation;
    private CustomerListRow? _editing;

    public CustomerListViewModel(
        IListCustomersHandler list,
        ICreateCustomerHandler create,
        IUpdateCustomerHandler update,
        IArchiveCustomerHandler archive)
    {
        _list = list;
        _create = create;
        _update = update;
        _archive = archive;
    }

    /// <summary>How long typing must pause before the list is re-read. Tests set it to zero.</summary>
    public TimeSpan SearchDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Where an unexpected failure is written (the app's log). Null only in tests, where it should surface.</summary>
    public Action<Exception>? ReportUnexpectedError { get; set; }

    public static string KindName(CustomerKind kind) => kind switch
    {
        CustomerKind.Legal => "حقوقی",
        CustomerKind.Foreign => "خارجی",
        _ => "حقیقی",
    };

    public ObservableCollection<CustomerListItem> Items { get; } = [];

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool OnlyDebtors { get; set; }

    [ObservableProperty]
    public partial CustomerListItem? SelectedItem { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial string CountText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DebtTotalText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AdvanceTotalText { get; set; } = string.Empty;

    /// <summary>Filled only when more customers matched than fit on screen — an honest hint, not a silent cut.</summary>
    [ObservableProperty]
    public partial string TruncatedText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string? Notice { get; set; }

    // ───── the form over the list ─────

    [ObservableProperty]
    public partial bool IsEditorOpen { get; set; }

    [ObservableProperty]
    public partial string EditorTitle { get; set; } = string.Empty;

    /// <summary>The shop's customer number shown in the form title when editing.</summary>
    [ObservableProperty]
    public partial string EditorCodeText { get; set; } = string.Empty;

    /// <summary>Index into <see cref="Kinds"/> — what the «نوع مشتری» box selects.</summary>
    [ObservableProperty]
    public partial int KindIndex { get; set; }

    [ObservableProperty]
    public partial string FirstName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LastName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CompanyName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Mobile { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NationalId { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EconomicCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RegistrationNumber { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PostalCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Phone { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Email { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string BirthDate { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Address { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Notes { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CreditLimitTomansText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OpeningBalanceTomansText { get; set; } = string.Empty;

    // Errors: one per box on the form. The first/last-name boxes share NameError (a name is one idea).
    [ObservableProperty]
    public partial string? NameError { get; set; }

    [ObservableProperty]
    public partial string? CompanyNameError { get; set; }

    [ObservableProperty]
    public partial string? MobileError { get; set; }

    [ObservableProperty]
    public partial string? NationalIdError { get; set; }

    [ObservableProperty]
    public partial string? EconomicCodeError { get; set; }

    [ObservableProperty]
    public partial string? RegistrationNumberError { get; set; }

    [ObservableProperty]
    public partial string? PostalCodeError { get; set; }

    [ObservableProperty]
    public partial string? PhoneError { get; set; }

    [ObservableProperty]
    public partial string? EmailError { get; set; }

    [ObservableProperty]
    public partial string? BirthDateError { get; set; }

    [ObservableProperty]
    public partial string? AddressError { get; set; }

    [ObservableProperty]
    public partial string? NotesError { get; set; }

    [ObservableProperty]
    public partial string? CreditLimitError { get; set; }

    [ObservableProperty]
    public partial string? OpeningBalanceError { get; set; }

    [ObservableProperty]
    public partial string? EditorError { get; set; }

    [ObservableProperty]
    public partial bool IsSaving { get; set; }

    public CustomerKind Kind => Kinds[Math.Clamp(KindIndex, 0, Kinds.Count - 1)];

    /// <summary>A company has a company name; a person (Iranian or foreign) has first and last name.</summary>
    public bool IsLegal => Kind == CustomerKind.Legal;

    public bool IsPerson => Kind != CustomerKind.Legal;

    /// <summary>«کد ملی» · «شناسه ملی» · «کد فراگیر» — the label follows the kind, as does the check applied to it.</summary>
    public string NationalIdLabel => Kind switch
    {
        CustomerKind.Legal => "شناسه ملی (۱۱ رقم)",
        CustomerKind.Foreign => "کد فراگیر",
        _ => "کد ملی (۱۰ رقم)",
    };

    /// <summary>The opening balance can be typed only for a new customer; later it would rewrite every balance the customer ever had.</summary>
    public bool IsOpeningBalanceEditable => _editing is null;

    // ───── «حذف» asks first ─────

    [ObservableProperty]
    public partial bool IsArchiveConfirmOpen { get; set; }

    [ObservableProperty]
    public partial string ArchiveConfirmText { get; set; } = string.Empty;

    public bool HasSelection => SelectedItem is not null;

    partial void OnSelectedItemChanged(CustomerListItem? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        EditCommand.NotifyCanExecuteChanged();
        ArchiveCommand.NotifyCanExecuteChanged();
    }

    partial void OnKindIndexChanged(int value)
    {
        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(IsLegal));
        OnPropertyChanged(nameof(IsPerson));
        OnPropertyChanged(nameof(NationalIdLabel));
    }

    partial void OnSearchTextChanged(string value) => _ = SearchAfterDelayAsync();

    partial void OnOnlyDebtorsChanged(bool value) => _ = SearchAfterDelayAsync();

    /// <summary>First open.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RefreshAsync(cancellationToken);
        }
        catch (Exception exception) when (ReportUnexpectedError is not null)
        {
            ReportUnexpectedError(exception);
            ErrorMessage = "لیست مشتریان خوانده نشد و خطا برای پشتیبانی ثبت شد. دوباره امتحان کنید.";
        }
    }

    /// <summary>Re-reads the list for the current search text and filter. Keeps the selection when that customer is still listed.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var keep = SelectedItem?.Row.Id;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var page = await _list.ExecuteAsync(new CustomerListQuery(SearchText, OnlyDebtors), cancellationToken);

            Items.Clear();
            foreach (var row in page.Rows)
            {
                Items.Add(new CustomerListItem(row));
            }

            SelectedItem = Items.FirstOrDefault(item => item.Row.Id == keep);
            IsEmpty = Items.Count == 0;
            CountText = PersianNumber.FormatGrouped(page.TotalCount);
            DebtTotalText = SalesText.Tomans(page.TotalDebt);
            AdvanceTotalText = SalesText.Tomans(page.TotalAdvance);
            TruncatedText = page.TotalCount > page.Rows.Count
                ? $"فقط {PersianNumber.FormatGrouped(page.Rows.Count)} مشتری اول از {PersianNumber.FormatGrouped(page.TotalCount)} نشان داده شد؛ جست‌وجو را دقیق‌تر کنید."
                : string.Empty;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task SearchAfterDelayAsync()
    {
        _searchCancellation?.Cancel();
        var source = new CancellationTokenSource();
        _searchCancellation = source;

        try
        {
            if (SearchDelay > TimeSpan.Zero)
            {
                await Task.Delay(SearchDelay, source.Token);
            }

            await RefreshAsync(source.Token);
        }
        catch (OperationCanceledException)
        {
            // a newer keystroke replaced this search — nothing to do
        }
        catch (Exception exception) when (ReportUnexpectedError is not null)
        {
            ReportUnexpectedError(exception);
            ErrorMessage = "لیست خوانده نشد و خطا برای پشتیبانی ثبت شد.";
        }
    }

    // ───── new / edit ─────

    /// <summary>«جدید (F2)».</summary>
    [RelayCommand]
    private void New()
    {
        _editing = null;
        Fill(
            "مشتری جدید",
            string.Empty,
            new CustomerProfileInput(CustomerKind.Individual, null, null, null, null, null, null, null, null, null, null, null, null, null),
            Money.Zero);
    }

    /// <summary>«ویرایش (F3)» on the selected customer.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Edit()
    {
        if (SelectedItem is not { } item)
        {
            return;
        }

        _editing = item.Row;
        Fill("ویرایش مشتری", item.CodeText, item.Row.Profile, item.Row.CreditLimit);
    }

    private void Fill(string title, string codeText, CustomerProfileInput profile, Money creditLimit)
    {
        EditorTitle = title;
        EditorCodeText = codeText;
        KindIndex = Math.Max(0, Kinds.ToList().IndexOf(profile.Kind));
        FirstName = profile.FirstName ?? string.Empty;
        LastName = profile.LastName ?? string.Empty;
        CompanyName = profile.CompanyName ?? string.Empty;
        Mobile = profile.Mobile is { } mobile ? PersianNumber.DigitsToPersian(mobile) : string.Empty;
        NationalId = Persian(profile.NationalId);
        EconomicCode = Persian(profile.EconomicCode);
        RegistrationNumber = Persian(profile.RegistrationNumber);
        PostalCode = Persian(profile.PostalCode);
        Phone = Persian(profile.Phone);
        Email = profile.Email ?? string.Empty;
        BirthDate = Persian(profile.BirthDate);
        Address = profile.Address ?? string.Empty;
        Notes = profile.Notes ?? string.Empty;
        CreditLimitTomansText = creditLimit.Rials == 0 ? string.Empty : PersianNumber.FormatGrouped(creditLimit.Rials / 10);
        OpeningBalanceTomansText = string.Empty;
        ClearEditorErrors();
        Notice = null;
        OnPropertyChanged(nameof(IsOpeningBalanceEditable));
        IsEditorOpen = true;
    }

    private static string Persian(string? value) => value is null ? string.Empty : PersianNumber.DigitsToPersian(value);

    [RelayCommand]
    private void CancelEditor() => IsEditorOpen = false;

    [RelayCommand]
    private async Task SaveAsync()
    {
        ClearEditorErrors();

        var input = new CustomerProfileInput(
            Kind, FirstName, LastName, CompanyName, Mobile, NationalId, EconomicCode, RegistrationNumber,
            PostalCode, Phone, Email, BirthDate, Address, Notes);

        // Every problem is shown at once, next to its box; the handler checks the same rules again.
        var fieldsOk = CustomerProfile.TryCreate(input, out _, out var fieldErrors);
        foreach (var error in fieldErrors)
        {
            ShowFieldError(error.Field, error.Message);
        }

        var amountsOk = TryReadAmounts(out var creditLimit, out var openingBalance);
        if (!fieldsOk || !amountsOk)
        {
            return;
        }

        IsSaving = true;
        try
        {
            var name = CustomerProfile.Create(input).DisplayName;

            if (_editing is { } editing)
            {
                var updated = await _update.ExecuteAsync(
                    new UpdateCustomerCommand(editing.Id, input, Money.FromRials(creditLimit)),
                    CancellationToken.None);
                if (!updated.IsSuccess)
                {
                    ShowSaveError(updated.Error?.Code, updated.Error?.Message);
                    return;
                }

                Notice = $"«{name}» ذخیره شد.";
            }
            else
            {
                var created = await _create.ExecuteAsync(
                    new CreateCustomerCommand(input, Money.FromRials(creditLimit), Money.FromRials(openingBalance)),
                    CancellationToken.None);
                if (!created.IsSuccess)
                {
                    ShowSaveError(created.Error?.Code, created.Error?.Message);
                    return;
                }

                Notice = $"«{name}» ساخته شد.";
            }

            IsEditorOpen = false;
            await RefreshAsync(CancellationToken.None);
        }
        catch (Exception exception) when (ReportUnexpectedError is not null)
        {
            ReportUnexpectedError(exception);
            EditorError = "ذخیره انجام نشد و خطا برای پشتیبانی ثبت شد. دوباره امتحان کنید.";
        }
        finally
        {
            IsSaving = false;
        }
    }

    private bool TryReadAmounts(out long creditLimitRials, out long openingBalanceRials)
    {
        creditLimitRials = 0;
        openingBalanceRials = 0;
        var ok = true;

        var limit = SalesText.ParseTomansToRials(CreditLimitTomansText);
        if (limit is null)
        {
            CreditLimitError = "سقف اعتبار را با عدد وارد کنید (تومان)، یا خالی بگذارید.";
            ok = false;
        }
        else
        {
            creditLimitRials = limit.Value;
        }

        if (IsOpeningBalanceEditable)
        {
            var opening = SalesText.ParseTomansToRials(OpeningBalanceTomansText);
            if (opening is null)
            {
                OpeningBalanceError = "مانده‌ی اول دوره را با عدد وارد کنید (تومان)، یا خالی بگذارید.";
                ok = false;
            }
            else
            {
                openingBalanceRials = opening.Value;
            }
        }

        return ok;
    }

    private void ShowFieldError(CustomerField field, string message)
    {
        switch (field)
        {
            case CustomerField.FirstName or CustomerField.LastName:
                NameError = message;
                break;
            case CustomerField.CompanyName:
                CompanyNameError = message;
                break;
            case CustomerField.Mobile:
                MobileError = message;
                break;
            case CustomerField.NationalId:
                NationalIdError = message;
                break;
            case CustomerField.EconomicCode:
                EconomicCodeError = message;
                break;
            case CustomerField.RegistrationNumber:
                RegistrationNumberError = message;
                break;
            case CustomerField.PostalCode:
                PostalCodeError = message;
                break;
            case CustomerField.Phone:
                PhoneError = message;
                break;
            case CustomerField.Email:
                EmailError = message;
                break;
            case CustomerField.BirthDate:
                BirthDateError = message;
                break;
            case CustomerField.Address:
                AddressError = message;
                break;
            case CustomerField.Notes:
                NotesError = message;
                break;
        }
    }

    private void ShowSaveError(string? code, string? message)
    {
        switch (code)
        {
            case "customers.customer.duplicate-mobile":
                MobileError = message;
                break;
            case "customers.customer.duplicate-national-id":
                NationalIdError = message;
                break;
            default:
                EditorError = message;
                break;
        }
    }

    private void ClearEditorErrors()
    {
        NameError = null;
        CompanyNameError = null;
        MobileError = null;
        NationalIdError = null;
        EconomicCodeError = null;
        RegistrationNumberError = null;
        PostalCodeError = null;
        PhoneError = null;
        EmailError = null;
        BirthDateError = null;
        AddressError = null;
        NotesError = null;
        CreditLimitError = null;
        OpeningBalanceError = null;
        EditorError = null;
    }

    // ───── delete = archive ─────

    /// <summary>«حذف (F4)»: asks first. Nothing is archived until <see cref="ConfirmArchiveCommand"/>.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Archive()
    {
        if (SelectedItem is not { } item)
        {
            return;
        }

        if (item.IsDebtor)
        {
            // No question to ask: it cannot be done. Say why, in the same words the handler uses.
            ErrorMessage = $"«{item.Name}» هنوز {item.BalanceText} تومان دارد. مشتری بدهکار حذف نمی‌شود؛ اول بدهی را تسویه کنید.";
            return;
        }

        ArchiveConfirmText =
            $"«{item.Name}» از لیست مشتریان و صفحه‌ی فروش برداشته شود؟ سابقه‌ی فاکتورها و حساب او در گزارش‌ها می‌ماند.";
        IsArchiveConfirmOpen = true;
    }

    [RelayCommand]
    private void CancelArchive() => IsArchiveConfirmOpen = false;

    [RelayCommand]
    private async Task ConfirmArchiveAsync()
    {
        IsArchiveConfirmOpen = false;
        if (SelectedItem is not { } item)
        {
            return;
        }

        try
        {
            var result = await _archive.ExecuteAsync(new ArchiveCustomerCommand(item.Row.Id), CancellationToken.None);
            if (result.IsSuccess)
            {
                Notice = $"«{item.Name}» حذف شد.";
                SelectedItem = null;
                await RefreshAsync(CancellationToken.None);
            }
            else
            {
                ErrorMessage = result.Error?.Message;
            }
        }
        catch (Exception exception) when (ReportUnexpectedError is not null)
        {
            ReportUnexpectedError(exception);
            ErrorMessage = "حذف انجام نشد و خطا برای پشتیبانی ثبت شد. دوباره امتحان کنید.";
        }
    }
}
