namespace EwaysVNext.Modules.Partners.Adapter.Inbound.Rest.Admin;

/// <summary>Null limits take the spec defaults; they do not mean "no limit".</summary>
public sealed record CreatePartnerRequest(
    string Name,
    IReadOnlyCollection<string> Scopes,
    int? RateLimitPerMinute,
    int? BulkMaxItems);

public sealed record SetScopesRequest(IReadOnlyCollection<string> Scopes);

public sealed record SetLimitsRequest(int RateLimitPerMinute, int BulkMaxItems);

/// <summary>Null scopes mean every scope the partner holds, which is the usual case.</summary>
public sealed record IssueKeyRequest(IReadOnlyCollection<string>? Scopes, DateTimeOffset? ExpiresAt);

/// <summary>
/// Hours rather than a TimeSpan: an overlap is an operational decision expressed in hours, and a
/// JSON TimeSpan is a format callers get wrong. Null takes the configured default.
/// </summary>
public sealed record RotateKeyRequest(double? OverlapHours);
