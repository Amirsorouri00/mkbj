using AwesomeAssertions;
using EwaysVNext.Database;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Customer.Adapter.Outbound;
using EwaysVNext.Modules.Customer.Application.Domain.Enums;
using EwaysVNext.Modules.Customer.Application.Domain.ValueObjects;
using EwaysVNext.Modules.Customer.Application.Ports.Outbound;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using CustomerAggregate = EwaysVNext.Modules.Customer.Application.Domain.Customers.Customer;

namespace EwaysVNext.UnitTests;

/// <summary>
/// End-to-end (real EF InMemory persistence + real repository) coverage for the registration
/// review read model: it is projected directly from Customer + CustomerVerification, with no
/// CustomerReviewHistory table involved anywhere.
/// </summary>
public class CustomerRegistrationReviewQueryTests
{
    private static readonly SnowflakeIdGenerator IdGenerator = new();

    private static EwaysVNextDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<EwaysVNextDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ICustomerRepository CreateRepository(EwaysVNextDbContext db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddCustomerOutboundAdapters(new ConfigurationBuilder().Build());
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<ICustomerRepository>();
    }

    [Fact]
    public async Task Registered_Customer_Without_Verification_Exposes_Only_The_Registration_Date()
    {
        await using var db = CreateDbContext();
        var customer = CustomerAggregate.Register(IdGenerator.Generate(), Guid.NewGuid(), MobileNumber.Create("09123456789"), TestTime.Now);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var review = await CreateRepository(db).GetRegistrationReviewAsync(customer.Id);

        review.Should().NotBeNull();
        review!.RegistrationDate.Should().Be(customer.CreatedAt);
        review.VerificationDate.Should().BeNull();
        review.VerificationMethod.Should().BeNull();
        review.Reason.Should().BeNull();
        review.StatusId.Should().Be((int)CustomerStatusType.PendingApproval);
    }

    [Fact]
    public async Task Approved_By_System_Exposes_The_Shahkar_Method_Its_Verification_Date_And_No_Reason()
    {
        await using var db = CreateDbContext();
        var customer = CustomerAggregate.Register(IdGenerator.Generate(), Guid.NewGuid(), MobileNumber.Create("09123456789"), TestTime.Now);
        customer.SetVerification(
            Modules.Customer.Application.Domain.Customers.CustomerVerification.Create(
                IdGenerator.Generate(), customer.Id, "0012345679", new DateOnly(1990, 1, 1), TestTime.Now),
            TestTime.Now);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var verifiedAt = TestTime.PlusDays(1);
        customer.Verification.MarkAsShahkarVerified(IdGenerator.Generate(), "John", "Doe", "Father", Gender.Male, true, verifiedAt, "fid-1");
        customer.UpdateVerificationStatus(VerificationStatusType.Verified, verifiedAt);
        customer.UpdateStatus(CustomerStatusType.Approved, verifiedAt);
        db.Customers.Update(customer);
        await db.SaveChangesAsync();

        var review = await CreateRepository(db).GetRegistrationReviewAsync(customer.Id);

        review!.StatusId.Should().Be((int)CustomerStatusType.Approved);
        review.VerificationMethod.Should().Be((int)VerificationMethodType.Shahkar);
        review.VerificationDate.Should().Be(verifiedAt.UtcDateTime);
        review.Reason.Should().BeNull();
    }

