namespace EwaysVNext.Modules.Location.Application.Domain.Locations;

public sealed class District 
{
    public int Id { get; set; }
    public int CityId { get; set; }
    public string Name { get; set; } = null!;
    public string? Code { get; set; }
    public bool IsActive { get; set; }

    // Navigation Properties
    public City City { get; set; } = null!;
}
