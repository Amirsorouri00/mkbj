using EwaysVNext.Modules.Customer.Application.Domain.ValueObjects;

namespace EwaysVNext.Modules.Customer.Application.Ports.Outbound;

public interface ICustomerRepository
{
    Task<Domain.Customers.Customer?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<Application.Domain.Customers.Customer?> GetByIdentityUserIdAsync(Guid identityUserId, CancellationToken ct = default);
    Task<Domain.Customers.Customer?> GetWithAddressesByIdentityUserIdAsync(Guid identityUserId, CancellationToken ct = default);
    Task<bool> ExistsForIdentityUserAsync(Guid identityUserId, CancellationToken ct = default);
    Task<bool> ExistsForMobileNumberAsync(MobileNumber mobile, CancellationToken ct = default);
    Task AddAsync(Domain.Customers.Customer aggregate, CancellationToken ct = default);
    void Update(Domain.Customers.Customer customer);
    Task<Application.Domain.Customers.Customer?> GetCustomerWithVerificationAsync(
    Guid identityUserId,
    CancellationToken ct = default);

    Task<Application.Domain.Customers.Customer?> GetCustomerWithLegalInformationAsync(
    Guid identityUserId,
    CancellationToken ct = default);

    Task<Application.Domain.Customers.Customer?> GetByIdWithLegalInformationAsync(
    long customerId,
    CancellationToken ct = default);

    IQueryable<CustomerAdminListItem> QueryForAdminList(CustomerAdminListFilter filter);

    /// <summary>Tracked customer with its job types and address links - what the admin edit mutates.</summary>
    Task<Domain.Customers.Customer?> GetForAdminUpdateAsync(long id, CancellationToken ct = default);

    Task<IReadOnlyCollection<int>> GetExistingJobTypeIdsAsync(IReadOnlyCollection<int> jobTypeIds, CancellationToken ct = default);

    IQueryable<JobTypeItem> QueryJobTypes();

    Task<CustomerRegistrationReview?> GetRegistrationReviewAsync(long customerId, CancellationToken ct = default);

    Task<CustomerAdminDetails?> GetAdminDetailsAsync(long customerId, CancellationToken ct = default);
}
