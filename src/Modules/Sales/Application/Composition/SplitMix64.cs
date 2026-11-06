namespace EwaysVNext.Modules.Sales.Application.Composition;

/// <summary>
/// SplitMix64 — a small, fast, fully specified PRNG.
/// </summary>
/// <remarks>
/// <para>
/// <c>System.Random</c> is deliberately not used. Its sequence is an implementation detail that
/// has changed between .NET versions, and the seed of a composition is persisted with the order:
/// a basket has to be reproducible from its seed years later, on whatever runtime is current
/// then. A published algorithm with fixed constants is the only way that holds.
/// </para>
/// <para>
/// <c>SplitMix64Tests</c> pins the first output for a known seed, so a well-meant "optimisation"
/// of these constants fails the build rather than silently making every past order
/// irreproducible.
/// </para>
/// </remarks>
internal struct SplitMix64(long seed)
{
    private const ulong Increment = 0x9E3779B97F4A7C15;
    private const ulong FirstMix = 0xBF58476D1CE4E5B9;
    private const ulong SecondMix = 0x94D049BB133111EB;

    private ulong _state = unchecked((ulong)seed);

    public ulong NextULong()
    {
        unchecked
        {
            _state += Increment;

            var z = _state;
            z = (z ^ (z >> 30)) * FirstMix;
            z = (z ^ (z >> 27)) * SecondMix;

            return z ^ (z >> 31);
        }
    }

    /// <summary>
    /// A value in <c>[minInclusive, maxExclusive)</c>.
    /// </summary>
    /// <remarks>
    /// Plain modulo, which is very slightly biased towards the low end of the range. That is
    /// accepted here: the engine uses this to pick candidates, not to deal cards, and the bias
    /// over a range of a few hundred is far below anything that changes which baskets are
    /// reachable. Rejection sampling would make the sequence depend on the range, which would
    /// make a replay depend on a pool size that may have changed.
    /// </remarks>
    public int Next(int minInclusive, int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(minInclusive, maxExclusive);

        return minInclusive + (int)(NextULong() % (ulong)(maxExclusive - minInclusive));
    }
}
