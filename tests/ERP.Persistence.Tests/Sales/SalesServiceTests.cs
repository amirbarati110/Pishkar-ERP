using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Application.Customers;
using ERP.Application.Inventory;
using ERP.Application.Sales;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Customers;
using ERP.Domain.Sales;
using ERP.Persistence.Catalog;
using ERP.Persistence.Customers;
using ERP.Persistence.Database;
using ERP.Persistence.Inventory;
using ERP.Persistence.Sales;
using ERP.Persistence.Services;
using ERP.Persistence.Tests.TestSupport;

namespace ERP.Persistence.Tests.Sales;

public sealed class SalesServiceTests
{
    [Fact]
    public async Task FullQuickSaleFlowDeductsFifoStockAndPersistsTheCompletedSale()
    {
        var context = await SetupAsync();

        var start = await context.Sales.ExecuteAsync(
            new StartSaleCommand(context.Defaults.MainWarehouseId),
            CancellationToken.None);
        Assert.True(start.IsSuccess);
        var saleId = start.Value;

        var firstLine = await context.Sales.ExecuteAsync(
            new AddSaleLineCommand(saleId, context.RiceId, 2),
            CancellationToken.None);
        var secondLine = await context.Sales.ExecuteAsync(
            new AddSaleLineCommand(saleId, context.OilId, 1),
            CancellationToken.None);
        Assert.True(firstLine.IsSuccess);
        Assert.True(secondLine.IsSuccess);

        // Cashier double-scans the rice by mistake, then removes it and re-adds
        // the right quantity — exercising remove + merge-on-add together.
        var duplicateScan = await context.Sales.ExecuteAsync(
            new AddSaleLineCommand(saleId, context.RiceId, 1),
            CancellationToken.None);
        Assert.True(duplicateScan.IsSuccess);

        var complete = await context.Sales.ExecuteAsync(
            new CompleteSaleCommand(saleId, PaymentMethod.Card, DiscountRials: 0, TaxRatePercent: 9),
            CancellationToken.None);

        Assert.True(complete.IsSuccess);
        // 3 × 245,000 (rice) + 1 × 850,000 (oil) = 1,585,000 Tomans subtotal.
        Assert.Equal(1_585_000, complete.Value!.Totals.Subtotal.ToTomansExact());
        Assert.Equal(0, complete.Value.Totals.Discount.Rials);

        var reloaded = await context.SaleRepository.GetAsync(saleId, CancellationToken.None);
        Assert.NotNull(reloaded);
        Assert.Equal(SaleStatus.Completed, reloaded!.Status);
        Assert.Equal(2, reloaded.Lines.Count);
        var riceLine = Assert.Single(reloaded.Lines, line => line.ProductId == context.RiceId);
        Assert.Equal(3, riceLine.Quantity.Value);

        var riceLedger = await context.StockLedgers.GetAsync(
            context.RiceId, context.Defaults.MainWarehouseId, CancellationToken.None);
        Assert.NotNull(riceLedger);
        Assert.Equal(7, riceLedger!.AvailableQuantity.Value); // 10 received − 3 sold
    }

    [Fact]
    public async Task LineDiscountPriceOverrideAndServiceChargeSurviveARoundTrip()
    {
        var context = await SetupAsync();

        var start = await context.Sales.ExecuteAsync(
            new StartSaleCommand(context.Defaults.MainWarehouseId),
            CancellationToken.None);
        var saleId = start.Value;
        await context.Sales.ExecuteAsync(
            new AddSaleLineCommand(saleId, context.RiceId, 3),
            CancellationToken.None);

        // مداد: قیمت این فاکتور ۲۰۰٬۰۰۰ (به‌جای ۲۴۵٬۰۰۰) + تخفیف سطر ۵۰٬۰۰۰
        var edit = await context.Sales.ExecuteAsync(
            new ChangeSaleLineCommand(
                saleId, context.RiceId, Quantity: 3,
                UnitPriceRials: Money.FromTomans(200_000).Rials,
                DiscountRials: Money.FromTomans(50_000).Rials),
            CancellationToken.None);
        Assert.True(edit.IsSuccess);

        // خدمات/هزینه ۳۰٬۰۰۰ و تخفیف فاکتور ۲۰٬۰۰۰
        var charges = await context.Sales.ExecuteAsync(
            new SetSaleChargesCommand(
                saleId,
                DiscountRials: Money.FromTomans(20_000).Rials,
                ServiceChargeRials: Money.FromTomans(30_000).Rials),
            CancellationToken.None);
        Assert.True(charges.IsSuccess);

        var reloaded = await context.SaleRepository.GetAsync(saleId, CancellationToken.None);
        var line = Assert.Single(reloaded!.Lines);
        Assert.Equal(200_000, line.UnitPrice.ToTomansExact());
        Assert.Equal(245_000, line.CatalogPrice.ToTomansExact());  // قیمت اصلی کالا دست‌نخورده
        Assert.True(line.PriceOverridden);
        Assert.Equal(50_000, line.Discount.ToTomansExact());
        Assert.Equal(550_000, line.LineTotal.ToTomansExact());     // ۳×۲۰۰٬۰۰۰ − ۵۰٬۰۰۰
        Assert.Equal(30_000, reloaded.ServiceCharge.ToTomansExact());
        Assert.Equal(20_000, reloaded.Discount.ToTomansExact());

        var complete = await context.Sales.ExecuteAsync(
            new CompleteSaleCommand(saleId, PaymentMethod.Card, 0, 9),
            CancellationToken.None);
        Assert.True(complete.IsSuccess);
        // (۵۵۰٬۰۰۰ − ۲۰٬۰۰۰ + ۳۰٬۰۰۰) = ۵۶۰٬۰۰۰ → مالیات ۹٪ = ۵۰٬۴۰۰ → ۶۱۰٬۴۰۰
        Assert.Equal(30_000, complete.Value!.Totals.ServiceCharge.ToTomansExact());
        Assert.Equal(50_400, complete.Value.Totals.Tax.ToTomansExact());
        Assert.Equal(610_400, complete.Value.Totals.Total.ToTomansExact());
    }

