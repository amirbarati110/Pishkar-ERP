using ERP.Application.Catalog;
using ERP.Application.Importing;
using ERP.Domain.Catalog;

namespace ERP.Application.Tests.Importing;

public sealed class ProductImportPreparerTests
{
    [Fact]
    public void PrepareMapsPersianHeadersNumbersCategoryAndUnit()
    {
        var categoryId = CategoryId.New();
        var unitId = UnitId.New();
        var table = new SpreadsheetTable(
            ["نام کالا", "کد کالا", "بارکد", "دسته‌بندی", "واحد", "قیمت فروش (تومان)"],
            [new SpreadsheetRow(2, ["برنج ایرانی", "RICE-1", "6260001000011", "مواد غذایی", "عدد", "۲۴۵٬۰۰۰"])]);
        var catalog = new CatalogLookupSnapshot(
            [new CategoryLookupItem(categoryId, "مواد غذایی", null, 0)],
            [new UnitLookupItem(unitId, "عدد", "عدد", false)],
            []);
        var preview = ProductImportPreparer.Prepare(table, catalog);

        Assert.True(preview.CanImport);
        var item = Assert.Single(preview.Items);
        Assert.Equal("برنج ایرانی", item.Name);
        Assert.Equal(2_450_000, item.SalePrice.Rials);
        Assert.Equal(categoryId, item.CategoryId);
        Assert.Equal(unitId, item.BaseUnitId);
    }

    [Fact]
    public void PrepareReportsUnknownCategoryOnItsExactRow()
    {
        var unitId = UnitId.New();
        var table = new SpreadsheetTable(
            ["نام کالا", "دسته‌بندی", "واحد", "قیمت فروش (تومان)"],
            [new SpreadsheetRow(7, ["چای ایرانی", "نوشیدنی گرم", "عدد", "۱۵۰٬۰۰۰"])]);
        var catalog = new CatalogLookupSnapshot(
            [],
            [new UnitLookupItem(unitId, "عدد", "عدد", false)],
            []);

        var preview = ProductImportPreparer.Prepare(table, catalog);

        Assert.False(preview.CanImport);
        var issue = Assert.Single(preview.Issues);
        Assert.Equal(7, issue.RowNumber);
        Assert.Equal("import.product.category-not-found", issue.Code);
        Assert.Equal("دسته‌بندی «نوشیدنی گرم» پیدا نشد.", issue.Message);
    }

    [Fact]
    public void PrepareReportsDuplicateBarcodeWithinTheFile()
    {
        var categoryId = CategoryId.New();
        var unitId = UnitId.New();
        var table = new SpreadsheetTable(
            ["نام کالا", "بارکد", "دسته‌بندی", "واحد", "قیمت فروش (تومان)"],
            [
                new SpreadsheetRow(2, ["دوغ", "6260001000099", "نوشیدنی", "عدد", "۳۵٬۰۰۰"]),
                new SpreadsheetRow(3, ["نوشابه", " 6260001000099 ", "نوشیدنی", "عدد", "۴۰٬۰۰۰"]),
            ]);
        var catalog = new CatalogLookupSnapshot(
            [new CategoryLookupItem(categoryId, "نوشیدنی", null, 0)],
            [new UnitLookupItem(unitId, "عدد", "عدد", false)],
            []);

        var preview = ProductImportPreparer.Prepare(table, catalog);

        Assert.False(preview.CanImport);
        var issue = Assert.Single(preview.Issues);
        Assert.Equal(3, issue.RowNumber);
        Assert.Equal("import.product.duplicate-barcode-in-file", issue.Code);
    }

    [Fact]
    public void PrepareReportsInvalidPriceInsteadOfThrowing()
    {
        var categoryId = CategoryId.New();
        var unitId = UnitId.New();
        var table = new SpreadsheetTable(
            ["نام کالا", "دسته‌بندی", "واحد", "قیمت فروش (تومان)"],
            [new SpreadsheetRow(12, ["روغن", "مواد غذایی", "عدد", "رایگان"])]);
        var catalog = new CatalogLookupSnapshot(
            [new CategoryLookupItem(categoryId, "مواد غذایی", null, 0)],
            [new UnitLookupItem(unitId, "عدد", "عدد", false)],
            []);

        var preview = ProductImportPreparer.Prepare(table, catalog);

        Assert.False(preview.CanImport);
        var issue = Assert.Single(preview.Issues);
        Assert.Equal(12, issue.RowNumber);
        Assert.Equal("import.product.invalid-price", issue.Code);
        Assert.Equal("قیمت فروش باید یک عدد صحیح و صفر یا بیشتر باشد.", issue.Message);
    }

