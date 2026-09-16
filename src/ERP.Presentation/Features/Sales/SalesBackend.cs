using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Application.Customers;
using ERP.Application.Sales;

namespace ERP.Presentation.Features.Sales;

/// <summary>The use cases the sales screen calls, gathered so the view model has one dependency instead of twenty.</summary>
public sealed record SalesBackend(
    IStartSaleHandler StartSale,
    IAddSaleLineHandler AddLine,
    IChangeSaleLineHandler ChangeLine,
    IRemoveSaleLineHandler RemoveLine,
    ISetSaleChargesHandler SetCharges,
    ISetSaleCustomerHandler SetCustomer,
    ICompleteSaleHandler Complete,
    ICancelSaleHandler Cancel,
    IGetSaleDetailsHandler Details,
    IListHeldSalesHandler HeldSales,
    IListSalesOfDayHandler SalesOfDay,
    IBrowseProductsForSaleHandler BrowseProducts,
    IReadSaleProductsHandler ReadProducts,
    IGetLineEditInfoHandler LineEditInfo,
    ISearchProductsHandler SearchProducts,
    ICatalogLookupReader CatalogLookup,
    ISearchCustomersHandler SearchCustomers,
    IQuickCreateCustomerHandler QuickCreateCustomer,
    IGetCustomerAccountHandler CustomerAccount,
    IRecordCustomerPaymentHandler ReceivePayment,
    IClock Clock);
