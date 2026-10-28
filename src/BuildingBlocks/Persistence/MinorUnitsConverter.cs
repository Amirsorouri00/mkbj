using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EwaysVNext.Persistence;

/// <summary>
/// Maps a decimal money amount to the <c>bigint</c> minor-unit column it is stored in.
/// The conversion throws rather than rounds, so a value that cannot be stored exactly fails the
/// save instead of reaching the database quietly altered.
/// </summary>
public sealed class MinorUnitsConverter() : ValueConverter<decimal, long>(
    amount => MinorUnits.ToMinor(amount),
    minor => MinorUnits.FromMinor(minor));
