namespace EwaysVNext.Modules.Location.Application.Domain.Locations;

public sealed class AddressStatus
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;

    public static AddressStatus Create(int id, string name)
    {
        return new AddressStatus { Id = id, Name = name };
    }
}
