namespace EwaysVNext.Modules.Customer.Application.Domain.Customers;

/// <summary>
/// Join between a <see cref="Customer"/> and one of its <see cref="JobType"/>s.
/// </summary>
public sealed class CustomerJobType
{
    public long Id { get; private set; }

    public long CustomerId { get; private set; }

    public int JobTypeId { get; private set; }

    private CustomerJobType()
    {
    }

    private CustomerJobType(
        long id,
        long customerId,
        int jobTypeId)
    {
        Id = id;
        CustomerId = customerId;
        JobTypeId = jobTypeId;
    }

    public static CustomerJobType Create(
        long id,
        long customerId,
        int jobTypeId)
    {
        return new CustomerJobType(
            id,
            customerId,
            jobTypeId);
    }
}
