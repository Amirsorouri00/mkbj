using EwaysVNext.Core.Application;

namespace EwaysVNext.Core.Implementation;

/// <summary>Scoped: one per request, set by CorrelationIdMiddleware.</summary>
public sealed class CorrelationContext : ICorrelationContext
{
    public string CorrelationId { get; set; } = string.Empty;
}
