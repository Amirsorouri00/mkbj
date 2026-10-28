using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Customer.Application.Domain.Enums;

namespace EwaysVNext.Modules.Customer.Adapter.Inbound.GraphQL.Customers;

public sealed record UpdateCustomerVerificationStatusInput(SnowflakeId CustomerId, VerificationStatusType Status, string? Reason = null);
