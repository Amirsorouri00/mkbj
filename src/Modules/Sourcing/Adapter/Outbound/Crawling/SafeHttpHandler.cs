using System.Net;
using System.Net.Sockets;

namespace EwaysVNext.Modules.Sourcing.Adapter.Outbound.Crawling;

/// <summary>Where the crawler may and may not go, and how fast.</summary>
public sealed class CrawlerOptions
{
    public const string SectionName = "Sourcing:Crawler";

    /// <summary>
    /// Identifies this crawler to the sites it visits.
    /// </summary>
    /// <remarks>
    /// Honest on purpose. A crawler that disguises itself as a browser is one a site cannot
    /// block by asking, which leaves blocking by force as the only option.
    /// </remarks>
    public string UserAgent { get; set; } = "EwaysVNextBot/1.0";

    /// <summary>The robots.txt product token, which is the agent without its version.</summary>
    public string ProductToken { get; set; } = "EwaysVNextBot";

    public int PolitenessDelayMs { get; set; } = 1_000;

    public int MaxHtmlBytes { get; set; } = 2 * 1024 * 1024;

    public int MaxImageBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// Whether the crawler may reach addresses inside this network.
    /// </summary>
    /// <remarks>
    /// False everywhere but the test suite. An administrator can type any URL into an import
    /// job, and without this the import feature is a way to make this server fetch its own
    /// metadata endpoint, its database, or anything else behind the firewall.
    /// </remarks>
    public bool AllowPrivateNetworks { get; set; }

    public int RequestTimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Refuses to connect anywhere that is not a public address.
/// </summary>
/// <remarks>
/// <para>
/// The check is on the <b>resolved address</b>, not the hostname, because a hostname is not a
/// destination. <c>internal.example.com</c> can resolve to 10.0.0.5, and a public name can be
/// re-pointed at a private address between one request and the next.
/// </para>
/// <para>
/// This is server-side request forgery in its ordinary form: the import feature exists to fetch
/// URLs a person supplies, which is exactly the capability an attacker wants. Blocking private
/// ranges is what stops "import this catalogue" from meaning "read the cloud metadata service".
/// </para>
/// </remarks>
public sealed class SafeHttpHandler(bool allowPrivateNetworks) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var uri = request.RequestUri
            ?? throw new InvalidOperationException("The request has no URI.");

        if (uri.Scheme is not ("http" or "https"))
        {
            throw new HttpRequestException(
                $"The scheme '{uri.Scheme}' is not fetchable; only http and https are.");
        }

        if (!allowPrivateNetworks) await AssertPublicAsync(uri, cancellationToken);

        return await base.SendAsync(request, cancellationToken);
    }

    private static async Task AssertPublicAsync(Uri uri, CancellationToken ct)
    {
        IPAddress[] addresses;

        if (IPAddress.TryParse(uri.Host, out var literal))
        {
            addresses = [literal];
        }
        else
        {
            try
            {
                addresses = await Dns.GetHostAddressesAsync(uri.Host, ct);
            }
            catch (SocketException exception)
            {
                throw new HttpRequestException($"'{uri.Host}' did not resolve.", exception);
            }
        }

        // Every address, not just the first: a host that resolves to one public and one private
        // address must not be reachable at all, because which one is used is not this code's
        // decision.
        foreach (var address in addresses)
        {
            if (IsPrivate(address))
            {
                throw new HttpRequestException(
                    $"'{uri.Host}' resolves to {address}, which is not a public address.");
            }
        }

        if (addresses.Length == 0)
            throw new HttpRequestException($"'{uri.Host}' resolved to no addresses.");
    }

    /// <summary>Anything that is not routable on the public internet.</summary>
    internal static bool IsPrivate(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (IPAddress.IsLoopback(address)) return true;

        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var octets = address.GetAddressBytes();

            return octets[0] switch
            {
                0 => true,                                    // "this network"
                10 => true,                                   // RFC 1918
                127 => true,                                  // loopback
                169 when octets[1] == 254 => true,            // link-local, incl. cloud metadata
                172 when octets[1] is >= 16 and <= 31 => true, // RFC 1918
                192 when octets[1] == 168 => true,            // RFC 1918
                100 when octets[1] is >= 64 and <= 127 => true, // carrier-grade NAT
                >= 224 => true,                               // multicast and reserved
                _ => false,
            };
        }

        return address.IsIPv6LinkLocal
            || address.IsIPv6SiteLocal
            || address.IsIPv6UniqueLocal
            || address.IsIPv6Multicast;
    }
}

/// <summary>
/// Keeps one host from being hit faster than it asked for.
/// </summary>
/// <remarks>
/// Per host rather than global, because politeness is owed to each site separately — crawling
/// two shops at once is not rude to either. In process, so several workers do not coordinate;
/// that is a known limit rather than an oversight, and the delay is a floor per worker.
/// </remarks>
public sealed class PolitenessGate
{
    private readonly Dictionary<string, DateTimeOffset> _nextAllowed = [];
    private readonly Lock _gate = new();

    public TimeSpan Reserve(string host, TimeSpan delay, DateTimeOffset now)
    {
        lock (_gate)
        {
            var next = _nextAllowed.GetValueOrDefault(host, now);
            var wait = next > now ? next - now : TimeSpan.Zero;

            _nextAllowed[host] = (next > now ? next : now) + delay;

            return wait;
        }
    }
}
