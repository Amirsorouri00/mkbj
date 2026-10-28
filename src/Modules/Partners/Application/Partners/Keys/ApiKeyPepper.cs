using System.Text;

namespace EwaysVNext.Modules.Partners.Application.Partners.Keys;

/// <summary>
/// The server-side secret mixed into every key hash.
/// <para>
/// It lives in configuration rather than in the database, which is the point: a stolen dump of
/// the key table cannot verify a key without it. The cost is that rotating it invalidates every
/// key ever issued, so it is set once per environment and left alone.
/// </para>
/// </summary>
public sealed record ApiKeyPepper(byte[] Value)
{
    public const string ConfigurationKey = "ApiKeys:Pepper";

    private const int MinimumLength = 32;

    /// <summary>
    /// Refuses rather than defaulting. A missing pepper that silently became empty would still
    /// hash, still verify, and still look like it was working — while offering no protection at
    /// all, in a way nothing downstream could detect.
    /// </summary>
    public static ApiKeyPepper FromConfiguration(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length < MinimumLength)
        {
            throw new InvalidOperationException(
                $"{ConfigurationKey} must be set to at least {MinimumLength} characters. "
                + "Rotating it invalidates every issued key, so set it once per environment.");
        }

        return new ApiKeyPepper(Encoding.UTF8.GetBytes(raw));
    }
}
