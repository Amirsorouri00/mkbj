using AwesomeAssertions;
using EwaysVNext.Database;
using EwaysVNext.Domain.Common;
using EwaysVNext.Domain.ValueObjects;
using EwaysVNext.Modules.Customer.Adapter.Inbound.GraphQL;
using EwaysVNext.Modules.Customer.Application.Domain.Enums;
using EwaysVNext.Modules.Customer.Application.Domain.ValueObjects;
using EwaysVNext.Modules.Customer.Application.Ports.Inbound.Dtos;
using EwaysVNext.Modules.Customer.Application.Ports.Outbound;
using EwaysVNext.Modules.Location.Contracts.Locations;
using EwaysVNext.Outcomes;
using EwaysVNext.Modules.Customer.Application.Service;
using EwaysVNext.Modules.Customer.Application.Service.Shahkar.Abstractions;
using EwaysVNext.Modules.Location.Contracts.Abstractions;
using EwaysVNext.Persistence;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using HotChocolate.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using CustomerAggregate = EwaysVNext.Modules.Customer.Application.Domain.Customers.Customer;

namespace EwaysVNext.UnitTests;

/// <summary>
/// Admin edit of a customer (updateCustomer mutation): gender, economic/INTA/role codes, email,
/// second contact number, job types (CustomerJobType join table) and the primary address. Name,
/// family, birth date, national code and the username (mobile number) are not editable.
/// </summary>
public class CustomerAdminUpdateTests
{
    private static readonly SnowflakeIdGenerator IdGenerator = new();
    private static readonly DateOnly BirthDate = new(1990, 1, 1);

    /// <summary>
    /// A freshly registered customer (registration status PendingApproval, i.e. awaiting the
    /// expert's call, so not editable by an admin).
    /// </summary>
    private static CustomerAggregate NewPendingCustomer() =>
        CustomerAggregate.Register(IdGenerator.Generate(), Guid.NewGuid(), MobileNumber.Create("09123456789"), TestTime.Now);

    /// <summary>
    /// A customer whose registration has left PendingApproval, so an admin may edit it.
    /// </summary>
    private static CustomerAggregate NewCustomer()
    {
        var customer = NewPendingCustomer();
        customer.UpdateStatus(CustomerStatusType.Active, TestTime.Now);
        return customer;
    }

    private static (long, int)[] JobTypes(params int[] ids) =>
        ids.Select(id => (IdGenerator.Generate(), id)).ToArray();

    private static void Update(CustomerAggregate c, string? intaCode, string? roleCode, params int[] jobTypeIds) =>
        Update(c, intaCode, roleCode, null, null, jobTypeIds);

    private static void Update(CustomerAggregate c, string? intaCode, string? roleCode,
        Email? email, PhoneNumber? secondaryPhoneNumber, params int[] jobTypeIds) =>
        c.UpdateByAdmin(Gender.Male, "1234567890", intaCode, roleCode, email, secondaryPhoneNumber,
            JobTypes(jobTypeIds), TestTime.Now);

    [Fact]
    public void UpdateByAdmin_Sets_The_Editable_Fields_And_Leaves_The_Registration_Identity_Alone()
    {
        var c = NewCustomer();
        c.RegisterCustomerInfo(NationalCode.Create("0012345679"), "Ali", "Rezaei", Gender.Female, BirthDate, TestTime.Now);
        var mobileNumber = c.MobileNumber;

        Update(c, " INTA-1 ", "R1", 1, 4);

        c.FirstName.Should().Be("Ali");
        c.LastName.Should().Be("Rezaei");
        c.NationalCode.Should().Be("0012345679");
        c.BirthDate.Should().Be(BirthDate);
        c.Gender.Should().Be(Gender.Male);
        c.EconomicCode.Should().Be("1234567890");
        c.IntaCode.Should().Be("INTA-1");
        c.RoleCode.Should().Be("R1");
        c.MobileNumber.Should().Be(mobileNumber);
        c.JobTypes.Select(j => j.JobTypeId).Should().BeEquivalentTo([1, 4]);
        c.JobTypes.Should().OnlyContain(j => j.CustomerId == c.Id);
    }

