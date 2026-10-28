namespace EwaysVNext.UnitTests;

/// <summary>
/// Locates the repository root for the few tests that assert on files shipped beside the code —
/// realm exports, compose files — rather than on compiled behaviour. Walking up to the solution
/// file keeps them working from any working directory a runner happens to pick.
/// </summary>
internal static class TestPaths
{
    public static string RepositoryRoot { get; } = Locate();

    private static string Locate()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EwaysVNext.slnx")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the EwaysVNext solution directory.");
    }
}
