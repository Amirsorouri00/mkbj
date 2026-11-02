using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Pricing.Application.Fx.Ports.Inbound;

/// <summary>Fetches and stores rates. Returns how many rows were actually new.</summary>
public interface IFxRefreshService
{
    Task<Result<int>> RefreshAsync(CancellationToken ct = default);

    /// <summary>
    /// Stores a batch somebody else fetched, skipping quotes already recorded for that provider
    /// and day. Separate from <see cref="RefreshAsync"/> so a test, a seed or a manual import can
    /// supply rates without reaching the network.
    /// </summary>
    Task<Result<int>> IngestAsync(FxProviderBatch batch, CancellationToken ct = default);
}

/// <summary>The recurring job's payload. It carries nothing: there is one thing to refresh.</summary>
public sealed record FxRefreshPayload();
