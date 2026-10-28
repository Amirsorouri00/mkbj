namespace EwaysVNext.Modules.Customer.Application.Domain.Enums;

[Flags]
public enum LegalInformationEditableField
{
    None = 0,
    CompanyName = 1 << 0,
    NationalId = 1 << 1,
    RegistrationDate = 1 << 2,
    RegistrationNumber = 1 << 3,
    CeoName = 1 << 4,
    CeoMobileNumber = 1 << 5,

    /// <summary>
    /// Province, city and district are validated and corrected together as one relationship.
    /// </summary>
    Location = 1 << 6,
    Address = 1 << 7,
    PostalCode = 1 << 8,
    LandlineNumber = 1 << 9,
    Fax = 1 << 10,
    IntroductionLetter = 1 << 11,
    OfficialGazette = 1 << 12
}
