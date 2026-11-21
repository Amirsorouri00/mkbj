using Mkbj.Modules.Pricing.Application.Fx.Ports.Inbound;
using Mkbj.Outcomes;

namespace Mkbj.Modules.Pricing.Application.Fx.Ports.Outbound;

/// <summary>
/// Somewhere rates come from. <see cref="Name"/> is stored on every row it produces, so two
/// providers disagreeing about a day is a fact the table can hold rather than a conflict.
/// </summary>
public interface IFxRateProvider
{
    string Name { get; }

    Task<Result<FxProviderBatch>> FetchAsync(CancellationToken ct = default);
}
