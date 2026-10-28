namespace EwaysVNext.Modules.Location.Application.Domain.Locations;

public sealed class City
{
    public int Id { get; set; }
    public int ProvinceId { get; set; }
    public string Name { get; set; } = null!;
    public string? Code { get; set; }
    public bool IsActive { get; set; }

    // Navigation Properties
    public Province Province { get; set; } = null!;

    public IReadOnlyCollection<District> Districts => _districts.AsReadOnly();

    private readonly List<District> _districts = new();
    public District? GetDistrict(int districtId)
       => _districts.FirstOrDefault(c => c.Id == districtId);
    public void AddDistrict(District district)
    {
        ArgumentNullException.ThrowIfNull(district);
        _districts.Add(district);
    }
}
