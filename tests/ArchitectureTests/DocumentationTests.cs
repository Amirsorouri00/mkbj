using AwesomeAssertions;
using Xunit;

namespace EwaysVNext.ArchitectureTests;

/// <summary>
/// Documentation that describes modules which do not exist is worse than no documentation: a
/// reader stops checking what they believe is already written down. These tests keep the two in
/// step for the facts a machine can check.
/// </summary>
public sealed class DocumentationTests
{
    [Fact]
    public void Agents_Md_Bolds_Exactly_The_Existing_Modules()
    {
        var agents = File.ReadAllText(Path.Combine(Root, "AGENTS.md"));

        HexagonalBoundaryTests.ModuleNames.Should().OnlyContain(module => agents.Contains($"**{module}**"));

        new[] { "Identity", "Orders", "Supplier", "Brand", "Payment", "Location", "Notification" }
            .Should().OnlyContain(module => !agents.Contains($"**{module}**"),
                "a module in bold reads as one that exists");

        agents.Should().NotContain("LocalDB").And.NotContain("SQL Server");
    }

    [Fact]
    public void Adr_0016_Is_Accepted_And_0017_To_0022_Exist()
    {
        var adrDirectory = Path.Combine(Root, "docs", "adr");

        File.ReadAllText(Path.Combine(adrDirectory, "0016-queryapplication-is-the-read-side-implementation.md"))
            .Should().Contain("Accepted — 2026-10-28");

        foreach (var number in new[] { "0017", "0018", "0019", "0020", "0021", "0022" })
        {
            Directory.EnumerateFiles(adrDirectory, $"{number}-*.md")
                .Should().ContainSingle("ADR-{0} must exist", number);
        }
    }

    [Theory]
    [InlineData("AGENTS.md")]
    [InlineData("docs/ONBOARDING.md")]
    [InlineData("docs/TESTING-GUARANTEES.md")]
    [InlineData("README.md")]
    public void Docs_Do_Not_Describe_The_Removed_Stack(string file)
    {
        var text = File.ReadAllText(Path.Combine(Root, file));

        foreach (var removed in new[] { "LocalDB", "SQL Server", "MSSQL", "Shahkar", "IRR" })
            text.Should().NotContain(removed, "{0} belongs to the stack this MVP replaced", removed);
    }

    private static string Root
    {
        get
        {
            var directory = new DirectoryInfo(Directory.GetCurrentDirectory());

            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EwaysVNext.slnx")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName
                ?? throw new DirectoryNotFoundException("Could not locate the EwaysVNext solution directory.");
        }
    }
}