    [Fact]
    public async Task TheInvoiceNoteSurvivesARoundTripAndCanBeClearedOrRejectedIfTooLong()
    {
        var context = await SetupAsync();

        var start = await context.Sales.ExecuteAsync(
            new StartSaleCommand(context.Defaults.MainWarehouseId), CancellationToken.None);
        var saleId = start.Value;

        var set = await context.Sales.ExecuteAsync(
            new SetSaleNoteCommand(saleId, "  تحویل عصر، درب پشتی  "), CancellationToken.None);
        Assert.True(set.IsSuccess);

        var reloaded = await context.SaleRepository.GetAsync(saleId, CancellationToken.None);
        Assert.Equal("تحویل عصر، درب پشتی", reloaded!.Note);

        var tooLong = await context.Sales.ExecuteAsync(
            new SetSaleNoteCommand(saleId, new string('ا', 501)), CancellationToken.None);
        Assert.False(tooLong.IsSuccess);
        var stillOld = await context.SaleRepository.GetAsync(saleId, CancellationToken.None);
        Assert.Equal("تحویل عصر، درب پشتی", stillOld!.Note); // rejected write did not overwrite the old note

        var cleared = await context.Sales.ExecuteAsync(new SetSaleNoteCommand(saleId, null), CancellationToken.None);
        Assert.True(cleared.IsSuccess);
        var final = await context.SaleRepository.GetAsync(saleId, CancellationToken.None);
        Assert.Null(final!.Note);
    }

    [Fact]
    public async Task UpdatingTheCatalogPriceFromTheLineEditorChangesTheProductToo()
    {
        var context = await SetupAsync();
        var start = await context.Sales.ExecuteAsync(
            new StartSaleCommand(context.Defaults.MainWarehouseId),
            CancellationToken.None);
        await context.Sales.ExecuteAsync(
            new AddSaleLineCommand(start.Value, context.OilId, 1),
            CancellationToken.None);

        await context.Sales.ExecuteAsync(
            new ChangeSaleLineCommand(
                start.Value, context.OilId, 1,
                UnitPriceRials: Money.FromTomans(900_000).Rials,
                DiscountRials: 0,
                UpdateCatalogPrice: true),
            CancellationToken.None);

        // فاکتور بعدی باید قیمت جدید کالا را بردارد
        var next = await context.Sales.ExecuteAsync(
            new StartSaleCommand(context.Defaults.MainWarehouseId),
            CancellationToken.None);
        await context.Sales.ExecuteAsync(
            new AddSaleLineCommand(next.Value, context.OilId, 1),
            CancellationToken.None);
        var reloaded = await context.SaleRepository.GetAsync(next.Value, CancellationToken.None);

        Assert.Equal(900_000, Assert.Single(reloaded!.Lines).UnitPrice.ToTomansExact());
    }

    [Fact]
    public async Task CompletingWithAllowNegativeStockSucceedsAndPersistsTheShortfall()
    {
        var context = await SetupAsync();

        var start = await context.Sales.ExecuteAsync(
            new StartSaleCommand(context.Defaults.MainWarehouseId),
            CancellationToken.None);
        var saleId = start.Value;
        // Only 10 rice on record; cashier sells 12 anyway with the override on.
        await context.Sales.ExecuteAsync(
            new AddSaleLineCommand(saleId, context.RiceId, 12),
            CancellationToken.None);

        var complete = await context.Sales.ExecuteAsync(
            new CompleteSaleCommand(saleId, PaymentMethod.Cash, 0, 9, AllowNegativeStock: true),
            CancellationToken.None);

        Assert.True(complete.IsSuccess);
        var reloaded = await context.SaleRepository.GetAsync(saleId, CancellationToken.None);
        Assert.Equal(SaleStatus.Completed, reloaded!.Status);
        var riceLedger = await context.StockLedgers.GetAsync(
            context.RiceId, context.Defaults.MainWarehouseId, CancellationToken.None);
        Assert.Equal(-2, riceLedger!.AvailableQuantity.Value);
    }

    [Fact]
    public async Task CompletingWithInsufficientStockRollsBackTheWholeTransaction()
    {
        var context = await SetupAsync();

        var start = await context.Sales.ExecuteAsync(
            new StartSaleCommand(context.Defaults.MainWarehouseId),
            CancellationToken.None);
        var saleId = start.Value;
        await context.Sales.ExecuteAsync(
            new AddSaleLineCommand(saleId, context.RiceId, 999),
            CancellationToken.None);

        var complete = await context.Sales.ExecuteAsync(
            new CompleteSaleCommand(saleId, PaymentMethod.Cash, DiscountRials: 0, TaxRatePercent: 9),
            CancellationToken.None);

        Assert.False(complete.IsSuccess);
        Assert.Equal("sales.sale.insufficient-stock", complete.Error?.Code);

        // Nothing about the failed completion should have stuck: the sale is
        // still a Draft and the rice stock is untouched (10 received, 0 sold).
        var reloaded = await context.SaleRepository.GetAsync(saleId, CancellationToken.None);
        Assert.Equal(SaleStatus.Draft, reloaded!.Status);
        var riceLedger = await context.StockLedgers.GetAsync(
            context.RiceId, context.Defaults.MainWarehouseId, CancellationToken.None);
        Assert.Equal(10, riceLedger!.AvailableQuantity.Value);
    }

