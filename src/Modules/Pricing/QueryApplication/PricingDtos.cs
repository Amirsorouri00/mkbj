namespace EwaysVNext.Modules.Pricing.QueryApplication;

/// <remarks>
/// <c>MinorUnits</c> travels with the code so a client can format an amount without a table of
/// its own — and so the day a currency has a different exponent, nothing has to be redeployed.
/// </remarks>
public sealed record CurrencyDto(string Code, int MinorUnits);

public sealed record CurrencyAdminDto(long Id, string Code, int MinorUnits, bool IsEnabled);

public sealed record CurrencyOverrideDto(string Currency, long AmountMinor, string PriceList);

public sealed record VariantPriceAdminDto(
    long VariantId,
    long BaseAmountMinor,
    string BaseCurrency,
    IReadOnlyList<CurrencyOverrideDto> Overrides,
    long? ProvenanceObservationId,
    string? ProvenanceSourceUrl,
    DateTimeOffset? ProvenanceCollectedAt,
    DateTimeOffset UpdatedAt);

public sealed record FxRateDto(
    long Id,
    string Base,
    string Quote,
    decimal Rate,
    string Provider,
    DateOnly RateDate,
    DateTimeOffset FetchedAt);

public sealed record FxOverrideDto(
    long Id,
    string Base,
    string Quote,
    decimal Rate,
    Guid SetBy,
    DateTimeOffset SetAt,
    DateTimeOffset? ValidUntil,
    bool IsActive);
