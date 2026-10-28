using EwaysVNext.Outcomes;
using EwaysVNext.Modules.Notification.Application.Ports.Outbound;
using Microsoft.Extensions.Options;
using EwaysVNext.Outcomes.Extensions;

namespace EwaysVNext.Modules.Notification.Adapter.Outbound.Sms;

internal sealed class SmsProviderResolver(IEnumerable<ISmsProvider> providers, IOptions<SmsOptions> options)
    : ISmsProviderResolver
{
    public Result<ISmsProvider> Resolve(string? providerName = null)
    {
        var name = providerName ?? options.Value.DefaultProvider;

        var provider = providers.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (provider is not null)
            return Result.Success(provider);

        var fallback = providers.FirstOrDefault();
        return fallback is not null
            ? Result.Success(fallback)
            : Result.Failure<ISmsProvider>(new Error("SMS_PROVIDER_NOT_CONFIGURED", MessageFormat.Format(SmsProviderResolverMessages.ProviderNotConfiguredFormat, name)));
    }
}