    [Fact]
    public async Task CompletedSalesGetAscendingNumbersThatSurviveAReloadAndDraftsGetNone()
    {
        var context = await SetupAsync();

        var first = await StartSaleWithAsync(context, context.RiceId, 1);
        var rejected = await StartSaleWithAsync(context, context.RiceId, 999);
        var second = await StartSaleWithAsync(context, context.OilId, 1);

        var firstComplete = await context.Sales.ExecuteAsync(
            new CompleteSaleCommand(first, PaymentMethod.Cash, 0, 9), CancellationToken.None);
        var rejectedComplete = await context.Sales.ExecuteAsync(
            new CompleteSaleCommand(rejected, PaymentMethod.Cash, 0, 9), CancellationToken.None);
        var secondComplete = await context.Sales.ExecuteAsync(
            new CompleteSaleCommand(second, PaymentMethod.Card, 0, 9), CancellationToken.None);

        Assert.True(firstComplete.IsSuccess);
        Assert.False(rejectedComplete.IsSuccess);
        Assert.True(secondComplete.IsSuccess);

        // A sale rejected by a business rule never reaches the sequence, so the
        // next real invoice follows straight on (no hole in the numbering).
        Assert.Equal(firstComplete.Value!.Number.Value + 1, secondComplete.Value!.Number.Value);

        var reloadedFirst = await context.SaleRepository.GetAsync(first, CancellationToken.None);
        var reloadedRejected = await context.SaleRepository.GetAsync(rejected, CancellationToken.None);
        Assert.Equal(firstComplete.Value.Number, reloadedFirst!.Number);
        Assert.Null(reloadedRejected!.Number);
    }

    [Fact]
    public async Task ACreditSaleToACustomerShowsOnTheirAccountUntilTheyPay()
    {
        var context = await SetupAsync();
        var customers = new FirebirdCustomerService(
            context.Factory,
            new TestUserContext(Guid.NewGuid()),
            new TestClock(new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero)));
        var customer = await customers.ExecuteAsync(
            new QuickCreateCustomerCommand("محمد رضایی", "09123456789"), CancellationToken.None);

        var saleId = await StartSaleWithAsync(context, context.OilId, 2); // ۲ × ۸۵۰٬۰۰۰
        var assign = await context.Sales.ExecuteAsync(
            new SetSaleCustomerCommand(saleId, customer.Value), CancellationToken.None);
        Assert.True(assign.IsSuccess);

        var complete = await context.Sales.ExecuteAsync(
            new CompleteSaleCommand(saleId, PaymentMethod.Credit, 0, TaxRatePercent: 0), CancellationToken.None);
        Assert.True(complete.IsSuccess);

        var reloaded = await context.SaleRepository.GetAsync(saleId, CancellationToken.None);
        Assert.Equal(customer.Value, reloaded!.CustomerId);
        Assert.Equal(complete.Value!.Totals, reloaded.Totals); // مبلغ نهایی ثبت و بازخوانی شد

        var owing = await customers.ExecuteAsync(new GetCustomerAccountQuery(customer.Value), CancellationToken.None);
        Assert.Equal(1_700_000, owing.Value!.Debt.ToTomansExact());
        Assert.Equal([complete.Value.Number.Value], owing.Value.OpenInvoiceNumbers);

        var paid = await customers.ExecuteAsync(
            new RecordCustomerPaymentCommand(
                customer.Value, Money.FromTomans(1_700_000).Rials, CustomerPaymentMethod.Card, null),
            CancellationToken.None);
        Assert.True(paid.IsSuccess);

