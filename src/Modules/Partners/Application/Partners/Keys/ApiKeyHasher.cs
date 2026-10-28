using System.Security.Cryptography;
using System.Text;

namespace EwaysVNext.Modules.Partners.Application.Partners.Keys;

/// <summary>
/// Hashes and verifies key secrets with HMAC-SHA256 under the pepper.
/// <para>
/// Not bcrypt or Argon2, deliberately (ADR-0021). Those exist to slow down guessing a secret a
/// human chose; a key here is 256 bits from a CSPRNG, so there is nothing to guess and a work
/// factor would only add latency to every partner request. What does matter is that verification
/// is constant-time, so a timing difference cannot be used to recover a stored hash byte by byte.
/// </para>
/// </summary>
public sealed class ApiKeyHasher(ApiKeyPepper pepper)
{
    public byte[] Hash(string secret) =>
        HMACSHA256.HashData(pepper.Value, Encoding.UTF8.GetBytes(secret));

    public bool Matches(string secret, byte[] storedHash) =>
        CryptographicOperations.FixedTimeEquals(Hash(secret), storedHash);
}
