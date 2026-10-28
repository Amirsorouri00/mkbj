using System.Numerics;
using System.Security.Cryptography;

namespace EwaysVNext.Modules.Partners.Application.Partners.Keys;

/// <summary>A freshly generated key. <see cref="Plaintext"/> is shown once and never stored.</summary>
public sealed record GeneratedApiKey(string Prefix, string Secret, string Plaintext);

/// <summary>
/// The partner API key format (ADR-0021): <c>pk_&lt;prefix&gt;_&lt;secret&gt;</c>.
/// <para>
/// Three parts, each earning its place. <c>pk_</c> lets the authentication pipeline route a
/// partner key away from the JWT reader by inspecting the header alone. The prefix is stored in
/// clear and indexed, so verifying a presented key is one indexed lookup rather than an HMAC
/// against every row. The secret is 32 random bytes — the only part that is secret, and the only
/// part that is hashed.
/// </para>
/// </summary>
public static class ApiKeyFormat
{
    public const string Marker = "pk_";
    public const int PrefixLength = 8;

    /// <summary>
    /// 43 base62 characters. 62^43 &gt; 2^256 &gt; 62^42, so this is the shortest length that can
    /// carry 32 bytes without truncating them.
    /// </summary>
    public const int SecretLength = 43;

    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    public static GeneratedApiKey Generate()
    {
        var prefix = string.Create(PrefixLength, 0, (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        });

        var secret = ToBase62(RandomNumberGenerator.GetBytes(32));

        return new GeneratedApiKey(prefix, secret, $"{Marker}{prefix}_{secret}");
    }

    /// <summary>
    /// Splits a presented key, or refuses. Shape only — this says nothing about whether the key
    /// exists, is live, or belongs to anyone.
    /// </summary>
    public static bool TryParse(string presented, out string prefix, out string secret)
    {
        prefix = string.Empty;
        secret = string.Empty;

        if (string.IsNullOrEmpty(presented)
            || !presented.StartsWith(Marker, StringComparison.Ordinal)
            || presented.Length != Marker.Length + PrefixLength + 1 + SecretLength)
        {
            return false;
        }

        var body = presented.AsSpan(Marker.Length);
        if (body[PrefixLength] != '_')
            return false;

        var prefixSpan = body[..PrefixLength];
        var secretSpan = body[(PrefixLength + 1)..];

        if (!IsBase62(prefixSpan) || !IsBase62(secretSpan))
            return false;

        prefix = prefixSpan.ToString();
        secret = secretSpan.ToString();
        return true;
    }

    private static bool IsBase62(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            var isBase62 = character is >= '0' and <= '9'
                or >= 'A' and <= 'Z'
                or >= 'a' and <= 'z';

            if (!isBase62)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Left-padded to a fixed width so every key looks alike. Without the padding, a value with
    /// leading zero bytes would encode shorter and leak that fact in the key's length.
    /// </summary>
    private static string ToBase62(byte[] bytes)
    {
        var value = new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
        var digits = new char[SecretLength];

        for (var i = SecretLength - 1; i >= 0; i--)
        {
            value = BigInteger.DivRem(value, 62, out var remainder);
            digits[i] = Alphabet[(int)remainder];
        }

        return new string(digits);
    }
}
