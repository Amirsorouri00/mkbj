using EwaysVNext.Domain.Common;

namespace EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Domain;

public sealed class MeasurementUnit : Entity<long>
{
    public string Name { get; private set; } = null!;

    public string Symbol { get; private set; } = null!;

    private MeasurementUnit() { }

    public static MeasurementUnit Create(long id, string name, string symbol)
    {
        if (id <= 0)
        {
            throw new DomainException("MEASUREMENT_UNIT_ID_INVALID", "Measurement unit ID must be positive.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("MEASUREMENT_UNIT_NAME_REQUIRED", "Measurement unit name is required.");
        }

        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new DomainException("MEASUREMENT_UNIT_SYMBOL_REQUIRED", "Measurement unit symbol is required.");
        }

        var normalizedName = name.Trim();
        var normalizedSymbol = symbol.Trim();
        if (normalizedName.Length > 100)
        {
            throw new DomainException("MEASUREMENT_UNIT_NAME_TOO_LONG", "Measurement unit name cannot exceed 100 characters.");
        }

        if (normalizedSymbol.Length > 20)
        {
            throw new DomainException("MEASUREMENT_UNIT_SYMBOL_TOO_LONG", "Measurement unit symbol cannot exceed 20 characters.");
        }

        return new MeasurementUnit
        {
            Id = id,
            Name = normalizedName,
            Symbol = normalizedSymbol
        };
    }
}
