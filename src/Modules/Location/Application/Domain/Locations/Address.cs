using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Location.Application.Domain.Enums;

namespace EwaysVNext.Modules.Location.Application.Domain.Locations;
public sealed class Address : AggregateRoot<long>
{
    public int? DistrictId { get; private set; }
    public int CityId { get; private set; }

    public string? AddressText { get; private set; } = null!;

    public string? PostalCode { get; private set; } = null!;

    public string? RecipientName { get; private set; } = null!;

    public string? RecipientMobile { get; private set; } = null!;

    public bool IsPrimary { get; private set; }

    public int StatusId { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? UpdatedAt { get; private set; }
    public AddressStatus AddressStatusNavigation { get; private set; } = null!;
    private Address()
    {
    }
    private Address(
        long id,
        int cityId,
        int? districtId,
        string? addressText,
        string? postalCode,
        string? recipientName,
        string? recipientMobile,
        bool isPrimary,
        DateTimeOffset now)
    {
        Id = id;
        CityId = cityId;
        DistrictId = districtId;
        AddressText = addressText;
        PostalCode = postalCode;
        RecipientName = recipientName;
        RecipientMobile = recipientMobile;
        IsPrimary = isPrimary;
        StatusId = (int)AddressStatusType.Active;

        CreatedAt = now.UtcDateTime;
        UpdatedAt = now.UtcDateTime;
    }


    public static Address Create(
        long id,
        int cityId,
        DateTimeOffset now,
        int? districtId = null,
        string? addressText = null,
        string? postalCode = null,
        string? recipientName = null,
        string? recipientMobile = null,
        bool isPrimary = false)
    {
        Validate(
            cityId,
            addressText,
            postalCode,
            recipientName,
            recipientMobile);

        return new Address(
            id,
            cityId,
            districtId,
            addressText,
            postalCode,
            recipientName,
            recipientMobile,
            isPrimary,
            now);
    }


    public void Update(
        int cityId,
        int? districtId,
        string? addressText,
        string? postalCode,
        string? recipientName,
        string? recipientMobile,
        DateTimeOffset now)
    {
        Validate(
            cityId,
            addressText,
            postalCode,
            recipientName,
            recipientMobile);
        EnsureNotPrimary();

        CityId = cityId;
        DistrictId = districtId;
        AddressText = addressText;
        PostalCode = postalCode;
        RecipientName = recipientName;
        RecipientMobile = recipientMobile;

        Touch(now);
    }


    public void CorrectLocation(
        int cityId,
        int? districtId,
        string? addressText,
        string? postalCode,
        DateTimeOffset now)
    {
        Validate(
            cityId,
            addressText,
            postalCode,
            RecipientName,
            RecipientMobile);

        CityId = cityId;
        DistrictId = districtId;
        AddressText = addressText;
        PostalCode = postalCode;

        Touch(now);
    }


    public void SetAsPrimary(DateTimeOffset now)
    {
        IsPrimary = true;
        Touch(now);
    }


    public void RemovePrimary(DateTimeOffset now)
    {
        IsPrimary = false;
        Touch(now);
    }


    public void Activate(DateTimeOffset now)
    {
        if ((int)AddressStatusType.Active == StatusId)
            return;
        StatusId = (int)AddressStatusType.Active;
        Touch(now);
    }


    public void Inactive(DateTimeOffset now)
    {
        if ((int)AddressStatusType.Inactive == StatusId)
            return;

        StatusId = (int)AddressStatusType.Inactive;
        IsPrimary = false;
        Touch(now);
    }
    public void Deleted(DateTimeOffset now)
    {
        if ((int)AddressStatusType.Deleted == StatusId)
            return;
        EnsureNotPrimary();

        StatusId = (int)AddressStatusType.Deleted;
        IsPrimary = false;
        Touch(now);
    }
    public void Archived(DateTimeOffset now)
    {
        if ((int)AddressStatusType.Archived == StatusId)
            return;

        StatusId = (int)AddressStatusType.Archived;
        IsPrimary = false;
        Touch(now);
    }


    private void EnsureNotPrimary()
    {
        if (IsPrimary)
            throw new DomainException("PRIMARY_ADDRESS_CANNOT_BE_MODIFIED", AddressMessages.PrimaryAddressCannotBeModified);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now.UtcDateTime;
    }


    private static void Validate(
        int cityId,
        string? addressText,
        string? postalCode,
        string? recipientName,
        string? recipientMobile)
    {
        if (cityId <= 0)
            throw new DomainException("CITYID_MUST_BE_GREATER_THAN_ZERO", AddressMessages.CityIdMustBeGreaterThanZero);

        if (!string.IsNullOrWhiteSpace(addressText) && addressText.Length > 1000)
            throw new DomainException("ADDRESS_CANNOT_EXCEED_ONE_THOUSAND_CHARACTERS", AddressMessages.AddressTooLong);



        if (!string.IsNullOrWhiteSpace(recipientName) && recipientName.Length > 200)
            throw new DomainException("RECIPIENTNAME_CANNOT_EXCEED_TWO_HUNDRED_CHARACTERS", AddressMessages.RecipientNameTooLong);

        if (!string.IsNullOrWhiteSpace(recipientMobile) && recipientMobile.Length > 20)
            throw new DomainException("RECIPIENTMOBILE_CANNOT_EXCEED_TWENTY_CHARACTERS", AddressMessages.RecipientMobileTooLong);
    }
}
