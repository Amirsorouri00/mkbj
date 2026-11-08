using System.Globalization;

namespace EwaysVNext.Modules.Sourcing.Adapter.Outbound.Crawling;

/// <summary>
/// What a site's robots.txt permits this crawler to do.
/// </summary>
/// <remarks>
/// <para>
/// Honoured, not consulted. A crawler that reads robots.txt and ignores it is worse than one
/// that never asked — it has recorded that it knew. This seller's reputation and its suppliers'
/// goodwill are worth more than any one import.
/// </para>
/// <para>
/// The product token is <c>EwaysVNextBot</c>, so a site can name it specifically. Rules for the
/// named agent win outright over <c>*</c>; that is what the standard says and what a site owner
/// writing a specific rule expects.
/// </para>
/// </remarks>
public sealed class RobotsTxt
{
    private readonly List<string> _allow;
    private readonly List<string> _disallow;

    private RobotsTxt(List<string> allow, List<string> disallow, TimeSpan? crawlDelay)
    {
        _allow = allow;
        _disallow = disallow;
        CrawlDelay = crawlDelay;
    }

    /// <summary>What the site asked for between requests, if it asked.</summary>
    public TimeSpan? CrawlDelay { get; }

    /// <summary>
    /// Nothing is disallowed.
    /// </summary>
    /// <remarks>
    /// Used when robots.txt is absent or unreadable. A missing file means no restrictions under
    /// the standard — refusing to crawl would be inventing a prohibition the site never made.
    /// </remarks>
    public static RobotsTxt AllowAll { get; } = new([], [], null);

    /// <summary>Everything is disallowed, for a site that said so.</summary>
    public static RobotsTxt DenyAll { get; } = new([], ["/"], null);

    public static RobotsTxt Parse(string? content, string userAgent)
    {
        if (string.IsNullOrWhiteSpace(content)) return AllowAll;

        var groups = new Dictionary<string, (List<string> Allow, List<string> Disallow, TimeSpan? Delay)>(
            StringComparer.OrdinalIgnoreCase);

        var currentAgents = new List<string>();
        var expectingAgents = true;

        foreach (var raw in content.Split('\n'))
        {
            var line = raw;

            var comment = line.IndexOf('#', StringComparison.Ordinal);
            if (comment >= 0) line = line[..comment];

            line = line.Trim();
            if (line.Length == 0) continue;

            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator < 0) continue;

            var field = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            if (field.Equals("user-agent", StringComparison.OrdinalIgnoreCase))
            {
                // Consecutive user-agent lines share one group of rules.
                if (!expectingAgents)
                {
                    currentAgents.Clear();
                    expectingAgents = true;
                }

                currentAgents.Add(value);
                groups.TryAdd(value, ([], [], null));
                continue;
            }

            expectingAgents = false;
            if (currentAgents.Count == 0) continue;

            foreach (var agent in currentAgents)
            {
                var group = groups[agent];

                if (field.Equals("disallow", StringComparison.OrdinalIgnoreCase))
                {
                    // An empty Disallow means "nothing is disallowed", not "disallow /".
                    if (value.Length > 0) group.Disallow.Add(value);
                }
                else if (field.Equals("allow", StringComparison.OrdinalIgnoreCase))
                {
                    if (value.Length > 0) group.Allow.Add(value);
                }
                else if (field.Equals("crawl-delay", StringComparison.OrdinalIgnoreCase)
                    && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                    && seconds > 0)
                {
                    group = group with { Delay = TimeSpan.FromSeconds(seconds) };
                }

                groups[agent] = group;
            }
        }

        // A rule naming this crawler wins outright; only if there is none does "*" apply.
        if (groups.TryGetValue(userAgent, out var mine))
            return new RobotsTxt(mine.Allow, mine.Disallow, mine.Delay);

        if (groups.TryGetValue("*", out var all))
            return new RobotsTxt(all.Allow, all.Disallow, all.Delay);

        return AllowAll;
    }

    /// <summary>
    /// Whether this path may be fetched.
    /// </summary>
    /// <remarks>
    /// The longest matching rule wins, and <c>Allow</c> beats <c>Disallow</c> at equal length —
    /// which is how a site carves one directory out of a blanket prohibition.
    /// </remarks>
    public bool IsAllowed(string path)
    {
        if (string.IsNullOrEmpty(path)) path = "/";

        var longestAllow = _allow
            .Where(rule => Matches(path, rule))
            .Select(rule => rule.Length)
            .DefaultIfEmpty(-1)
            .Max();

        var longestDisallow = _disallow
            .Where(rule => Matches(path, rule))
            .Select(rule => rule.Length)
            .DefaultIfEmpty(-1)
            .Max();

        if (longestDisallow < 0) return true;

        return longestAllow >= longestDisallow;
    }

    /// <remarks>
    /// Prefix matching with <c>*</c> and a trailing <c>$</c>, which is what real robots.txt
    /// files use even though the original standard had neither.
    /// </remarks>
    private static bool Matches(string path, string rule)
    {
        var anchored = rule.EndsWith('$');
        if (anchored) rule = rule[..^1];

        if (!rule.Contains('*', StringComparison.Ordinal))
            return anchored ? path == rule : path.StartsWith(rule, StringComparison.Ordinal);

        var segments = rule.Split('*');
        var index = 0;

        for (var position = 0; position < segments.Length; position++)
        {
            var segment = segments[position];
            if (segment.Length == 0) continue;

            var found = path.IndexOf(segment, index, StringComparison.Ordinal);
            if (found < 0) return false;

            index = found + segment.Length;
        }

        return !anchored || index == path.Length;
    }
}