    [Fact]
    public void UpdateByAdmin_Replaces_Job_Types_And_Keeps_Rows_That_Are_Still_Selected()
    {
        var c = NewCustomer();
        Update(c, null, null, 1, 4);
        var keptRowId = c.JobTypes.Single(j => j.JobTypeId == 4).Id;

        Update(c, null, null, 4, 9, 9);

        c.JobTypes.Select(j => j.JobTypeId).Should().BeEquivalentTo([4, 9]);
        c.JobTypes.Single(j => j.JobTypeId == 4).Id.Should().Be(keptRowId);
    }

    [Fact]
    public void UpdateByAdmin_Clears_Blank_Optional_Codes()
    {
        var c = NewCustomer();
        Update(c, "INTA-1", "R1");

        Update(c, "  ", null);

        c.IntaCode.Should().BeNull();
        c.RoleCode.Should().BeNull();
    }

    [Fact]
    public void UpdateByAdmin_Rejects_An_IntaCode_Longer_Than_20_Characters()
    {
        var c = NewCustomer();
        var act = () => Update(c, new string('1', 21), null);
        act.Should().Throw<DomainException>().Where(e => e.Code == "INTACODE_TOO_LONG");
    }

    [Fact]
    public void UpdateByAdmin_Rejects_An_Undefined_Gender()
    {
        var c = NewCustomer();
        var act = () => c.UpdateByAdmin((Gender)99, null, null, null, null, null, [], TestTime.Now);
        act.Should().Throw<DomainException>().Where(e => e.Code == "INVALID_GENDER");
    }

    [Fact]
    public void UpdateByAdmin_Rejects_A_RoleCode_Longer_Than_20_Characters()
    {
        var c = NewCustomer();
        var act = () => Update(c, null, new string('1', 21));
        act.Should().Throw<DomainException>().Where(e => e.Code == "ROLECODE_TOO_LONG");
    }

    [Fact]
    public void UpdateByAdmin_Sets_Email_And_Secondary_Phone_Number_And_Blank_Clears_Them()
    {
        var c = NewCustomer();

        Update(c, null, null, Email.Create(" John.Doe@Example.com "), PhoneNumber.Create("021-8888 1234"));

        c.Email.Should().Be("john.doe@example.com");
        c.SecondaryPhoneNumber.Should().Be("+982188881234");

        Update(c, null, null, null, null);

        c.Email.Should().BeNull();
        c.SecondaryPhoneNumber.Should().BeNull();
    }

    [Theory]
    [InlineData("09121112233", "+989121112233")]
    [InlineData("+989121112233", "+989121112233")]
    [InlineData("00989121112233", "+989121112233")]
    [InlineData("۰۲۱۸۸۸۸۱۲۳۴", "+982188881234")]
    [InlineData("(031) 3222-1234", "+983132221234")]
    public void PhoneNumber_Accepts_Mobile_And_Landline_Numbers(string input, string expected) =>
        PhoneNumber.Create(input).Value.Should().Be(expected);

    [Theory]
    [InlineData("12345")]
    [InlineData("0212345")]
    [InlineData("002188881234567")]
    [InlineData("00021888812345")]
    [InlineData("abc")]
    public void PhoneNumber_Rejects_Malformed_Numbers(string input)
    {
        var act = () => PhoneNumber.Create(input);
        act.Should().Throw<DomainException>().Where(e => e.Code == "PHONENUMBER_INVALID");
    }

    [Fact]
    public void UpdateByAdmin_Rejects_A_Secondary_Phone_Number_Equal_To_The_Mobile_Number()
    {
        var c = NewCustomer();
        var act = () => Update(c, null, null, null, PhoneNumber.Create("09123456789"));
        act.Should().Throw<DomainException>().Where(e => e.Code == "SECONDARY_PHONENUMBER_SAME_AS_MOBILE");
    }

    [Fact]
    public void UpdateByAdmin_Is_Refused_While_Awaiting_The_Expert_Call()
    {
        var c = NewPendingCustomer();

        var act = () => Update(c, "INTA-1", null);

        act.Should().Throw<DomainException>().Where(e => e.Code == "CUSTOMER_EDIT_NOT_ALLOWED_WHILE_AWAITING_EXPERT_CALL");
        c.IntaCode.Should().BeNull();
    }

    [Theory]
    [InlineData(CustomerStatusType.Active, true)]
    [InlineData(CustomerStatusType.Inactive, true)]
    [InlineData(CustomerStatusType.Suspended, true)]
    [InlineData(CustomerStatusType.PendingApproval, false)]
    [InlineData(CustomerStatusType.Approved, true)]
    [InlineData(CustomerStatusType.Rejected, true)]
    public void CanBeEditedByAdmin_Is_False_Only_While_Registration_Is_Pending_Approval(CustomerStatusType status, bool expected) =>
        CustomerAggregate.CanBeEditedByAdmin((int)status).Should().Be(expected);

