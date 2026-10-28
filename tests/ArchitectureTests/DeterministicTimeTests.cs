using AwesomeAssertions;
using Xunit;

namespace EwaysVNext.ArchitectureTests;

public class DeterministicTimeTests
{
    private static readonly string[] AllowedFiles = ["DomainEvent.cs", "IClock.cs"];

    [Fact]
    public void No_Aggregate_Reads_The_Ambient_Clock()
    {
        var offenders = DomainFiles()
            .Where(f => File.ReadAllText(f.Path).Contains("DateTimeOffset.UtcNow", StringComparison.Ordinal))
            .Select(f => f.Relative)
            .ToList();

        offenders.Should().BeEmpty(
            "an aggregate that reads the clock cannot be tested deterministically: take " +
            "`DateTimeOffset now` as a parameter and let the application service supply " +
            "IClock.UtcNow, read once per operation");
    }

    [Fact]
    public void No_Aggregate_Reads_DateTime_Now_Either()
    {
        var offenders = DomainFiles()
            .Where(f =>
            {
                var text = File.ReadAllText(f.Path);
                return text.Contains("DateTime.UtcNow", StringComparison.Ordinal)
                    || text.Contains("DateTime.Now", StringComparison.Ordinal);
            })
            .Select(f => f.Relative)
            .ToList();

        offenders.Should().BeEmpty(
            "DateTime.Now is both ambient AND local-time; the domain uses DateTimeOffset " +
            "supplied by the caller");
    }

    private static IEnumerable<(string Path, string Relative)> DomainFiles()
    {
        var src = FindSourceRoot();
        var roots = new List<string> { Path.Combine(src, "BuildingBlocks", "Domain") };
        roots.AddRange(Directory.EnumerateDirectories(Path.Combine(src, "Modules"))
            .Select(m => Path.Combine(m, "Application", "Domain"))
            .Where(Directory.Exists));

        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                if (AllowedFiles.Contains(Path.GetFileName(file), StringComparer.Ordinal)) continue;

                yield return (file, Path.GetRelativePath(src, file));
            }
        }
    }

    private static string FindSourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EwaysVNext.slnx")))
            dir = dir.Parent;

        dir.Should().NotBeNull("the test must be able to locate the repository root");
        return Path.Combine(dir!.FullName, "src");
    }
}
