namespace EwaysVNext.Modules.Customer.Application.Domain.Customers;

public sealed class VerificationStatus
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;

    public static VerificationStatus Create(int id, string name)
    {
        return new VerificationStatus { Id = id, Name = name };
    }
}
