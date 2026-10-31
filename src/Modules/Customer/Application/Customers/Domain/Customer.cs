using EwaysVNext.Domain.Common;
using EwaysVNext.Domain.ValueObjects;

namespace EwaysVNext.Modules.Customer.Application.Customers.Domain;

/// <summary>
/// A buyer, from either sales path (spec §5.2).
/// <para>
/// One aggregate for both kinds, because they are the same person to the business and an order
/// refers to one customer however it arrived. What differs is who owns the details:
/// <see cref="CustomerKind.Website"/> customers own their own and Keycloak vouches for their
/// email; <see cref="CustomerKind.PartnerLinked"/> customers have whatever their partner last
/// sent, which nobody has verified.
/// </para>
/// <para>
/// Every change to a contact field is recorded. A partner can overwrite an email on any order, so
/// "we sent the invoice where you told us to" has to be answerable long afterwards.
/// </para>
/// </summary>
public sealed class Customer : AggregateRoot<long>
{
    private const string Validation = "VALIDATION_FAILED";

    private readonly List<ContactChange> _contactChanges = [];

    private Customer()
    {
    }

    public CustomerKind Kind { get; private set; }

    /// <summary>Keycloak's subject. A Guid this codebase does not own, so it stays a Guid.</summary>
    public Guid? IdentityUserId { get; private set; }

    public long? PartnerId { get; private set; }

    /// <summary>The partner's own id for this customer; unique within that partner.</summary>
    public string? ExternalCustomerId { get; private set; }

    public string? Email { get; private set; }

    public string? Phone { get; private set; }

    /// <summary>
    /// True only when an identity provider has confirmed the address. Never set by a partner
    /// sync: a partner asserting an email is not the same as a customer proving one.
    /// </summary>
    public bool EmailVerified { get; private set; }

    public string? FullName { get; private set; }

    public string? CompanyName { get; private set; }

    public Address? BillingAddress { get; private set; }

    public string? RegistrationNumber { get; private set; }

    public CustomerStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyList<ContactChange> ContactChanges => _contactChanges;

    public static Customer CreateWebsite(
        long id, Guid identityUserId, Email email, bool emailVerified, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(email);

        if (identityUserId == Guid.Empty)
            throw new DomainException(Validation, "A website customer needs an identity subject.");

        return new Customer
        {
            Id = id,
            Kind = CustomerKind.Website,
            IdentityUserId = identityUserId,
            Email = email.Value,
            EmailVerified = emailVerified,
            Status = CustomerStatus.Active,
            CreatedAt = now,
        };
    }

    public static Customer CreatePartnerLinked(
        long id,
        long partnerId,
        string externalCustomerId,
        Email? email,
        Phone? phone,
        string? fullName,
        DateTimeOffset now)
    {
        // At least one way to reach them. An order produces an invoice that has to go somewhere,
        // and a customer record with neither is one the seller can never contact about it.
        if (email is null && phone is null)
        {
            throw new DomainException("CUSTOMER_CONTACT_REQUIRED",
                "A partner-linked customer needs an email address or a phone number.");
        }

        var external = externalCustomerId?.Trim();

        if (string.IsNullOrEmpty(external))
            throw new DomainException(Validation, "The partner's customer reference is required.");

        if (external.Length > 100)
            throw new DomainException(Validation, "The partner's customer reference cannot exceed 100 characters.");

        return new Customer
        {
            Id = id,
            Kind = CustomerKind.PartnerLinked,
            PartnerId = partnerId,
            ExternalCustomerId = external,
            Email = email?.Value,
            Phone = phone?.Value,

            // A partner's assertion is not proof, whatever the partner believes.
            EmailVerified = false,
            FullName = Trimmed(fullName),
            Status = CustomerStatus.Active,
            CreatedAt = now,
        };
    }

    /// <summary>
    /// Applies contact details a partner sent. Returns whether anything actually changed, so the
    /// caller can skip a save for the common case where the partner re-sent what we already hold.
    /// </summary>
    public bool SyncPartnerContact(
        ContactUpdate update, long sourcePartnerId, string correlationId, Func<long> nextId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(nextId);

        RequireKind(CustomerKind.PartnerLinked);

        var changed = false;

        changed |= Apply(ContactField.Email, Email, update.Email?.Value,
            value => Email = value, sourcePartnerId, correlationId, nextId, now);

        changed |= Apply(ContactField.Phone, Phone, update.Phone?.Value,
            value => Phone = value, sourcePartnerId, correlationId, nextId, now);

        changed |= Apply(ContactField.FullName, FullName, Trimmed(update.FullName),
            value => FullName = value, sourcePartnerId, correlationId, nextId, now);

        return changed;
    }

    /// <summary>
    /// Brings a website customer's email into line with the identity provider, which is the
    /// authority for it — including whether it is verified.
    /// </summary>
    public void MirrorIdentity(
        Email email, bool emailVerified, string correlationId, Func<long> nextId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(nextId);

        RequireKind(CustomerKind.Website);

        Apply(ContactField.Email, Email, email.Value,
            value => Email = value, sourcePartnerId: null, correlationId, nextId, now);

        // Set unconditionally, and outside Apply: verification is not a contact change, and a
        // customer confirming an address they already had is not something to record as an edit.
        EmailVerified = emailVerified;
    }

    public void UpdateProfile(ProfileUpdate update, string correlationId, Func<long> nextId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(nextId);

        RequireKind(CustomerKind.Website);

        if (Status != CustomerStatus.Active)
        {
            throw new DomainException("CUSTOMER_SUSPENDED",
                "A suspended customer cannot change their profile.");
        }

        Apply(ContactField.FullName, FullName, Trimmed(update.FullName),
            value => FullName = value, sourcePartnerId: null, correlationId, nextId, now);

        Apply(ContactField.Phone, Phone, update.Phone?.Value,
            value => Phone = value, sourcePartnerId: null, correlationId, nextId, now);

        // Not contact fields, so they change without a recorded history: a dispute turns on where
        // we were told to send things, not on what the company was called at the time.
        CompanyName = update.CompanyName is null ? CompanyName : Trimmed(update.CompanyName);
        RegistrationNumber = update.RegistrationNumber is null ? RegistrationNumber : Trimmed(update.RegistrationNumber);
        BillingAddress = update.BillingAddress ?? BillingAddress;
    }

    public void Suspend() => Status = CustomerStatus.Suspended;

    public void Reactivate() => Status = CustomerStatus.Active;

    /// <summary>
    /// Writes one field and records the change, unless the value was omitted or is what we
    /// already hold. Returns whether it changed anything.
    /// </summary>
    private bool Apply(
        ContactField field,
        string? current,
        string? incoming,
        Action<string?> set,
        long? sourcePartnerId,
        string correlationId,
        Func<long> nextId,
        DateTimeOffset now)
    {
        // Null means omitted, never "clear this". A partner that does not hold phone numbers
        // sends null on every order; treating that as a deletion would erase details the
        // customer gave the seller directly.
        if (incoming is null || string.Equals(current, incoming, StringComparison.Ordinal))
            return false;

        set(incoming);

        _contactChanges.Add(ContactChange.Record(
            nextId(), Id, field, current, incoming, sourcePartnerId, correlationId, now));

        return true;
    }

    private void RequireKind(CustomerKind required)
    {
        if (Kind != required)
        {
            throw new DomainException(Validation,
                $"This operation applies to {required} customers; this one is {Kind}.");
        }
    }

    private static string? Trimmed(string? value)
    {
        var trimmed = value?.Trim();

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
