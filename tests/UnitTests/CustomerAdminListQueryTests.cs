using System.Globalization;
using System.Reflection;
using AwesomeAssertions;
using EwaysVNext.Database;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Customer.Adapter.Outbound;
using EwaysVNext.Modules.Customer.Application.Domain.Enums;
using EwaysVNext.Modules.Customer.Application.Domain.ValueObjects;
using EwaysVNext.Modules.Customer.Application.Ports.Outbound;
using EwaysVNext.Modules.Customer.Adapter.Inbound.GraphQL.Customers;
using EwaysVNext.Modules.Location.Application.Domain.Locations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;
using CustomerAggregate = EwaysVNext.Modules.Customer.Application.Domain.Customers.Customer;

namespace EwaysVNext.UnitTests;

public class CustomerAdminListQueryTests
{
    private static EwaysVNextDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<EwaysVNextDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new EwaysVNextDbContext(options);
    }

    private static string ValidNationalCode(string first9Digits)
    {
        var sum = 0;
        for (var i = 0; i < 9; i++)
            sum += (first9Digits[i] - '0') * (10 - i);
        var remainder = sum % 11;
        var check = remainder < 2 ? remainder : 11 - remainder;
        return first9Digits + check;
    }

    private static readonly SnowflakeIdGenerator IdGenerator = new();

    private static CustomerAggregate NewCustomer(
        string mobile, string firstName, string lastName, string nationalCode,
        VerificationStatusType verificationStatus = VerificationStatusType.NotVerified)
    {
        var customer = CustomerAggregate.Register(IdGenerator.Generate(), Guid.NewGuid(), MobileNumber.Create(mobile), TestTime.Now);
        customer.RegisterCustomerInfo(NationalCode.Create(ValidNationalCode(nationalCode[..9])), firstName, lastName, Gender.Male, new DateOnly(1990, 1, 1), TestTime.Now);
        customer.UpdateVerificationStatus(verificationStatus, TestTime.Now);
        return customer;
    }

    private static void SetPrivateProperty(object target, string propertyName, object? value) =>
        target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(target, value);

    private static void SetCreatedAt(CustomerAggregate customer, DateTime createdAt) =>
        SetPrivateProperty(customer, nameof(CustomerAggregate.CreatedAt), createdAt);

    private static void SetRegistrationStatus(CustomerAggregate customer, CustomerStatusType status) =>
        SetPrivateProperty(customer, nameof(CustomerAggregate.StatusId), (int)status);

    private static ICustomerRepository CreateRepository(EwaysVNextDbContext db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddCustomerOutboundAdapters(new ConfigurationBuilder().Build());
        return services.BuildServiceProvider().GetRequiredService<ICustomerRepository>();
    }

    private static async Task<Address> SeedAddressAsync(EwaysVNextDbContext db, int cityId, int? districtId = null, bool isPrimary = true)
    {
        var address = Address.Create(IdGenerator.Generate(), cityId, TestTime.Now, districtId: districtId, isPrimary: isPrimary);
        db.Addresses.Add(address);
        await db.SaveChangesAsync();
        return address;
    }

    private static async Task LinkAddressAsync(EwaysVNextDbContext db, CustomerAggregate customer, Address address)
    {
        customer.AddAddress(Modules.Customer.Application.Domain.Customers.CustomerAddress.Create(IdGenerator.Generate(), customer.Id, address.Id));
        await db.SaveChangesAsync();
    }

    private static void SeedLocation(EwaysVNextDbContext db, int provinceId, int cityId, string? provinceName = null, string? cityName = null)
    {
        if (db.Provinces.Find(provinceId) is null)
        {
            var province = new Province();
            SetPrivateProperty(province, nameof(Province.Id), provinceId);
            SetPrivateProperty(province, nameof(Province.Name), provinceName ?? $"Province-{provinceId}");
            SetPrivateProperty(province, nameof(Province.IsActive), true);
            db.Provinces.Add(province);
        }

        db.Set<City>().Add(new City { Id = cityId, ProvinceId = provinceId, Name = cityName ?? $"City-{cityId}", IsActive = true });
        db.SaveChanges();
    }

    private static void SeedDistrict(EwaysVNextDbContext db, int districtId, int cityId, string name)
    {
        db.Set<District>().Add(new District { Id = districtId, CityId = cityId, Name = name, IsActive = true });
        db.SaveChanges();
    }

    [Fact]
    public async Task Search_Matches_By_Name()
    {
        await using var db = CreateDbContext();
        var repo = CreateRepository(db);
        db.Customers.AddRange(
            NewCustomer("09121111111", "Ali", "Rezaei", "0011111111"),
            NewCustomer("09122222222", "Sara", "Ahmadi", "0022222222"));
        await db.SaveChangesAsync();

        var result = await repo.QueryForAdminList(new CustomerAdminListFilter(Name: "Ali")).ToListAsync();

        result.Should().ContainSingle(c => c.FirstName == "Ali");
    }

    [Fact]
    public async Task Search_Matches_By_Family()
    {
        await using var db = CreateDbContext();
        var repo = CreateRepository(db);
        db.Customers.AddRange(
            NewCustomer("09121111111", "Ali", "Rezaei", "0011111111"),
            NewCustomer("09122222222", "Sara", "Ahmadi", "0022222222"));
        await db.SaveChangesAsync();

        var result = await repo.QueryForAdminList(new CustomerAdminListFilter(Family: "Ahmadi")).ToListAsync();

        result.Should().ContainSingle(c => c.LastName == "Ahmadi");
    }

    [Fact]
    public async Task Search_Matches_By_UserCode_Which_Is_The_Customer_Id()
    {
        await using var db = CreateDbContext();
        var repo = CreateRepository(db);
        var target = NewCustomer("09121111111", "Ali", "Rezaei", "0011111111");
        db.Customers.AddRange(target, NewCustomer("09122222222", "Sara", "Ahmadi", "0022222222"));
        await db.SaveChangesAsync();

        var result = await repo.QueryForAdminList(new CustomerAdminListFilter(UserCode: target.Id.ToString(CultureInfo.InvariantCulture))).ToListAsync();

        result.Should().ContainSingle(c => c.Id == target.Id);
    }

    [Fact]
    public async Task Search_Matches_By_NationalId()
    {
        await using var db = CreateDbContext();
        var repo = CreateRepository(db);
        var target = NewCustomer("09122222222", "Sara", "Ahmadi", "0022222222");
        db.Customers.AddRange(NewCustomer("09121111111", "Ali", "Rezaei", "0011111111"), target);
        await db.SaveChangesAsync();

        var result = await repo.QueryForAdminList(new CustomerAdminListFilter(NationalId: target.NationalCode)).ToListAsync();

        result.Should().ContainSingle(c => c.NationalCode == target.NationalCode);
    }

    [Fact]
    public async Task Search_Matches_By_PhoneNumber()
    {
        await using var db = CreateDbContext();
        var repo = CreateRepository(db);
        var target = NewCustomer("09122222222", "Sara", "Ahmadi", "0022222222");
        db.Customers.AddRange(NewCustomer("09121111111", "Ali", "Rezaei", "0011111111"), target);
        await db.SaveChangesAsync();

        var result = await repo.QueryForAdminList(new CustomerAdminListFilter(PhoneNumber: target.MobileNumber)).ToListAsync();

        result.Should().ContainSingle(c => c.MobileNumber == target.MobileNumber);
    }

    [Fact]
    public async Task Search_Fields_Combine_With_AND_Not_OR()
    {
        await using var db = CreateDbContext();
        var repo = CreateRepository(db);
        var match = NewCustomer("09121111111", "Ali", "Rezaei", "0011111111");
        var sameNameDifferentFamily = NewCustomer("09122222222", "Ali", "Ahmadi", "0022222222");
        db.Customers.AddRange(match, sameNameDifferentFamily);
        await db.SaveChangesAsync();

        var result = await repo.QueryForAdminList(new CustomerAdminListFilter(Name: "Ali", Family: "Rezaei")).ToListAsync();

        result.Select(c => c.Id.Value).Should().BeEquivalentTo([match.Id]);
    }

    [Fact]
    public async Task DateRange_Includes_The_Entire_ToDate_Day_And_Excludes_Outside_Records()
    {
        await using var db = CreateDbContext();
        var repo = CreateRepository(db);

        var inRange = NewCustomer("09121111111", "InRange", "Customer", "0011111111");
        var beforeRange = NewCustomer("09122222222", "Before", "Range", "0022222222");
        var afterRange = NewCustomer("09123333333", "After", "Range", "0033333333");
        db.Customers.AddRange(inRange, beforeRange, afterRange);
        await db.SaveChangesAsync();

        SetCreatedAt(inRange, new DateTime(2026, 9, 5, 23, 59, 0));
        SetCreatedAt(beforeRange, new DateTime(2026, 9, 1, 0, 0, 0));
        SetCreatedAt(afterRange, new DateTime(2026, 9, 6, 0, 0, 0));
        await db.SaveChangesAsync();

        var filter = new CustomerAdminListFilter(
            FromDate: new DateTime(2026, 9, 2),
            ToDate: new DateTime(2026, 9, 5)); // date-only ToDate must still include the whole day

        var result = await repo.QueryForAdminList(filter).ToListAsync();

        result.Select(c => c.Id.Value).Should().BeEquivalentTo([inRange.Id]);
    }

    [Fact]
    public async Task BirthDate_Is_Projected_Onto_The_Grid_Row()
    {
        await using var db = CreateDbContext();
        var repo = CreateRepository(db);
        var target = NewCustomer("09121111111", "Ali", "Rezaei", "0011111111");
        db.Customers.Add(target);
        await db.SaveChangesAsync();

        var result = await repo.QueryForAdminList(new CustomerAdminListFilter()).ToListAsync();

        result.Should().ContainSingle(c => c.Id == target.Id && c.BirthDate == new DateOnly(1990, 1, 1));
    }

    [Fact]
    public async Task Province_Filter_Returns_Only_Customers_With_A_Primary_Address_In_That_Province_And_Projects_Its_Name()
    {
        await using var db = CreateDbContext();
        var repo = CreateRepository(db);
        SeedLocation(db, provinceId: 1, cityId: 10, provinceName: "Tehran", cityName: "Tehran");
        SeedLocation(db, provinceId: 2, cityId: 20, provinceName: "Fars", cityName: "Shiraz");

        var inTehran = NewCustomer("09121111111", "Tehran", "Customer", "0011111111");
        var inOther = NewCustomer("09122222222", "Other", "Customer", "0022222222");
        db.Customers.AddRange(inTehran, inOther);
        await db.SaveChangesAsync();

        var tehranAddress = await SeedAddressAsync(db, cityId: 10);
        var otherAddress = await SeedAddressAsync(db, cityId: 20);
        await LinkAddressAsync(db, inTehran, tehranAddress);
        await LinkAddressAsync(db, inOther, otherAddress);

        var result = await repo.QueryForAdminList(new CustomerAdminListFilter(ProvinceId: 1)).ToListAsync();

        result.Should().ContainSingle(c => c.Id == inTehran.Id);
        result.Single().ProvinceId.Should().Be(1);
        result.Single().ProvinceName.Should().Be("Tehran");
        result.Single().CityName.Should().Be("Tehran");
    }

    [Fact]
    public async Task City_Filter_Returns_Only_Customers_With_A_Primary_Address_In_That_City()
    {
        await using var db = CreateDbContext();
        var repo = CreateRepository(db);
        SeedLocation(db, provinceId: 1, cityId: 10);
        SeedLocation(db, provinceId: 1, cityId: 11);

        var inCity10 = NewCustomer("09121111111", "CityTen", "Customer", "0011111111");
        var inCity11 = NewCustomer("09122222222", "CityEleven", "Customer", "0022222222");
        db.Customers.AddRange(inCity10, inCity11);
        await db.SaveChangesAsync();

        await LinkAddressAsync(db, inCity10, await SeedAddressAsync(db, cityId: 10));
        await LinkAddressAsync(db, inCity11, await SeedAddressAsync(db, cityId: 11));

        var result = await repo.QueryForAdminList(new CustomerAdminListFilter(CityId: 11)).ToListAsync();

        result.Should().ContainSingle(c => c.Id == inCity11.Id);
    }

    [Fact]
    public async Task Region_Filter_Returns_Only_Customers_With_A_Primary_Address_In_That_District_And_Projects_Its_Name()
    {
        await using var db = CreateDbContext();
        var repo = CreateRepository(db);
        SeedLocation(db, provinceId: 1, cityId: 10);
        SeedDistrict(db, districtId: 5, cityId: 10, name: "District Five");

        var inDistrict5 = NewCustomer("09121111111", "District", "Five", "0011111111");
        var noDistrict = NewCustomer("09122222222", "No", "District", "0022222222");
        db.Customers.AddRange(inDistrict5, noDistrict);
        await db.SaveChangesAsync();

        await LinkAddressAsync(db, inDistrict5, await SeedAddressAsync(db, cityId: 10, districtId: 5));
        await LinkAddressAsync(db, noDistrict, await SeedAddressAsync(db, cityId: 10, districtId: null));

        var result = await repo.QueryForAdminList(new CustomerAdminListFilter(DistrictId: 5)).ToListAsync();

        result.Should().ContainSingle(c => c.Id == inDistrict5.Id);
        result.Single().DistrictId.Should().Be(5);
        result.Single().DistrictName.Should().Be("District Five");
    }

    [Fact]
    public async Task RegistrationStatus_Filters_By_The_Persisted_CustomerStatus_Not_VerificationStatus()
    {
        await using var db = CreateDbContext();
        var repo = CreateRepository(db);
        var approvedButUnverified = NewCustomer("09121111111", "Approved", "One", "0011111111", VerificationStatusType.NotVerified);
        var rejectedButVerified = NewCustomer("09122222222", "Rejected", "Two", "0022222222", VerificationStatusType.Verified);
        var pending = NewCustomer("09123333333", "Pending", "Three", "0033333333", VerificationStatusType.Verified);
        db.Customers.AddRange(approvedButUnverified, rejectedButVerified, pending);
        await db.SaveChangesAsync();

        SetRegistrationStatus(approvedButUnverified, CustomerStatusType.Approved);
        SetRegistrationStatus(rejectedButVerified, CustomerStatusType.Rejected);
        await db.SaveChangesAsync();

        var approvedResult = await repo.QueryForAdminList(new CustomerAdminListFilter(
            RegistrationStatuses: [CustomerStatusType.Approved])).ToListAsync();
        var rejectedResult = await repo.QueryForAdminList(new CustomerAdminListFilter(
            RegistrationStatuses: [CustomerStatusType.Rejected])).ToListAsync();
        var pendingResult = await repo.QueryForAdminList(new CustomerAdminListFilter(
            RegistrationStatuses: [CustomerStatusType.PendingApproval])).ToListAsync();

        approvedResult.Select(c => c.Id.Value).Should().BeEquivalentTo([approvedButUnverified.Id]);
        rejectedResult.Select(c => c.Id.Value).Should().BeEquivalentTo([rejectedButVerified.Id]);
        pendingResult.Select(c => c.Id.Value).Should().BeEquivalentTo([pending.Id]);
    }

    [Fact]
    public async Task New_Registrations_Default_To_PendingApproval_Regardless_Of_VerificationStatus()
    {
        await using var db = CreateDbContext();
        var repo = CreateRepository(db);
        var customer = NewCustomer("09121111111", "Ali", "Rezaei", "0011111111", VerificationStatusType.Verified);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var result = await repo.QueryForAdminList(new CustomerAdminListFilter(
            RegistrationStatuses: [CustomerStatusType.PendingApproval])).ToListAsync();

        result.Should().ContainSingle(c => c.Id == customer.Id);
    }

    [Fact]
    public async Task Multiple_Filters_Combine_With_AND_Semantics()
    {
        await using var db = CreateDbContext();
        var repo = CreateRepository(db);
        SeedLocation(db, provinceId: 1, cityId: 10);
        SeedLocation(db, provinceId: 2, cityId: 20);

        var match = NewCustomer("09121111111", "Ali", "Rezaei", "0011111111");
        var wrongProvince = NewCustomer("09122222222", "Ali", "Rezaei", "0022222222");
        var wrongStatus = NewCustomer("09123333333", "Ali", "Rezaei", "0033333333");
        db.Customers.AddRange(match, wrongProvince, wrongStatus);
        await db.SaveChangesAsync();

        SetCreatedAt(match, new DateTime(2026, 9, 5));
        SetCreatedAt(wrongProvince, new DateTime(2026, 9, 5));
        SetCreatedAt(wrongStatus, new DateTime(2026, 9, 5));
        SetRegistrationStatus(wrongStatus, CustomerStatusType.Approved);
        await db.SaveChangesAsync();

        await LinkAddressAsync(db, match, await SeedAddressAsync(db, cityId: 10));
        await LinkAddressAsync(db, wrongProvince, await SeedAddressAsync(db, cityId: 20));
        await LinkAddressAsync(db, wrongStatus, await SeedAddressAsync(db, cityId: 10));

        var filter = new CustomerAdminListFilter(
            Name: "Ali",
            FromDate: new DateTime(2026, 9, 1),
            ToDate: new DateTime(2026, 9, 9),
            ProvinceId: 1,
            RegistrationStatuses: [CustomerStatusType.PendingApproval]);

        var result = await repo.QueryForAdminList(filter).ToListAsync();

        result.Select(c => c.Id.Value).Should().BeEquivalentTo([match.Id]);
    }

    [Fact]
    public async Task Pagination_Applies_Skip_Take_On_A_Deterministic_Order_Without_Loading_Everything_First()
    {
        await using var db = CreateDbContext();
        var repo = CreateRepository(db);
        for (var i = 0; i < 5; i++)
        {
            var c = NewCustomer($"0912000{i:D4}", $"Customer{i}", "Test", $"00{i:D8}");
            db.Customers.Add(c);
        }
        await db.SaveChangesAsync();

        var queries = new CustomerQueries();
        var ordered = queries.Customers(new CustomerAdminListFilter(FromDate: DateTime.MinValue, ToDate: new DateTime(9998, 12, 31)), repo, new FixedClock());

        var page1 = await ordered.Skip(0).Take(2).ToListAsync();
        var page2 = await ordered.Skip(2).Take(2).ToListAsync();

        page1.Should().HaveCount(2);
        page2.Should().HaveCount(2);
        page1.Select(c => c.Id).Should().NotIntersectWith(page2.Select(c => c.Id));
    }

    [Fact]
    public async Task Empty_Result_Returns_An_Empty_List_Without_Throwing()
    {
        await using var db = CreateDbContext();
        var repo = CreateRepository(db);
        db.Customers.Add(NewCustomer("09121111111", "Ali", "Rezaei", "0011111111"));
        await db.SaveChangesAsync();

        var act = () => repo.QueryForAdminList(new CustomerAdminListFilter(Name: "NoSuchCustomerXyz")).ToListAsync();

        (await act.Should().NotThrowAsync()).Which.Should().BeEmpty();
    }

    [Fact]
    public void Default_View_Filters_To_Today_And_Applies_No_Status_Filter()
    {
        var repository = Substitute.For<ICustomerRepository>();
        repository.QueryForAdminList(Arg.Any<CustomerAdminListFilter>())
            .Returns(Enumerable.Empty<CustomerAdminListItem>().AsQueryable());
        var queries = new CustomerQueries();

        _ = queries.Customers(new CustomerAdminListFilter(), repository, new FixedClock()).ToList();

        repository.Received(1).QueryForAdminList(Arg.Is<CustomerAdminListFilter>(f =>
            f.FromDate == TestTime.Now.UtcDateTime.Date &&
            f.ToDate == TestTime.Now.UtcDateTime.Date &&
            f.RegistrationStatuses == null));
    }

    [Fact]
    public void Omitting_The_Filter_Argument_Defaults_To_Today()
    {
        var repository = Substitute.For<ICustomerRepository>();
        repository.QueryForAdminList(Arg.Any<CustomerAdminListFilter>())
            .Returns(Enumerable.Empty<CustomerAdminListItem>().AsQueryable());
        var queries = new CustomerQueries();

        _ = queries.Customers(null, repository, new FixedClock()).ToList();

        repository.Received(1).QueryForAdminList(Arg.Is<CustomerAdminListFilter>(f =>
            f.FromDate == TestTime.Now.UtcDateTime.Date &&
            f.ToDate == TestTime.Now.UtcDateTime.Date));
    }

    [Fact]
    public void Defaulting_The_Dates_Keeps_Every_Other_Filter_The_Caller_Sent()
    {
        var repository = Substitute.For<ICustomerRepository>();
        repository.QueryForAdminList(Arg.Any<CustomerAdminListFilter>())
            .Returns(Enumerable.Empty<CustomerAdminListItem>().AsQueryable());
        var queries = new CustomerQueries();

        _ = queries.Customers(
            new CustomerAdminListFilter(Name: "Ali", CityId: 7, RegistrationStatuses: [CustomerStatusType.Active]),
            repository,
            new FixedClock()).ToList();

        repository.Received(1).QueryForAdminList(Arg.Is<CustomerAdminListFilter>(f =>
            f.Name == "Ali" &&
            f.CityId == 7 &&
            f.RegistrationStatuses != null && f.RegistrationStatuses.Single() == CustomerStatusType.Active &&
            f.FromDate == TestTime.Now.UtcDateTime.Date &&
            f.ToDate == TestTime.Now.UtcDateTime.Date));
    }

    [Fact]
    public void An_Explicit_Date_Range_Is_Left_Alone()
    {
        var repository = Substitute.For<ICustomerRepository>();
        repository.QueryForAdminList(Arg.Any<CustomerAdminListFilter>())
            .Returns(Enumerable.Empty<CustomerAdminListItem>().AsQueryable());
        var queries = new CustomerQueries();
        var from = new DateTime(2026, 1, 1);

        _ = queries.Customers(new CustomerAdminListFilter(FromDate: from), repository, new FixedClock()).ToList();

        repository.Received(1).QueryForAdminList(Arg.Is<CustomerAdminListFilter>(f =>
            f.FromDate == from && f.ToDate == null));
    }

    [Fact]
    public void Clearing_All_Filters_Produces_The_Same_Default_Filter_As_The_First_Page_Load()
    {
        var repository = Substitute.For<ICustomerRepository>();
        repository.QueryForAdminList(Arg.Any<CustomerAdminListFilter>())
            .Returns(Enumerable.Empty<CustomerAdminListItem>().AsQueryable());
        var queries = new CustomerQueries();
        var clock = new FixedClock();

        _ = queries.Customers(new CustomerAdminListFilter(Name: "stale", FromDate: new DateTime(2026, 1, 1)), repository, clock).ToList();
        _ = queries.Customers(new CustomerAdminListFilter(), repository, clock).ToList();

        Received.InOrder(() =>
        {
            repository.QueryForAdminList(Arg.Is<CustomerAdminListFilter>(f => f.Name == "stale"));
            repository.QueryForAdminList(Arg.Is<CustomerAdminListFilter>(f =>
                f.Name == null &&
                f.FromDate == TestTime.Now.UtcDateTime.Date &&
                f.ToDate == TestTime.Now.UtcDateTime.Date &&
                f.RegistrationStatuses == null));
        });
    }

    [Fact]
    public async Task Legacy_Account_Activity_Statuses_Are_Valid_RegistrationStatuses_Filter_Values_Too()
    {
        await using var db = CreateDbContext();
        var repo = CreateRepository(db);
        var active = NewCustomer("09121111111", "Active", "One", "0011111111");
        var pending = NewCustomer("09122222222", "Pending", "Two", "0022222222");
        db.Customers.AddRange(active, pending);
        await db.SaveChangesAsync();

        SetRegistrationStatus(active, CustomerStatusType.Active);
        await db.SaveChangesAsync();

        var result = await repo.QueryForAdminList(new CustomerAdminListFilter(
            RegistrationStatuses: [CustomerStatusType.Active])).ToListAsync();

        result.Select(c => c.Id.Value).Should().BeEquivalentTo([active.Id]);
    }

    [Fact]
    public async Task AdminDetails_Returns_Contact_Info_Job_Types_And_Primary_Location()
    {
        await using var db = CreateDbContext();
        var repo = CreateRepository(db);
        SeedLocation(db, provinceId: 1, cityId: 10, provinceName: "Tehran", cityName: "Tehran City");
        SeedDistrict(db, districtId: 100, cityId: 10, name: "District 1");
        db.Set<EwaysVNext.Modules.Customer.Application.Domain.Customers.JobType>().AddRange(
            EwaysVNext.Modules.Customer.Application.Domain.Customers.JobType.Create(901, "Cafe Net"),
            EwaysVNext.Modules.Customer.Application.Domain.Customers.JobType.Create(902, "Pishkhan"));
        var customer = NewCustomer("09121111111", "Ali", "Rezaei", "0011111111");
        customer.UpdateStatus(CustomerStatusType.Active, TestTime.Now);
        customer.UpdateByAdmin(Gender.Male, null, "INTA-1", null,
            EwaysVNext.Domain.ValueObjects.Email.Create("ali@example.com"), PhoneNumber.Create("02188881234"),
            [(IdGenerator.Generate(), 902)], TestTime.Now);
        db.Customers.AddRange(customer, NewCustomer("09122222222", "Sara", "Ahmadi", "0022222222"));
        await db.SaveChangesAsync();
        await LinkAddressAsync(db, customer, await SeedAddressAsync(db, cityId: 10, districtId: 100));

        var details = await repo.GetAdminDetailsAsync(customer.Id);

        details.Should().NotBeNull();
        details!.Id.Should().Be(customer.Id);
        details.Email.Should().Be("ali@example.com");
        details.SecondaryPhoneNumber.Should().Be("+982188881234");
        details.IntaCode.Should().Be("INTA-1");
        details.Gender.Should().Be(Gender.Male);
        details.JobTypes.Should().BeEquivalentTo([new JobTypeItem(902, "Pishkhan")]);
        details.ProvinceName.Should().Be("Tehran");
        details.CityName.Should().Be("Tehran City");
        details.DistrictName.Should().Be("District 1");
    }

    [Fact]
    public async Task AdminDetails_Returns_Null_For_An_Unknown_Customer()
    {
        await using var db = CreateDbContext();
        var repo = CreateRepository(db);

        (await repo.GetAdminDetailsAsync(42)).Should().BeNull();
    }
}
