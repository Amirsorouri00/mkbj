using EwaysVNext.Database;
using EwaysVNext.Modules.Customer.Application.Domain.Customers;
using EwaysVNext.Modules.Customer.Application.Domain.ValueObjects;
using EwaysVNext.Modules.Customer.Application.Ports.Outbound;
using EwaysVNext.Modules.Location.Application.Domain.Locations;
using EwaysVNext.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EwaysVNext.Modules.Customer.Adapter.Outbound.Repositories;
    internal sealed class CustomerRepository(EwaysVNextDbContext db) : EfCoreRepository<Application.Domain.Customers.Customer, long>(db),
    ICustomerRepository
{
    private readonly EwaysVNextDbContext _db = db;
    public new async Task<Application.Domain.Customers.Customer?> GetByIdAsync(long id, CancellationToken ct = default)
        => await _db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
    public async Task<Application.Domain.Customers.Customer?> GetByIdentityUserIdAsync(Guid identityUserId, CancellationToken ct = default)
        => await _db.Customers.FirstOrDefaultAsync(c => c.IdentityUserId == identityUserId, ct);
    public async Task<Application.Domain.Customers.Customer?> GetCustomerWithVerificationAsync(
    Guid identityUserId,
    CancellationToken ct = default)=>
         await _db.Customers
            .Include(c => c.Verification)
            .FirstOrDefaultAsync(c => c.IdentityUserId == identityUserId, ct);
    public async Task<Application.Domain.Customers.Customer?> GetWithAddressesByIdentityUserIdAsync(
    Guid identityUserId,
    CancellationToken ct = default) =>
         await _db.Customers
            .Include(c => c.Addresses)
            .FirstOrDefaultAsync(c => c.IdentityUserId == identityUserId, ct);

    public async Task<Application.Domain.Customers.Customer?> GetCustomerWithLegalInformationAsync(
        Guid identityUserId,
        CancellationToken ct = default) =>
        await _db.Customers
            .Include(c => c.LegalInformation)
            .ThenInclude(l => l!.Documents)
            .Include(c => c.Addresses)
            .FirstOrDefaultAsync(c => c.IdentityUserId == identityUserId, ct);

    public async Task<Application.Domain.Customers.Customer?> GetByIdWithLegalInformationAsync(
        long customerId,
        CancellationToken ct = default) =>
        await _db.Customers
            .Include(c => c.LegalInformation)
            .ThenInclude(l => l!.Documents)
            .Include(c => c.Addresses)
            .FirstOrDefaultAsync(c => c.Id == customerId, ct);

    public Task<bool> ExistsForIdentityUserAsync(Guid id, CancellationToken ct = default)
        => _db.Customers.AnyAsync(c => c.IdentityUserId == id, ct);

    public override async Task AddAsync(Application.Domain.Customers.Customer aggregate, CancellationToken ct = default)
        => await _db.Customers.AddAsync(aggregate, ct);

    public void Update(Application.Domain.Customers.Customer aggregate)
        => _db.Customers.Update(aggregate);
    public async Task<bool> ExistsForMobileNumberAsync(MobileNumber mobileNumber, CancellationToken ct = default)
         => await _db.Customers.AnyAsync(c => c.MobileNumber == mobileNumber.Value, ct);

    public IQueryable<CustomerAdminListItem> QueryForAdminList(CustomerAdminListFilter filter)
    {
        var query = _db.Customers.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Name))
        {
            var name = filter.Name.Trim();
            query = query.Where(c => c.FirstName != null && c.FirstName.Contains(name));
        }

        if (!string.IsNullOrWhiteSpace(filter.Family))
        {
            var family = filter.Family.Trim();
            query = query.Where(c => c.LastName != null && c.LastName.Contains(family));
        }

        if (!string.IsNullOrWhiteSpace(filter.UserCode))
        {
            var userCode = filter.UserCode.Trim();
            // EF Core translates the parameterless ToString() to a SQL CAST; the
            // IFormatProvider overload CA1305 asks for is not translatable.
#pragma warning disable CA1305
            query = query.Where(c => c.Id.ToString().Contains(userCode));
#pragma warning restore CA1305
        }

        if (!string.IsNullOrWhiteSpace(filter.NationalId))
        {
            var nationalId = filter.NationalId.Trim();
            query = query.Where(c => c.NationalCode != null && c.NationalCode.Contains(nationalId));
        }

        if (!string.IsNullOrWhiteSpace(filter.PhoneNumber))
        {
            var phoneNumber = filter.PhoneNumber.Trim();
            query = query.Where(c => c.MobileNumber.Contains(phoneNumber));
        }

        if (filter.FromDate is not null)
            query = query.Where(c => c.CreatedAt >= filter.FromDate.Value.Date);

        if (filter.ToDate is not null)
            query = query.Where(c => c.CreatedAt < filter.ToDate.Value.Date.AddDays(1));

        if (filter.ProvinceId is not null || filter.CityId is not null || filter.DistrictId is not null)
        {
            var addressIds = _db.Addresses.AsNoTracking()
                .Where(a => filter.DistrictId == null || a.DistrictId == filter.DistrictId)
                .Where(a => filter.CityId == null || a.CityId == filter.CityId)
                .Where(a => filter.ProvinceId == null ||
                    _db.Set<City>().Any(city => city.Id == a.CityId && city.ProvinceId == filter.ProvinceId))
                .Select(a => a.Id);

            query = query.Where(c => c.Addresses.Any(ca => addressIds.Contains(ca.AddressId)));
        }

        if (filter.RegistrationStatuses is not null)
        {
            var statusIds = filter.RegistrationStatuses.Select(s => (int)s).ToArray();
            query = query.Where(c => c.StatusId != null && statusIds.Contains(c.StatusId.Value));
        }

        return query
            .Select(c => new
            {
                Customer = c,
                PrimaryLocation = (
                    from a in _db.Addresses.AsNoTracking()
                    join city in _db.Set<City>() on a.CityId equals city.Id
                    join province in _db.Provinces on city.ProvinceId equals province.Id
                    join district in _db.Set<District>() on a.DistrictId equals (int?)district.Id into districtJoin
                    from district in districtJoin.DefaultIfEmpty()
                    where a.IsPrimary && c.Addresses.Any(ca => ca.AddressId == a.Id)
                    select new
                    {
                        CityId = (int?)a.CityId,
                        CityName = (string?)city.Name,
                        ProvinceId = (int?)city.ProvinceId,
                        ProvinceName = (string?)province.Name,
                        a.DistrictId,
                        DistrictName = (string?)district.Name
                    }
                ).FirstOrDefault()
            })
            .OrderByDescending(c => c.Customer.Id)
            .Select(x => new CustomerAdminListItem(
                x.Customer.Id,
                x.Customer.FirstName,
                x.Customer.LastName,
                x.Customer.NationalCode,
                x.Customer.MobileNumber,
                x.Customer.BirthDate,
                x.Customer.CreatedAt,
                x.Customer.StatusId,
                x.Customer.VerificationStatusId,
                x.Customer.Verification != null
                ? x.Customer.Verification.VerificationMethod
                : null,

                x.Customer.Verification != null? x.Customer.Verification.Reason
                : null,
                x.PrimaryLocation != null ? x.PrimaryLocation.ProvinceId : null,
                x.PrimaryLocation != null ? x.PrimaryLocation.ProvinceName : null,
                x.PrimaryLocation != null ? x.PrimaryLocation.CityId : null,
                x.PrimaryLocation != null ? x.PrimaryLocation.CityName : null,
                x.PrimaryLocation != null ? x.PrimaryLocation.DistrictId : null,
                x.PrimaryLocation != null ? x.PrimaryLocation.DistrictName : null));
    }

    public async Task<Application.Domain.Customers.Customer?> GetForAdminUpdateAsync(long id, CancellationToken ct = default)
        => await _db.Customers
            .Include(c => c.JobTypes)
            .Include(c => c.Addresses)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IReadOnlyCollection<int>> GetExistingJobTypeIdsAsync(IReadOnlyCollection<int> jobTypeIds, CancellationToken ct = default)
        => await _db.Set<JobType>().AsNoTracking()
            .Where(j => jobTypeIds.Contains(j.Id))
            .Select(j => j.Id)
            .ToListAsync(ct);

    public IQueryable<JobTypeItem> QueryJobTypes()
        => _db.Set<JobType>().AsNoTracking()
            .OrderBy(j => j.Id)
            .Select(j => new JobTypeItem(j.Id, j.Name));

    public async Task<CustomerRegistrationReview?> GetRegistrationReviewAsync(long customerId, CancellationToken ct = default) =>
        await _db.Customers.AsNoTracking()
            .Where(c => c.Id == customerId)
            .Select(c => new CustomerRegistrationReview(
                c.Id,
                c.CreatedAt,
                c.Verification != null ? c.Verification.VerifiedAt : null,
                c.StatusId,
                c.Verification != null ? c.Verification.VerificationMethod : null,
                c.Verification != null ? c.Verification.Reason : null))
            .SingleOrDefaultAsync(ct);
    public async Task<CustomerAdminDetails?> GetAdminDetailsAsync(long customerId, CancellationToken ct = default)
    {
        var details = await _db.Customers.AsNoTracking()
            .Where(c => c.Id == customerId)
            .Select(c => new
            {
                Customer = c,
                c.Verification,
                PrimaryLocation = (
                    from a in _db.Addresses.AsNoTracking()
                    join city in _db.Set<City>() on a.CityId equals city.Id
                    join province in _db.Provinces on city.ProvinceId equals province.Id
                    join district in _db.Set<District>() on a.DistrictId equals (int?)district.Id into districtJoin
                    from district in districtJoin.DefaultIfEmpty()
                    where a.IsPrimary && c.Addresses.Any(ca => ca.AddressId == a.Id)
                    select new
                    {
                        CityId = (int?)a.CityId,
                        CityName = (string?)city.Name,
                        ProvinceId = (int?)city.ProvinceId,
                        ProvinceName = (string?)province.Name,
                        a.DistrictId,
                        DistrictName = (string?)district.Name,
                        a.AddressText,
                        a.PostalCode
                    }
                ).FirstOrDefault(),
                JobTypes = (
                    from cj in c.JobTypes
                    join j in _db.Set<JobType>() on cj.JobTypeId equals j.Id
                    orderby j.Id
                    select new JobTypeItem(j.Id, j.Name)
                ).ToList()
            })
            .SingleOrDefaultAsync(ct);

        if (details is null)
            return null;

        var customer = details.Customer;
        var location = details.PrimaryLocation;
        return new CustomerAdminDetails(
            customer.Id,
            customer.FirstName,
            customer.LastName,
            customer.NationalCode,
            customer.MobileNumber,
            customer.Email,
            customer.SecondaryPhoneNumber,
            customer.Gender,
            customer.BirthDate,
            customer.EconomicCode,
            customer.IntaCode,
            customer.RoleCode,
            customer.CreatedAt,
            customer.StatusId,
            customer.VerificationStatusId,
            details.Verification?.VerificationMethod,
            details.Verification?.Reason,
            location?.ProvinceId,
            location?.ProvinceName,
            location?.CityId,
            location?.CityName,
            location?.DistrictId,
            location?.DistrictName,
            location?.AddressText,
            location?.PostalCode,
            details.JobTypes);
    }
}
