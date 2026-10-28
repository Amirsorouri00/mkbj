using Microsoft.IdentityModel.Protocols;

namespace EwaysVNext.Host.Authentication;

public sealed class HostRewritingDocumentRetriever(string externalAuthority, string internalAuthority) : IDocumentRetriever
{
    private readonly HttpDocumentRetriever _inner = new() { RequireHttps = false };
    private readonly string _externalAuthority = externalAuthority.TrimEnd('/');
    private readonly string _internalAuthority = internalAuthority.TrimEnd('/');

    public Task<string> GetDocumentAsync(string address, CancellationToken cancel)
    {
        var target = address.StartsWith(_externalAuthority, StringComparison.OrdinalIgnoreCase)
            ? _internalAuthority + address[_externalAuthority.Length..]
            : address;

        return _inner.GetDocumentAsync(target, cancel);
    }
}
