using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using EwaysVNext.Modules.Notification.Application.Ports.Outbound;

namespace EwaysVNext.Modules.Notification.Adapter.Outbound.Sms.PayamSms;

public static class DependencyInjection
{
    public static IServiceCollection AddPayamSmsProvider(this IServiceCollection services, IConfiguration config)
    {
        var section = config.GetSection("PayamSms");
        services.Configure<PayamSmsOptions>(section);
        var options = section.Get<PayamSmsOptions>()
            ?? throw new InvalidOperationException(DependencyInjectionMessages.ConfigurationSectionMissing);

        var attemptTimeout = TimeSpan.FromSeconds(Math.Min(45, options.TimeoutSeconds));
        var totalRequestTimeout = TimeSpan.FromSeconds(Math.Max(options.TimeoutSeconds, attemptTimeout.TotalSeconds));

        services.AddHttpClient<PayamSmsProvider>(client => client.BaseAddress = new Uri(options.BaseUrl))
            .AddStandardResilienceHandler(resilience =>
            {
                resilience.AttemptTimeout.Timeout = attemptTimeout;
                resilience.TotalRequestTimeout.Timeout = totalRequestTimeout;
                resilience.CircuitBreaker.SamplingDuration = attemptTimeout * 2 + TimeSpan.FromSeconds(10);
            });

        services.AddScoped<ISmsProvider>(sp => sp.GetRequiredService<PayamSmsProvider>());

        return services;
    }
}
