using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Customer.Application.Domain.Customers;
using EwaysVNext.Modules.Customer.Application.Domain.Enums;
using Xunit;

namespace EwaysVNext.UnitTests;

/// <summary>
/// CustomerVerification is where the registration review read model gets its verification/
/// rejection date, method and reason from - these tests cover that data directly, independent of
/// CustomerService's orchestration (covered in CustomerServiceTests).
/// </summary>
public class CustomerVerificationDomainTests
{
    private static readonly SnowflakeIdGenerator IdGenerator = new();

    private static CustomerVerification NewVerification() =>
        CustomerVerification.Create(IdGenerator.Generate(), IdGenerator.Generate(), "0012345679", new DateOnly(1990, 1, 1), TestTime.Now);

    [Fact]
    public void MarkAsShahkarVerified_Sets_Method_To_Shahkar_And_Records_The_Verification_Date()
    {
        var v = NewVerification();

        v.MarkAsShahkarVerified(IdGenerator.Generate(), "John", "Doe", "Father", Gender.Male, true, TestTime.Now, "fid-1");

        v.VerificationMethod.Should().Be((int)VerificationMethodType.Shahkar);
        v.VerifiedAt.Should().Be(TestTime.Now.UtcDateTime);
        v.Reason.Should().BeNull();
    }

    [Fact]
    public void MarkAsExpertVerified_Sets_Method_To_Agent_And_Records_The_Verification_Date()
    {
        var v = NewVerification();

        v.MarkAsExpertVerified(1, TestTime.Now);

        v.VerificationMethod.Should().Be((int)VerificationMethodType.Agent);
        v.VerifiedAt.Should().Be(TestTime.Now.UtcDateTime);
    }

    [Fact]
    public void MarkAsNotVerified_Keeps_Method_As_Shahkar_Since_It_Is_Only_Reached_From_The_Automatic_Pipeline()
    {
        var v = NewVerification();
        v.MarkAsShahkarVerified(IdGenerator.Generate(), "John", "Doe", "Father", Gender.Male, true, TestTime.Now, "fid-1");

        v.MarkAsNotVerified("fid-2", TestTime.PlusDays(1));

        v.VerificationMethod.Should().Be(
            (int)VerificationMethodType.Shahkar,
            "a failed automatic check is still a system (Shahkar) determination, not an absence of one");
        v.VerifiedAt.Should().BeNull();
    }

    [Fact]
    public void RecordRejection_Requires_A_Non_Empty_Reason()
    {
        var v = NewVerification();

        var act = () => v.RecordRejection("  ", TestTime.Now);

        act.Should().Throw<DomainException>().Where(e => e.Code == "REJECTION_REASON_REQUIRED");
    }

    [Fact]
    public void RecordRejection_Stores_The_Reason_And_The_Decision_Date_Without_Changing_The_Method()
    {
        var v = NewVerification();
        v.MarkAsExpertVerified(1, TestTime.Now);

        v.RecordRejection("Failed a secondary business check.", TestTime.PlusDays(1));

        v.Reason.Should().Be("Failed a secondary business check.");
        v.VerifiedAt.Should().Be(TestTime.PlusDays(1).UtcDateTime);
        v.VerificationMethod.Should().Be((int)VerificationMethodType.Agent, "rejection does not change how the identity check was performed");
    }

    [Fact]
    public void ClearRejectionReason_Removes_A_Previously_Stored_Reason()
    {
        var v = NewVerification();
        v.RecordRejection("Some reason.", TestTime.Now);

        v.ClearRejectionReason(TestTime.PlusDays(1));

        v.Reason.Should().BeNull();
    }
}
