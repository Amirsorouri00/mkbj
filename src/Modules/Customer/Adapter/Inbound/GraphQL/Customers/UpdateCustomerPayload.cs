namespace EwaysVNext.Modules.Customer.Adapter.Inbound.GraphQL.Customers;

public sealed record UpdateCustomerPayload(long CustomerId, bool Success);
