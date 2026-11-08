using System.Globalization;
using System.Threading.RateLimiting;

using EwaysVNext.Core.Application;
using EwaysVNext.Core.Application.Security;
using EwaysVNext.Outcomes;
using EwaysVNext.Presentation.Endpoint;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using EwaysVNext.Presentation.Endpoint.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EwaysVNext.Host.RateLimiting;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Per IP, for callers with no partner key. A partner is limited by its own figure.</summary>
    public int AnonymousPermitPerMinute { get; set; } = 100;

    /// <summary>
    /// Per backoffice user, for the admin surface.
    /// </summary>
    /// <remarks>
    /// 120, not the 10 this started as. A console that draws a dashboard, a customer and that
    /// customer's orders is three requests before anyone has clicked anything, and a limit tuned
    /// for a script makes the UI unusable. The partition is the user's subject, so one
    /// administrator cannot exhaust another's allowance.
    /// </remarks>
    public int AdminPermitPerMinute { get; set; } = 120;
}

public static class RateLimitingSetup
{
    public static IServiceCollection AddEwaysRateLimiting(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName));

        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                if (PartnerPrincipal.TryFrom(context.User, out var partner))
                {
                    var limit = PartnerRateLimit(context);

                    // The limit is part of the partition key on purpose. A fixed-window limiter
                    // caches its options per key, so without this an admin raising a partner's
                    // limit would not take effect until the old partition expired.
                    return RateLimitPartition.GetFixedWindowLimiter(
                        $"partner:{partner!.PartnerId}:{limit}",
                        _ => Window(limit));
                }

                // Resolved per request, not captured at registration. Reading configuration
                // eagerly here looked equivalent and was not: under WebApplicationFactory the
                // test's configuration is added after this method has already run, so an eager
                // read silently used the default and the rate-limit test stopped limiting.
                var anonymousLimit = context.RequestServices
                    .GetRequiredService<IOptionsMonitor<RateLimitingOptions>>()
                    .CurrentValue.AnonymousPermitPerMinute;

                return RateLimitPartition.GetFixedWindowLimiter(
                    $"{context.Connection.RemoteIpAddress?.ToString() ?? "anon"}:{anonymousLimit}",
                    _ => Window(anonymousLimit));
            });

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // A rejected request never reaches a handler, so it needs the standard body written
            // here or it would be the one response shape clients cannot parse.
            options.OnRejected = async (context, _) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "60";
                await ErrorResponse.WriteAsync(context.HttpContext, Error.RateLimited);
            };

            // The admin surface is limited per backoffice user rather than per address: several
            // administrators behind one office IP are several people, not one noisy client.
            options.AddPolicy(RateLimitPolicies.Admin, AdminPartition);

            // GraphQL is one endpoint serving both the backoffice and the anonymous storefront.
            // A Backoffice principal gets the admin partition; everyone else falls through to
            // the global limiter, which already covers them by address.
            options.AddPolicy(RateLimitPolicies.GraphQL, context =>
                AdminSubject(context) is null
                    ? RateLimitPartition.GetNoLimiter("graphql:unlimited")
                    : AdminPartition(context));
        });

        return services;
    }

    /// <summary>
    /// One fixed window per backoffice subject, at the configured admin limit.
    /// </summary>
    /// <remarks>
    /// The limit is part of the partition key for the same reason it is on the partner path: a
    /// fixed-window limiter caches its options per key, so a configuration change would
    /// otherwise not take effect until the old partition expired.
    /// </remarks>
    private static RateLimitPartition<string> AdminPartition(HttpContext context)
    {
        var limit = context.RequestServices
            .GetRequiredService<IOptionsMonitor<RateLimitingOptions>>()
            .CurrentValue.AdminPermitPerMinute;

        var subject = AdminSubject(context)
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "anon";

        return RateLimitPartition.GetFixedWindowLimiter(
            $"admin:{subject}:{limit}", _ => Window(limit));
    }

    /// <summary>The backoffice user behind this request, or null if there is not one.</summary>
    private static string? AdminSubject(HttpContext context) =>
        context.User.HasClaim(EwaysClaimTypes.Tenant, AdminRoles.BackofficeRealm)
            ? context.User.FindFirst(EwaysClaimTypes.Subject)?.Value
                ?? context.User.Identity?.Name
            : null;

    /// <summary>
    /// Read from the claim the API-key handler minted, so the hot path needs no database call.
    /// Falls back to a conservative figure if the claim is missing or unparseable rather than to
    /// "unlimited" — a malformed claim must not be a way to escape the limiter.
    /// </summary>
    private static int PartnerRateLimit(HttpContext context)
    {
        var claim = context.User.FindFirst(EwaysClaimTypes.RateLimitPerMinute)?.Value;

        return int.TryParse(claim, NumberStyles.None, CultureInfo.InvariantCulture, out var limit) && limit > 0
            ? limit
            : new RateLimitingOptions().AnonymousPermitPerMinute;
    }

    private static FixedWindowRateLimiterOptions Window(int permitLimit) => new()
    {
        PermitLimit = permitLimit,
        Window = TimeSpan.FromMinutes(1),
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        QueueLimit = 0,
    };
}
