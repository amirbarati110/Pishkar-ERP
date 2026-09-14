# Foundation, Catalog and Opening Inventory Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ساخت یک برنامه WinUI 3 قابل اجرا که دسته‌بندی چندسطحی، واحد، کالا، بارکد و موجودی اولیه را به‌صورت دستی مدیریت کند و تمام قواعد اصلی آن تست شده باشد.

**Architecture:** هسته دامنه هیچ وابستگی به UI و دیتابیس ندارد. Use Caseها در Application تعریف می‌شوند، Firebird آن‌ها را در Persistence ذخیره می‌کند و WinUI فقط ViewModelها و Contractهای Application را مصرف می‌کند. Stock Movement مرجع حقیقت موجودی است و موجودی نمایشی از روی آن ساخته می‌شود.

**Tech Stack:** C# 14، .NET SDK 10.0.401، WinUI 3 / Windows App SDK Stable، MVVM، Firebird 5، FirebirdSql.Data.FirebirdClient و xUnit. برای کاهش وابستگی و ریسک مجوز تجاری، Assertionها با خود xUnit نوشته می‌شوند.

**Spec:** `docs/superpowers/specs/2026-09-14-retail-erp-milestone-1-design.md`

## Global Constraints

- زبان و UI پیش‌فرض فارسی و راست‌به‌چپ است.
- Target Framework تمام پروژه‌های هسته `net10.0` و Desktop برابر `net10.0-windows` با Windows SDK رسمی است.
- هیچ قانون دامنه‌ای در Code-behind قرار نمی‌گیرد.
- مبلغ در دیتابیس ریال و عدد صحیح است؛ نمایش پیش‌فرض تومان است.
- هر عملیات تغییردهنده دارای Validation و در صورت حساس‌بودن Audit است.
- اسناد و Movementهای ثبت‌شده حذف فیزیکی نمی‌شوند.
- هر Task با تست شکست‌خورده آغاز و با اجرای کامل تست‌ها تمام می‌شود.
- هیچ صفحه، دکمه یا داده نمایشی بدون عملکرد واقعی وارد Build نمی‌شود.

---

### Task 1: Toolchain, repository and solution boundary

**Files:**
- Create: `.gitignore`
- Create: `global.json`
- Create: `Directory.Build.props`
- Create: `Directory.Packages.props`
- Create: `RetailERP.slnx`
- Create: `src/ERP.Domain/ERP.Domain.csproj`
- Create: `src/ERP.Application/ERP.Application.csproj`
- Create: `src/ERP.Infrastructure/ERP.Infrastructure.csproj`
- Create: `src/ERP.Persistence/ERP.Persistence.csproj`
- Create: `tests/ERP.Domain.Tests/ERP.Domain.Tests.csproj`
- Create: `tests/ERP.Architecture.Tests/ERP.Architecture.Tests.csproj`

**Interfaces:**
- Consumes: Windows x64، Git و WinGet.
- Produces: Solution قابل Restore/Build و dependency direction قابل تست.

- [x] **Step 1: نصب ابزارهای رسمی و بررسی نسخه**

```powershell
winget install --id Microsoft.DotNet.SDK.10 --exact --silent --accept-package-agreements --accept-source-agreements
winget install --id Microsoft.winappcli --exact --silent --accept-package-agreements --accept-source-agreements
dotnet --version
winapp --version
```

Expected: `dotnet` نسخه `10.0.401` یا patch سازگار جدیدتر و `winapp` نسخه `0.6.0` یا جدیدتر را نشان دهد.

- [x] **Step 2: ایجاد Git و فایل‌های Build**

`global.json`:

```json
{
  "sdk": {
    "version": "10.0.401",
    "rollForward": "latestPatch",
    "allowPrerelease": false
  }
}
```

`Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <LangVersion>14</LangVersion>
  </PropertyGroup>
</Project>
```

- [x] **Step 3: ایجاد Solution و پروژه‌ها**

