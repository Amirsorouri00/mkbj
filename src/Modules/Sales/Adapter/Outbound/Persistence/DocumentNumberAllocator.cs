using EwaysVNext.Database;
using EwaysVNext.Modules.Sales.Application.Domain.Documents;
using EwaysVNext.Modules.Sales.Application.Domain.Orders;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;
using Microsoft.EntityFrameworkCore;

namespace EwaysVNext.Modules.Sales.Adapter.Outbound.Persistence;

/// <summary>
/// Draws the next order or document number from its sequence.
/// </summary>
/// <remarks>
/// <para>
/// A Postgres sequence rather than <c>max(number) + 1</c>: the latter needs a lock to be safe
/// under concurrency, and two orders that both read the same maximum would be issued the same
/// invoice number — which is the one kind of duplicate an auditor will certainly notice.
/// </para>
/// <para>
/// A sequence is <b>not</b> transactional: a rolled-back order consumes its number, leaving a
/// gap. That is the accepted trade. Gapless numbering under concurrency requires serialising
/// every order behind one lock, and a gap is explainable where a duplicate is not.
/// </para>
/// </remarks>
internal sealed class DocumentNumberAllocator(EwaysVNextDbContext db) : IDocumentNumberAllocator
{
    private const string NpgsqlProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";

    /// <summary>
    /// Counters for the development host, which runs on EF InMemory with no sequences.
    /// </summary>
    /// <remarks>
    /// Process-local and therefore useless for anything but a single developer's machine —
    /// which is exactly what it is for. It is keyed per kind so the numbers still look right.
    /// </remarks>
    private static readonly Dictionary<DocumentNumberKind, long> InMemoryCounters = [];

    public async Task<string> NextAsync(DocumentNumberKind kind, CancellationToken ct = default)
    {
        var next = db.Database.ProviderName == NpgsqlProvider
            ? await NextFromSequenceAsync(kind, ct)
            : NextInMemory(kind);

        return Format(kind, next);
    }

    /// <summary>
    /// Whole statements, one per kind, rather than a name interpolated into one.
    /// </summary>
    /// <remarks>
    /// A sequence name cannot be a parameter, so the alternative is interpolation — and an
    /// interpolated identifier is the shape a SQL injection takes, whether or not this
    /// particular value comes from a closed enum. Constants mean there is nothing to review.
    /// The alias is <c>"Value"</c> because that is the column name a scalar
    /// <c>SqlQueryRaw&lt;T&gt;</c> looks for.
    /// </remarks>
    private async Task<long> NextFromSequenceAsync(DocumentNumberKind kind, CancellationToken ct)
    {
        const string Order = "SELECT nextval('sales.order_number_seq') AS \"Value\"";
        const string ProForma = "SELECT nextval('sales.pf_number_seq') AS \"Value\"";
        const string Commercial = "SELECT nextval('sales.inv_number_seq') AS \"Value\"";
        const string CreditNote = "SELECT nextval('sales.cn_number_seq') AS \"Value\"";

        var sql = kind switch
        {
            DocumentNumberKind.Order => Order,
            DocumentNumberKind.ProForma => ProForma,
            DocumentNumberKind.Commercial => Commercial,
            DocumentNumberKind.CreditNote => CreditNote,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown number kind."),
        };

        return await db.Database.SqlQueryRaw<long>(sql).SingleAsync(ct);
    }

    private static long NextInMemory(DocumentNumberKind kind)
    {
        lock (InMemoryCounters)
        {
            var next = InMemoryCounters.GetValueOrDefault(kind) + 1;
            InMemoryCounters[kind] = next;

            return next;
        }
    }

    private static string Format(DocumentNumberKind kind, long sequence) => kind switch
    {
        DocumentNumberKind.Order => OrderNumberFormat.Order(sequence),
        DocumentNumberKind.ProForma => OrderNumberFormat.Document(DocumentType.ProForma, sequence),
        DocumentNumberKind.Commercial => OrderNumberFormat.Document(DocumentType.Commercial, sequence),
        DocumentNumberKind.CreditNote => OrderNumberFormat.Document(DocumentType.CreditNote, sequence),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown number kind."),
    };
}
