using AwesomeAssertions;
using Xunit;

namespace EwaysVNext.ArchitectureTests;

/// <summary>
/// Comparing a secret with <c>==</c>, <c>SequenceEqual</c> or <c>Equals</c> returns as soon as two
/// bytes differ, so how long the comparison took says how many leading bytes were right. That is
/// enough to recover a stored hash one byte at a time, and no unit test can see it: the mutation
/// from <c>FixedTimeEquals</c> to <c>SequenceEqual</c> leaves every behavioural assertion passing,
/// which is exactly why this is a source scan rather than a test of behaviour.
/// </summary>
public sealed class CredentialComparisonTests
{
    private static readonly string[] UnsafeComparisons =
        ["SequenceEqual", ".Equals(", "=="];

    [Fact]
    public void Credential_Hashes_Are_Compared_In_Constant_Time()
    {
        var offenders = new List<string>();

        foreach (var (path, relative) in CredentialFiles())
        {
            var text = File.ReadAllText(path);

            foreach (var line in text.Split('\n')
                         .Select((Text, Number) => (Text: Text.Trim(), Number: Number + 1))
                         .Where(line => line.Text.Contains("Hash", StringComparison.Ordinal)
                                        && !line.Text.StartsWith("//", StringComparison.Ordinal)
                                        && !line.Text.StartsWith("///", StringComparison.Ordinal))
                         .Where(line => UnsafeComparisons.Any(token =>
                             line.Text.Contains(token, StringComparison.Ordinal))))
            {
                offenders.Add($"{relative}:{line.Number} — {line.Text}");
            }
        }

        offenders.Should().BeEmpty(
            "a hash comparison that short-circuits leaks, through its timing, how many leading "
            + "bytes the caller guessed correctly; use CryptographicOperations.FixedTimeEquals");
    }

    [Fact]
    public void The_Scan_Actually_Reaches_The_Hasher() =>
        CredentialFiles().Select(file => Path.GetFileName(file.Path))
            .Should().Contain("ApiKeyHasher.cs",
                "a scan that matches no file passes without asserting anything");

    /// <summary>Files whose job is to handle a credential, by name.</summary>
    private static IEnumerable<(string Path, string Relative)> CredentialFiles()
    {
        var source = Path.Combine(Root, "src");

        return Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                return name.Contains("Hasher", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("ApiKey", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Credential", StringComparison.OrdinalIgnoreCase);
            })
            .Select(path => (path, Path.GetRelativePath(Root, path)));
    }

    private static string Root
    {
        get
        {
            var directory = new DirectoryInfo(Directory.GetCurrentDirectory());

            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EwaysVNext.slnx")))
                directory = directory.Parent;

            return directory?.FullName
                ?? throw new DirectoryNotFoundException("Could not locate the EwaysVNext solution directory.");
        }
    }
}
