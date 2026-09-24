using ERP.Domain.Identity;

namespace ERP.Presentation.Features.Navigation;

/// <summary>
/// One card on a section's page — one self-contained screen (checklist «م»).
/// <see cref="Key"/> is the stable id the shell routes on; <see cref="Glyph"/> is a
/// Segoe Fluent Icons code point. <see cref="RequiredRight"/> is null for anything the till
/// itself needs (§15.2 — no switch to see); a manager-only screen names the right that opens it.
/// </summary>
public sealed record HubCard(string Key, string Title, string Description, string Glyph, AccessRight? RequiredRight = null)
{
    /// <summary>The UI-Automation id, so UI tests and screen readers can reach the card.</summary>
    public string AutomationId => "HubCard_" + Key;
}

/// <summary>A top-level menu section: a title and the cards it opens (§3.13 «Domain-based»).</summary>
public sealed record NavSection(string Key, string Title, string Subtitle, IReadOnlyList<HubCard> Cards)
{
    public string AutomationId => "NavSection_" + Key;
}

/// <summary>
/// The whole menu structure in one place: section → cards → screen. A card is listed
/// here only when its screen really works (user rule, 1405/06/29: an unbuilt card is
/// not shown at all — not «به‌زودی», not dimmed). New screens are added here and
/// nowhere else, so the menu, the hub pages and the breadcrumb can never disagree.
/// </summary>
public static class NavigationMap
{
    public const string Home = "home";

    public const string NewSale = "new-sale";
    public const string HeldInvoices = "held";
    public const string Returns = "returns";
    public const string CashShift = "till";
    public const string ProductList = "product-list";
    public const string Categories = "categories";
    public const string Import = "import";
    public const string OpeningStock = "opening-stock";
    public const string WarehouseList = "warehouse-list";
    public const string CustomerList = "customer-list";
    public const string Backup = "backup";
    public const string Users = "users";

    public const string SalesSection = "sales";
    public const string CatalogSection = "catalog";
    public const string InventorySection = "inventory";
    public const string PeopleSection = "people";
    public const string SystemSection = "system";

    public static IReadOnlyList<NavSection> Sections { get; } =
    [
        new(SalesSection, "فروش و صندوق", "کار روزانه‌ی صندوق: فروش، فاکتورهای معلق، مرجوعی و شیفت",
        [
            new(NewSale, "فروش جدید", "باز کردن صفحه‌ی فروش و شروع یک فاکتور تازه", ""),
            new(HeldInvoices, "فاکتورهای معلق", "ادامه‌ی فاکتورهای نیمه‌تمام و دیدن فاکتورهای امروز", ""),
            new(Returns, "مرجوعی و تعویض", "برگشت کالا از یک فاکتور و بازپرداخت یا تعویض", "", AccessRight.ReturnsAndExchange),
            new(CashShift, "شیفت صندوق", "شروع و بستن شیفت و شمارش پول صندوق", ""),
        ]),
        new(CatalogSection, "کالا و قیمت‌گذاری", "کالاها، دسته‌بندی‌ها و وارد کردن اطلاعات",
        [
            new(ProductList, "لیست کالاها", "همه‌ی کالاها با جست‌وجو؛ ساخت، ویرایش و حذف کالا", "", AccessRight.ManageCatalog),
            new(Categories, "دسته‌بندی‌ها", "درخت دسته‌بندی کالاها", "", AccessRight.ManageCatalog),
            new(Import, "وارد کردن از اکسل", "وارد کردن گروهی کالا از فایل اکسل", "", AccessRight.ManageCatalog),
        ]),
        new(InventorySection, "انبار و موجودی", "انبارها و موجودی کالاها در آن‌ها",
        [
            new(WarehouseList, "لیست انبارها", "انبارها با نشانی؛ ساخت، ویرایش و حذف انبار", "\uE7B8", AccessRight.ManageStock),
            new(OpeningStock, "موجودی اول دوره", "ثبت تعداد و بهای واقعی موجودی برای شروع کار", "", AccessRight.ManageStock),
        ]),
        new(PeopleSection, "مشتریان و حساب‌ها", "مشتری‌ها، مانده‌ی حساب و سقف اعتبار",
        [
            new(CustomerList, "لیست مشتریان", "همه‌ی مشتری‌ها با مانده‌ی حساب؛ ساخت، ویرایش و حذف مشتری", ""),
        ]),
        new(SystemSection, "تنظیمات و سیستم", "سلامت سیستم، نسخه‌ی پشتیبان و کاربران",
        [
            new(Backup, "پشتیبان‌گیری و سلامت سیستم", "گرفتن نسخه‌ی پشتیبان و بررسی اعتبار آن", "", AccessRight.ManageBackups),
            new(Users, "کاربران", "ساخت کاربر، تغییر نقش و اجازه‌ها، رمز تازه و غیرفعال‌کردن", "\uE77B", AccessRight.ManageUsers),
        ]),
    ];

    public static NavSection? FindSection(string key) =>
        Sections.FirstOrDefault(section => section.Key == key);

    public static HubCard? FindCard(string key) =>
        Sections.SelectMany(section => section.Cards).FirstOrDefault(card => card.Key == key);

    /// <summary>The section a card belongs to — the menu item to highlight while its screen is open.</summary>
    public static NavSection? SectionOf(string cardKey) =>
        Sections.FirstOrDefault(section => section.Cards.Any(card => card.Key == cardKey));

    /// <summary>
    /// The cards a section shows to whoever is signed in right now (user decision 1405/07/02): a
    /// card whose <see cref="HubCard.RequiredRight"/> the acting user lacks is not listed at all —
    /// the same rule as an unbuilt screen (§10: hiding a button alone is never the real security,
    /// but the menu should still only offer what a person may actually open).
    /// </summary>
    public static IReadOnlyList<HubCard> VisibleCards(NavSection section, Func<AccessRight, bool> can)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(can);

        return [.. section.Cards.Where(card => card.RequiredRight is not { } right || can(right))];
    }
}
