using Customer.Infrastructure.Shahkar.DTOs;
using EwaysVNext.Modules.Customer.Application.Service.CompanyVerification.Abstractions;
using EwaysVNext.Modules.Customer.Application.Service.Shahkar.Abstractions;
using EwaysVNext.Outcomes;
using System.Text;

namespace EwaysVNext.Modules.Customer.Application.Service.CompanyVerification;

public sealed class ItHubCompanyVerificationService(IShahkarService shahkarService) : ICompanyVerificationService
{
    public const string UnavailableErrorCode = "COMPANY_VERIFICATION_UNAVAILABLE";
    public const string CompanyNotFoundErrorCode = "COMPANY_VERIFICATION_COMPANY_NOT_FOUND";
    public const string CeoNotFoundErrorCode = "COMPANY_VERIFICATION_CEO_NOT_FOUND";
    public const string CeoNameMismatchErrorCode = "COMPANY_VERIFICATION_CEO_NAME_MISMATCH";

    private const string CeoRole = "مدیرعامل";

    private static readonly Error Unavailable = new(
        UnavailableErrorCode,
        "Company/tax-authority verification is not available in this environment. Please try again later or contact support.",
        503);

    private static readonly Error CompanyNotFound = new(CompanyNotFoundErrorCode, "No company was found for the given national id.");
    private static readonly Error CeoNotFound = new(CeoNotFoundErrorCode, "The company's CEO could not be found in the registry data.");
    private static readonly Error CeoNameMismatch = new(CeoNameMismatchErrorCode, "The CEO name does not match the registry data.");

    public async Task<Result> VerifyAsync(CompanyVerificationRequest request, CancellationToken ct = default)
    {
        CompanyInquiryResponse inquiry;
        try
        {
            var result = await shahkarService.GetCompanyInquiryAsync(request.NationalId, ct);
            if (result.IsFailure)
                return Result.Failure(Unavailable);

            inquiry = result.Value!;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            //todo set log
            return Result.Failure(Unavailable);
        }

        var company = inquiry.Data;
        if (company is null)
            return Result.Failure(CompanyNotFound);

        var ceos = company.CompanyPerson.Where(IsCeo).ToList();
        if (ceos.Count == 0)
            return Result.Failure(CeoNotFound);

        // Prefer the CEO whose term has not ended; fall back to every CEO entry if none is open-ended.
        var currentCeos = ceos.Where(p => string.IsNullOrWhiteSpace(p.EndDate)).ToList();
        if (currentCeos.Count > 0)
            ceos = currentCeos;

        var expectedName = NormalizeName(request.CeoName);
        var matches = ceos.Any(p =>
        {
            var registryName = NormalizeName(p.Title);
            return registryName.Length > 0 && expectedName.Length > 0
                && (registryName == expectedName || registryName.Contains(expectedName, StringComparison.Ordinal));
        });

        return matches ? Result.Success() : Result.Failure(CeoNameMismatch);
    }

    private static bool IsCeo(CompanyInquiryPerson person)
    {
        var ceoRole = NormalizeName(CeoRole);
        return NormalizeName(person.Position?.Title).Contains(ceoRole, StringComparison.Ordinal)
            || NormalizeName(person.FirstRole).Contains(ceoRole, StringComparison.Ordinal);
    }

    /// <summary>
    /// Normalizes a Persian name for comparison: Arabic Yeh/Kaf to Persian, drops diacritics,
    /// ZWNJ and all whitespace, so "علي  محمدي" and "علی‌محمدی" compare equal.
    /// </summary>
    private static string NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case 'ي' or 'ى':
                    builder.Append('ی');
                    break;
                case 'ك':
                    builder.Append('ک');
                    break;
                case 'ة':
                    builder.Append('ه');
                    break;
                case 'أ' or 'إ' or 'ٱ':
                    builder.Append('ا');
                    break;
                case '‌' or '‍' or 'ـ':
                    break;
                case >= 'ً' and <= 'ٟ':
                    break;
                default:
                    if (!char.IsWhiteSpace(ch))
                        builder.Append(char.ToLowerInvariant(ch));
                    break;
            }
        }

        return builder.ToString();
    }
}
