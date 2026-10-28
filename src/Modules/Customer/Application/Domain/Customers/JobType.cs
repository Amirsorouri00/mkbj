namespace EwaysVNext.Modules.Customer.Application.Domain.Customers;

/// <summary>
/// Lookup of business fields a customer can work in (کافی نت، دفتر پیشخوان، ...).
/// </summary>
public sealed class JobType
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public static JobType Create(int id, string name)
    {
        return new JobType { Id = id, Name = name };
    }
}
