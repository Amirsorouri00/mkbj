using AwesomeAssertions;
using EwaysVNext.Audit.Implementation;
using Xunit;

namespace EwaysVNext.UnitTests;

/// <summary>
/// An audit row records what changed, not the whole object. Storing every field on every write
/// would bury the one that moved and would copy personal data into the audit table on edits that
/// never touched it.
/// </summary>
public sealed class AuditDiffTests
{
    [Fact]
    public void Diff_Keeps_Only_Changed_Fields()
    {
        var (before, after) = AuditDiff.Changed(
            new { Name = "a", Email = "x@y.z" },
            new { Name = "b", Email = "x@y.z" });

        before!.ToJsonString().Should().Be("""{"name":"a"}""");
        after!.ToJsonString().Should().Be("""{"name":"b"}""");
    }

    [Fact]
    public void Diff_Of_A_Creation_Keeps_Every_After_Field()
    {
        var (before, after) = AuditDiff.Changed(null, new { Name = "a", Email = "x@y.z" });

        before.Should().BeNull();
        after!.ToJsonString().Should().Be("""{"name":"a","email":"x@y.z"}""");
    }

    [Fact]
    public void Diff_Of_Identical_Objects_Is_Empty()
    {
        var (before, after) = AuditDiff.Changed(new { Name = "a" }, new { Name = "a" });

        before!.ToJsonString().Should().Be("{}");
        after!.ToJsonString().Should().Be("{}");
    }

    [Fact]
    public void A_Property_Present_On_Only_One_Side_Counts_As_Changed()
    {
        // A plain value on purpose: the default encoder escapes "+" to its unicode form, and this
        // test is about which properties the diff keeps, not about JSON escaping.
        var (before, after) = AuditDiff.Changed(new { Name = "a" }, new { Name = "a", Phone = "65" });

        before!.ToJsonString().Should().Be("{}");
        after!.ToJsonString().Should().Be("""{"phone":"65"}""");
    }
}
