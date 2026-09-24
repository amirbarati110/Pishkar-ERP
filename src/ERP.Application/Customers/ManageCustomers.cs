using ERP.Application.Accounting;
using ERP.Application.Audit;
using ERP.Application.Common;
using ERP.Domain.Common;
using ERP.Domain.Customers;

namespace ERP.Application.Customers;

// «لیست مشتریان» (checklist «ن»): reading the list with each customer's balance, creating
// and editing a customer, and taking one out of use. «حذف» on the screen is the archive
// below — a customer who ever bought stays in the books (rule 15 / §3.10), and one who
// still owes money cannot be removed at all, or the debt would vanish from every screen.

/// <param name="Term">Part of a name or company, part of a mobile / national ID number in Persian or Latin digits, or a customer number.</param>
/// <param name="OnlyDebtors">Only customers who owe something («فقط بدهکارها»).</param>
public sealed record CustomerListQuery(string? Term, bool OnlyDebtors, int Take = ListCustomersHandler.DefaultTake);

/// <summary>One row — carries everything the edit form needs, so opening an edit costs no second read.</summary>
/// <param name="Code">The customer number (شماره اشتراک).</param>
/// <param name="Profile">Every descriptive field as text, exactly what the form shows when it opens for editing.</param>
public sealed record CustomerListRow(
    CustomerId Id,
    long Code,
    string Name,
    CustomerProfileInput Profile,
    Money CreditLimit,
    Money OpeningBalance,
    Money Debt,
    Money Advance)
{
    public string Mobile => Profile.Mobile ?? string.Empty;

    public string? Address => Profile.Address;
}

/// <summary>
/// <see cref="Rows"/> is at most the asked-for count; <see cref="TotalCount"/> and
/// <see cref="TotalDebt"/> cover every match, so the footer stays true when fewer rows are drawn.
/// </summary>
public sealed record CustomerListPage(IReadOnlyList<CustomerListRow> Rows, int TotalCount, Money TotalDebt, Money TotalAdvance);

public interface ICustomerListReader
{
    /// <param name="numberDigits">The term reduced to Latin digits, or null when it is not a number (it has letters in it).</param>
    Task<CustomerListPage> ListAsync(CustomerListQuery query, string? numberDigits, CancellationToken cancellationToken);
}

public interface IListCustomersHandler
{
    Task<CustomerListPage> ExecuteAsync(CustomerListQuery query, CancellationToken cancellationToken);
}

public sealed class ListCustomersHandler : IListCustomersHandler
{
    public const int DefaultTake = 300;
    public const int MaximumTake = 500;

    private readonly ICustomerListReader _reader;

    public ListCustomersHandler(ICustomerListReader reader)
    {
        _reader = reader;
    }

    public Task<CustomerListPage> ExecuteAsync(CustomerListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var term = string.IsNullOrWhiteSpace(query.Term) ? null : query.Term.Trim();
        var take = Math.Clamp(query.Take, 1, MaximumTake);
        return _reader.ListAsync(query with { Term = term, Take = take }, ToMobileDigits(term), cancellationToken);
    }

    private static string? ToMobileDigits(string? term)
    {
        // Same rule as the sales screen's customer search: digits and the separators people
        // type between them are a number; anything with letters is a name. Even one digit counts
        // here — it can be a customer number — and the reader decides which columns a short
        // number may be compared with (a mobile or national ID needs at least three digits).
        if (term is null || !term.All(character => char.IsDigit(character) || character is ' ' or '-' or '(' or ')' or '+'))
        {
            return null;
        }

        var digits = PersianNumber.ToLatinDigitsOnly(term);
        return digits.Length > 0 ? digits : null;
    }
}

/// <param name="OpeningBalance">The debt carried in from the old books (مانده اول دوره). Fixed once the customer exists.</param>
public sealed record CreateCustomerCommand(
    CustomerProfileInput Profile,
    Money CreditLimit,
    Money OpeningBalance);

public interface ICreateCustomerHandler
{
    Task<Result<CustomerId>> ExecuteAsync(CreateCustomerCommand command, CancellationToken cancellationToken);
}

