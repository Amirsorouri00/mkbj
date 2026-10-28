namespace EwaysVNext.Modules.Partners.Application.Partners.Domain;

/// <summary>
/// The bounds a partner's limits must fall inside.
/// <para>
/// Both have a minimum of 1 rather than 0 on purpose: zero would be a partner who is nominally
/// active and silently cannot do anything, which reads to them as an outage. Stopping a partner
/// is <see cref="PartnerStatus.Suspended"/>, which says so.
/// </para>
/// </summary>
public static class PartnerLimits
{
    public const int RateLimitMin = 1;
    public const int RateLimitMax = 6000;
    public const int DefaultRateLimitPerMinute = 300;

    public const int BulkMin = 1;
    public const int BulkMax = 1000;
    public const int DefaultBulkMaxItems = 100;

    public const int NameMaxLength = 200;
}
