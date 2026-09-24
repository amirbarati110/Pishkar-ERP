namespace ERP.Domain.Identity;

/// <summary>
/// Everything the app gates by who is signed in (§15.1–15.2). A manager (<see cref="UserRole.Admin"/>)
/// holds every right. A cashier always holds <see cref="SellAndTill"/>; the four
/// <see cref="CashierPermissions"/> rights are switched on or off per cashier by a manager; the
/// «Manage…» rights are a manager's only (user decision 1405/07/02).
/// </summary>
public enum AccessRight
{
    /// <summary>Selling, the cash shift, quick-creating a customer and «دریافت از مشتری» — the till's daily work.</summary>
    SellAndTill = 1,

    /// <summary>«مرجوعی و تعویض».</summary>
    ReturnsAndExchange = 2,

    /// <summary>«صورتحساب اصلاحی» for a posted invoice.</summary>
    CorrectPostedInvoices = 3,

    /// <summary>Seeing cost of goods, margin and stock value (§15.2 «View Cost»).</summary>
    ViewCostAndProfit = 4,

    /// <summary>The full customer form: create, edit and delete in «لیست مشتریان».</summary>
    EditCustomers = 5,

    /// <summary>Products, categories, barcodes and a product's master price (§15.2 «Edit Master Price»).</summary>
    ManageCatalog = 10,

    /// <summary>Opening stock, product import and warehouses.</summary>
    ManageStock = 11,

    /// <summary>Taking, verifying and restoring backups.</summary>
    ManageBackups = 12,

    /// <summary>Users, their roles and their permissions.</summary>
    ManageUsers = 13,
}

/// <summary>The rights a manager can switch on or off for one cashier — all on unless a manager turns one off.</summary>
[Flags]
public enum CashierPermissions
{
    None = 0,
    ReturnsAndExchange = 1,
    CorrectPostedInvoices = 2,
    ViewCostAndProfit = 4,
    EditCustomers = 8,
    All = ReturnsAndExchange | CorrectPostedInvoices | ViewCostAndProfit | EditCustomers,
}

/// <summary>The one table of who may do what — used by the server-side checks and to hide what a person may not use.</summary>
public static class AccessRules
{
    public static bool Allows(UserRole role, CashierPermissions permissions, AccessRight right) => role switch
    {
        UserRole.Admin => true,
        UserRole.Cashier => right switch
        {
            AccessRight.SellAndTill => true,
            AccessRight.ReturnsAndExchange => permissions.HasFlag(CashierPermissions.ReturnsAndExchange),
            AccessRight.CorrectPostedInvoices => permissions.HasFlag(CashierPermissions.CorrectPostedInvoices),
            AccessRight.ViewCostAndProfit => permissions.HasFlag(CashierPermissions.ViewCostAndProfit),
            AccessRight.EditCustomers => permissions.HasFlag(CashierPermissions.EditCustomers),
            _ => false,
        },
        _ => false,
    };

    /// <summary>What a person is told when a right is missing — names the operation, never a code.</summary>
    public static string DeniedMessage(AccessRight right) => right switch
    {
        AccessRight.ReturnsAndExchange => "اجازه‌ی ثبت مرجوعی را ندارید؛ مدیر می‌تواند در «کاربران» این اجازه را بدهد.",
        AccessRight.CorrectPostedInvoices => "اجازه‌ی اصلاح فاکتور ثبت‌شده را ندارید؛ مدیر می‌تواند در «کاربران» این اجازه را بدهد.",
        AccessRight.ViewCostAndProfit => "اجازه‌ی دیدن بهای تمام‌شده و سود را ندارید.",
        AccessRight.EditCustomers => "اجازه‌ی ویرایش و حذف مشتری را ندارید؛ مدیر می‌تواند در «کاربران» این اجازه را بدهد.",
        AccessRight.ManageCatalog => "تعریف و ویرایش کالا و قیمت اصلی فقط کار مدیر است.",
        AccessRight.ManageStock => "موجودی اول دوره، وارد کردن از اکسل و انبارها فقط کار مدیر است.",
        AccessRight.ManageBackups => "پشتیبان‌گیری و بازیابی فقط کار مدیر است.",
        AccessRight.ManageUsers => "مدیریت کاربران فقط کار مدیر است.",
        _ => "اجازه‌ی این کار را ندارید.",
    };
}