    [Fact]
    public void PrepareReportsMissingRequiredHeaderAsFileIssue()
    {
        var table = new SpreadsheetTable(
            ["نام کالا", "دسته‌بندی", "واحد"],
            [new SpreadsheetRow(2, ["رب گوجه", "مواد غذایی", "عدد"])]);

        var preview = ProductImportPreparer.Prepare(
            table,
            new CatalogLookupSnapshot([], [], []));

        Assert.False(preview.CanImport);
        var issue = Assert.Single(preview.Issues);
        Assert.Null(issue.RowNumber);
        Assert.Equal("import.product.missing-column", issue.Code);
        Assert.Equal("ستون «قیمت فروش (تومان)» در فایل اکسل وجود ندارد.", issue.Message);
    }

    [Fact]
    public void PrepareReportsBlankProductNameOnItsExactRow()
    {
        var categoryId = CategoryId.New();
        var unitId = UnitId.New();
        var table = new SpreadsheetTable(
            ["نام کالا", "دسته‌بندی", "واحد", "قیمت فروش (تومان)"],
            [new SpreadsheetRow(4, [null, "مواد غذایی", "عدد", "10000"])]);
        var catalog = new CatalogLookupSnapshot(
            [new CategoryLookupItem(categoryId, "مواد غذایی", null, 0)],
            [new UnitLookupItem(unitId, "عدد", "عدد", false)],
            []);

        var preview = ProductImportPreparer.Prepare(table, catalog);

        Assert.False(preview.CanImport);
        var issue = Assert.Single(preview.Issues);
        Assert.Equal(4, issue.RowNumber);
        Assert.Equal("import.product.name-required", issue.Code);
        Assert.Equal("نام کالا را وارد کنید.", issue.Message);
    }

    [Fact]
    public void PrepareReportsBlankCategoryOnItsExactRowInsteadOfThrowing()
    {
        var unitId = UnitId.New();
        var table = new SpreadsheetTable(
            ["نام کالا", "دسته‌بندی", "واحد", "قیمت فروش (تومان)"],
            [new SpreadsheetRow(5, ["چای", null, "عدد", "10000"])]);
        var catalog = new CatalogLookupSnapshot(
            [],
            [new UnitLookupItem(unitId, "عدد", "عدد", false)],
            []);

        var preview = ProductImportPreparer.Prepare(table, catalog);

        Assert.False(preview.CanImport);
        var issue = Assert.Single(preview.Issues);
        Assert.Equal(5, issue.RowNumber);
        Assert.Equal("import.product.category-required", issue.Code);
        Assert.Equal("دسته‌بندی را وارد کنید.", issue.Message);
    }

    [Fact]
    public void PrepareReportsBlankUnitOnItsExactRowInsteadOfThrowing()
    {
        var categoryId = CategoryId.New();
        var table = new SpreadsheetTable(
            ["نام کالا", "دسته‌بندی", "واحد", "قیمت فروش (تومان)"],
            [new SpreadsheetRow(6, ["چای", "مواد غذایی", null, "10000"])]);
        var catalog = new CatalogLookupSnapshot(
            [new CategoryLookupItem(categoryId, "مواد غذایی", null, 0)],
            [],
            []);

        var preview = ProductImportPreparer.Prepare(table, catalog);

        Assert.False(preview.CanImport);
        var issue = Assert.Single(preview.Issues);
        Assert.Equal(6, issue.RowNumber);
        Assert.Equal("import.product.unit-required", issue.Code);
        Assert.Equal("واحد را وارد کنید.", issue.Message);
    }

    [Fact]
    public void PrepareReportsBlankPriceOnItsExactRowInsteadOfThrowing()
    {
        var categoryId = CategoryId.New();
        var unitId = UnitId.New();
        var table = new SpreadsheetTable(
            ["نام کالا", "دسته‌بندی", "واحد", "قیمت فروش (تومان)"],
            [new SpreadsheetRow(8, ["چای", "مواد غذایی", "عدد", null])]);
        var catalog = new CatalogLookupSnapshot(
            [new CategoryLookupItem(categoryId, "مواد غذایی", null, 0)],
            [new UnitLookupItem(unitId, "عدد", "عدد", false)],
            []);

        var preview = ProductImportPreparer.Prepare(table, catalog);

        Assert.False(preview.CanImport);
        var issue = Assert.Single(preview.Issues);
        Assert.Equal(8, issue.RowNumber);
        Assert.Equal("import.product.invalid-price", issue.Code);
        Assert.Equal("قیمت فروش باید یک عدد صحیح و صفر یا بیشتر باشد.", issue.Message);
    }
}