/// <summary>The full customer form (list screen), as opposed to the name-and-mobile quick create of the sales screen.</summary>
public sealed class CreateCustomerHandler : ICreateCustomerHandler
{
    private readonly ICustomerRepository _customers;
    private readonly IAuditWriter _audit;
    private readonly IJournalEntryRepository _journal;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public CreateCustomerHandler(
        ICustomerRepository customers,
        IAuditWriter audit,
        IJournalEntryRepository journal,
        IUnitOfWork unitOfWork,
        IUserContext userContext,
        IClock clock)
    {
        _customers = customers;
        _audit = audit;
        _journal = journal;
        _unitOfWork = unitOfWork;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<CustomerId>> ExecuteAsync(CreateCustomerCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            var customer = Customer.Create(CustomerProfile.Create(command.Profile));
            customer.SetCreditLimit(command.CreditLimit);
            customer.SetOpeningBalance(command.OpeningBalance);

            var duplicate = await CustomerDuplicates
                .FindAsync(_customers, customer.Profile, exceptCustomer: null, cancellationToken)
                .ConfigureAwait(false);
            if (duplicate is not null)
            {
                return Result.Failure<CustomerId>(duplicate.Code, duplicate.Message);
            }

            await _customers.AddAsync(customer, cancellationToken).ConfigureAwait(false);
            if (OperationalJournalEntryFactory.CustomerOpeningBalance(customer.Id, customer.OpeningBalance, _clock.UtcNow) is { } opening)
            {
                await _journal.SaveAsync(opening, cancellationToken).ConfigureAwait(false);
            }

            await _audit.WriteAsync(
                new AuditEntry(
                    Guid.NewGuid(),
                    _userContext.UserId,
                    "customers.customer.created",
                    nameof(Customer),
                    customer.Id.ToString(),
                    null,
                    Describe(customer),
                    _clock.UtcNow),
                cancellationToken).ConfigureAwait(false);
            await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(customer.Id);
        }
        catch (DomainException exception)
        {
            return Result.Failure<CustomerId>("customers.customer.invalid", exception.Message);
        }
        catch (DataConflictException exception)
        {
            return Result.Failure<CustomerId>(exception.Code, exception.Message);
        }
    }

    internal static string Describe(Customer customer) =>
        $"#{customer.Code} {customer.Name} | {customer.Profile.Kind} | {customer.Mobile} | {customer.Profile.NationalId} | {customer.Address} | سقف {customer.CreditLimit.Rials} | اول دوره {customer.OpeningBalance.Rials}";
}

internal sealed record CustomerDuplicate(string Code, string Message);

/// <summary>The two numbers that must belong to one customer only: the mobile (§6.5) and the identity number.</summary>
internal static class CustomerDuplicates
{
    public static async Task<CustomerDuplicate?> FindAsync(
        ICustomerRepository customers,
        CustomerProfile profile,
        CustomerId? exceptCustomer,
        CancellationToken cancellationToken)
    {
        var sameMobile = await customers.FindByMobileAsync(profile.Mobile, cancellationToken).ConfigureAwait(false);
        if (sameMobile is not null && sameMobile.Id != exceptCustomer)
        {
            return new("customers.customer.duplicate-mobile", $"این شماره موبایل قبلاً برای «{sameMobile.Name}» ثبت شده است.");
        }

        if (profile.NationalId is { } nationalId)
        {
            var sameId = await customers.FindByNationalIdAsync(nationalId, cancellationToken).ConfigureAwait(false);
            if (sameId is not null && sameId.Id != exceptCustomer)
            {
                return new("customers.customer.duplicate-national-id", $"این شماره هویتی قبلاً برای «{sameId.Name}» ثبت شده است.");
            }
        }

        return null;
    }
}

/// <summary>The opening balance is deliberately absent: changing it later would silently rewrite every balance the customer ever had.</summary>
public sealed record UpdateCustomerCommand(
    CustomerId CustomerId,
    CustomerProfileInput Profile,
    Money CreditLimit);

public interface IUpdateCustomerHandler
{
    Task<Result<bool>> ExecuteAsync(UpdateCustomerCommand command, CancellationToken cancellationToken);
}

