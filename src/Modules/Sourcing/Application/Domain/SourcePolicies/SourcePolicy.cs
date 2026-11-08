using System.Globalization;
using EwaysVNext.Domain.Common;

namespace EwaysVNext.Modules.Sourcing.Application.Domain.SourcePolicies;

/// <summary>
/// One host, reduced to the form two URLs for the same site agree on.
/// </summary>
/// <remarks>
/// A rights policy keyed by raw host text would be a policy somebody can walk around by typing
/// the address differently. <c>WWW.Shop.Example.com:8443</c> and <c>shop.example.com</c> are the
/// same site and must reach the same decision, so the key is normalised before anything is
/// stored or looked up.
/// </remarks>
public static class SourceDomain
{
    public static string Normalize(string hostOrUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostOrUrl);

        var text = hostOrUrl.Trim();

        // Accepts a bare host as well as a URL: an administrator typing a policy writes the
        // host, and the crawler arrives with the whole address.
        if (Uri.TryCreate(text, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https")
        {
            text = absolute.Host;
        }
        else if (Uri.TryCreate($"http://{text}", UriKind.Absolute, out var implied))
        {
            text = implied.Host;
        }

        // IdnMapping, so an internationalised host and its punycode spelling are one key.
        var host = text.ToLowerInvariant();
        try
        {
            host = new IdnMapping().GetAscii(host);
        }
        catch (ArgumentException)
        {
            // Not a valid IDN; the lower-cased text is the best key available and the caller's
            // own validation will refuse it if it is nonsense.
        }

        // One "www.", not all of them: www.www.example.com is a different host from
        // www.example.com, and collapsing both would merge two sites' policies.
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }
}

/// <summary>
/// What the seller has decided about one source: may we crawl it, use its words, use its images.
/// </summary>
/// <remarks>
/// Three separate permissions because they are separately negotiated. A supplier will often
/// agree to its catalogue text being republished while its photography stays its own, and a
/// single "allowed" flag would force an answer that is wrong in one direction or the other.
/// </remarks>
public sealed class SourcePolicy : AggregateRoot<long>
{
    private const int MaxNotesLength = 2_000;

    private SourcePolicy() { }

    public string Domain { get; private set; } = string.Empty;
    public bool CrawlAllowed { get; private set; }
    public bool ContentUseAllowed { get; private set; }
    public bool ImageUseAllowed { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    public static SourcePolicy Create(
        long id,
        string domain,
        bool crawlAllowed,
        bool contentUseAllowed,
        bool imageUseAllowed,
        string? notes,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            throw new DomainException(
                "SOURCE_POLICY_DOMAIN_REQUIRED", "A source policy needs a domain.");
        }

        AssertNotes(notes);

        return new SourcePolicy
        {
            Id = id,
            Domain = SourceDomain.Normalize(domain),
            CrawlAllowed = crawlAllowed,
            ContentUseAllowed = contentUseAllowed,
            ImageUseAllowed = imageUseAllowed,
            Notes = notes,
            CreatedAt = now,
        };
    }

    /// <remarks>
    /// The domain is not updatable. A policy is about one site, and letting it move would
    /// silently transfer one supplier's permission to another.
    /// </remarks>
    public void Update(
        bool crawlAllowed,
        bool contentUseAllowed,
        bool imageUseAllowed,
        string? notes,
        DateTimeOffset now)
    {
        AssertNotes(notes);

        CrawlAllowed = crawlAllowed;
        ContentUseAllowed = contentUseAllowed;
        ImageUseAllowed = imageUseAllowed;
        Notes = notes;
        UpdatedAt = now;
    }

    private static void AssertNotes(string? notes)
    {
        if (notes is not null && notes.Length > MaxNotesLength)
        {
            throw new DomainException(
                "SOURCE_POLICY_NOTES_TOO_LONG",
                $"Notes are at most {MaxNotesLength.ToString(CultureInfo.InvariantCulture)} characters.");
        }
    }
}