```powershell
dotnet new sln --name RetailERP --format slnx
dotnet new classlib --name ERP.Domain --output src/ERP.Domain
dotnet new classlib --name ERP.Application --output src/ERP.Application
dotnet new classlib --name ERP.Infrastructure --output src/ERP.Infrastructure
dotnet new classlib --name ERP.Persistence --output src/ERP.Persistence
dotnet new xunit --name ERP.Domain.Tests --output tests/ERP.Domain.Tests
dotnet new xunit --name ERP.Architecture.Tests --output tests/ERP.Architecture.Tests
dotnet sln RetailERP.slnx add src/ERP.Domain/ERP.Domain.csproj src/ERP.Application/ERP.Application.csproj src/ERP.Infrastructure/ERP.Infrastructure.csproj src/ERP.Persistence/ERP.Persistence.csproj tests/ERP.Domain.Tests/ERP.Domain.Tests.csproj tests/ERP.Architecture.Tests/ERP.Architecture.Tests.csproj
```

- [x] **Step 4: نوشتن تست جهت وابستگی‌ها**

`tests/ERP.Architecture.Tests/ProjectReferenceTests.cs` باید XML پروژه‌ها را بخواند و ثابت کند `ERP.Domain` هیچ ProjectReference ندارد و `ERP.Application` فقط به `ERP.Domain` وابسته است.

- [x] **Step 5: اجرای Build و تست**

```powershell
dotnet restore RetailERP.slnx
dotnet build RetailERP.slnx --no-restore
dotnet test RetailERP.slnx --no-build
```

Expected: Build و همه تست‌ها PASS.

- [x] **Step 6: Commit**

```powershell
git add .
git commit -m "build: bootstrap clean architecture solution"
```

### Task 2: Shared domain kernel and money safety

**Files:**
- Create: `src/ERP.Domain/Common/Entity.cs`
- Create: `src/ERP.Domain/Common/DomainException.cs`
- Create: `src/ERP.Domain/Common/IDomainEvent.cs`
- Create: `src/ERP.Domain/Common/Money.cs`
- Create: `src/ERP.Domain/Common/Quantity.cs`
- Test: `tests/ERP.Domain.Tests/Common/MoneyTests.cs`
- Test: `tests/ERP.Domain.Tests/Common/QuantityTests.cs`

**Interfaces:**
- Produces: `Money.FromRials(long)`, `Money.FromTomans(long)`, `Money.ToTomansExact()`, `Quantity.Create(decimal)`.

- [x] **Step 1: تست شکست‌خورده Money**

```csharp
[Fact]
public void FromTomans_converts_to_rials_without_rounding()
{
    Assert.Equal(125_000, Money.FromTomans(12_500).Rials);
}

[Fact]
public void Add_rejects_overflow()
{
    var action = () => Money.FromRials(long.MaxValue).Add(Money.FromRials(1));
    Assert.Throws<OverflowException>(action);
}
```

- [x] **Step 2: اجرای تست و مشاهده FAIL**

Run: `dotnet test tests/ERP.Domain.Tests --filter FullyQualifiedName~MoneyTests`

- [x] **Step 3: پیاده‌سازی حداقلی با arithmetic checked و Value Equality**

`Money` باید `readonly record struct` باشد، مقدار منفی را فقط برای عملیات صریح حسابداری بپذیرد و تبدیل تومان به ریال را با `checked(tomans * 10)` انجام دهد.

- [x] **Step 4: تست Quantity**

```csharp
[Theory]
[InlineData(0)]
[InlineData(-0.001)]
public void Create_rejects_non_positive_value(decimal value)
{
    var action = () => Quantity.Create(value);
    Assert.Throws<DomainException>(action);
}
```

- [x] **Step 5: اجرای همه تست‌ها و Commit**

```powershell
dotnet test RetailERP.slnx
git add src/ERP.Domain tests/ERP.Domain.Tests
git commit -m "feat: add safe money and quantity primitives"
```

### Task 3: Hierarchical category aggregate

**Files:**
- Create: `src/ERP.Domain/Catalog/Category.cs`
- Create: `src/ERP.Domain/Catalog/CategoryId.cs`
- Create: `src/ERP.Domain/Catalog/CategoryStatus.cs`
- Create: `src/ERP.Domain/Catalog/Events/CategoryMoved.cs`
- Test: `tests/ERP.Domain.Tests/Catalog/CategoryTests.cs`

**Interfaces:**
- Produces: `Category.Create(string name, CategoryId? parentId, int sortOrder)`, `Rename`, `MoveTo`, `Archive`, `Restore`, `SetVisibility`.

