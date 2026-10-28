namespace EwaysVNext.Modules.Customer.Application.Domain.Customers;

public sealed class CustomerStatus 
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public static CustomerStatus Create(int id, string name)
    {
        return new CustomerStatus { Id = id, Name = name };
    }
}
