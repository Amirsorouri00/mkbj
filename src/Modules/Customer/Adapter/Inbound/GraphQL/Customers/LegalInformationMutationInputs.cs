using EwaysVNext.Modules.Customer.Application.Domain.Enums;

namespace EwaysVNext.Modules.Customer.Adapter.Inbound.GraphQL.Customers;

public sealed record RejectLegalInformationInput(
    long CustomerId,
    string Reason,
    LegalInformationEditableField[] EditableFields);
