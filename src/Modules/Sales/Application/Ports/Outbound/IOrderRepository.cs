using EwaysVNext.Modules.Sales.Application.Domain.Documents;
using EwaysVNext.Modules.Sales.Application.Domain.Orders;

namespace EwaysVNext.Modules.Sales.Application.Ports.Outbound;

/// <summary>Enough of an existing partner order to decide what a repeat request means.</summary>
public sealed record PartnerOrderKey(long OrderId, string RequestFingerprint);

/// <summary>
/// Who is asking for a document, which decides which documents they may see.
/// </summary>
/// <remarks>
/// A type rather than a nullable pair of ids, so a caller cannot forget to scope the query. The
/// alternative — passing <c>partnerId: null</c> to mean "admin" — makes an unscoped read look
/// exactly like a bug, and one of them eventually is one.
/// </remarks>
public abstract record DocumentAccess
{
    public sealed record Partner(long PartnerId) : DocumentAccess;

    public sealed record WebsiteCustomer(long CustomerId) : DocumentAccess;

    public sealed record Admin : DocumentAccess;
}

public sealed record DocumentForRendering(
    long OrderId,
    long DocumentId,
    DocumentType Type,
    string Number,
    DocumentContent Content,
    string PdfObjectKey);

public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(long orderId, CancellationToken ct = default);

    /// <summary>
    /// The order a partner already has under this external id, if any.
    /// </summary>
    /// <remarks>
    /// The fingerprint comes back with it so the caller can tell a retry of the same request
    /// from a reuse of the same key for a different one.
    /// </remarks>
    Task<PartnerOrderKey?> FindPartnerOrderKeyAsync(
        long partnerId, string externalOrderId, CancellationToken ct = default);

    /// <summary>
    /// Inserts the order, or reports that this partner already used that external id.
    /// </summary>
    /// <remarks>
    /// The race is real: two copies of the same retry can both pass a "does it exist" check and
    /// then both insert. The unique index is what actually decides, and this turns losing that
    /// race into <c>false</c> rather than an exception — with the order graph detached, so the
    /// caller's next save does not try again.
    /// </remarks>
    Task<bool> TryInsertPartnerOrderAsync(Order order, CancellationToken ct = default);

    Task AddAsync(Order order, CancellationToken ct = default);

    /// <summary>
    /// A document, if the caller is entitled to it.
    /// </summary>
    /// <remarks>
    /// Scoped in the query rather than checked afterwards: a "load then compare" shape returns
    /// the content to the caller's process before deciding, and the difference between 404 and
    /// 403 then depends on code that has already read the row.
    /// </remarks>
    Task<DocumentForRendering?> FindDocumentAsync(
        long documentId, DocumentAccess access, CancellationToken ct = default);
}

/// <summary>Which gapless sequence a number comes from.</summary>
public enum DocumentNumberKind
{
    Order = 1,
    ProForma = 2,
    Commercial = 3,
    CreditNote = 4,
}

/// <summary>
/// Allocates the next number of a kind.
/// </summary>
/// <remarks>
/// A port rather than a call to a sequence, because numbering is a shared, gapless, auditable
/// thing that no single aggregate owns — and because the numbers must keep working when the
/// development host runs on EF InMemory with no sequences at all.
/// </remarks>
public interface IDocumentNumberAllocator
{
    Task<string> NextAsync(DocumentNumberKind kind, CancellationToken ct = default);
}
