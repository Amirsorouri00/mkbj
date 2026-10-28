namespace EwaysVNext.Core.Application;

/// <summary>
/// The current request's correlation id, for code that is nowhere near an <c>HttpContext</c> —
/// a job handler, a gateway, an audit row.
/// </summary>
public interface ICorrelationContext
{
    string CorrelationId { get; set; }
}