- [x] **Step 1: تست نام و ساخت دسته**

```csharp
[Theory]
[InlineData("")]
[InlineData("   ")]
public void Create_rejects_empty_name(string name)
{
    var action = () => Category.Create(name, null, 0);
    Assert.Throws<DomainException>(action);
}
```

- [x] **Step 2: تست جلوگیری از والدشدن خود دسته**

```csharp
[Fact]
public void MoveTo_rejects_self_as_parent()
{
    var category = Category.Create("خشکبار", null, 0);
    var action = () => category.MoveTo(category.Id, 1);
    Assert.Throws<DomainException>(action);
}
```

- [x] **Step 3: اجرای تست‌های شکست‌خورده**

Run: `dotnet test tests/ERP.Domain.Tests --filter FullyQualifiedName~CategoryTests`

- [x] **Step 4: پیاده‌سازی Aggregate و Event**

نام باید Trim شود، طول آن حداکثر 120 نویسه باشد، SortOrder منفی رد شود و Archive وضعیت را تغییر دهد؛ حذف فیزیکی API عمومی ندارد.

- [x] **Step 5: اجرای تست‌ها و Commit**

```powershell
dotnet test RetailERP.slnx
git add src/ERP.Domain/Catalog tests/ERP.Domain.Tests/Catalog
git commit -m "feat: add hierarchical product categories"
```

### Task 4: Units, products and barcodes

**Files:**
- Create: `src/ERP.Domain/Catalog/Unit.cs`
- Create: `src/ERP.Domain/Catalog/UnitConversion.cs`
- Create: `src/ERP.Domain/Catalog/Product.cs`
- Create: `src/ERP.Domain/Catalog/ProductId.cs`
- Create: `src/ERP.Domain/Catalog/ProductStatus.cs`
- Create: `src/ERP.Domain/Catalog/ProductBarcode.cs`
- Test: `tests/ERP.Domain.Tests/Catalog/ProductTests.cs`
- Test: `tests/ERP.Domain.Tests/Catalog/UnitConversionTests.cs`

**Interfaces:**
- Consumes: `CategoryId`, `Money`, `Quantity`.
- Produces: `Product.Create(name, optionalSku, categoryId, baseUnitId, salePrice)`, `AddBarcode`, `ChangePrice`, `Archive`; `UnitConversion.Create(from, to, factor)`. SKU برای راحتی کاربر اختیاری است و در Application می‌تواند خودکار تولید شود.

- [x] **Step 1: تست کالا و بارکد**

```csharp
[Fact]
public void AddBarcode_rejects_duplicate_on_same_product()
{
    var product = ProductTestFactory.Create();
    product.AddBarcode("6260000000012");
    var action = () => product.AddBarcode("6260000000012");
    action.Should().Throw<DomainException>();
}
```

- [x] **Step 2: تست تبدیل واحد**

```csharp
[Fact]
public void Convert_multiplies_by_positive_factor()
{
    var conversion = UnitConversion.Create(UnitId.New(), UnitId.New(), 12m);
    Assert.Equal(24m, conversion.Convert(Quantity.Create(2)).Value);
}
```

- [x] **Step 3: اجرای FAIL، پیاده‌سازی و اجرای PASS**

Run before and after: `dotnet test tests/ERP.Domain.Tests --filter "FullyQualifiedName~ProductTests|FullyQualifiedName~UnitConversionTests"`

- [x] **Step 4: Commit**

```powershell
git add src/ERP.Domain/Catalog tests/ERP.Domain.Tests/Catalog
git commit -m "feat: add products barcodes and unit conversions"
```

### Task 5: Inventory ledger and opening stock

**Files:**
- Create: `src/ERP.Domain/Inventory/InventoryLayer.cs`
- Create: `src/ERP.Domain/Inventory/StockMovement.cs`
- Create: `src/ERP.Domain/Inventory/StockMovementType.cs`
- Create: `src/ERP.Domain/Inventory/StockLedger.cs`
- Create: `src/ERP.Domain/Inventory/WarehouseId.cs`
- Test: `tests/ERP.Domain.Tests/Inventory/StockLedgerTests.cs`

**Interfaces:**
- Consumes: `ProductId`, `Money`, `Quantity`.
- Produces: `StockLedger.ReceiveOpeningStock`, `GetAvailableQuantity`, `ConsumeFifo` و immutable `StockMovement` history.

