using System.Diagnostics.Metrics;

namespace EwaysVNext.Modules.Sales.Application;

/// <summary>
/// What Sales reports about itself.
/// </summary>
/// <remarks>
/// Both of these count things that are deliberately <b>not</b> failures. A contact sync that
/// fails still leaves a sold, paid and invoiced order, and a composition that takes a while is
/// not an error — so neither would show up in an error rate. Without a counter they would be
/// invisible until someone noticed by hand.
/// </remarks>
public static class SalesMetrics
{
    public const string MeterName = "EwaysVNext.Sales";

    private static readonly Meter Meter = new(MeterName);

    /// <summary>
    /// Contact details that could not be pushed back to Customer after a successful order.
    /// </summary>
    /// <remarks>
    /// A steady trickle means partners' records are drifting out of date while every order still
    /// reports success.
    /// </remarks>
    public static readonly Counter<long> CustomerContactSyncFailures =
        Meter.CreateCounter<long>("customer_contact_sync_failures");

    /// <summary>How long composing one basket took.</summary>
    public static readonly Histogram<long> CompositionDurationMs =
        Meter.CreateHistogram<long>("composition_duration_ms");
}
