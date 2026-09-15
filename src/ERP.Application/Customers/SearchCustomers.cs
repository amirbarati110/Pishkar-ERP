using ERP.Domain.Common;
using ERP.Domain.Customers;

namespace ERP.Application.Customers;

/// <param name="Term">As typed: part of a name, or part of a mobile number in Persian or Latin digits.</param>
public sealed record SearchCustomersQuery(string Term, int MaxResults = 8);

public sealed record CustomerSearchResult(CustomerId Id, string Name, string Mobile);

/// <summary>
/// Reads matching active customers from storage, ranked and limited in the
/// database the same way product search is (appendix B): matches that start
/// with the term first, then matches found elsewhere in the field.
/// </summary>
public interface ICustomerSearchReader
{
    /// <param name="nameTerm">The trimmed term, matched against the name.</param>
    /// <param name="mobileDigits">
    /// The term reduced to Latin digits, matched against the mobile — or null
    /// when the term is not a phone-number fragment, so a name like «علی ۲» does
    /// not match every number containing a 2.
    /// </param>
    Task<IReadOnlyList<CustomerSearchResult>> SearchAsync(
        string nameTerm,
        string? mobileDigits,
        int maxResults,
        CancellationToken cancellationToken);
}

public interface ISearchCustomersHandler
{
    Task<IReadOnlyList<CustomerSearchResult>> ExecuteAsync(
        SearchCustomersQuery query,
        CancellationToken cancellationToken);
}

/// <summary>
/// Backs «انتخاب مشتری» (F4) on the sales screen — source-of-truth §6.4.
/// Covers name and mobile, which are the two fields a customer record has
/// today; §6.4 also lists customer code, national ID and club card, which join
/// the search when those fields are added to the customer (they are not
/// stubbed here).
/// </summary>
public sealed class SearchCustomersHandler : ISearchCustomersHandler
{
    private const int DefaultMaxResults = 8;
    private const int HardResultCap = 50;

    /// <summary>
    /// Fewer digits than this match too many numbers to be useful («۹» is in
    /// almost every mobile), so a shorter numeric term searches names only.
    /// </summary>
    private const int MinimumMobileDigits = 3;

    private readonly ICustomerSearchReader _reader;

    public SearchCustomersHandler(ICustomerSearchReader reader)
    {
        _reader = reader;
    }

    public Task<IReadOnlyList<CustomerSearchResult>> ExecuteAsync(
        SearchCustomersQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var term = query.Term?.Trim() ?? string.Empty;
        if (term.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<CustomerSearchResult>>([]);
        }

        var maxResults = Math.Clamp(
            query.MaxResults <= 0 ? DefaultMaxResults : query.MaxResults,
            1,
            HardResultCap);

        return _reader.SearchAsync(term, ToMobileDigits(term), maxResults, cancellationToken);
    }

    private static string? ToMobileDigits(string term)
    {
        // A phone fragment is digits plus the separators people type between
        // them; anything with letters in it is a name.
        var isPhoneLike = term.All(character =>
            char.IsDigit(character) || character is ' ' or '-' or '(' or ')' or '+');
        if (!isPhoneLike)
        {
            return null;
        }

        var digits = PersianNumber.ToLatinDigitsOnly(term);
        return digits.Length >= MinimumMobileDigits ? digits : null;
    }
}
