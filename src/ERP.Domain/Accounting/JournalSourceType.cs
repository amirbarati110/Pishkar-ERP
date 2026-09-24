namespace ERP.Domain.Accounting;

public enum JournalSourceType
{
    Sale = 1,
    SaleCorrection = 2,
    SaleReturn = 3,

    /// <summary>
    /// Takes back the sales part of the entry of the invoice a «صورتحساب اصلاحی» replaces — without
    /// it the corrected invoice's revenue stayed in the books beside the correction's (audit
    /// 1405/07/02). Source id: the correction's sale id.
    /// </summary>
    SaleCorrectionReversal = 4,

    /// <summary>«دریافت از مشتری». Source id: the payment's id.</summary>
    CustomerPayment = 5,

    /// <summary>«موجودی اول دوره» (by hand or from import). Source id: the inventory layer it created.</summary>
    OpeningStock = 6,

    /// <summary>A customer's «مانده اول دوره» carried in from the old books. Source id: the customer's id.</summary>
    CustomerOpeningBalance = 7,
}
