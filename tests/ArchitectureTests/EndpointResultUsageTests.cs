using System.Text.RegularExpressions;
using AwesomeAssertions;
using Xunit;

namespace EwaysVNext.ArchitectureTests;

public class EndpointResultUsageTests
{
    private static readonly Regex RawResultsCall = new(@"\bResults\.", RegexOptions.Compiled);

    [Fact]
    public void No_Rest_Endpoint_Builds_A_Response_With_The_Raw_Results_Api()
    {
        var offenders = RestAdapterFiles()
            .Where(f => RawResultsCall.IsMatch(File.ReadAllText(f.Path)))
            .Select(f => f.Relative)
            .ToList();

        offenders.Should().BeEmpty(
            "REST endpoints must build responses through EndpointResult (Succeed, Created, Unauthorized, " +
            "Forbidden, BadRequest) or a Result extension (ToHttpResult, ToCreatedHttpResult) so every " +
            "endpoint returns the same response envelope; add the missing case to EndpointResult instead " +
            "of calling Microsoft.AspNetCore.Http.Results directly");
    }

    private static IEnumerable<(string Path, string Relative)> RestAdapterFiles()
    {
        var src = FindSourceRoot();
        var roots = Directory.EnumerateDirectories(Path.Combine(src, "Modules"))
            .Select(m => Path.Combine(m, "Adapter", "Inbound", "Rest"))
            .Where(Directory.Exists);

        foreach (var root in roots)
        {
            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

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
