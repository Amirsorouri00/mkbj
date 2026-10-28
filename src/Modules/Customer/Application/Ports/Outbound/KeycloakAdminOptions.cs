namespace EwaysVNext.Modules.Customer.Application.Ports.Outbound;


public sealed class KeycloakAdminOptions
{
    public string ServerUrl { get; set; } = string.Empty;

    public string AdminRealm { get; set; } = string.Empty;

    public string AdminClientId { get; set; } = string.Empty;

    public string AdminClientSecret { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 30;
}
