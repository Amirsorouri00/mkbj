using EwaysVNext.Domain.Common;

namespace EwaysVNext.Modules.Location.Application.Domain.Locations;

public sealed class Province : AggregateRoot<int>
{
    public Province() { }
    public string Name { get;private set; } = null!;
    public string? Code { get; private set; }
    public bool IsActive { get; private set; }

    // Navigation Properties
    public IReadOnlyCollection<City> Cities => _cities.AsReadOnly();
    
    private readonly List<City> _cities = new();
    public void AddCity(City city)
    {
        ArgumentNullException.ThrowIfNull(city);
        _cities.Add(city);
    }
    public IList<City> GetCities()
       => _cities.ToList();
    public City? GetCity(int cityId)
       => _cities.FirstOrDefault(c => c.Id == cityId);

    public IList<District> GetDistrictsByCity(int cityId)
    {
        var city = GetCity(cityId);
        return city?.Districts.ToList() ?? new List<District>();
    }

    public IList<District> GetAllDistricts()
        => _cities.SelectMany(c => c.Districts).ToList();
}

