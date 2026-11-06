namespace EwaysVNext.Modules.Sales.Application.Composition;

/// <summary>
/// Composes a basket of real SKUs totalling a partner's requested amount exactly.
/// </summary>
/// <remarks>
/// Deliberately pure — no ports, no IO, no clock, no logger — and
/// <c>SalesArchitectureTests.Sales_Composition_Has_No_IO_Or_Port_Dependencies</c> enforces it.
/// The seed is persisted with the order, so a composition has to be replayable years later from
/// its inputs alone; one ambient dependency and that stops being true.
/// <para>Filled in by task 5.</para>
/// </remarks>
public static class CompositionEngine
{
    /// <summary>
    /// Persisted with every order, so a basket stays explainable after the strategy changes.
    /// Bump it when the algorithm changes in a way that would compose a different basket from
    /// the same seed.
    /// </summary>
    public const string StrategyVersion = "v1";
}