    [Fact]
    public async Task Approved_By_Expert_Exposes_The_Agent_Method_Its_Verification_Date_And_No_Reason()
    {
        await using var db = CreateDbContext();
        var customer = CustomerAggregate.Register(IdGenerator.Generate(), Guid.NewGuid(), MobileNumber.Create("09123456789"), TestTime.Now);
        customer.SetVerification(
            Modules.Customer.Application.Domain.Customers.CustomerVerification.Create(
                IdGenerator.Generate(), customer.Id, "0012345679", new DateOnly(1990, 1, 1), TestTime.Now),
            TestTime.Now);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var verifiedAt = TestTime.PlusDays(1);
        customer.Verification.MarkAsExpertVerified(1, verifiedAt);
        customer.UpdateVerificationStatus(VerificationStatusType.VerifiedByAgent, verifiedAt, expertId: 1);
        customer.UpdateStatus(CustomerStatusType.Approved, verifiedAt);
        db.Customers.Update(customer);
        await db.SaveChangesAsync();

        var review = await CreateRepository(db).GetRegistrationReviewAsync(customer.Id);

        review!.StatusId.Should().Be((int)CustomerStatusType.Approved);
        review.VerificationMethod.Should().Be((int)VerificationMethodType.Agent);
        review.VerificationDate.Should().Be(verifiedAt.UtcDateTime);
        review.Reason.Should().BeNull();
    }

    [Fact]
    public async Task Rejected_By_System_Exposes_The_Shahkar_Method_The_Decision_Date_And_The_Stored_Reason()
    {
        await using var db = CreateDbContext();
        var customer = CustomerAggregate.Register(IdGenerator.Generate(), Guid.NewGuid(), MobileNumber.Create("09123456789"), TestTime.Now);
        customer.SetVerification(
            Modules.Customer.Application.Domain.Customers.CustomerVerification.Create(
                IdGenerator.Generate(), customer.Id, "0012345679", new DateOnly(1990, 1, 1), TestTime.Now),
            TestTime.Now);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        customer.Verification.MarkAsNotVerified("fid-failed", TestTime.PlusDays(1));
        customer.UpdateVerificationStatus(VerificationStatusType.NotVerified, TestTime.PlusDays(1));
        var rejectedAt = TestTime.PlusDays(2);
        customer.Verification.RecordRejection("National ID could not be verified.", rejectedAt);
        customer.UpdateStatus(CustomerStatusType.Rejected, rejectedAt);
        db.Customers.Update(customer);
        await db.SaveChangesAsync();

        var review = await CreateRepository(db).GetRegistrationReviewAsync(customer.Id);

        review!.StatusId.Should().Be((int)CustomerStatusType.Rejected);
        review.VerificationMethod.Should().Be((int)VerificationMethodType.Shahkar);
        review.VerificationDate.Should().Be(rejectedAt.UtcDateTime);
        review.Reason.Should().Be("National ID could not be verified.");
    }

    [Fact]
    public async Task Rejected_By_Expert_Exposes_The_Agent_Method_The_Decision_Date_And_The_Stored_Reason()
    {
        await using var db = CreateDbContext();
        var customer = CustomerAggregate.Register(IdGenerator.Generate(), Guid.NewGuid(), MobileNumber.Create("09123456789"), TestTime.Now);
        customer.SetVerification(
            Modules.Customer.Application.Domain.Customers.CustomerVerification.Create(
                IdGenerator.Generate(), customer.Id, "0012345679", new DateOnly(1990, 1, 1), TestTime.Now),
            TestTime.Now);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        customer.Verification.MarkAsExpertVerified(1, TestTime.PlusDays(1));
        var rejectedAt = TestTime.PlusDays(2);
        customer.Verification.RecordRejection("Failed a secondary business check.", rejectedAt);
        customer.UpdateStatus(CustomerStatusType.Rejected, rejectedAt);
        db.Customers.Update(customer);
        await db.SaveChangesAsync();

        var review = await CreateRepository(db).GetRegistrationReviewAsync(customer.Id);

        review!.StatusId.Should().Be((int)CustomerStatusType.Rejected);
        review.VerificationMethod.Should().Be((int)VerificationMethodType.Agent);
        review.VerificationDate.Should().Be(rejectedAt.UtcDateTime);
        review.Reason.Should().Be("Failed a secondary business check.");
    }

    [Fact]
    public async Task Unknown_Customer_Returns_Null()
    {
        await using var db = CreateDbContext();

        var review = await CreateRepository(db).GetRegistrationReviewAsync(999999);

        review.Should().BeNull();
    }
}
