using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Notification.Application.Ports.Outbound;

public interface ISmsProviderResolver
{
    Result<ISmsProvider> Resolve(string? providerName = null);
}
