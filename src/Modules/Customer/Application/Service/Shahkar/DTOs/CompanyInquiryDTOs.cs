using System.Text.Json;
using System.Text.Json.Serialization;

namespace Customer.Infrastructure.Shahkar.DTOs;

/// <summary>
/// Response DTO from LegalInquiry/GetCompanyInquiry endpoint
/// </summary>
public class CompanyInquiryResponse
{
    [JsonPropertyName("data")]
    public CompanyInquiryData? Data { get; set; }

    [JsonPropertyName("meta")]
    public JsonElement? Meta { get; set; }

    [JsonPropertyName("error")]
    public VerifyMobileResponse.ShahkarErrorResponse? Error { get; set; }
}

/// <summary>
/// Company information from ITHub legal inquiry
/// </summary>
[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
public class CompanyInquiryData
{
    [JsonPropertyName("id")]
    public long? Id { get; set; }

    /// <summary>
    /// National id of the company
    /// </summary>
    [JsonPropertyName("companyId")]
    public long? CompanyId { get; set; }

    [JsonPropertyName("latestCompanyName")]
    public string? LatestCompanyName { get; set; }

    [JsonPropertyName("registrationNumber")]
    public string? RegistrationNumber { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>
    /// Establishment announcement information
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Registration date in Shamsi format
    /// </summary>
    [JsonPropertyName("persianRegistrationDate")]
    public string? PersianRegistrationDate { get; set; }

    [JsonPropertyName("address")]
    public string? Address { get; set; }

    [JsonPropertyName("postalCode")]
    public string? PostalCode { get; set; }

    [JsonPropertyName("website")]
    public string? Website { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    /// <summary>
    /// Knowledge-based status of the company
    /// </summary>
    [JsonPropertyName("state")]
    public string? State { get; set; }

    /// <summary>
    /// Knowledge-based category of the company
    /// </summary>
    [JsonPropertyName("category")]
    public string? Category { get; set; }

    /// <summary>
    /// Knowledge-based confirmation date
    /// </summary>
    [JsonPropertyName("confirmationDate")]
    public string? ConfirmationDate { get; set; }

    [JsonPropertyName("capitalTo")]
    public long? CapitalTo { get; set; }

    [JsonPropertyName("newspaperDate")]
    public DateTime? NewspaperDate { get; set; }

    [JsonPropertyName("newsLetterDate")]
    public DateTime? NewsLetterDate { get; set; }

    [JsonPropertyName("newspaperNumber")]
    public string? NewspaperNumber { get; set; }

    [JsonPropertyName("newspaperCityType")]
    public string? NewspaperCityType { get; set; }

    [JsonPropertyName("pageNumber")]
    public int? PageNumber { get; set; }

    [JsonPropertyName("indicatorNumber")]
    public string? IndicatorNumber { get; set; }

    /// <summary>
    /// Board members and managers
    /// </summary>
    [JsonPropertyName("companyPerson")]
    public List<CompanyInquiryPerson> CompanyPerson { get; set; } = [];

    [JsonPropertyName("newsPerson")]
    public List<CompanyInquiryPerson> NewsPerson { get; set; } = [];

    /// <summary>
    /// Fields not documented precisely (certificates, activity fields, innovative status, ...)
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>
/// Board member / manager of the company
/// </summary>
[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
public class CompanyInquiryPerson
{
    [JsonPropertyName("personId")]
    public long? PersonId { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("firstRole")]
    public string? FirstRole { get; set; }

    [JsonPropertyName("startDate")]
    public string? StartDate { get; set; }

    [JsonPropertyName("endDate")]
    public string? EndDate { get; set; }

    [JsonPropertyName("duration")]
    public JsonElement? Duration { get; set; }

    [JsonPropertyName("privateMobile")]
    public string? PrivateMobile { get; set; }

    [JsonPropertyName("position")]
    public CompanyInquiryPosition? Position { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

public class CompanyInquiryPosition
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }
}
