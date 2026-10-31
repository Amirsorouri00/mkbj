using EwaysVNext.Domain.Common;

namespace EwaysVNext.Modules.Settings.Application.Settings.Domain;

/// <summary>
/// Who the seller is, as it appears on an invoice. A singleton: there is one seller.
/// </summary>
public sealed class SellerProfile : AggregateRoot<long>
{
    private readonly List<string> _requiredBuyerFields = [];

    private SellerProfile()
    {
    }

    public string LegalName { get; private set; } = string.Empty;

    /// <summary>Singapore's Unique Entity Number. Printed verbatim on every invoice.</summary>
    public string Uen { get; private set; } = string.Empty;

    public string Address { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public bool GstRegistered { get; private set; }

    public string InvoiceFooter { get; private set; } = string.Empty;

    public IReadOnlyList<string> RequiredBuyerFields => _requiredBuyerFields;

    public static SellerProfile CreateDefault()
    {
        var profile = new SellerProfile { Id = SettingsDefaults.SingletonId };

        profile.Update(
            SettingsDefaults.LegalName,
            SettingsDefaults.Uen,
            SettingsDefaults.Address,
            SettingsDefaults.Email,
            gstRegistered: false,
            SettingsDefaults.InvoiceFooter,
            [SettingsDefaults.RequiredBuyerFieldsEntry]);

        return profile;
    }

    public void Update(
        string legalName,
        string uen,
        string address,
        string email,
        bool gstRegistered,
        string invoiceFooter,
        IReadOnlyList<string> requiredBuyerFields)
    {
        // Refused rather than stored and ignored. The tax policy returns zero unconditionally, so
        // a profile claiming GST registration would produce invoices showing no GST for a
        // business that is registered for it — a filing problem, not a display one. When GST is
        // actually supported this flag becomes meaningful; until then it must not be settable.
        if (gstRegistered)
        {
            throw new DomainException("VALIDATION_FAILED",
                "GST registration is not supported: the tax policy returns zero tax, so invoices "
                + "would under-report. Implement ITaxPolicy before enabling this.");
        }

        BuyerFieldRules.Validate(requiredBuyerFields);

        LegalName = Require(legalName, nameof(legalName), 200);
        Uen = Require(uen, nameof(uen), 32);
        Address = Require(address, nameof(address), 500);
        Email = Require(email, nameof(email), 200);
        GstRegistered = false;
        InvoiceFooter = (invoiceFooter ?? string.Empty).Trim();

        if (InvoiceFooter.Length > 1000)
            throw new DomainException("VALIDATION_FAILED", "The invoice footer cannot exceed 1000 characters.");

        _requiredBuyerFields.Clear();
        _requiredBuyerFields.AddRange(requiredBuyerFields);
    }

    private static string Require(string? value, string name, int maxLength)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed))
            throw new DomainException("VALIDATION_FAILED", $"{name} is required.");

        if (trimmed.Length > maxLength)
            throw new DomainException("VALIDATION_FAILED", $"{name} cannot exceed {maxLength} characters.");

        return trimmed;
    }
}
