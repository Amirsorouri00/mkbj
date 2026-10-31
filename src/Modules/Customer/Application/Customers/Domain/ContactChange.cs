using EwaysVNext.Domain.Common;

namespace EwaysVNext.Modules.Customer.Application.Customers.Domain;

/// <summary>
/// One recorded change to a customer's contact details.
/// <para>
/// These exist because a partner can overwrite a customer's email on any order, and "we sent the
/// invoice where you told us to" needs to be answerable months later: who changed it, from what,
/// to what, on whose instruction, and under which request. Without the old value the record
/// cannot settle anything.
/// </para>
/// </summary>
public sealed class ContactChange : Entity<long>
{
    private ContactChange()
    {
    }

    public long CustomerId { get; private set; }

    public ContactField Field { get; private set; }

    public string? OldValue { get; private set; }

    public string? NewValue { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }

    /// <summary>The partner whose sync caused this, or null when the customer changed it themselves.</summary>
    public long? SourcePartnerId { get; private set; }

    /// <summary>Ties the change to the request that made it, so a log search finds the whole story.</summary>
    public string CorrelationId { get; private set; } = string.Empty;

    internal static ContactChange Record(
        long id,
        long customerId,
        ContactField field,
        string? oldValue,
        string? newValue,
        long? sourcePartnerId,
        string correlationId,
        DateTimeOffset now) =>
        new()
        {
            Id = id,
            CustomerId = customerId,
            Field = field,
            OldValue = oldValue,
            NewValue = newValue,
            SourcePartnerId = sourcePartnerId,
            CorrelationId = correlationId ?? string.Empty,
            ChangedAt = now,
        };
}
