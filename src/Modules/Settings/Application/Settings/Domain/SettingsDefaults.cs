namespace EwaysVNext.Modules.Settings.Application.Settings.Domain;

/// <summary>
/// What a fresh database starts with.
/// <para>
/// The seller values are deliberate placeholders, not plausible-looking data. An invoice carrying
/// "Configure seller legal name" is obviously wrong to whoever reads it; one carrying a guessed
/// company name and a real-looking UEN is wrong in a way nobody notices until a customer does.
/// The runbook says to configure these before go-live, and these values are what makes skipping
/// that step visible.
/// </para>
/// </summary>
public static class SettingsDefaults
{
    /// <summary>
    /// Every settings aggregate is a singleton at this id, enforced by a CHECK constraint.
    /// Snowflake ids are far larger, so a generated id can never collide with it.
    /// </summary>
    public const long SingletonId = 1;

    public const long MinUsdMinor = 1_000;
    public const long MaxUsdMinor = 100_000_000;
    public const int BulkDefaultMax = 100;
    public const int CompositionDeadlineMs = 8000;
    public const int CompositionMaxLines = 200;

    public const string LegalName = "Configure seller legal name";
    public const string Uen = "UNSET";
    public const string Address = "Singapore";
    public const string Email = "billing@example.invalid";
    public const string InvoiceFooter = "";

    /// <summary>A customer must give at least one way to reach them.</summary>
    public const string RequiredBuyerFieldsEntry = "email|phone";

    public const string InitialPaymentInstructionText =
        "Pay by bank transfer using the details on your pro forma invoice. "
        + "Quote the pro forma number as the payment reference.";
}
