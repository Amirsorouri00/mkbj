using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Providers;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Mellat;

public static class MellatRegistration
{
    public const string HttpClientName = "Payment.Mellat";
    public const string InquiryHttpClientName = "Payment.Mellat.Inquiry";

    public static IServiceCollection AddMellatGateway(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MellatOptions.SectionName);
        if (!section.GetValue<bool>(nameof(MellatOptions.Enabled))) return services;

        services.AddOptions<MellatOptions>().Bind(section)
            .Validate(o => o.TerminalId > 0, "Mellat TerminalId is required when enabled.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.UserName), "Mellat UserName is required when enabled.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.UserPassword), "Mellat UserPassword is required when enabled.")
            .Validate(o => MellatOptions.IsHttpsAddress(o.BaseUrl), "Mellat BaseUrl must be HTTPS without credentials, query or fragment.")
            .Validate(o => MellatOptions.IsHttpsAddress(o.PaymentPageUrl), "Mellat PaymentPageUrl must be HTTPS without credentials, query or fragment.")
            .Validate(o => o.RequestTimeout.TotalMilliseconds >= 1 && o.RequestTimeout.TotalMilliseconds <= int.MaxValue,
                "Mellat RequestTimeout must be between 1 and 2147483647 milliseconds.")
            .ValidateOnStart();

        services.AddHttpClient<MellatHttpClient>(HttpClientName, (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<MellatOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = options.RequestTimeout;
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(options.UserName + ":" + options.UserPassword));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false })
        .RemoveAllLoggers();

        var inquirySection = configuration.GetSection(MellatInquiryOptions.SectionName);
        services.AddOptions<MellatInquiryOptions>().Bind(inquirySection)
            .Validate(o => !o.Enabled || MellatOptions.IsHttpsAddress(o.BaseUrl),
                "Mellat inquiry BaseUrl must be HTTPS without credentials, query or fragment when enabled.")
            .Validate(o => !o.Enabled || !string.IsNullOrWhiteSpace(o.UserName),
                "Mellat inquiry UserName is required when enabled.")
            .Validate(o => !o.Enabled || !string.IsNullOrWhiteSpace(o.Password),
                "Mellat inquiry Password is required when enabled.")
            .Validate(o => !o.Enabled || o.RequestTimeout.TotalMilliseconds >= 1
                && o.RequestTimeout.TotalMilliseconds <= int.MaxValue,
                "Mellat inquiry RequestTimeout must be between 1 and 2147483647 milliseconds when enabled.")
            .ValidateOnStart();

        services.AddHttpClient<MellatInquiryHttpClient>(InquiryHttpClientName, (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<MellatInquiryOptions>>().Value;
            var defaults = new MellatInquiryOptions();
            var baseUrl = MellatOptions.IsHttpsAddress(options.BaseUrl) ? options.BaseUrl : defaults.BaseUrl;
            var timeout = options.RequestTimeout.TotalMilliseconds is >= 1 and <= int.MaxValue
                ? options.RequestTimeout : defaults.RequestTimeout;
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            client.Timeout = timeout;
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (options.Enabled)
            {
                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(options.UserName + ":" + options.Password));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            }
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false })
        .RemoveAllLoggers();

        services.TryAddEnumerable(ServiceDescriptor.Transient<IPaymentGatewayProvider, MellatGatewayProvider>());
        return services;
    }
}
