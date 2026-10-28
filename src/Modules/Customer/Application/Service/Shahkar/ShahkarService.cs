using Customer.Infrastructure.Shahkar.DTOs;
using EwaysVNext.Outcomes;
using EwaysVNext.Modules.Customer.Application.Service.Shahkar.Abstractions;
using Polly.Timeout;
using System.Globalization;

namespace EwaysVNext.Modules.Customer.Application.Service.Shahkar;

public class ShahkarService(IVerificationApi verificationApi,
    ITokenService tokenService,
    IVerifyMobileTokenService verifyMobileToken,
    IVerifyMobileApi verifyMobileApi,
    ILegalInquiryApi legalInquiryApi) : IShahkarService
{
    private static readonly Error ShahkarTimeout = new("ShahkarTimeout", ShahkarServiceMessages.Timeout);

    public async Task<Result<ApiResponse<ShahkarIdentityResponse>>> VerifyCitizenshipAsync(string nationalCode, DateOnly birthDate, CancellationToken ct)
    {
        try
        {
            var token = await tokenService.GetAccessTokenAsync(ct);
            var authorization = $"Bearer {token}";

            return Result.Success(await verificationApi.VerifyCitizenshipAsync(authorization, new ShahkarIdentityRequest
            {
                NationalCode = nationalCode,
                BirthDate = ConvertToShamsiDate(birthDate)
            }));
        }
        catch (TimeoutRejectedException)
        {
            return Result.Failure<ApiResponse<ShahkarIdentityResponse>>(ShahkarTimeout);
        }
    }

    public async Task<Result<VerifyMobileResponse>> VerifyMobileNationalCodeAsync(string mobile, string nationalCode, CancellationToken ct)
    {
        try
        {
            var token = await verifyMobileToken.GetAccessTokenAsync(ct);
            var authorization = $"Bearer {token}";

            return Result.Success(await verifyMobileApi.VerifyMobileNationalCodeAsync(authorization, new VerifyMobileRequest
            {
                NationalCode = nationalCode,
                Mobile = mobile
            }));
        }
        catch (TimeoutRejectedException)
        {
            return Result.Failure<VerifyMobileResponse>(ShahkarTimeout);
        }
    }

    public async Task<Result<CompanyInquiryResponse>> GetCompanyInquiryAsync(string companyId, CancellationToken ct)
    {
        try
        {
            var token = await verifyMobileToken.GetAccessTokenAsync(ct);
            var authorization = $"Bearer {token}";

            return Result.Success(await legalInquiryApi.GetCompanyInquiryAsync(authorization, companyId, ct));
        }
        catch (TimeoutRejectedException)
        {
            //todo set log
            return Result.Failure<CompanyInquiryResponse>(ShahkarTimeout);
        }
    }
    private static string ConvertToShamsiDate(DateOnly gregorianDate)
    {
        var dt = gregorianDate.ToDateTime(TimeOnly.MinValue);

        var persianCalendar = new PersianCalendar();

        int year = persianCalendar.GetYear(dt);
        int month = persianCalendar.GetMonth(dt);
        int day = persianCalendar.GetDayOfMonth(dt);

        return $"{year:D4}/{month:D2}/{day:D2}";
    }
}
