using EwaysVNext.Modules.Customer.Application.Service.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Refit;

namespace EwaysVNext.Modules.Customer.Application.Service.Shahkar.Abstractions;

public static class ShahkarConfiguration
{
    public static IServiceCollection AddShahkarServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection("Shahkar");
        services.Configure<ShahkarOptions>(section);
        var apiSettings = section.Get<ShahkarOptions>()
            ?? throw new InvalidOperationException(ShahkarConfigurationMessages.ConfigurationSectionMissing);
        services.AddTransient<ShahkarLoggingHandler>();
        services.AddRefitClient<IAuthApi>()
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri(apiSettings.BaseUrl);
            })
            .AddHttpMessageHandler<ShahkarLoggingHandler>()
            .AddStandardResilienceHandler(options =>
            {
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(apiSettings.TimeoutSeconds);
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
            });        

        services.AddRefitClient<IVerificationApi>()
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri(apiSettings.ApiBaseUrl);
            })
            .AddHttpMessageHandler<ShahkarLoggingHandler>()
            .AddStandardResilienceHandler(options =>
            {
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(apiSettings.TimeoutSeconds);
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
            });

        services.AddRefitClient<IAuthVerifyMobileApi>()
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri(apiSettings.ApiVerficationMobileUrl);
            })
            .AddHttpMessageHandler<ShahkarLoggingHandler>()
            .AddStandardResilienceHandler(options =>
            {
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(apiSettings.TimeoutSeconds);
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
            });

        services.AddRefitClient<IVerifyMobileApi>()
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri(apiSettings.ApiVerficationMobileUrl);
            })
            .AddHttpMessageHandler<ShahkarLoggingHandler>()
            .AddStandardResilienceHandler(options =>
            {
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(apiSettings.TimeoutSeconds);
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
            });

        services.AddRefitClient<ILegalInquiryApi>()
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri(apiSettings.ApiVerficationMobileUrl);
            })
            .AddHttpMessageHandler<ShahkarLoggingHandler>()
            .AddStandardResilienceHandler(options =>
            {
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(apiSettings.TimeoutSeconds);
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
            });

        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IVerifyMobileTokenService, VerifyMobileTokenService>();
        services.AddScoped<IShahkarService, ShahkarService>();

        return services;
    }
}