public sealed class UpdateCustomerHandler : IUpdateCustomerHandler
{
    private readonly ICustomerRepository _customers;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public UpdateCustomerHandler(
        ICustomerRepository customers,
        IAuditWriter audit,
        IUnitOfWork unitOfWork,
        IUserContext userContext,
        IClock clock)
    {
        _customers = customers;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<bool>> ExecuteAsync(UpdateCustomerCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            var customer = await _customers.GetByIdAsync(command.CustomerId, cancellationToken).ConfigureAwait(false);
            if (customer is null)
            {
                return Result.Failure<bool>("customers.customer.not-found", "این مشتری پیدا نشد؛ ممکن است حذف شده باشد.");
            }

            if (customer.Status == CustomerStatus.Archived)
            {
                return Result.Failure<bool>("customers.customer.archived", "این مشتری حذف شده و دیگر قابل ویرایش نیست.");
            }

            var before = CreateCustomerHandler.Describe(customer);
            customer.UpdateProfile(CustomerProfile.Create(command.Profile));
            customer.SetCreditLimit(command.CreditLimit);

            var duplicate = await CustomerDuplicates
                .FindAsync(_customers, customer.Profile, customer.Id, cancellationToken)
                .ConfigureAwait(false);
            if (duplicate is not null)
            {
                return Result.Failure<bool>(duplicate.Code, duplicate.Message);
            }

            await _customers.UpdateAsync(customer, cancellationToken).ConfigureAwait(false);
            await _audit.WriteAsync(
                new AuditEntry(
                    Guid.NewGuid(),
                    _userContext.UserId,
                    "customers.customer.updated",
                    nameof(Customer),
                    customer.Id.ToString(),
                    before,
                    CreateCustomerHandler.Describe(customer),
                    _clock.UtcNow),
                cancellationToken).ConfigureAwait(false);
            await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(true);
        }
        catch (DomainException exception)
        {
            return Result.Failure<bool>("customers.customer.invalid", exception.Message);
        }
        catch (DataConflictException exception)
        {
            return Result.Failure<bool>(exception.Code, exception.Message);
        }
    }
}

public sealed record ArchiveCustomerCommand(CustomerId CustomerId);

public interface IArchiveCustomerHandler
{
    Task<Result<bool>> ExecuteAsync(ArchiveCustomerCommand command, CancellationToken cancellationToken);
}

/// <summary>Takes a customer out of use without erasing them. A customer who still owes money is refused. Doing it twice is not an error.</summary>
public sealed class ArchiveCustomerHandler : IArchiveCustomerHandler
{
    private readonly ICustomerRepository _customers;
    private readonly ICustomerLedgerReader _ledger;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public ArchiveCustomerHandler(
        ICustomerRepository customers,
        ICustomerLedgerReader ledger,
        IAuditWriter audit,
        IUnitOfWork unitOfWork,
        IUserContext userContext,
        IClock clock)
    {
        _customers = customers;
        _ledger = ledger;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<bool>> ExecuteAsync(ArchiveCustomerCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var customer = await _customers.GetByIdAsync(command.CustomerId, cancellationToken).ConfigureAwait(false);
        if (customer is null)
        {
            return Result.Failure<bool>("customers.customer.not-found", "این مشتری پیدا نشد؛ ممکن است قبلاً حذف شده باشد.");
        }

        if (customer.Status == CustomerStatus.Archived)
        {
            return Result.Success(true);
        }

        var account = await CustomerAccounts.LoadAsync(customer, _ledger, cancellationToken).ConfigureAwait(false);
        if (account.Debt.Rials > 0)
        {
            return Result.Failure<bool>(
                "customers.customer.has-debt",
                "این مشتری هنوز بدهکار است و حذف نمی‌شود؛ اول بدهی‌اش را تسویه کنید.");
        }

        customer.Archive();
        await _customers.UpdateAsync(customer, cancellationToken).ConfigureAwait(false);
        await _audit.WriteAsync(
            new AuditEntry(
                Guid.NewGuid(),
                _userContext.UserId,
                "customers.customer.archived",
                nameof(Customer),
                customer.Id.ToString(),
                null,
                customer.Name,
                _clock.UtcNow),
            cancellationToken).ConfigureAwait(false);
        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(true);
    }
}
