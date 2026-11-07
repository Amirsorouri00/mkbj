using EwaysVNext.Database;
using EwaysVNext.Modules.Sales.Application.Domain.Orders;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;
using EwaysVNext.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EwaysVNext.Modules.Sales.Adapter.Outbound.Persistence;

/// <summary>
/// Orders, read and written through the one shared context.
/// </summary>
internal sealed class OrderRepository(EwaysVNextDbContext db, IUnitOfWork unitOfWork) : IOrderRepository
{
    private const string PartnerExternalIndex = "ux_orders_partner_external";
    private const string UniqueViolation = "23505";

    public Task<Order?> GetByIdAsync(long orderId, CancellationToken ct = default) =>
        db.Orders.FirstOrDefaultAsync(order => order.Id == orderId, ct);

    public async Task<PartnerOrderKey?> FindPartnerOrderKeyAsync(
        long partnerId, string externalOrderId, CancellationToken ct = default) =>
        await db.Orders
            .AsNoTracking()
            .Where(order => order.PartnerId == partnerId
                && order.ExternalOrderId == externalOrderId
                && order.Channel == OrderChannel.PartnerApi)
            .Select(order => new PartnerOrderKey(order.Id, order.RequestFingerprint!))
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Inserts, or reports that this partner already used that external id.
    /// </summary>
    /// <remarks>
    /// The check-then-insert race is real — two copies of one retry can both find nothing and
    /// both insert — so the unique index is what decides, and losing is an ordinary outcome
    /// rather than an exception. The graph is detached on the way out so the caller's next save
    /// does not try the same insert again.
    /// </remarks>
    public async Task<bool> TryInsertPartnerOrderAsync(Order order, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        db.Orders.Add(order);

        try
        {
            await unitOfWork.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException exception) when (IsPartnerExternalClash(exception))
        {
            Detach(order);
            return false;
        }
    }

    public Task AddAsync(Order order, CancellationToken ct = default)
    {
        db.Orders.Add(order);
        return Task.CompletedTask;
    }

    /// <summary>
    /// A document, if this caller is entitled to it.
    /// </summary>
    /// <remarks>
    /// The entitlement is part of the query. A "load, then compare" shape pulls the content into
    /// the process before deciding, and then the only thing standing between a partner and
    /// another partner's invoice is the comparison afterwards.
    /// </remarks>
    public async Task<DocumentForRendering?> FindDocumentAsync(
        long documentId, DocumentAccess access, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(access);

        var orders = db.Orders.AsNoTracking();

        orders = access switch
        {
            DocumentAccess.Partner partner => orders.Where(order =>
                order.PartnerId == partner.PartnerId && order.Channel == OrderChannel.PartnerApi),
            DocumentAccess.WebsiteCustomer customer => orders.Where(order =>
                order.CustomerId == customer.CustomerId && order.Channel == OrderChannel.Website),
            DocumentAccess.Admin => orders,
            _ => throw new ArgumentOutOfRangeException(nameof(access), access, "Unknown access."),
        };

        return await orders
            .SelectMany(order => order.Documents
                .Where(document => document.Id == documentId)
                .Select(document => new DocumentForRendering(
                    order.Id,
                    document.Id,
                    document.Type,
                    document.Number,
                    document.Content,
                    document.PdfObjectKey)))
            .FirstOrDefaultAsync(ct);
    }

    private static bool IsPartnerExternalClash(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: UniqueViolation,
            ConstraintName: PartnerExternalIndex,
        };

    /// <summary>
    /// Detaches the order and everything the context dragged in with it.
    /// </summary>
    /// <remarks>
    /// Detaching the root alone leaves its owned lines, payments and documents Added, and the
    /// next save retries exactly the insert that just failed.
    /// </remarks>
    private void Detach(Order order)
    {
        foreach (var entry in db.ChangeTracker.Entries().ToArray())
        {
            if (ReferenceEquals(entry.Entity, order) || entry.State == EntityState.Added)
            {
                entry.State = EntityState.Detached;
            }
        }
    }
}
