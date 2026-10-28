using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Providers;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Parsian;

public static class ParsianRegistration
{
    public const string HttpClientName = "Payment.Parsian";
    public const string InquiryHttpClientName = "Payment.Parsian.Inquiry";

    public static IServiceCollection AddParsianGateway(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(ParsianOptions.SectionName);
        if (!section.GetValue<bool>(nameof(ParsianOptions.Enabled))) return services;

        services.AddOptions<ParsianOptions>().Bind(section)
            .Validate(o => !string.IsNullOrWhiteSpace(o.LoginAccount), "Parsian LoginAccount is required when enabled.")
            .Validate(o => ParsianOptions.IsHttpsAddress(o.BaseUrl), "Parsian BaseUrl must be HTTPS without credentials, query or fragment.")
            .Validate(o => ParsianOptions.IsHttpsAddress(o.PaymentPageUrl), "Parsian PaymentPageUrl must be HTTPS without credentials, query or fragment.")
            .Validate(o => o.RequestTimeout.TotalMilliseconds >= 1 && o.RequestTimeout.TotalMilliseconds <= int.MaxValue,
                "Parsian RequestTimeout must be between 1 and 2147483647 milliseconds.")
            .ValidateOnStart();

        services.AddHttpClient<ParsianHttpClient>(HttpClientName, (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<ParsianOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = options.RequestTimeout;
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        })
        // These settings belong only to this provider. Redirects must not forward credentials.
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false })
        .RemoveAllLoggers();

        var inquirySection = configuration.GetSection(ParsianInquiryOptions.SectionName);
        services.AddOptions<ParsianInquiryOptions>().Bind(inquirySection)
            .Validate(o => ParsianOptions.IsHttpsAddress(o.BaseUrl),
                "Parsian inquiry BaseUrl must be HTTPS without credentials, query or fragment.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.ApiVersion),
                "Parsian inquiry ApiVersion is required.")
            .Validate(o => o.RequestTimeout.TotalMilliseconds >= 1
                && o.RequestTimeout.TotalMilliseconds <= int.MaxValue,
                "Parsian inquiry RequestTimeout must be between 1 and 2147483647 milliseconds.")
            .ValidateOnStart();

        services.AddHttpClient(InquiryHttpClientName, (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<ParsianInquiryOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = options.RequestTimeout;
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Add("apiVersion", options.ApiVersion);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false })
        .RemoveAllLoggers();

        services.AddTransient(sp => new ParsianSaleReportHttpClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(InquiryHttpClientName),
            sp.GetService<IParsianInquiryAccessTokenProvider>()));

        services.TryAddEnumerable(ServiceDescriptor.Transient<IPaymentGatewayProvider, ParsianGatewayProvider>());
        return services;
    }
}
