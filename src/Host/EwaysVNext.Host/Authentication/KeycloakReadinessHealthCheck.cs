using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace EwaysVNext.Host.Authentication;

public sealed class KeycloakReadinessHealthCheck(
    IReadOnlyDictionary<string, IConfigurationManager<OpenIdConnectConfiguration>> configurationManagers)
    : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var unhealthyTenants = new List<string>();

        foreach (var (tenant, configManager) in configurationManagers)
        {
            try
            {
                var config = await configManager.GetConfigurationAsync(cancellationToken);
                if (config.SigningKeys.Count == 0)
                    unhealthyTenants.Add(string.Format(KeycloakReadinessHealthCheckMessages.NoSigningKeysFormat, tenant));
            }
            catch (Exception ex)
            {
                unhealthyTenants.Add(string.Format(KeycloakReadinessHealthCheckMessages.ExceptionFormat, tenant, ex.GetType().Name));
            }
        }

        return unhealthyTenants.Count == 0
            ? HealthCheckResult.Healthy(KeycloakReadinessHealthCheckMessages.Healthy)
            : HealthCheckResult.Unhealthy(
                string.Format(KeycloakReadinessHealthCheckMessages.UnhealthyFormat, string.Join(", ", unhealthyTenants)));
    }
}
