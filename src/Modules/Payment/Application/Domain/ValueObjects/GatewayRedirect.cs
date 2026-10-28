namespace EwaysVNext.Modules.Payment.Application.Domain.ValueObjects;

public enum GatewayRedirectMethod { Get, Post }

/// <summary>Hosted payment handoff supporting both GET redirects and POST forms.</summary>
public sealed record GatewayRedirect(Uri Url, GatewayRedirectMethod Method, IReadOnlyDictionary<string, string> Fields);
