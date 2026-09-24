using ERP.Application.Cashiering;
using ERP.Application.Common;
using ERP.Persistence.Audit;
using ERP.Persistence.Customers;
using ERP.Persistence.Database;
using ERP.Persistence.Sales;

namespace ERP.Persistence.Cashiering;

/// <summary>Composition root for the Cashiering module, one unit of work per call — the same shape as <see cref="Customers.FirebirdCustomerService"/>.</summary>
public sealed class FirebirdCashieringService :
    IOpenCashShiftHandler,
    ICloseCashShiftHandler,
    IGetCashShiftStatusHandler
{
    private readonly FirebirdConnectionFactory _connectionFactory;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public FirebirdCashieringService(
        FirebirdConnectionFactory connectionFactory,
        IUserContext userContext,
        IClock clock)
    {
        _connectionFactory = connectionFactory;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<Domain.Cashiering.CashShiftId>> ExecuteAsync(
        OpenCashShiftCommand command,
        CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new OpenCashShiftHandler(
                new FirebirdCashShiftRepository(unitOfWork),
                new FirebirdAuditWriter(unitOfWork),
                unitOfWork,
                _userContext,
                _clock)
            .ExecuteAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<CashShiftCloseSummary>> ExecuteAsync(
        CloseCashShiftCommand command,
        CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new CloseCashShiftHandler(
                new FirebirdCashShiftRepository(unitOfWork),
                new FirebirdSaleReadReader(unitOfWork),
                new FirebirdCustomerCashReceiptReader(unitOfWork),
                new FirebirdAuditWriter(unitOfWork),
                unitOfWork,
                _userContext,
                _clock)
            .ExecuteAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<CashShiftStatusView> ExecuteAsync(
        GetCashShiftStatusQuery query,
        CancellationToken cancellationToken)
    {
        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        return await new GetCashShiftStatusHandler(
                new FirebirdCashShiftRepository(unitOfWork),
                new FirebirdSaleReadReader(unitOfWork),
                new FirebirdCustomerCashReceiptReader(unitOfWork),
                _clock)
            .ExecuteAsync(query, cancellationToken)
            .ConfigureAwait(false);
    }
}