- [x] **Step 1: تست موجودی اولیه**

```csharp
[Fact]
public void Opening_stock_creates_layer_and_movement()
{
    var ledger = StockLedger.Empty(ProductId.New(), WarehouseId.New());
    ledger.ReceiveOpeningStock(Quantity.Create(10), Money.FromTomans(100_000), DateOnly.FromDateTime(DateTime.Today));
    Assert.Equal(10m, ledger.AvailableQuantity.Value);
    Assert.Single(ledger.Movements, x => x.Type == StockMovementType.OpeningBalance);
}
```

- [x] **Step 2: تست جلوگیری از موجودی منفی و FIFO**

مصرف بیشتر از موجودی باید `InsufficientStockException` بدهد و مصرف دو Layer قدیمی‌تر را زودتر کم کند.

- [x] **Step 3: اجرای FAIL، پیاده‌سازی و اجرای PASS**

Run: `dotnet test tests/ERP.Domain.Tests --filter FullyQualifiedName~StockLedgerTests`

- [x] **Step 4: Commit**

```powershell
git add src/ERP.Domain/Inventory tests/ERP.Domain.Tests/Inventory
git commit -m "feat: add inventory layers and opening stock ledger"
```

### Task 6: Application use cases and validation contracts

**Files:**
- Create: `src/ERP.Application/Common/IUnitOfWork.cs`
- Create: `src/ERP.Application/Common/Result.cs`
- Create: `src/ERP.Application/Audit/IAuditWriter.cs`
- Create: `src/ERP.Application/Catalog/ICategoryRepository.cs`
- Create: `src/ERP.Application/Catalog/IProductRepository.cs`
- Create: `src/ERP.Application/Inventory/IStockLedgerRepository.cs`
- Create: `src/ERP.Application/Catalog/CreateCategory.cs`
- Create: `src/ERP.Application/Catalog/CreateProduct.cs`
- Create: `src/ERP.Application/Inventory/ReceiveOpeningStock.cs`
- Test: `tests/ERP.Application.Tests/Catalog/CreateCategoryTests.cs`
- Test: `tests/ERP.Application.Tests/Catalog/CreateProductTests.cs`
- Test: `tests/ERP.Application.Tests/Inventory/ReceiveOpeningStockTests.cs`
- Test: `tests/ERP.Application.Tests/Audit/AuditEmissionTests.cs`

**Interfaces:**
- Produces: async handlers returning `Result<T>` with Persian validation messages and `CancellationToken`.

- [ ] **Step 1: تست Use Case ساخت دسته با نام تکراری**

```csharp
[Fact]
public async Task Execute_returns_persian_error_for_duplicate_sibling_name()
{
    var result = await handler.Execute(new CreateCategory.Command("خشکبار", null, 0), CancellationToken.None);
    Assert.Equal("در این سطح، دسته‌بندی دیگری با همین نام وجود دارد.", result.Error!.Message);
}
```

- [ ] **Step 2: تست ساخت کالا با بارکد سراسری تکراری و موجودی اولیه اتمیک**

Repository fake باید نشان دهد در صورت خطای ذخیره Movement، هیچ Product یا Layer نیمه‌کاره Commit نمی‌شود.

هر عملیات موفق ساخت/ویرایش/آرشیو دسته، کالا و موجودی اولیه باید دقیقاً یک Audit Record شامل UserId، Action، EntityId، زمان و Old/New Value تولید کند. عملیات ناموفق نباید Audit موفق ثبت کند.

- [ ] **Step 3: اجرای FAIL، پیاده‌سازی و اجرای PASS**

Run: `dotnet test tests/ERP.Application.Tests`

- [ ] **Step 4: Commit**

```powershell
git add src/ERP.Application tests/ERP.Application.Tests RetailERP.slnx
git commit -m "feat: add catalog and opening stock use cases"
```

### Task 7: Firebird schema, migrations and repositories

