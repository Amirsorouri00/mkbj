using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Customer.Application.Domain.Customers;
using EwaysVNext.Modules.Customer.Application.Domain.Enums;
using EwaysVNext.Modules.Customer.Application.Domain.ValueObjects;
using EwaysVNext.Modules.Customer.Application.Ports.Inbound.Dtos;
using EwaysVNext.Modules.Customer.Application.Ports.Outbound;
using EwaysVNext.Modules.Customer.Application.Service;
using EwaysVNext.Modules.Location.Application.Domain.Enums;
using EwaysVNext.Modules.Location.Application.Domain.Locations;
using EwaysVNext.Modules.Location.Application.Ports.Outbound;
using EwaysVNext.Modules.Location.Application.Service;
using EwaysVNext.Modules.Location.Contracts.Abstractions;
using EwaysVNext.Modules.Location.Contracts.Locations;
using EwaysVNext.Outcomes;
using NSubstitute;
using Xunit;
using CustomerAggregate = EwaysVNext.Modules.Customer.Application.Domain.Customers.Customer;
using PersistenceUnitOfWork = EwaysVNext.Persistence.IUnitOfWork;

namespace EwaysVNext.UnitTests;

/// <summary>Address aggregate rules used by the customer address book.</summary>
public sealed class AddressDomainTests
{
    private static readonly SnowflakeIdGenerator Ids = new();

    private static Address Normal() => Address.Create(Ids.Generate(), 1, TestTime.Now, 10, "Old street", "1234567890", "Ali", "+989121112233");
    private static Address Primary() => Address.Create(Ids.Generate(), 1, TestTime.Now, 10, "Registration", null, null, null, isPrimary: true);

    [Fact]
    public void Update_Changes_Every_Editable_Field_Including_City_And_Stamps_The_Supplied_Time()
    {
        var address = Normal();
        var later = TestTime.PlusMinutes(5);

        address.Update(2, null, "New street", "0987654321", "Sara", "+989351112233", later);

        address.CityId.Should().Be(2, "Update used to silently keep the old city");
        address.DistrictId.Should().BeNull();
        address.AddressText.Should().Be("New street");
        address.PostalCode.Should().Be("0987654321");
        address.RecipientName.Should().Be("Sara");
        address.RecipientMobile.Should().Be("+989351112233");
        address.CreatedAt.Should().Be(TestTime.Now.UtcDateTime);
        address.UpdatedAt.Should().Be(later.UtcDateTime);
    }

    [Fact]
    public void Primary_Address_Cannot_Be_Updated()
    {
        var address = Primary();

        var act = () => address.Update(2, null, "x", "0987654321", "Sara", "+989351112233", TestTime.PlusMinutes(1));

        act.Should().Throw<DomainException>().Which.Code.Should().Be("PRIMARY_ADDRESS_CANNOT_BE_MODIFIED");
        address.CityId.Should().Be(1);
    }

    [Fact]
    public void Deleted_Soft_Deletes_A_Normal_Address_Via_Status()
    {
        var address = Normal();

        address.Deleted(TestTime.PlusMinutes(1));

        address.StatusId.Should().Be((int)AddressStatusType.Deleted, "the status guard used to be inverted, making Deleted a no-op");
        address.UpdatedAt.Should().Be(TestTime.PlusMinutes(1).UtcDateTime);
    }

    [Fact]
    public void Primary_Address_Cannot_Be_Deleted()
    {
        var address = Primary();

        var act = () => address.Deleted(TestTime.PlusMinutes(1));

        act.Should().Throw<DomainException>().Which.Code.Should().Be("PRIMARY_ADDRESS_CANNOT_BE_MODIFIED");
        address.StatusId.Should().Be((int)AddressStatusType.Active);
        address.IsPrimary.Should().BeTrue();
    }
}

/// <summary>Location-side address operations: IClock usage, primary protection, primary transfer.</summary>
public sealed class LocationAddressServiceTests
{
    private static readonly SnowflakeIdGenerator Ids = new();
    private readonly IAddressRepository _addresses = Substitute.For<IAddressRepository>();
    private readonly PersistenceUnitOfWork _unitOfWork = Substitute.For<PersistenceUnitOfWork>();
    private readonly FixedClock _clock = new();
    private readonly LocationService _sut;

