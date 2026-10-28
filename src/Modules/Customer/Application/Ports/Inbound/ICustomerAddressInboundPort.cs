using EwaysVNext.Modules.Customer.Application.Ports.Inbound.Dtos;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Customer.Application.Ports.Inbound;

public interface ICustomerAddressInboundPort
{
    Task<Result<IReadOnlyList<CustomerAddressDto>>> GetAddressesAsync(Guid identityUserId, CancellationToken ct = default);

    Task<Result<CustomerAddressDto>> GetAddressAsync(Guid identityUserId, long addressId, CancellationToken ct = default);

    Task<Result<CustomerAddressDto>> CreateAddressAsync(Guid identityUserId, CustomerAddressInput input, CancellationToken ct = default);

    Task<Result<CustomerAddressDto>> UpdateAddressAsync(Guid identityUserId, long addressId, CustomerAddressInput input, CancellationToken ct = default);

    Task<Result> DeleteAddressAsync(Guid identityUserId, long addressId, CancellationToken ct = default);

    Task<Result<CustomerAddressDto>> SetDefaultAddressAsync(Guid identityUserId, long addressId, CancellationToken ct = default);
}
