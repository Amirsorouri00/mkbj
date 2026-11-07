using System.Security.Cryptography;
using EwaysVNext.Modules.Sales.Application.Composition;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;

namespace EwaysVNext.Modules.Sales.Adapter.Outbound.Persistence;

/// <summary>A real wall-clock budget, measured with monotonic timestamps.</summary>
internal sealed class StopwatchBudgetFactory : ICompositionBudgetFactory
{
    public ICompositionBudget Start(TimeSpan deadline) => new StopwatchBudget(deadline);
}

/// <summary>
/// Cryptographically random seeds.
/// </summary>
/// <remarks>
/// Not because a seed is a secret, but because a predictable one makes compositions collide: two
/// partners asking for the same amount in the same second would be sold the identical basket,
/// which looks like a system with one answer rather than a catalogue.
/// </remarks>
internal sealed class RandomSeedSource : ICompositionSeedSource
{
    public long NextSeed() => BitConverter.ToInt64(RandomNumberGenerator.GetBytes(sizeof(long)));
}
