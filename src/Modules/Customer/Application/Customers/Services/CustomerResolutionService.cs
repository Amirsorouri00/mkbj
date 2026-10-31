using EwaysVNext.Domain.Common;
using EwaysVNext.Domain.ValueObjects;
using EwaysVNext.Modules.Customer.Application.Customers.Domain;
using EwaysVNext.Modules.Customer.Application.Customers.Ports.Inbound;
using EwaysVNext.Modules.Customer.Application.Customers.Ports.Outbound;
using EwaysVNext.Outcomes;
using EwaysVNext.Persistence;

using CustomerAggregate = EwaysVNext.Modules.Customer.Application.Customers.Domain.Customer;

namespace EwaysVNext.Modules.Customer.Application.Customers.Services;

public sealed class CustomerResolutionService(
    ICustomerRepository repository,
    IUnitOfWork unitOfWork,
    IIdGenerator ids,
    IClock clock) : ICustomerResolutionPort
{
    public async Task<PartnerCustomerRef?> FindPartnerCustomerAsync(
        long partnerId, string externalCustomerId, CancellationToken ct = default)
    {
        var customer = await repository.FindPartnerLinkedAsync(partnerId, externalCustomerId, ct);

        return customer is null ? null : Ref(customer);
    }

    public async Task<Result<PartnerCustomerRef>> CreatePartnerCustomerAsync(
        long partnerId,
        string externalCustomerId,
        ContactInput contact,
        string correlationId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(contact);

        if (!TryContact(contact, out var email, out var phone, out var contactError))
            return Result.Failure<PartnerCustomerRef>(contactError!);

        CustomerAggregate customer;
        try
        {
            customer = CustomerAggregate.CreatePartnerLinked(
                ids.Generate(), partnerId, externalCustomerId, email, phone, contact.FullName, clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Result.Failure<PartnerCustomerRef>(new Error(ex.Code, ex.Message));
        }

        repository.Add(customer);

        var outcome = await unitOfWork.TrySaveChangesAsync(ct);

        if (outcome == SaveOutcome.UniqueViolation)
        {
            // Another request created the same (partner, external id) between our check and our
            // insert. The filtered unique index is what makes that safe to race: the loser reads
            // the winner's row instead of failing, so a partner retrying an order never sees a
            // conflict it cannot act on.
            var existing = await repository.FindPartnerLinkedAsync(partnerId, externalCustomerId, ct);

            return existing is null
                ? Result.Failure<PartnerCustomerRef>(Error.PersistenceConflict)
                : Result.Success(Ref(existing));
        }

        return Result.Success(Ref(customer));
    }

    public async Task<Result> SyncPartnerContactAsync(
        long customerId,
        long partnerId,
        ContactInput contact,
        string correlationId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(contact);

        var customer = await repository.GetAsync(customerId, ct);

        // A customer belonging to another partner is reported as not found, not as forbidden.
        // "Forbidden" would confirm the id exists, which is enough to probe for a competitor's
        // customers one id at a time.
        if (customer is null || customer.PartnerId != partnerId)
            return Result.Failure(Error.NotFound);

        if (!TryContact(contact, out var email, out var phone, out var contactError, requireOne: false))
            return Result.Failure(contactError!);

        bool changed;
        try
        {
            changed = customer.SyncPartnerContact(
                new ContactUpdate(email, phone, contact.FullName), partnerId, correlationId, ids.Generate, clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Result.Failure(new Error(ex.Code, ex.Message));
        }

        // Nothing moved, so nothing to write. A partner re-sending the details it already sent is
        // the common case, and saving anyway would bump the row's xmin and make every such order
        // look like an edit.
        if (changed)
            await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result<InvoiceParty>> GetInvoicePartyAsync(long customerId, CancellationToken ct = default)
    {
        var customer = await repository.GetAsync(customerId, ct);

        if (customer is null)
            return Result.Failure<InvoiceParty>(Error.NotFound);

        var address = customer.BillingAddress is null
            ? null
            : new AddressSnapshot(
                customer.BillingAddress.Line1,
                customer.BillingAddress.Line2,
                customer.BillingAddress.City,
                customer.BillingAddress.PostalCode,
                customer.BillingAddress.CountryIso2);

        return Result.Success(new InvoiceParty(
            customer.Id, customer.FullName, customer.CompanyName,
            customer.Email, customer.Phone, address, customer.RegistrationNumber));
    }

    /// <summary>
    /// Parses what the partner sent. A malformed value is refused rather than dropped: silently
    /// ignoring a bad phone number would leave the partner believing they had supplied one.
    /// </summary>
    private static bool TryContact(
        ContactInput contact, out Email? email, out Phone? phone, out Error? error, bool requireOne = true)
    {
        email = null;
        phone = null;
        error = null;

        if (contact.Email is not null && !Email.TryCreate(contact.Email, out email))
        {
            error = new Error("VALIDATION_FAILED", $"'{contact.Email}' is not a valid email address.");
            return false;
        }

        if (contact.Phone is not null && !Phone.TryCreate(contact.Phone, out phone))
        {
            error = new Error("VALIDATION_FAILED",
                $"'{contact.Phone}' is not a valid phone number. Use E.164, for example '+6591234567'.");
            return false;
        }

        if (requireOne && email is null && phone is null)
        {
            error = new Error("CUSTOMER_CONTACT_REQUIRED",
                "A partner-linked customer needs an email address or a phone number.", 400);
            return false;
        }

        return true;
    }

    private static PartnerCustomerRef Ref(CustomerAggregate customer) =>
        new(customer.Id, customer.Email, customer.Phone, customer.FullName);
}