**Files:**
- Create: `src/ERP.Persistence/Database/FirebirdOptions.cs`
- Create: `src/ERP.Persistence/Database/FirebirdConnectionFactory.cs`
- Create: `src/ERP.Persistence/Migrations/IMigration.cs`
- Create: `src/ERP.Persistence/Migrations/MigrationRunner.cs`
- Create: `src/ERP.Persistence/Migrations/V001_CreateCatalogAndInventory.cs`
- Create: `src/ERP.Persistence/Catalog/FirebirdCategoryRepository.cs`
- Create: `src/ERP.Persistence/Catalog/FirebirdProductRepository.cs`
- Create: `src/ERP.Persistence/Inventory/FirebirdStockLedgerRepository.cs`
- Test: `tests/ERP.Persistence.Tests/Migrations/MigrationRunnerTests.cs`
- Test: `tests/ERP.Persistence.Tests/Catalog/CatalogRepositoryTests.cs`
- Test: `tests/ERP.Persistence.Tests/Inventory/InventoryTransactionTests.cs`

**Interfaces:**
- Consumes: Application repository contracts.
- Produces: Firebird Embedded repositories and transaction-scoped `IUnitOfWork`.

- [ ] **Step 1: تست Migration روی دیتابیس خالی و اجرای دوباره**

Migration اول باید جدول نسخه، Category، Unit، Product، ProductBarcode، InventoryLayer و StockMovement را با UTF8 بسازد. اجرای دوم نباید تغییری ایجاد کند.

- [ ] **Step 2: تست Unique Constraintها**

Barcode باید سراسری یکتا و نام دسته در یک Parent یکتا باشد. تست باید `FbException` را به Conflict قابل فهم Application نگاشت کند.

- [ ] **Step 3: تست Rollback تراکنش**

در صورت شکست Insert موجودی، Product و StockMovement هر دو باید Rollback شوند.

- [ ] **Step 4: پیاده‌سازی SQL پارامتری و اجرای تست‌ها**

Run: `dotnet test tests/ERP.Persistence.Tests`

- [ ] **Step 5: Commit**

```powershell
git add src/ERP.Persistence tests/ERP.Persistence.Tests Directory.Packages.props RetailERP.slnx
git commit -m "feat: persist catalog and inventory in firebird"
```

### Task 8: Persian WinUI shell and catalog screens

**Files:**
- Create via official template: `src/ERP.Desktop/ERP.Desktop.csproj`
- Modify: `src/ERP.Desktop/App.xaml`
- Modify: `src/ERP.Desktop/MainWindow.xaml`
- Create: `src/ERP.Desktop/Resources/Strings.fa-IR.resw`
- Create: `src/ERP.Desktop/DesignSystem/Colors.xaml`
- Create: `src/ERP.Desktop/DesignSystem/Typography.xaml`
- Create: `src/ERP.Desktop/Features/Categories/CategoriesPage.xaml`
- Create: `src/ERP.Desktop/Features/Categories/CategoriesViewModel.cs`
- Create: `src/ERP.Desktop/Features/Products/ProductEditorPage.xaml`
- Create: `src/ERP.Desktop/Features/Products/ProductEditorViewModel.cs`
- Create: `src/ERP.Desktop/Features/Inventory/OpeningStockPage.xaml`
- Create: `src/ERP.Desktop/Features/Inventory/OpeningStockViewModel.cs`
- Create: `src/ERP.Desktop/Help/Workflows/category-management.yaml`
- Create: `src/ERP.Desktop/Help/Workflows/product-create.yaml`
- Create: `src/ERP.Desktop/Help/Workflows/opening-stock.yaml`
- Test: `tests/ERP.Desktop.Tests/Features/CategoriesViewModelTests.cs`
- Test: `tests/ERP.Desktop.Tests/Features/ProductEditorViewModelTests.cs`
- Test: `tests/ERP.Desktop.Tests/Help/WorkflowContractTests.cs`

**Interfaces:**
- Consumes: Application handlers from Task 6.
- Produces: RTL navigation and working manual category/product/opening-stock UI.

- [ ] **Step 1: ایجاد پروژه رسمی MVVM**

```powershell
winapp new --name ERP.Desktop --output src/ERP.Desktop --template winui-mvvm --template-version latest --use-defaults
dotnet sln RetailERP.slnx add src/ERP.Desktop/ERP.Desktop.csproj
```

- [ ] **Step 2: تست ViewModel قبل از XAML**

