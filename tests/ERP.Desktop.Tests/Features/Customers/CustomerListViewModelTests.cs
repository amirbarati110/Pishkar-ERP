using ERP.Application.Common;
using ERP.Application.Customers;
using ERP.Domain.Common;
using ERP.Domain.Customers;
using ERP.Presentation.Features.Customers;

namespace ERP.Desktop.Tests.Features.Customers;

public sealed class CustomerListViewModelTests
{
    private static CustomerProfileInput Input(
        string? first,
        string? last = null,
        string mobile = "09123456789",
        CustomerKind kind = CustomerKind.Individual,
        string? company = null,
        string? nationalId = null,
        string? address = "تهران") =>
        new(kind, first, last, company, mobile, nationalId, null, null, null, null, null, null, address, null);

    private static long nextCode;

    private static CustomerListRow Row(
        string name,
        long debtTomans = 0,
        long advanceTomans = 0,
        long limitTomans = 0,
        string mobile = "09123456789",
        CustomerKind kind = CustomerKind.Individual,
        string? nationalId = null) =>
        new(
            CustomerId.New(),
            ++nextCode,
            name,
            Input(name, null, mobile, kind, kind == CustomerKind.Legal ? name : null, nationalId),
            Money.FromTomans(limitTomans),
            Money.Zero,
            Money.FromTomans(debtTomans),
            Money.FromTomans(advanceTomans));

    private static (CustomerListViewModel Vm, Fakes Fakes) Create(params CustomerListRow[] rows)
    {
        var fakes = new Fakes { Rows = [.. rows] };
        var vm = new CustomerListViewModel(fakes, fakes, fakes, fakes) { SearchDelay = TimeSpan.Zero };
        return (vm, fakes);
    }

    [Fact]
    public async Task LoadShowsRowsWithPersianDigitsBalanceWordsKindAndTotals()
    {
        var (vm, _) = Create(
            Row("محمد رضایی", debtTomans: 1_500_000, limitTomans: 3_000_000, nationalId: "0499370899"),
            Row("زهرا کریمی", advanceTomans: 150_000),
            Row("مهر", kind: CustomerKind.Legal));

        await vm.LoadAsync(CancellationToken.None);

        Assert.Equal(3, vm.Items.Count);
        Assert.Equal("۰۹۱۲۳۴۵۶۷۸۹", vm.Items[0].MobileText);
        Assert.Equal("۰۴۹۹۳۷۰۸۹۹", vm.Items[0].NationalIdText);
        Assert.Equal(string.Empty, vm.Items[1].NationalIdText);
        Assert.Contains("بدهکار", vm.Items[0].BalanceText);
        Assert.True(vm.Items[0].IsDebtor);
        Assert.Contains("بستانکار", vm.Items[1].BalanceText);
        Assert.False(vm.Items[1].IsDebtor);
        Assert.Equal("تسویه", vm.Items[2].BalanceText);
        Assert.Equal("بدون سقف", vm.Items[2].CreditLimitText);
        Assert.Equal("حقیقی", vm.Items[0].KindText);
        Assert.Equal("حقوقی", vm.Items[2].KindText);
        Assert.Equal("۳", vm.CountText);
        Assert.False(vm.IsEmpty);
        Assert.Contains("بدهکار", vm.Items[0].AccessibleName);
    }

    [Fact]
    public async Task TypingAndTheDebtorsFilterRereadTheListWithTheirValues()
    {
        var (vm, fakes) = Create(Row("محمد"));
        await vm.LoadAsync(CancellationToken.None);

        vm.SearchText = "محم";
        Assert.Equal("محم", fakes.LastQuery!.Term);
        Assert.False(fakes.LastQuery.OnlyDebtors);

        vm.OnlyDebtors = true;
        Assert.True(fakes.LastQuery!.OnlyDebtors);
    }

    [Fact]
    public async Task AnEmptyResultIsFlaggedAndMoreMatchesThanShownAreSaid()
    {
        var (vm, fakes) = Create();
        await vm.LoadAsync(CancellationToken.None);
        Assert.True(vm.IsEmpty);

        fakes.Rows = [Row("الف")];
        fakes.TotalCountOverride = 900;
        await vm.RefreshAsync(CancellationToken.None);
        Assert.Contains("۹۰۰", vm.TruncatedText);
    }

