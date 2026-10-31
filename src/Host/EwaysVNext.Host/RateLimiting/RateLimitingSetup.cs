using System.Globalization;
using System.Threading.RateLimiting;

using EwaysVNext.Core.Application;
using EwaysVNext.Core.Application.Security;
using EwaysVNext.Outcomes;
using EwaysVNext.Presentation.Endpoint;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EwaysVNext.Host.RateLimiting;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Per IP, for callers with no partner key. A partner is limited by its own figure.</summary>
    public int AnonymousPermitPerMinute { get; set; } = 100;
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

            // Unchanged: the admin surface is limited per user, far more tightly.
            options.AddPolicy("admin", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.User.Identity?.Name
                        ?? context.Connection.RemoteIpAddress?.ToString()
                        ?? "anon",
                    _ => Window(10)));
        });

        return services;
    }

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
