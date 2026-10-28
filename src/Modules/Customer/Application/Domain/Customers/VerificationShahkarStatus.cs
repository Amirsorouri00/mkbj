using EwaysVNext.Domain.Common;

namespace EwaysVNext.Modules.Customer.Application.Domain.Customers;

public sealed class VerificationShahkarStatus : AggregateRoot<int>
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
}