    [Fact]
    public async Task NewOpensAnEmptyPersonFormWithTheOpeningBalanceField()
    {
        var (vm, _) = Create(Row("محمد"));
        await vm.LoadAsync(CancellationToken.None);

        vm.NewCommand.Execute(null);

        Assert.True(vm.IsEditorOpen);
        Assert.Equal("مشتری جدید", vm.EditorTitle);
        Assert.Equal(CustomerKind.Individual, vm.Kind);
        Assert.True(vm.IsPerson);
        Assert.False(vm.IsLegal);
        Assert.Equal(string.Empty, vm.FirstName);
        Assert.True(vm.IsOpeningBalanceEditable);
    }

    [Fact]
    public async Task EditFillsEveryFieldFromTheRowAndHidesTheOpeningBalance()
    {
        var row = Row("محمد رضایی", limitTomans: 3_000_000, nationalId: "0499370899");
        var (vm, _) = Create(row);
        await vm.LoadAsync(CancellationToken.None);
        Assert.False(vm.EditCommand.CanExecute(null)); // nothing selected

        vm.SelectedItem = vm.Items[0];
        vm.EditCommand.Execute(null);

        Assert.True(vm.IsEditorOpen);
        Assert.Equal("ویرایش مشتری", vm.EditorTitle);
        Assert.Equal(vm.SelectedItem.CodeText, vm.EditorCodeText);
        Assert.Equal("محمد رضایی", vm.FirstName);
        Assert.Equal("۰۹۱۲۳۴۵۶۷۸۹", vm.Mobile);
        Assert.Equal("۰۴۹۹۳۷۰۸۹۹", vm.NationalId);
        Assert.Equal("تهران", vm.Address);
        Assert.Equal("۳٬۰۰۰٬۰۰۰", vm.CreditLimitTomansText.Replace(",", "٬"));
        Assert.False(vm.IsOpeningBalanceEditable);
    }

