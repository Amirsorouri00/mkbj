using EwaysVNext.Domain.Common;
using EwaysVNext.Domain.ValueObjects;
using EwaysVNext.Modules.Customer.Application.Customers.Domain;
using EwaysVNext.Modules.Customer.Application.Customers.Ports.Inbound;
using EwaysVNext.Modules.Customer.Application.Customers.Ports.Outbound;
using EwaysVNext.Outcomes;
using EwaysVNext.Persistence;

using CustomerAggregate = EwaysVNext.Modules.Customer.Application.Customers.Domain.Customer;

namespace EwaysVNext.Modules.Customer.Application.Customers.Services;

public sealed class WebsiteCustomerService(
    ICustomerRepository repository,
    IUnitOfWork unitOfWork,
    IIdGenerator ids,
    IClock clock) : IWebsiteCustomerPort
{
    /// <summary>
    /// Identity mirroring is not a person's edit, so it is tagged rather than left with whatever
    /// request happened to trigger it.
    /// </summary>
    private const string IdentityCorrelationId = "identity-sync";

    public async Task<Result<WebsiteCustomerRef>> EnsureFromIdentityAsync(
        Guid identityUserId, string email, bool emailVerified, CancellationToken ct = default)
    {
        if (!Email.TryCreate(email, out var parsed))
            return Result.Failure<WebsiteCustomerRef>(new Error("VALIDATION_FAILED", $"'{email}' is not a valid email address."));

        var existing = await repository.FindWebsiteAsync(identityUserId, ct);

        if (existing is not null)
        {
            var before = (existing.Email, existing.EmailVerified);

            existing.MirrorIdentity(parsed!, emailVerified, IdentityCorrelationId, ids.Generate, clock.UtcNow);

            // Saved only when the identity actually moved. This runs on every authenticated
            // request, and writing each time would make the row a contention point for a user
            // with several tabs open.
            if (before != (existing.Email, existing.EmailVerified))
                await unitOfWork.SaveChangesAsync(ct);

            return Result.Success(Ref(existing));
        }

        CustomerAggregate customer;
        try
        {
            customer = CustomerAggregate.CreateWebsite(ids.Generate(), identityUserId, parsed!, emailVerified, clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Result.Failure<WebsiteCustomerRef>(new Error(ex.Code, ex.Message));
        }

        repository.Add(customer);

        if (await unitOfWork.TrySaveChangesAsync(ct) == SaveOutcome.UniqueViolation)
        {
            // Two tabs authenticating at once both find nothing and both insert. The filtered
            // unique index on the identity subject decides it; the loser reads the winner's row.
            var raced = await repository.FindWebsiteAsync(identityUserId, ct);

            return raced is null
                ? Result.Failure<WebsiteCustomerRef>(Error.PersistenceConflict)
                : Result.Success(Ref(raced));
        }

        return Result.Success(Ref(customer));
    }

    private static WebsiteCustomerRef Ref(CustomerAggregate customer) =>
        new(customer.Id, customer.EmailVerified, customer.Status == CustomerStatus.Active);
}
