namespace EwaysVNext.Modules.Customer.Application.Domain.Customers;

public sealed class VerificationMethod 
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;

    public static VerificationMethod Create(int id, string name)
    {
        return new VerificationMethod { Id = id, Name = name };
    }
}
