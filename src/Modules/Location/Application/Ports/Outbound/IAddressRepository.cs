
using EwaysVNext.Modules.Location.Application.Domain.Locations;

namespace EwaysVNext.Modules.Location.Application.Ports.Outbound;

public interface IAddressRepository
{
    Task AddAsync(Address address, CancellationToken ct);
    Task<Address?> GetByIdAsync(long id,CancellationToken ct);
    Task<List<Address>?> GetByIdsAsync(List<long> id,CancellationToken ct);
    Task<List<Address>> GetTrackedByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct);
}