```csharp
[Fact]
public async Task Save_shows_persian_validation_without_calling_handler()
{
    viewModel.Name = " ";
    await viewModel.SaveCommand.ExecuteAsync(null);
    Assert.Equal("نام کالا را وارد کنید.", viewModel.NameError);
    Assert.Equal(0, handler.Calls);
}
```

- [ ] **Step 3: ساخت Shell راست‌به‌چپ و صفحه دسته‌بندی**

Navigation سطح اول در این Build فقط «میز کار»، «کالا و قیمت‌گذاری»، «انبار» و «تنظیمات» را نشان می‌دهد. دسته‌ها در TreeView، با دکمه اصلی «دسته‌بندی جدید» و Actionهای ویرایش، جابه‌جایی و آرشیو نمایش داده می‌شوند.

- [ ] **Step 4: ساخت فرم کالا و موجودی اولیه**

فرم کالا فقط نام، دسته، واحد اصلی، SKU، بارکد و قیمت فروش را در نمای اصلی نشان می‌دهد. ایجاد سریع دسته‌بندی داخل همان فرم باز می‌شود. موجودی اولیه در صفحه جدا با انبار، تعداد و بهای واحد ثبت می‌شود.

برای هر سه صفحه فایل Workflow فارسی با `page`، `control`، `instruction_fa` و `action` نوشته می‌شود. تست Contract فایل‌ها را Parse می‌کند و نبود راهنما برای هر Page ID را شکست می‌دهد؛ F1 راهنمای صفحه فعال را باز می‌کند.

- [ ] **Step 5: Build، تست و اجرای برنامه**

```powershell
dotnet test RetailERP.slnx
winapp run --project src/ERP.Desktop/ERP.Desktop.csproj --arch x64 --debug-output
```

Expected: برنامه باز شود، RTL باشد و ساخت دسته، کالا و موجودی اولیه واقعاً در Firebird ذخیره شود.

- [ ] **Step 6: Commit**

```powershell
git add src/ERP.Desktop tests/ERP.Desktop.Tests RetailERP.slnx
git commit -m "feat: add persian catalog and opening stock desktop flow"
```

### Task 9: Quality gate and stage evidence

**Files:**
- Create: `docs/quality/foundation-catalog-inventory-test-report.md`
- Create: `scripts/Test-Stage1.ps1`
- Create: `src/ERP.Domain/Catalog/README.md`
- Create: `src/ERP.Domain/Inventory/README.md`

**Interfaces:**
- Consumes: تمام خروجی Taskهای 1 تا 8.
- Produces: گزارش قابل تکرار از سلامت مرحله و دستور تست یک‌مرحله‌ای.

- [ ] **Step 1: اسکریپت کنترل کامل**

`scripts/Test-Stage1.ps1` باید Restore، Build Release، همه Testها، Architecture Test و اندازه‌گیری زمان تست تراکنش را اجرا کند و روی اولین خطا با exit code غیرصفر متوقف شود.

- [ ] **Step 2: اجرای کنترل**

Run: `powershell -ExecutionPolicy Bypass -File scripts/Test-Stage1.ps1`

Expected: خروجی نهایی `Stage 1 verification passed.` و exit code صفر.

- [ ] **Step 3: بررسی دستی UI**

در گزارش ثبت شود: RTL، فارسی، Keyboard-only، رزولوشن 1366×768، Light/Dark، ایجاد سریع دسته، Duplicate Barcode و خطای موجودی نامعتبر.

- [ ] **Step 4: Commit**

```powershell
git add docs/quality scripts src/ERP.Domain/Catalog/README.md src/ERP.Domain/Inventory/README.md
git commit -m "test: verify foundation catalog and inventory stage"
```

## Plan self-review result

- Spec coverage این Plan: ساختار Solution، Domain Kernel، Catalog، دسته‌بندی چندسطحی، واحد و تبدیل، بارکد، Firebird، موجودی اولیه، UI فارسی/RTL، Help و Audit پایه در مرزهای Application/Persistence.
- موارد عمداً منتقل‌شده به Plan بعدی: Import Excel، کاربران و مجوز کامل، POS، پرداخت، چاپ، Backup و Installer نهایی.
- تمام نوع‌های مصرف‌شده در Taskهای بعدی در Task قبلی تعریف شده‌اند.
- هر Task یک خروجی مستقل قابل تست و Commit دارد.
