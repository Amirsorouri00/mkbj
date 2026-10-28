namespace EwaysVNext.Modules.Customer.Application.Domain.Customers;

public sealed class CustomerAddress 
{
    public long Id { get; set; }

    public long CustomerId { get; private set; }

    public long AddressId { get; private set; }

    private CustomerAddress()
    {
    }


    private CustomerAddress(
        long id,
        long customerId,
        long addressId)
    {
        Id = id;
        CustomerId = customerId;
        AddressId = addressId;
    }


    public static CustomerAddress Create(
        long id,
        long customerId,
        long addressId)
    {
        return new CustomerAddress(
            id,
            customerId,
            addressId);
    }
}