    [Fact]
    public void UpdateByAdmin_Is_Allowed_Again_Once_The_Registration_Status_Is_Set()
    {
        var c = NewPendingCustomer();
        c.UpdateStatus(CustomerStatusType.Rejected, TestTime.Now);

        Update(c, "INTA-1", null);

        c.IntaCode.Should().Be("INTA-1");
    }

    private static CustomerService Service(ICustomerRepository repository, IUnitOfWork unitOfWork, ILocationService? locations = null) =>
        new(repository,
            Substitute.For<ITeamMemberRepository>(),
            unitOfWork,
            Substitute.For<IShahkarService>(),
            Substitute.For<ILogger<CustomerService>>(),
            locations ?? Substitute.For<ILocationService>(),
            new FixedClock(),
            IdGenerator);

    private static AdminCustomerUpdate Command(
        string? email = null, string? secondaryPhoneNumber = null, int[]? jobTypeIds = null,
        AdminCustomerAddressUpdate? address = null) =>
        new(Gender.Male, null, null, null, email, secondaryPhoneNumber, jobTypeIds ?? [], address);

    private const int TehranProvinceId = 1;
    private const int TehranCityId = 10;
    private const int DistrictId = 100;

    /// <summary>Location stub knowing one city (10, in province 1, with district 100).</summary>
    private static ILocationService LocationsWithTehran()
    {
        var locations = Substitute.For<ILocationService>();
        locations.GetCityById(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<CitiesResponse>(Error.NotFound));
        locations.GetCityById(TehranCityId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new CitiesResponse(TehranCityId, "Tehran", [new DistrictResponse(DistrictId, "D1")], TehranProvinceId)));
        return locations;
    }

    private static AddressResponse AddressResponse(long id, bool isPrimary) =>
        new(id, TehranProvinceId, TehranCityId, null, null, null, null, null, isPrimary, 1, TestTime.Now.UtcDateTime, null);

    [Fact]
    public async Task UpdateCustomerByAdminAsync_Unknown_Job_Type_Fails_Without_Saving()
    {
        var repository = Substitute.For<ICustomerRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var customer = NewCustomer();
        repository.GetForAdminUpdateAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);
        repository.GetExistingJobTypeIdsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns([1]);

        var result = await Service(repository, unitOfWork).UpdateCustomerByAdminAsync(
            customer.Id, Command(jobTypeIds: [1, 999]));

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("JOBTYPE_NOT_FOUND");
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("not-an-email", null, "EMAIL_INVALID")]
    [InlineData(null, "123", "PHONENUMBER_INVALID")]
    public async Task UpdateCustomerByAdminAsync_Invalid_Contact_Info_Returns_A_Failure_Without_Saving(
        string? email, string? secondaryPhoneNumber, string expectedCode)
    {
        var repository = Substitute.For<ICustomerRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var customer = NewCustomer();
        repository.GetForAdminUpdateAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);

        var result = await Service(repository, unitOfWork).UpdateCustomerByAdminAsync(
            customer.Id, Command(email, secondaryPhoneNumber));

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(expectedCode);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateCustomerByAdminAsync_Is_Refused_While_Awaiting_The_Expert_Call()
    {
        var repository = Substitute.For<ICustomerRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var customer = NewPendingCustomer();
        repository.GetForAdminUpdateAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);

        var result = await Service(repository, unitOfWork).UpdateCustomerByAdminAsync(customer.Id, Command());

        result.Error!.Code.Should().Be("CUSTOMER_EDIT_NOT_ALLOWED_WHILE_AWAITING_EXPERT_CALL");
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateCustomerByAdminAsync_Unknown_Customer_Fails()
    {
        var result = await Service(Substitute.For<ICustomerRepository>(), Substitute.For<IUnitOfWork>())
            .UpdateCustomerByAdminAsync(42, Command());

        result.Error!.Code.Should().Be("CUSTOMER_NOT_FOUND");
    }

    [Theory]
    [InlineData(2, TehranCityId, null, "1234567890", "CITY_NOT_IN_PROVINCE")]
    [InlineData(TehranProvinceId, 999, null, "1234567890", "INVALID_CITY")]
    [InlineData(TehranProvinceId, TehranCityId, 555, "1234567890", "INVALID_DISTRICT")]
    [InlineData(TehranProvinceId, TehranCityId, null, "12345", "INVALID_POSTAL_CODE")]
    public async Task UpdateCustomerByAdminAsync_Invalid_Address_Fails_Before_Touching_Anything(
        int provinceId, int cityId, int? districtId, string postalCode, string expectedCode)
    {
        var repository = Substitute.For<ICustomerRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var locations = LocationsWithTehran();
        var customer = NewCustomer();
        repository.GetForAdminUpdateAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);

        var result = await Service(repository, unitOfWork, locations).UpdateCustomerByAdminAsync(
            customer.Id, Command(email: "ali@example.com",
                address: new AdminCustomerAddressUpdate(provinceId, cityId, districtId, "Street 1", postalCode)));

        result.Error!.Code.Should().Be(expectedCode);
        customer.Email.Should().BeNull();
        await locations.DidNotReceiveWithAnyArgs().CorrectAddressLocationAsync(default, default, default, default, default, default);
        await locations.DidNotReceiveWithAnyArgs().CreateAddressAsync(default);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateCustomerByAdminAsync_Corrects_The_Existing_Primary_Address()
    {
        var repository = Substitute.For<ICustomerRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var locations = LocationsWithTehran();
        var customer = NewCustomer();
        customer.AddAddress(EwaysVNext.Modules.Customer.Application.Domain.Customers.CustomerAddress.Create(IdGenerator.Generate(), customer.Id, 7));
        repository.GetForAdminUpdateAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);
        locations.GetPrimaryAddressAsync(Arg.Any<List<long>>(), Arg.Any<CancellationToken>()).Returns(AddressResponse(7, isPrimary: true));
        locations.CorrectAddressLocationAsync(default, default, default, default, default, default)
            .ReturnsForAnyArgs(Result.Success(AddressResponse(7, isPrimary: true)));

        var result = await Service(repository, unitOfWork, locations).UpdateCustomerByAdminAsync(
            customer.Id, Command(address: new AdminCustomerAddressUpdate(TehranProvinceId, TehranCityId, DistrictId, "  Street 1  ", " 1234567890 ")));

        result.IsSuccess.Should().BeTrue();
        await locations.Received(1).CorrectAddressLocationAsync(7, TehranCityId, DistrictId, "Street 1", "1234567890", Arg.Any<CancellationToken>());
        await locations.DidNotReceiveWithAnyArgs().CreateAddressAsync(default);
        customer.Addresses.Should().ContainSingle();
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateCustomerByAdminAsync_Creates_A_Primary_Address_When_The_Customer_Has_None()
    {
        var repository = Substitute.For<ICustomerRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var locations = LocationsWithTehran();
        var customer = NewCustomer();
        repository.GetForAdminUpdateAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);
        locations.CreateAddressAsync(default).ReturnsForAnyArgs(Result.Success(AddressResponse(8, isPrimary: true)));

        var result = await Service(repository, unitOfWork, locations).UpdateCustomerByAdminAsync(
            customer.Id, Command(address: new AdminCustomerAddressUpdate(TehranProvinceId, TehranCityId, null, "Street 1", null)));

        result.IsSuccess.Should().BeTrue();
        await locations.Received(1).CreateAddressAsync(TehranCityId, null, "Street 1", null, null, null, true, Arg.Any<CancellationToken>());
        customer.Addresses.Should().ContainSingle(a => a.AddressId == 8);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Primary_Address_Refuses_A_Recipient_Update_But_Accepts_An_Admin_Location_Correction()
    {
        var address = EwaysVNext.Modules.Location.Application.Domain.Locations.Address.Create(
            IdGenerator.Generate(), TehranCityId, TestTime.Now, addressText: "Old", recipientName: "Ali", isPrimary: true);

        var update = () => address.Update(20, null, "New", "1234567890", "Ali", null, TestTime.Now);
        update.Should().Throw<DomainException>().Where(e => e.Code == "PRIMARY_ADDRESS_CANNOT_BE_MODIFIED");

        address.CorrectLocation(20, 200, "New", "1234567890", TestTime.Now);

        address.CityId.Should().Be(20);
        address.DistrictId.Should().Be(200);
        address.AddressText.Should().Be("New");
        address.PostalCode.Should().Be("1234567890");
        address.RecipientName.Should().Be("Ali");
        address.IsPrimary.Should().BeTrue();
    }

    /// <summary>
    /// Newly added CustomerJobType rows carry client-generated (Snowflake) keys; they must be
    /// inserted, not treated as existing rows, and removed ones must be deleted.
    /// </summary>
    [Fact]
    public async Task Job_Type_Changes_Round_Trip_Through_EF()
    {
        var options = new DbContextOptionsBuilder<EwaysVNextDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var customer = NewCustomer();

        await using (var db = new EwaysVNextDbContext(options))
        {
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
        }

        await using (var db = new EwaysVNextDbContext(options))
        {
            var loaded = await db.Customers.Include(c => c.JobTypes).SingleAsync(c => c.Id == customer.Id);
            Update(loaded, "INTA-1", "R1", 1, 4);
            await db.SaveChangesAsync();
        }

        await using (var db = new EwaysVNextDbContext(options))
        {
            var loaded = await db.Customers.Include(c => c.JobTypes).SingleAsync(c => c.Id == customer.Id);
            loaded.JobTypes.Select(j => j.JobTypeId).Should().BeEquivalentTo([1, 4]);
            loaded.IntaCode.Should().Be("INTA-1");
            Update(loaded, null, null, 4);
            await db.SaveChangesAsync();
        }

        await using (var db = new EwaysVNextDbContext(options))
        {
            var loaded = await db.Customers.Include(c => c.JobTypes).SingleAsync(c => c.Id == customer.Id);
            loaded.JobTypes.Select(j => j.JobTypeId).Should().BeEquivalentTo([4]);
            (await db.Set<EwaysVNext.Modules.Customer.Application.Domain.Customers.CustomerJobType>().CountAsync())
                .Should().Be(1);
        }
    }

    [Fact]
    public async Task UpdateCustomer_Mutation_And_JobTypes_Query_Are_Discoverable_Without_A_Username_Field()
    {
        var services = new ServiceCollection();
        services.AddCustomerGraphQL();
        var sdl = (await services.AddGraphQLBootstrap().BuildSchemaAsync()).ToString();

        sdl.Should().Contain("updateCustomer(input: UpdateCustomerInput!): UpdateCustomerPayload!");
        sdl.Should().Contain("jobTypes: [JobTypeItem!]!");

        var input = sdl[sdl.IndexOf("input UpdateCustomerInput", StringComparison.Ordinal)..];
        input = input[..(input.IndexOf('}') + 1)];
        input.Should().Contain("intaCode: String");
        input.Should().Contain("roleCode: String");
        input.Should().Contain("jobTypeIds: [Int!]");
        input.Should().Contain("email: String");
        input.Should().Contain("secondaryPhoneNumber: String");
        input.Should().Contain("address: UpdateCustomerAddressInput");
        input.Should().NotContain("mobile");
        input.Should().NotContain("username");
        input.Should().NotContain("firstName");
        input.Should().NotContain("lastName");
        input.Should().NotContain("nationalCode");
        input.Should().NotContain("birthDate");

        var address = sdl[sdl.IndexOf("input UpdateCustomerAddressInput", StringComparison.Ordinal)..];
        address = address[..(address.IndexOf('}') + 1)];
        address.Should().Contain("provinceId: Int!");
        address.Should().Contain("cityId: Int!");
        address.Should().Contain("districtId: Int");
        address.Should().Contain("addressText: String");
        address.Should().Contain("postalCode: String");
    }

    [Fact]
    public async Task CustomerById_Query_Exposes_Contact_Info_Job_Types_And_The_Edit_Flag()
    {
        var services = new ServiceCollection();
        services.AddCustomerGraphQL();
        var sdl = (await services.AddGraphQLBootstrap().BuildSchemaAsync()).ToString();

        sdl.Should().Contain("customerById(customerId: Long!): CustomerDetails");

        var type = sdl[sdl.IndexOf("type CustomerDetails", StringComparison.Ordinal)..];
        type = type[..(type.IndexOf('}') + 1)];
        type.Should().Contain("email: String");
        type.Should().Contain("secondaryPhoneNumber: String");
        type.Should().Contain("jobTypes: [JobTypeItem!]!");
        type.Should().Contain("registrationStatus: CustomerStatusType");
        type.Should().Contain("addressText: String");
        type.Should().Contain("postalCode: String");
        type.Should().Contain("canEdit: Boolean!");
    }
}