        var settled = await customers.ExecuteAsync(new GetCustomerAccountQuery(customer.Value), CancellationToken.None);
        Assert.Equal(0, settled.Value!.Debt.Rials);
        Assert.Empty(settled.Value.OpenInvoiceNumbers);
    }

    [Fact]
    public async Task TheCustomerListShowsEachBalanceFiltersDebtorsAndEditsAndArchives()
    {
        var context = await SetupAsync();
        var customers = new FirebirdCustomerService(
            context.Factory,
            new TestUserContext(Guid.NewGuid()),
            new TestClock(new DateTimeOffset(2026, 9, 21, 10, 0, 0, TimeSpan.Zero)));

        // محمد: ۵۰۰٬۰۰۰ مانده‌ی اول دوره + یک فاکتور نسیه‌ی ۱٬۷۰۰٬۰۰۰ − ۷۰۰٬۰۰۰ دریافتی = ۱٬۵۰۰٬۰۰۰ بدهکار
        var mohammad = (await customers.ExecuteAsync(
            new CreateCustomerCommand(
                Person("محمد", "رضایی", "09123456789", nationalId: "0499370899", address: "تهران، خیابان ولیعصر", postalCode: "1234567890", birthDate: "۱۳۷۰/۰۵/۱۲"),
                Money.FromTomans(3_000_000),
                Money.FromTomans(500_000)),
            CancellationToken.None)).Value;
        var saleId = await StartSaleWithAsync(context, context.OilId, 2);
        await context.Sales.ExecuteAsync(new SetSaleCustomerCommand(saleId, mohammad), CancellationToken.None);
        var sold = await context.Sales.ExecuteAsync(
            new CompleteSaleCommand(saleId, PaymentMethod.Credit, 0, TaxRatePercent: 0), CancellationToken.None);
        Assert.True(sold.IsSuccess);
        await customers.ExecuteAsync(
            new RecordCustomerPaymentCommand(mohammad, Money.FromTomans(700_000).Rials, CustomerPaymentMethod.Cash, null),
            CancellationToken.None);

        // علی: بدون سابقه؛ زهرا: بیشتر از مانده‌ی اول دوره پرداخته = بستانکار
        var ali = (await customers.ExecuteAsync(
            new CreateCustomerCommand(Person("علی", "احمدی", "09121111111"), Money.Zero, Money.Zero), CancellationToken.None)).Value;
        var zahra = (await customers.ExecuteAsync(
            new CreateCustomerCommand(Person("زهرا", "کریمی", "09122222222"), Money.Zero, Money.FromTomans(100_000)), CancellationToken.None)).Value;
        await customers.ExecuteAsync(
            new RecordCustomerPaymentCommand(zahra, Money.FromTomans(250_000).Rials, CustomerPaymentMethod.Card, null),
            CancellationToken.None);

        // یک شرکت با شناسه‌ی ملی و کد اقتصادی و شماره ثبت
        var company = (await customers.ExecuteAsync(
            new CreateCustomerCommand(
                new CustomerProfileInput(
                    CustomerKind.Legal, null, null, "فروشگاه‌های زنجیره‌ای مهر", "09125550000", "10380284790", "411123456789",
                    "123456", null, "02188001122", "info@mehr.example", null, "تهران", null),
                Money.FromTomans(10_000_000),
                Money.Zero),
            CancellationToken.None)).Value;

        // همه‌ی مشتری‌ها؛ جمع بدهی فقط بدهکارها را جمع می‌زند و جمع بستانکاری فقط بستانکارها را
        var all = await customers.ExecuteAsync(new CustomerListQuery(null, false), CancellationToken.None);
        Assert.Equal(4, all.TotalCount);
        Assert.Equal(1_500_000, all.TotalDebt.ToTomansExact());
        Assert.Equal(150_000, all.TotalAdvance.ToTomansExact());
        var mohammadRow = all.Rows.Single(row => row.Id == mohammad);
        Assert.Equal("محمد رضایی", mohammadRow.Name);
        Assert.Equal(1_500_000, mohammadRow.Debt.ToTomansExact());
        Assert.Equal(0, mohammadRow.Advance.Rials);
        Assert.Equal(3_000_000, mohammadRow.CreditLimit.ToTomansExact());
        Assert.Equal(500_000, mohammadRow.OpeningBalance.ToTomansExact());
        Assert.Equal("تهران، خیابان ولیعصر", mohammadRow.Address);
        Assert.Equal("0499370899", mohammadRow.Profile.NationalId);
        Assert.Equal("1234567890", mohammadRow.Profile.PostalCode);
        Assert.Equal("1370/05/12", mohammadRow.Profile.BirthDate);
        Assert.Equal(CustomerKind.Individual, mohammadRow.Profile.Kind);
        var zahraRow = all.Rows.Single(row => row.Id == zahra);
        Assert.Equal(0, zahraRow.Debt.Rials);
        Assert.Equal(150_000, zahraRow.Advance.ToTomansExact());

        // شماره اشتراک به ترتیب ساخت داده می‌شود و تکراری نیست
        Assert.Equal([1L, 2L, 3L, 4L], all.Rows.Select(row => row.Code).OrderBy(code => code));

        // شرکت با نام شرکت نمایش داده می‌شود و همه‌ی هویتش ذخیره است
        var companyRow = all.Rows.Single(row => row.Id == company);
        Assert.Equal("فروشگاه‌های زنجیره‌ای مهر", companyRow.Name);
        Assert.Equal(CustomerKind.Legal, companyRow.Profile.Kind);
        Assert.Equal("10380284790", companyRow.Profile.NationalId);
        Assert.Equal("411123456789", companyRow.Profile.EconomicCode);
        Assert.Equal("123456", companyRow.Profile.RegistrationNumber);
        Assert.Equal("02188001122", companyRow.Profile.Phone);
        Assert.Equal("info@mehr.example", companyRow.Profile.Email);

        // عدد لیست همان عددی است که بنر مشتری در صفحه‌ی فروش نشان می‌دهد
        var account = (await customers.ExecuteAsync(new GetCustomerAccountQuery(mohammad), CancellationToken.None)).Value!;
        Assert.Equal(account.Debt, mohammadRow.Debt);

        // فقط بدهکارها
        var debtors = await customers.ExecuteAsync(new CustomerListQuery(null, true), CancellationToken.None);
        Assert.Equal(mohammad, Assert.Single(debtors.Rows).Id);
        Assert.Equal(1, debtors.TotalCount);

        // جست‌وجو: نیم‌کلمه‌ی نام، نام شرکت، بخشی از موبایل با رقم فارسی، کد ملی، شناسه ملی، شماره اشتراک، و بدون نتیجه
        Assert.Equal(ali, Assert.Single((await customers.ExecuteAsync(new CustomerListQuery("لی اح", false), CancellationToken.None)).Rows).Id);
        Assert.Equal(company, Assert.Single((await customers.ExecuteAsync(new CustomerListQuery("زنجیره", false), CancellationToken.None)).Rows).Id);
        Assert.Equal(zahra, Assert.Single((await customers.ExecuteAsync(new CustomerListQuery("۰۹۱۲۲۲", false), CancellationToken.None)).Rows).Id);
        Assert.Equal(mohammad, Assert.Single((await customers.ExecuteAsync(new CustomerListQuery("۰۴۹۹۳۷", false), CancellationToken.None)).Rows).Id);
        Assert.Equal(company, Assert.Single((await customers.ExecuteAsync(new CustomerListQuery("10380", false), CancellationToken.None)).Rows).Id);
        Assert.Equal(zahra, Assert.Single((await customers.ExecuteAsync(new CustomerListQuery("۳", false), CancellationToken.None)).Rows).Id);
        Assert.Empty((await customers.ExecuteAsync(new CustomerListQuery("چیزی-که-نیست", false), CancellationToken.None)).Rows);
        Assert.Empty((await customers.ExecuteAsync(new CustomerListQuery("زهرا", true), CancellationToken.None)).Rows);

        // ویرایش: نوع، نام، موبایل، نشانی، سقف اعتبار؛ مانده‌ی اول دوره و شماره اشتراک دست نمی‌خورند
        var edited = await customers.ExecuteAsync(
            new UpdateCustomerCommand(ali, Person("علی", "احمدی‌نژاد", "09123334444", address: "کرج", email: "ali@example.com"), Money.FromTomans(1_000_000)),
            CancellationToken.None);
        Assert.True(edited.IsSuccess, edited.Error?.Message);
        var aliRow = (await customers.ExecuteAsync(new CustomerListQuery("نژاد", false), CancellationToken.None)).Rows.Single();
        Assert.Equal("09123334444", aliRow.Mobile);
        Assert.Equal("کرج", aliRow.Address);
        Assert.Equal("ali@example.com", aliRow.Profile.Email);
        Assert.Equal(1_000_000, aliRow.CreditLimit.ToTomansExact());
        Assert.Equal(all.Rows.Single(row => row.Id == ali).Code, aliRow.Code);

        // موبایل یا کد ملی تکراری (چه هنگام ساخت چه ویرایش) رد می‌شود و چیزی عوض نمی‌شود
        var duplicateMobile = await customers.ExecuteAsync(
            new UpdateCustomerCommand(ali, Person("علی", "احمدی‌نژاد", "09123456789"), Money.Zero), CancellationToken.None);
        Assert.Equal("customers.customer.duplicate-mobile", duplicateMobile.Error?.Code);
        Assert.Equal("09123334444", (await customers.ExecuteAsync(new CustomerListQuery("نژاد", false), CancellationToken.None)).Rows.Single().Mobile);
        var duplicateNationalId = await customers.ExecuteAsync(
            new CreateCustomerCommand(Person("دیگری", "کسی", "09127776666", nationalId: "0499370899"), Money.Zero, Money.Zero),
            CancellationToken.None);
        Assert.Equal("customers.customer.duplicate-national-id", duplicateNationalId.Error?.Code);
        Assert.Equal(4, (await customers.ExecuteAsync(new CustomerListQuery(null, false), CancellationToken.None)).TotalCount);

        // کد ملی و شناسه‌ی ملیِ اشتباه (رقم کنترل) قبل از ذخیره رد می‌شوند
        var badNationalCode = await customers.ExecuteAsync(
            new CreateCustomerCommand(Person("الف", "ب", "09127776666", nationalId: "0499370898"), Money.Zero, Money.Zero), CancellationToken.None);
        Assert.Equal("customers.customer.invalid", badNationalCode.Error?.Code);
        Assert.Contains("کد ملی", badNationalCode.Error?.Message);

        // «حذف»: مشتری بدهکار رد می‌شود؛ مشتری بی‌بدهی بایگانی می‌شود و از لیست می‌رود
        var refused = await customers.ExecuteAsync(new ArchiveCustomerCommand(mohammad), CancellationToken.None);
        Assert.Equal("customers.customer.has-debt", refused.Error?.Code);
        Assert.True((await customers.ExecuteAsync(new ArchiveCustomerCommand(ali), CancellationToken.None)).IsSuccess);
        Assert.True((await customers.ExecuteAsync(new ArchiveCustomerCommand(ali), CancellationToken.None)).IsSuccess);
        var afterArchive = await customers.ExecuteAsync(new CustomerListQuery(null, false), CancellationToken.None);
        Assert.Equal(3, afterArchive.TotalCount);
        Assert.DoesNotContain(afterArchive.Rows, row => row.Id == ali);
        Assert.Equal(
            "customers.customer.archived",
            (await customers.ExecuteAsync(
                new UpdateCustomerCommand(ali, Person("x", null, "09123334444"), Money.Zero), CancellationToken.None)).Error?.Code);

        // بعد از تسویه‌ی کامل، محمد هم قابل حذف است
        await customers.ExecuteAsync(
            new RecordCustomerPaymentCommand(mohammad, Money.FromTomans(1_500_000).Rials, CustomerPaymentMethod.Cash, null),
            CancellationToken.None);
        Assert.True((await customers.ExecuteAsync(new ArchiveCustomerCommand(mohammad), CancellationToken.None)).IsSuccess);
    }

    private static CustomerProfileInput Person(
        string? firstName,
        string? lastName,
        string mobile,
        string? nationalId = null,
        string? address = null,
        string? postalCode = null,
        string? email = null,
        string? birthDate = null) =>
        new(CustomerKind.Individual, firstName, lastName, null, mobile, nationalId, null, null, postalCode, null, email, birthDate, address, null);

    [Fact]
    public async Task TheDayListSplitsInvoicesAtTehranMidnightNotUtcMidnight()
    {
        var context = await SetupAsync();
        var afternoon = ServiceAt(context, new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero));      // ۲۳ شهریور ۱۳:۳۰
        var afterMidnight = ServiceAt(context, new DateTimeOffset(2026, 9, 14, 21, 0, 0, TimeSpan.Zero));  // ۲۴ شهریور ۰۰:۳۰

        var first = await StartSaleWithAsync(context, context.RiceId, 1);
        var second = await StartSaleWithAsync(context, context.OilId, 1);
        var firstDone = await afternoon.ExecuteAsync(
            new CompleteSaleCommand(first, PaymentMethod.Cash, 0, 0), CancellationToken.None);
        var secondDone = await afterMidnight.ExecuteAsync(
            new CompleteSaleCommand(second, PaymentMethod.Card, 0, 0), CancellationToken.None);

        var day23 = await context.Sales.ExecuteAsync(
            new ListSalesOfDayQuery(PersianDate.Create(1405, 6, 23)), CancellationToken.None);
        var day24 = await context.Sales.ExecuteAsync(
            new ListSalesOfDayQuery(PersianDate.Create(1405, 6, 24)), CancellationToken.None);

        var only23 = Assert.Single(day23.Sales);
        Assert.Equal(firstDone.Value!.Number, only23.Number);
        Assert.Equal(245_000, only23.Amount!.Value.ToTomansExact());
        Assert.Equal(1, only23.ItemCount);
        Assert.Equal(245_000, day23.TotalsByPaymentMethod[PaymentMethod.Cash].ToTomansExact());

        var only24 = Assert.Single(day24.Sales);
        Assert.Equal(secondDone.Value!.Number, only24.Number);
        Assert.Equal(PaymentMethod.Card, only24.PaymentMethod);
    }

    [Fact]
    public async Task HeldInvoicesAreDraftsWithItemsAndShowTheirSubtotalAndCustomer()
    {
        var context = await SetupAsync();
        var customers = new FirebirdCustomerService(
            context.Factory, new TestUserContext(Guid.NewGuid()), new TestClock(DateTimeOffset.UtcNow));
        var customer = await customers.ExecuteAsync(
            new QuickCreateCustomerCommand("محمد رضایی", "09123456789"), CancellationToken.None);

        var held = await StartSaleWithAsync(context, context.RiceId, 3);
        await context.Sales.ExecuteAsync(
            new ChangeSaleLineCommand(held, context.RiceId, 3, Money.FromTomans(245_000).Rials, Money.FromTomans(35_000).Rials),
            CancellationToken.None);
        await context.Sales.ExecuteAsync(new SetSaleCustomerCommand(held, customer.Value), CancellationToken.None);

        await context.Sales.ExecuteAsync(new StartSaleCommand(context.Defaults.MainWarehouseId), CancellationToken.None); // خالی
        var completed = await StartSaleWithAsync(context, context.OilId, 1);
        await context.Sales.ExecuteAsync(new CompleteSaleCommand(completed, PaymentMethod.Cash, 0, 0), CancellationToken.None);

        var list = await context.Sales.ExecuteAsync(new ListHeldSalesQuery(), CancellationToken.None);

        var item = Assert.Single(list);
        Assert.Equal(held, item.SaleId);
        Assert.Null(item.Number);
        Assert.Equal("محمد رضایی", item.CustomerName);
        Assert.Equal(1, item.ItemCount);

        var aggregate = await context.SaleRepository.GetAsync(held, CancellationToken.None);
        Assert.Equal(aggregate!.Subtotal, item.Amount); // ۳×۲۴۵٬۰۰۰ − ۳۵٬۰۰۰ = ۷۰۰٬۰۰۰
        Assert.Equal(700_000, item.Amount!.Value.ToTomansExact());
    }

    [Fact]
    public async Task DetailsShowNamesUnitsAndStockThatAgreesWithTheLedgerEvenWhenNegative()
    {
        var context = await SetupAsync();

        // ۱۰ برنج موجود؛ ۱۲ تا با اجازه‌ی فروش منفی فروخته می‌شود → موجودی −۲
        var oversold = await StartSaleWithAsync(context, context.RiceId, 12);
        await context.Sales.ExecuteAsync(
            new CompleteSaleCommand(oversold, PaymentMethod.Cash, 0, 0, AllowNegativeStock: true), CancellationToken.None);

        var next = await StartSaleWithAsync(context, context.RiceId, 1);
        var details = await context.Sales.ExecuteAsync(new GetSaleDetailsQuery(next), CancellationToken.None);

        Assert.True(details.IsSuccess);
        var line = Assert.Single(details.Value!.Lines);
        Assert.Equal("برنج ایرانی", line.ProductName);
        Assert.Equal("RICE-1", line.Sku);
        Assert.Equal("عدد", line.UnitSymbol);

        var ledger = await context.StockLedgers.GetAsync(context.RiceId, context.Defaults.MainWarehouseId, CancellationToken.None);
        Assert.Equal(ledger!.AvailableQuantity.Value, line.Available.Value);
        Assert.Equal(-2, line.Available.Value);
    }

    [Fact]
    public async Task BrowsingFiltersByCategoryTreeTopSellersAndLowStockAndPages()
    {
        var context = await SetupAsync();
        var setup = new FirebirdRetailSetupService(
            context.Factory, new TestUserContext(Guid.NewGuid()), new TestClock(new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero)));

        // «خشکبار» زیر «مواد غذایی»؛ کالای «گردو» فقط ۳ عدد موجودی دارد
        var foods = await context.Sales.ExecuteAsync(
            new BrowseProductsForSaleQuery(context.Defaults.MainWarehouseId, null, ProductListFilter.All), CancellationToken.None);
        var parentCategory = await setup.ExecuteAsync(new CreateCategoryCommand("خوراکی", null, 2), CancellationToken.None);
        var nuts = await setup.ExecuteAsync(new CreateCategoryCommand("خشکبار", parentCategory.Value, 1), CancellationToken.None);
        var walnut = await setup.ExecuteAsync(
            new CreateProductCommand("گردو", "NUT-1", nuts.Value, context.Defaults.EachUnitId, Money.FromTomans(1_250_000), ["6260000009009"]),
            CancellationToken.None);
        await setup.ExecuteAsync(
            new ReceiveOpeningStockCommand(walnut.Value, context.Defaults.MainWarehouseId, 3, 900_000_0, new DateOnly(2026, 9, 14)),
            CancellationToken.None);

        var inParent = await context.Sales.ExecuteAsync(
            new BrowseProductsForSaleQuery(context.Defaults.MainWarehouseId, parentCategory.Value, ProductListFilter.All),
            CancellationToken.None);
        var item = Assert.Single(inParent.Items); // زیردسته هم شامل می‌شود
        Assert.Equal("گردو", item.Name);
        Assert.Equal(3, item.Available.Value);
        Assert.Equal("عدد", item.UnitSymbol);

        var lowStock = await context.Sales.ExecuteAsync(
            new BrowseProductsForSaleQuery(context.Defaults.MainWarehouseId, null, ProductListFilter.LowStock), CancellationToken.None);
        Assert.Equal("گردو", lowStock.Items[0].Name); // کمترین موجودی اول

        // اوایل هیچ‌چیز فروش نرفته → پرفروش خالی؛ بعد از فروش روغن، روغن اول است
        var noneSold = await context.Sales.ExecuteAsync(
            new BrowseProductsForSaleQuery(context.Defaults.MainWarehouseId, null, ProductListFilter.TopSelling), CancellationToken.None);
        Assert.Empty(noneSold.Items);

        var sold = await StartSaleWithAsync(context, context.OilId, 4);
        await context.Sales.ExecuteAsync(new CompleteSaleCommand(sold, PaymentMethod.Cash, 0, 0), CancellationToken.None);
        var riceSale = await StartSaleWithAsync(context, context.RiceId, 1);
        await context.Sales.ExecuteAsync(new CompleteSaleCommand(riceSale, PaymentMethod.Cash, 0, 0), CancellationToken.None);

        var topSelling = await context.Sales.ExecuteAsync(
            new BrowseProductsForSaleQuery(context.Defaults.MainWarehouseId, null, ProductListFilter.TopSelling), CancellationToken.None);
        Assert.Equal(["روغن حیوانی", "برنج ایرانی"], topSelling.Items.Select(product => product.Name));

        var firstPage = await context.Sales.ExecuteAsync(
            new BrowseProductsForSaleQuery(context.Defaults.MainWarehouseId, null, ProductListFilter.All, Page: 1, PageSize: 2),
            CancellationToken.None);
        var secondPage = await context.Sales.ExecuteAsync(
            new BrowseProductsForSaleQuery(context.Defaults.MainWarehouseId, null, ProductListFilter.All, Page: 2, PageSize: 2),
            CancellationToken.None);
        Assert.Equal(3, firstPage.TotalCount);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Single(secondPage.Items);
        Assert.Equal(2, firstPage.PageCount);
        Assert.Equal(2, foods.TotalCount); // قبل از افزودن گردو
    }

    [Fact]
    public async Task AScannedCodeFindsTheProductByAnyBarcodeOrSkuExactly()
    {
        var context = await SetupAsync();
        var search = new SearchProductsHandler(new FirebirdProductSearchReader(context.Factory));

        Assert.Equal(context.RiceId, (await search.FindByExactCodeAsync("6260000009001", CancellationToken.None))?.Id);
        Assert.Equal(context.OilId, (await search.FindByExactCodeAsync("oil-1", CancellationToken.None))?.Id);
        Assert.Null(await search.FindByExactCodeAsync("626000000900", CancellationToken.None)); // ناقص → هیچ
    }

    [Fact]
    public async Task CancellingADraftTakesItOffTheHeldList()
    {
        var context = await SetupAsync();
        var draft = await StartSaleWithAsync(context, context.RiceId, 1);

        var cancel = await context.Sales.ExecuteAsync(new CancelSaleCommand(draft), CancellationToken.None);

        Assert.True(cancel.IsSuccess);
        Assert.Empty(await context.Sales.ExecuteAsync(new ListHeldSalesQuery(), CancellationToken.None));
    }

    private static FirebirdSalesService ServiceAt(TestContext context, DateTimeOffset now)
    {
        return new FirebirdSalesService(context.Factory, new TestUserContext(Guid.NewGuid()), new TestClock(now));
    }

    private static async Task<SaleId> StartSaleWithAsync(TestContext context, ProductId productId, decimal quantity)
    {
        var start = await context.Sales.ExecuteAsync(
            new StartSaleCommand(context.Defaults.MainWarehouseId),
            CancellationToken.None);
        await context.Sales.ExecuteAsync(
            new AddSaleLineCommand(start.Value, productId, quantity),
            CancellationToken.None);
        return start.Value;
    }

    private static async Task<TestContext> SetupAsync()
    {
        var database = FirebirdTestDatabase.Create();
        var factory = new FirebirdConnectionFactory(database.Options);
        var defaults = await new FirebirdDatabaseBootstrapper(factory)
            .InitializeAsync(CancellationToken.None);
        var userContext = new TestUserContext(Guid.NewGuid());
        var clock = new TestClock(new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero));

        var setup = new FirebirdRetailSetupService(factory, userContext, clock);
        var category = await setup.ExecuteAsync(
            new CreateCategoryCommand("مواد غذایی", null, 1), CancellationToken.None);

        var rice = await setup.ExecuteAsync(
            new CreateProductCommand(
                "برنج ایرانی", "RICE-1", category.Value, defaults.EachUnitId,
                Money.FromTomans(245_000), ["6260000009001"]),
            CancellationToken.None);
        var oil = await setup.ExecuteAsync(
            new CreateProductCommand(
                "روغن حیوانی", "OIL-1", category.Value, defaults.EachUnitId,
                Money.FromTomans(850_000), ["6260000009002"]),
            CancellationToken.None);

        await setup.ExecuteAsync(
            new ReceiveOpeningStockCommand(
                rice.Value, defaults.MainWarehouseId, 10, 200_000_0, DateOnly.FromDateTime(clock.UtcNow.Date)),
            CancellationToken.None);
        await setup.ExecuteAsync(
            new ReceiveOpeningStockCommand(
                oil.Value, defaults.MainWarehouseId, 10, 700_000_0, DateOnly.FromDateTime(clock.UtcNow.Date)),
            CancellationToken.None);

        var saleRepository = new SaleReader(factory);
        var stockLedgers = new StockLedgerReader(factory);

        return new TestContext(
            database,
            factory,
            defaults,
            rice.Value,
            oil.Value,
            new FirebirdSalesService(factory, userContext, clock),
            saleRepository,
            stockLedgers);
    }

    private sealed record TestContext(
        FirebirdTestDatabase Database,
        FirebirdConnectionFactory Factory,
        RetailSetupDefaults Defaults,
        ProductId RiceId,
        ProductId OilId,
        FirebirdSalesService Sales,
        ISaleRepository SaleRepository,
        IStockLedgerRepository StockLedgers);

    private sealed record TestUserContext(Guid UserId) : IUserContext;

    private sealed record TestClock(DateTimeOffset UtcNow) : IClock;

    /// <summary>
    /// A read-only sale lookup for assertions, using its own short-lived unit
    /// of work per call — Firebird's default isolation would otherwise let a
    /// long-lived read transaction miss commits made afterward by
    /// <see cref="FirebirdSalesService"/>'s own short transactions.
    /// </summary>
    private sealed class SaleReader : ISaleRepository
    {
        private readonly FirebirdConnectionFactory _factory;

        public SaleReader(FirebirdConnectionFactory factory)
        {
            _factory = factory;
        }

        public async Task<Sale?> GetAsync(SaleId saleId, CancellationToken cancellationToken)
        {
            await using var unitOfWork = await FirebirdUnitOfWork
                .CreateAsync(_factory, cancellationToken)
                .ConfigureAwait(false);
            return await new FirebirdSaleRepository(unitOfWork)
                .GetAsync(saleId, cancellationToken)
                .ConfigureAwait(false);
        }

        public Task SaveAsync(Sale sale, CancellationToken cancellationToken)
        {
            throw new NotSupportedException("Test reader is read-only.");
        }

        public async Task<bool> HasCorrectionAsync(SaleId saleId, CancellationToken cancellationToken)
        {
            await using var unitOfWork = await FirebirdUnitOfWork
                .CreateAsync(_factory, cancellationToken)
                .ConfigureAwait(false);
            return await new FirebirdSaleRepository(unitOfWork)
                .HasCorrectionAsync(saleId, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A read-only stock-ledger lookup for assertions, using its own
    /// short-lived unit of work per call so it always sees what has actually
    /// been committed by the service under test (not a shared, possibly stale,
    /// in-progress transaction).
    /// </summary>
    private sealed class StockLedgerReader : IStockLedgerRepository
    {
        private readonly FirebirdConnectionFactory _factory;

        public StockLedgerReader(FirebirdConnectionFactory factory)
        {
            _factory = factory;
        }

        public async Task<Domain.Inventory.StockLedger?> GetAsync(
            ProductId productId,
            Domain.Inventory.WarehouseId warehouseId,
            CancellationToken cancellationToken)
        {
            await using var unitOfWork = await FirebirdUnitOfWork
                .CreateAsync(_factory, cancellationToken)
                .ConfigureAwait(false);
            return await new FirebirdStockLedgerRepository(unitOfWork)
                .GetAsync(productId, warehouseId, cancellationToken)
                .ConfigureAwait(false);
        }

        public Task SaveAsync(Domain.Inventory.StockLedger ledger, CancellationToken cancellationToken)
        {
            throw new NotSupportedException("Test reader is read-only.");
        }
    }
}
