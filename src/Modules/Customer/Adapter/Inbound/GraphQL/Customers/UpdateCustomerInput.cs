using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Customer.Application.Domain.Enums;

namespace EwaysVNext.Modules.Customer.Adapter.Inbound.GraphQL.Customers;

public sealed record UpdateCustomerInput(
    SnowflakeId CustomerId,
    Gender Gender,
    string? EconomicCode = null,
    string? IntaCode = null,
    string? RoleCode = null,
    IReadOnlyList<int>? JobTypeIds = null,
    string? Email = null,
    string? SecondaryPhoneNumber = null,
    UpdateCustomerAddressInput? Address = null);
