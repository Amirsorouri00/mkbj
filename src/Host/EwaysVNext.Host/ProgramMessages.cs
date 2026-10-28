namespace EwaysVNext.Host;

internal static class ProgramMessages
{
    public const string OtlpNotConfigured = "WARNING: no OTLP endpoint configured (Otlp:Endpoint or OTEL_EXPORTER_OTLP_ENDPOINT). Traces and metrics — including sagas.compensation_failures — are collected and DISCARDED.";
    public const string MigrateSkippedFormat = "[migrate] skipped ({0} is non-relational).";
    public const string MigrateAuthorizationSkippedFormat = "[migrate] Authorization skipped ({0} is non-relational).";
    public const string MigrateDone = "[migrate] Done.";
    public const string SeedDone = "[seed] Done.";
    public const string SeedRefusedInProduction =
        "--seed refuses to run when ASPNETCORE_ENVIRONMENT=Production";
    public const string ProbeUsage = "usage: --probe <url>";
}
