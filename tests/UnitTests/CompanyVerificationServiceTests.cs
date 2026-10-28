using AwesomeAssertions;
using Customer.Infrastructure.Shahkar.DTOs;
using EwaysVNext.Modules.Customer.Application.Service.CompanyVerification;
using EwaysVNext.Modules.Customer.Application.Service.CompanyVerification.Abstractions;
using EwaysVNext.Modules.Customer.Application.Service.Shahkar.Abstractions;
using EwaysVNext.Outcomes;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace EwaysVNext.UnitTests;

/// <summary>
/// Company verification against the ITHub legal inquiry: the submitted CEO name must match the
/// registry's CEO entry. The Shahkar client is faked - no real external provider is called.
/// </summary>
public class CompanyVerificationServiceTests
{
    private const string CompanyNationalId = "14008019700";

    private readonly IShahkarService _shahkar = Substitute.For<IShahkarService>();

    private ItHubCompanyVerificationService CreateSut() => new(_shahkar);

    private static CompanyVerificationRequest Request(string ceoName) =>
        new("شرکت نمونه", CompanyNationalId, new DateOnly(2019, 1, 5), "12345", ceoName);

    private void Returns(params CompanyInquiryPerson[] persons) =>
        _shahkar.GetCompanyInquiryAsync(CompanyNationalId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new CompanyInquiryResponse
            {
                Data = new CompanyInquiryData { CompanyPerson = persons.ToList() }
            }));

    private static CompanyInquiryPerson Person(string name, string position, string? endDate = null) => new()
    {
        Title = name,
        Position = new CompanyInquiryPosition { Title = position },
        EndDate = endDate
    };

    [Fact]
    public async Task Succeeds_When_Ceo_Name_Matches()
    {
        Returns(Person("علی محمدی", "عضو هیئت مدیره"), Person("رضا احمدی", "مدیرعامل"));

        var result = await CreateSut().VerifyAsync(Request("رضا احمدی"));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Succeeds_Despite_Arabic_Letters_Spacing_And_Prefix()
    {
        Returns(Person("آقای علي  كريمي", "مدير عامل و عضو هیئت مدیره"));

        var result = await CreateSut().VerifyAsync(Request("علی‌کریمی"));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Fails_When_Name_Belongs_To_A_Non_Ceo_Member()
    {
        Returns(Person("علی محمدی", "عضو هیئت مدیره"), Person("رضا احمدی", "مدیرعامل"));

        var result = await CreateSut().VerifyAsync(Request("علی محمدی"));

        result.Error!.Code.Should().Be(ItHubCompanyVerificationService.CeoNameMismatchErrorCode);
    }

    [Fact]
    public async Task Fails_When_Name_Belongs_To_A_Former_Ceo()
    {
        Returns(Person("علی محمدی", "مدیرعامل", endDate: "1400/01/01"), Person("رضا احمدی", "مدیرعامل"));

        var result = await CreateSut().VerifyAsync(Request("علی محمدی"));

        result.Error!.Code.Should().Be(ItHubCompanyVerificationService.CeoNameMismatchErrorCode);
    }

    [Fact]
    public async Task Fails_When_Registry_Has_No_Ceo()
    {
        Returns(Person("علی محمدی", "عضو هیئت مدیره"));

        var result = await CreateSut().VerifyAsync(Request("علی محمدی"));

        result.Error!.Code.Should().Be(ItHubCompanyVerificationService.CeoNotFoundErrorCode);
    }

    [Fact]
    public async Task Fails_When_Company_Is_Not_Found()
    {
        _shahkar.GetCompanyInquiryAsync(CompanyNationalId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new CompanyInquiryResponse()));

        var result = await CreateSut().VerifyAsync(Request("علی محمدی"));

        result.Error!.Code.Should().Be(ItHubCompanyVerificationService.CompanyNotFoundErrorCode);
    }

    [Fact]
    public async Task Reports_Unavailable_On_Timeout()
    {
        _shahkar.GetCompanyInquiryAsync(CompanyNationalId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<CompanyInquiryResponse>(new Error("ShahkarTimeout", "Shahkar Timeout.")));

        var result = await CreateSut().VerifyAsync(Request("علی محمدی"));

        result.Error!.Code.Should().Be(ItHubCompanyVerificationService.UnavailableErrorCode);
    }

    [Fact]
    public async Task Reports_Unavailable_When_Provider_Throws()
    {
        _shahkar.GetCompanyInquiryAsync(CompanyNationalId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("boom"));

        var result = await CreateSut().VerifyAsync(Request("علی محمدی"));

        result.Error!.Code.Should().Be(ItHubCompanyVerificationService.UnavailableErrorCode);
    }
}