    public LocationAddressServiceTests() =>
        _sut = new LocationService(Substitute.For<IProvinceRepository>(), _addresses, _unitOfWork, Ids, _clock);

    private Address Stored(bool isPrimary = false)
    {
        var a = Address.Create(Ids.Generate(), 1, TestTime.Now, null, "Street", "1234567890", "Ali", "+989121112233", isPrimary);
        _addresses.GetByIdAsync(a.Id, Arg.Any<CancellationToken>()).Returns(a);
        return a;
    }

    [Fact]
    public async Task CreateAddress_Uses_IIdGenerator_And_IClock()
    {
        var result = await _sut.CreateAddressAsync(1, null, "Street", "1234567890", "Ali", "+989121112233", false, default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.id.Should().BePositive();
        result.Value.createdAt.Should().Be(TestTime.Now.UtcDateTime);
        result.Value.isPrimary.Should().BeFalse();
        await _addresses.Received(1).AddAsync(Arg.Is<Address>(a => a.Id == result.Value.id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAddress_Stamps_UpdatedAt_From_IClock()
    {
        var address = Stored();
        _clock.Advance(TimeSpan.FromHours(2));

        var result = await _sut.UpdateAddressAsync(address.Id, 2, null, "New", "0987654321", "Sara", "+989351112233");

        result.IsSuccess.Should().BeTrue();
        result.Value!.updatedAt.Should().Be(TestTime.Plus(TimeSpan.FromHours(2)).UtcDateTime);
        result.Value.createdAt.Should().Be(TestTime.Now.UtcDateTime);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAddress_Refuses_The_Primary_Address_Without_Throwing()
    {
        var address = Stored(isPrimary: true);

        var result = await _sut.UpdateAddressAsync(address.Id, 2, null, "New", "0987654321", "Sara", "+989351112233");

        result.Error!.Code.Should().Be("PRIMARY_ADDRESS_CANNOT_BE_MODIFIED");
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAddress_Refuses_The_Primary_Address_And_Soft_Deletes_A_Normal_One()
    {
        var primary = Stored(isPrimary: true);
        var normal = Stored();

        (await _sut.DeleteAddressAsync(primary.Id)).Error!.Code.Should().Be("PRIMARY_ADDRESS_CANNOT_BE_MODIFIED");
        (await _sut.DeleteAddressAsync(normal.Id)).IsSuccess.Should().BeTrue();

        primary.StatusId.Should().Be((int)AddressStatusType.Active);
        normal.StatusId.Should().Be((int)AddressStatusType.Deleted);
        (await _sut.DeleteAddressAsync(normal.Id)).Error!.Code.Should().Be("ADDRESS_NOT_FOUND", "a deleted address is gone");
    }

    [Fact]
    public async Task SetPrimary_Moves_The_Flag_In_A_Single_Save()
    {
        var oldPrimary = Address.Create(Ids.Generate(), 1, TestTime.Now, isPrimary: true);
        var target = Address.Create(Ids.Generate(), 1, TestTime.Now);
        _addresses.GetTrackedByIdsAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns([oldPrimary, target]);

        var result = await _sut.SetPrimaryAddressAsync(target.Id, [oldPrimary.Id, target.Id]);

        result.IsSuccess.Should().BeTrue();
        target.IsPrimary.Should().BeTrue();
        oldPrimary.IsPrimary.Should().BeFalse();
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetPrimary_On_The_Current_Primary_Is_A_No_Op_Success()
    {
        var primary = Address.Create(Ids.Generate(), 1, TestTime.Now, isPrimary: true);
        _addresses.GetTrackedByIdsAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns([primary]);

        var result = await _sut.SetPrimaryAddressAsync(primary.Id, [primary.Id]);

        result.IsSuccess.Should().BeTrue();
        primary.IsPrimary.Should().BeTrue();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}

/// <summary>Customer-side address book: ownership (IDOR), validation, recipient resolution.</summary>
public sealed class CustomerAddressServiceTests
{
    private static readonly SnowflakeIdGenerator Ids = new();
    private const int CityId = 5;
    private const int DistrictInCity = 50;
    private const int DistrictElsewhere = 99;

    private readonly ICustomerRepository _customers = Substitute.For<ICustomerRepository>();
    private readonly ILocationService _locations = Substitute.For<ILocationService>();
    private readonly PersistenceUnitOfWork _unitOfWork = Substitute.For<PersistenceUnitOfWork>();
    private readonly CustomerAddressService _sut;
    private readonly CustomerAggregate _customer;
    private readonly long _ownedAddressId = Ids.Generate();

    public CustomerAddressServiceTests()
    {
        _sut = new CustomerAddressService(_customers, _locations, _unitOfWork, Ids);

        _customer = CustomerAggregate.Register(Ids.Generate(), Guid.NewGuid(), MobileNumber.Create("09121234567"), TestTime.Now);
        _customer.RegisterCustomerInfo(NationalCode.Create("0012345679"), "Sara", "Ahmadi", Gender.Female, new DateOnly(1990, 1, 1), TestTime.Now);
        _customer.AddAddress(CustomerAddress.Create(Ids.Generate(), _customer.Id, _ownedAddressId));
        _customers.GetWithAddressesByIdentityUserIdAsync(_customer.IdentityUserId, Arg.Any<CancellationToken>()).Returns(_customer);

        _locations.GetCityById(CityId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new CitiesResponse(CityId, "Tehran", [new DistrictResponse(DistrictInCity, "D1")])));
        _locations.GetCityById(Arg.Is<int>(c => c != CityId), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<CitiesResponse>(Error.NotFound));
        _locations.CreateAddressAsync(default, default, default, default, default, default, default, default)
            .ReturnsForAnyArgs(ci => Result.Success(Response(Ids.Generate(), ci.ArgAt<string?>(4), ci.ArgAt<string?>(5), ci.ArgAt<bool>(6))));
    }

    private static AddressResponse Response(long id, string? name = "Ali", string? mobile = "+989351112233", bool isPrimary = false) =>
        new(id, 0, CityId, null, "Street", "1234567890", name, mobile, isPrimary, (int)AddressStatusType.Active, TestTime.Now.UtcDateTime, null);

    private static CustomerAddressInput Input(
        string? postalCode = "1234567890", int cityId = CityId, int? districtId = DistrictInCity,
        bool self = false, string? name = "Ali Rezaei", string? mobile = "09351112233") =>
        new(cityId, districtId, "Street 1", postalCode, self, name, mobile);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123456789")]
    [InlineData("12345678901")]
    [InlineData("12345-67890")]
    [InlineData("12345 67890")]
    [InlineData("abcdefghij")]
    [InlineData("۱۲۳۴۵۶۷۸۹۰")] // Persian digits: \d would wrongly accept these
    public async Task Create_Rejects_A_Postal_Code_That_Is_Not_Exactly_10_Ascii_Digits(string? postalCode)
    {
        var result = await _sut.CreateAddressAsync(_customer.IdentityUserId, Input(postalCode: postalCode));

        result.Error!.Code.Should().Be("INVALID_POSTAL_CODE");
        await _locations.DidNotReceiveWithAnyArgs().CreateAddressAsync(default, default, default, default, default, default, default, default);
    }

    [Fact]
    public async Task Create_Accepts_A_Valid_Postal_Code_Links_To_The_Customer_And_Is_Never_Primary()
    {
        var result = await _sut.CreateAddressAsync(_customer.IdentityUserId, Input());

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsPrimary.Should().BeFalse();
        await _locations.Received(1).CreateAddressAsync(CityId, DistrictInCity, "Street 1", "1234567890",
            "Ali Rezaei", "+989351112233", false, Arg.Any<CancellationToken>());
        _customer.Addresses.Should().Contain(a => a.AddressId == result.Value.Id && a.CustomerId == _customer.Id);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_Rejects_An_Unknown_City()
    {
        (await _sut.CreateAddressAsync(_customer.IdentityUserId, Input(cityId: 777, districtId: null)))
            .Error!.Code.Should().Be("INVALID_CITY");
    }

    [Fact]
    public async Task Create_Rejects_A_District_That_Belongs_To_Another_City()
    {
        (await _sut.CreateAddressAsync(_customer.IdentityUserId, Input(districtId: DistrictElsewhere)))
            .Error!.Code.Should().Be("INVALID_DISTRICT");
    }

    [Fact]
    public async Task Self_Recipient_Uses_The_Profile_And_Ignores_Client_Values_Without_Touching_The_Profile()
    {
        var profileUpdatedAt = _customer.UpdatedAt;

        var result = await _sut.CreateAddressAsync(_customer.IdentityUserId,
            Input(self: true, name: "Hacker", mobile: "09390000000"));

        result.IsSuccess.Should().BeTrue();
        await _locations.Received(1).CreateAddressAsync(CityId, DistrictInCity, "Street 1", "1234567890",
            "Sara Ahmadi", "+989121234567", false, Arg.Any<CancellationToken>());
        _customer.FirstName.Should().Be("Sara");
        _customer.MobileNumber.Should().Be("+989121234567");
        _customer.UpdatedAt.Should().Be(profileUpdatedAt);
    }

    [Theory]
    [InlineData(null, "09351112233", "RECIPIENT_NAME_REQUIRED")]
    [InlineData("  ", "09351112233", "RECIPIENT_NAME_REQUIRED")]
    [InlineData("Ali", null, "MOBILENUMBER_REQUIRED")]
    [InlineData("Ali", "12345", "MOBILENUMBER_CHECKSUM_IS_INVALID")]
    public async Task Different_Recipient_Requires_A_Name_And_A_Valid_Mobile(string? name, string? mobile, string expected)
    {
        (await _sut.CreateAddressAsync(_customer.IdentityUserId, Input(name: name, mobile: mobile)))
            .Error!.Code.Should().Be(expected);
    }

    [Fact]
    public async Task Another_Customers_Address_Is_Not_Found_For_Every_Operation_And_Location_Is_Never_Touched()
    {
        var foreignId = Ids.Generate();
        var me = _customer.IdentityUserId;

        (await _sut.GetAddressAsync(me, foreignId)).Error!.Code.Should().Be("ADDRESS_NOT_FOUND");
        (await _sut.UpdateAddressAsync(me, foreignId, Input())).Error!.Code.Should().Be("ADDRESS_NOT_FOUND");
        (await _sut.DeleteAddressAsync(me, foreignId)).Error!.Code.Should().Be("ADDRESS_NOT_FOUND");
        (await _sut.SetDefaultAddressAsync(me, foreignId)).Error!.Code.Should().Be("ADDRESS_NOT_FOUND");

        await _locations.DidNotReceiveWithAnyArgs().GetAddressesAsync(default!, default);
        await _locations.DidNotReceiveWithAnyArgs().UpdateAddressAsync(default, default, default, default, default, default, default, default);
        await _locations.DidNotReceiveWithAnyArgs().DeleteAddressAsync(default, default);
        await _locations.DidNotReceiveWithAnyArgs().SetPrimaryAddressAsync(default, default!, default);
    }

    [Fact]
    public async Task List_Asks_Location_Only_For_The_Customers_Own_Address_Ids()
    {
        _locations.GetAddressesAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns([Response(_ownedAddressId, isPrimary: true)]);

        var result = await _sut.GetAddressesAsync(_customer.IdentityUserId);

        result.Value!.Should().ContainSingle().Which.IsPrimary.Should().BeTrue();
        await _locations.Received(1).GetAddressesAsync(
            Arg.Is<IReadOnlyCollection<long>>(ids => ids.SequenceEqual(new[] { _ownedAddressId })), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Set_Default_Passes_The_Whole_Owned_Set_So_The_Previous_Primary_Can_Be_Demoted()
    {
        _locations.SetPrimaryAddressAsync(_ownedAddressId, Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Response(_ownedAddressId, isPrimary: true)));

        var result = await _sut.SetDefaultAddressAsync(_customer.IdentityUserId, _ownedAddressId);

        result.Value!.IsPrimary.Should().BeTrue();
        await _locations.Received(1).SetPrimaryAddressAsync(_ownedAddressId,
            Arg.Is<IReadOnlyCollection<long>>(ids => ids.SequenceEqual(new[] { _ownedAddressId })), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unknown_Customer_Is_Reported_As_Such()
    {
        (await _sut.GetAddressesAsync(Guid.NewGuid())).Error!.Code.Should().Be("CUSTOMER_NOT_FOUND");
    }
}