    [Fact]
    public async Task ChoosingLegalSwitchesTheFieldsAndTheIdentityLabel()
    {
        var (vm, _) = Create();
        vm.NewCommand.Execute(null);
        Assert.Contains("کد ملی", vm.NationalIdLabel);

        vm.KindIndex = 1;
        Assert.True(vm.IsLegal);
        Assert.False(vm.IsPerson);
        Assert.Contains("شناسه ملی", vm.NationalIdLabel);

        vm.KindIndex = 2;
        Assert.Equal(CustomerKind.Foreign, vm.Kind);
        Assert.Contains("کد فراگیر", vm.NationalIdLabel);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task SavingWithBadFieldsShowsEveryErrorAtOnceUnderItsBoxAndCallsNothing()
    {
        var (vm, fakes) = Create();
        await vm.LoadAsync(CancellationToken.None);
        vm.NewCommand.Execute(null);
        vm.FirstName = "  ";
        vm.Mobile = "0912";
        vm.NationalId = "1234567890";
        vm.PostalCode = "12";
        vm.Email = "x";
        vm.CreditLimitTomansText = "abc";
        vm.OpeningBalanceTomansText = "-5";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(vm.NameError);
        Assert.NotNull(vm.MobileError);
        Assert.NotNull(vm.NationalIdError);
        Assert.NotNull(vm.PostalCodeError);
        Assert.NotNull(vm.EmailError);
        Assert.NotNull(vm.CreditLimitError);
        Assert.NotNull(vm.OpeningBalanceError);
        Assert.Null(vm.PhoneError);
        Assert.True(vm.IsEditorOpen);
        Assert.Empty(fakes.Created);
    }

    [Fact]
    public async Task SavingANewCustomerSendsTheTypedProfileClosesTheFormAndRefreshes()
    {
        var (vm, fakes) = Create();
        await vm.LoadAsync(CancellationToken.None);
        vm.NewCommand.Execute(null);
        vm.FirstName = " محمد ";
        vm.LastName = "رضایی";
        vm.Mobile = "۰۹۱۲ ۳۴۵ ۶۷۸۹";
        vm.NationalId = "۰۴۹۹۳۷۰۸۹۹";
        vm.Address = "کرج";
        vm.CreditLimitTomansText = "۳٬۰۰۰٬۰۰۰";
        vm.OpeningBalanceTomansText = "۵۰۰٬۰۰۰";

        await vm.SaveCommand.ExecuteAsync(null);

        var command = Assert.Single(fakes.Created);
        Assert.Equal(CustomerKind.Individual, command.Profile.Kind);
        Assert.Equal(" محمد ", command.Profile.FirstName);
        Assert.Equal("رضایی", command.Profile.LastName);
        Assert.Equal("۰۴۹۹۳۷۰۸۹۹", command.Profile.NationalId);
        Assert.Equal(3_000_000, command.CreditLimit.ToTomansExact());
        Assert.Equal(500_000, command.OpeningBalance.ToTomansExact());
        Assert.False(vm.IsEditorOpen);
        Assert.Contains("محمد رضایی", vm.Notice);
        Assert.Contains("ساخته شد", vm.Notice);
        Assert.Equal(2, fakes.ListCalls); // load + refresh after save
    }

    [Fact]
    public async Task SavingACompanyNeedsACompanyNameNotAPersonName()
    {
        var (vm, fakes) = Create();
        vm.NewCommand.Execute(null);
        vm.KindIndex = 1;
        vm.Mobile = "09123456789";

        await vm.SaveCommand.ExecuteAsync(null);
        Assert.NotNull(vm.CompanyNameError);
        Assert.Null(vm.NameError);

        vm.CompanyName = "مهر";
        vm.NationalId = "10380284790";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(CustomerKind.Legal, Assert.Single(fakes.Created).Profile.Kind);
    }

    [Fact]
    public async Task SavingAnEditUpdatesTheSameCustomerAndNeverSendsAnOpeningBalance()
    {
        var row = Row("محمد");
        var (vm, fakes) = Create(row);
        await vm.LoadAsync(CancellationToken.None);
        vm.SelectedItem = vm.Items[0];
        vm.EditCommand.Execute(null);
        vm.LastName = "رضایی";
        vm.Address = " ";

        await vm.SaveCommand.ExecuteAsync(null);

        var command = Assert.Single(fakes.Updated);
        Assert.Equal(row.Id, command.CustomerId);
        Assert.Equal("رضایی", command.Profile.LastName);
        Assert.Empty(fakes.Created);
        Assert.False(vm.IsEditorOpen);
    }

    [Theory]
    [InlineData("customers.customer.duplicate-mobile", true)]
    [InlineData("customers.customer.duplicate-national-id", false)]
    public async Task ADuplicateIsShownUnderTheRightBoxAndTheFormStaysOpen(string code, bool mobile)
    {
        var (vm, fakes) = Create();
        fakes.CreateFailure = (code, "قبلاً برای «علی» ثبت شده است.");
        await vm.LoadAsync(CancellationToken.None);
        vm.NewCommand.Execute(null);
        vm.FirstName = "محمد";
        vm.Mobile = "09123456789";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("قبلاً برای «علی» ثبت شده است.", mobile ? vm.MobileError : vm.NationalIdError);
        Assert.True(vm.IsEditorOpen);
    }

    [Fact]
    public async Task DeleteAsksFirstAndNothingIsArchivedUntilConfirmed()
    {
        var (vm, fakes) = Create(Row("علی احمدی"));
        await vm.LoadAsync(CancellationToken.None);
        Assert.False(vm.ArchiveCommand.CanExecute(null));

        vm.SelectedItem = vm.Items[0];
        vm.ArchiveCommand.Execute(null);

        Assert.True(vm.IsArchiveConfirmOpen);
        Assert.Contains("علی احمدی", vm.ArchiveConfirmText);
        Assert.Empty(fakes.Archived);

        vm.CancelArchiveCommand.Execute(null);
        Assert.False(vm.IsArchiveConfirmOpen);
        Assert.Empty(fakes.Archived);
    }

    [Fact]
    public async Task ADebtorCannotBeDeletedSoNoQuestionIsAskedJustTheReason()
    {
        var (vm, fakes) = Create(Row("محمد", debtTomans: 1_500_000));
        await vm.LoadAsync(CancellationToken.None);
        vm.SelectedItem = vm.Items[0];

        vm.ArchiveCommand.Execute(null);

        Assert.False(vm.IsArchiveConfirmOpen);
        Assert.Contains("بدهکار حذف نمی‌شود", vm.ErrorMessage);
        Assert.Empty(fakes.Archived);
    }

    [Fact]
    public async Task ConfirmedDeleteArchivesTheSelectedCustomerAndRefreshesTheList()
    {
        var ali = Row("علی احمدی");
        var (vm, fakes) = Create(ali, Row("زهرا کریمی"));
        await vm.LoadAsync(CancellationToken.None);
        vm.SelectedItem = vm.Items[0];
        vm.ArchiveCommand.Execute(null);
        fakes.Rows = [.. fakes.Rows.Skip(1)];

        await vm.ConfirmArchiveCommand.ExecuteAsync(null);

        Assert.Equal([ali.Id], fakes.Archived);
        Assert.Single(vm.Items);
        Assert.Contains("حذف شد", vm.Notice);
        Assert.Null(vm.SelectedItem);
    }

    [Fact]
    public async Task AFailedDeleteShowsTheReasonAndKeepsTheList()
    {
        var (vm, fakes) = Create(Row("علی"));
        fakes.ArchiveFailure = "این مشتری پیدا نشد.";
        await vm.LoadAsync(CancellationToken.None);
        vm.SelectedItem = vm.Items[0];
        vm.ArchiveCommand.Execute(null);

        await vm.ConfirmArchiveCommand.ExecuteAsync(null);

        Assert.Equal("این مشتری پیدا نشد.", vm.ErrorMessage);
        Assert.Single(vm.Items);
    }

    private sealed class Fakes : IListCustomersHandler, ICreateCustomerHandler, IUpdateCustomerHandler, IArchiveCustomerHandler
    {
        public List<CustomerListRow> Rows { get; set; } = [];

        public CustomerListQuery? LastQuery { get; private set; }

        public int ListCalls { get; private set; }

        public int? TotalCountOverride { get; set; }

        public List<CreateCustomerCommand> Created { get; } = [];

        public List<UpdateCustomerCommand> Updated { get; } = [];

        public List<CustomerId> Archived { get; } = [];

        public (string Code, string Message)? CreateFailure { get; set; }

        public string? ArchiveFailure { get; set; }

        public Task<CustomerListPage> ExecuteAsync(CustomerListQuery query, CancellationToken cancellationToken)
        {
            LastQuery = query;
            ListCalls++;
            var debt = Rows.Sum(row => row.Debt.Rials);
            var advance = Rows.Sum(row => row.Advance.Rials);
            return Task.FromResult(new CustomerListPage(
                Rows, TotalCountOverride ?? Rows.Count, Money.FromRials(debt), Money.FromRials(advance)));
        }

        public Task<Result<CustomerId>> ExecuteAsync(CreateCustomerCommand command, CancellationToken cancellationToken)
        {
            if (CreateFailure is { } failure)
            {
                return Task.FromResult(Result.Failure<CustomerId>(failure.Code, failure.Message));
            }

            Created.Add(command);
            return Task.FromResult(Result.Success(CustomerId.New()));
        }

        public Task<Result<bool>> ExecuteAsync(UpdateCustomerCommand command, CancellationToken cancellationToken)
        {
            Updated.Add(command);
            return Task.FromResult(Result.Success(true));
        }

        public Task<Result<bool>> ExecuteAsync(ArchiveCustomerCommand command, CancellationToken cancellationToken)
        {
            if (ArchiveFailure is not null)
            {
                return Task.FromResult(Result.Failure<bool>("customers.customer.not-found", ArchiveFailure));
            }

            Archived.Add(command.CustomerId);
            return Task.FromResult(Result.Success(true));
        }
    }
}
