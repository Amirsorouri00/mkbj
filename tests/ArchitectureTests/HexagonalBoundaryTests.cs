using System.Reflection;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using NetArchTest.Rules;
using Xunit;

namespace EwaysVNext.ArchitectureTests;

/// <summary>
/// Executable module-boundary rules. Later phases append to <see cref="ModuleNames"/> and to
/// <see cref="SanctionedReferences"/> as they create each module; a module missing from
/// <see cref="ModuleNames"/> escapes every rule here with the build still green, so adding the
/// name is part of adding the module.
/// </summary>
public sealed class HexagonalBoundaryTests
{
    // Final set (later phases append as they create each module):
    // Catalog, Customer, Partners, Settings, Pricing, Sales, Sourcing, Admin.
    public static readonly string[] ModuleNames =
        ["Catalog", "Customer", "Partners", "Settings", "Pricing", "Sales"];

    /// <summary>
    /// Rows are (consumerModule, providerModule): `EwaysVNext.Modules.{consumer}.Adapter.Outbound`
    /// may reference `EwaysVNext.Modules.{provider}.Application`. Phase 0 has no rows — each later
    /// phase adds its own in the task that introduces the reference. The final set (spec §3.6) is
    /// (Sales,Catalog), (Sales,Pricing), (Sales,Customer), (Sales,Settings), (Pricing,Catalog),
    /// (Pricing,Settings), (Sourcing,Catalog), (Sourcing,Pricing).
    /// </summary>
    public static TheoryData<string, string> SanctionedReferences => new()
    {
        // Pricing prices what the catalogue says is sellable, and reads the seller's FX staleness
        // limit from Settings. Both go through its Adapter.Outbound, never its Application.
        { "Pricing", "Catalog" },
        { "Pricing", "Settings" },

        // Sales composes a basket from what Catalog says is sellable at what Pricing says it
        // costs, bills a Customer and prints the Settings seller block. Four providers, all read
        // through its Adapter.Outbound — and none of those calls is in the same transaction as
        // the order it writes.
        { "Sales", "Catalog" },
        { "Sales", "Pricing" },
        { "Sales", "Customer" },
        { "Sales", "Settings" },
    };

    public static TheoryData<string> Modules => new(ModuleNames);

    /// <summary>Modules that reference no other module, so they may never be a sanctioned consumer.</summary>
    private static readonly string[] ModulesThatConsumeNothing = ["Catalog", "Customer", "Settings", "Partners"];

    /// <summary>
    /// Pricing reaches other modules only from its Adapter.Outbound. Its Application is the
    /// hexagon, and a reference there would make the dependency structural rather than a port.
    /// </summary>
    [Fact]
    public void Pricing_Application_References_No_Other_Module() =>
        OtherModuleReferences("Pricing", "EwaysVNext.Modules.Pricing.Application").Should().BeEmpty();

    [Fact]
    public void Src_Modules_Are_Exactly_ModuleNames() =>
        Directory.EnumerateDirectories(Path.Combine(Root, "src", "Modules"))
            .Select(Path.GetFileName).Should().BeEquivalentTo(ModuleNames);

    [Theory, MemberData(nameof(Modules))]
    public void Module_References_No_Other_Module_Unless_Sanctioned(string module) =>
        ForbiddenCrossModuleReferences(module).Should().BeEmpty();

    /// <summary>
    /// The same rule read from the project files rather than the built assemblies.
    /// <para>
    /// Both are needed. The compiler elides a reference no code uses, so a freshly scaffolded
    /// module passes the assembly check however it is wired — the reference only becomes visible
    /// once something calls through it, which is exactly when it is too late to be told. This
    /// catches the wiring the moment it is written.
    /// </para>
    /// </summary>
    [Theory, MemberData(nameof(Modules))]
    public void Module_Project_References_Are_Sanctioned(string module) =>
        ForbiddenProjectReferences(module).Should().BeEmpty();

    [Theory(SkipTestWithoutData = true), MemberData(nameof(SanctionedReferences))]
    public void Sanctioned_Row_Is_Well_Formed(string consumer, string provider)
    {
        ModuleNames.Should().Contain([consumer, provider]);
        consumer.Should().NotBe(provider);
        provider.Should().NotBe("Partners", "nothing may call into Partners — the partner id arrives as an authenticated principal");
        ModulesThatConsumeNothing.Should().NotContain(consumer,
            "these modules reference no other module");
    }

    [Theory, MemberData(nameof(Modules))]
    public void Application_Depends_On_No_Adapter_And_No_EfCore(string module)
    {
        var application = Load($"EwaysVNext.Modules.{module}.Application");
        if (application.GetTypes().Length == 0) return; // a module stripped to a shell has nothing to check

        Types.InAssembly(application).ShouldNot()
            .HaveDependencyOnAny($"EwaysVNext.Modules.{module}.Adapter", "Microsoft.EntityFrameworkCore")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Theory, MemberData(nameof(Modules))]
    public void Application_Services_Are_Sealed(string module)
    {
        var application = Load($"EwaysVNext.Modules.{module}.Application");
        if (application.GetTypes().Length == 0) return;

        var result = Types.InAssembly(application).That().AreClasses()
            .And().AreNotAbstract().And().HaveNameEndingWith("Service")
            .Should().BeSealed().GetResult();

        result.IsSuccessful.Should().BeTrue(
            "unsealed: {0}", string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Theory, MemberData(nameof(Modules))]
    public void QueryApplication_Never_Writes(string module) =>
        SourceFiles($"src/Modules/{module}/QueryApplication")
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"SaveChanges|IUnitOfWork|ExecuteUpdate|ExecuteDelete"))
            .Should().BeEmpty();

    [Fact]
    public void No_User_Initiated_Transactions_In_Src() =>
        SourceFiles("src")
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"BeginTransaction|UseTransaction"))
            .Should().BeEmpty("rule 8: EnableRetryOnFailure cannot replay a user-initiated transaction");

    [Fact]
    public void No_Wolverine_Anywhere() =>
        Directory.EnumerateFiles(Root, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".csproj", StringComparison.Ordinal) || f.EndsWith(".props", StringComparison.Ordinal))
            .Where(NotInBinObj)
            .Where(f => File.ReadAllText(f).Contains("Wolverine", StringComparison.Ordinal))
            .Should().BeEmpty();

    /// <summary>
    /// "{assembly} -> {reference}" for every reference from one module's assemblies into another
    /// module, except the two sanctioned shapes: a row in <see cref="SanctionedReferences"/>
    /// (Adapter.Outbound → provider Application), and Admin's composition layer, which may read any
    /// module's QueryApplication or Application (ADR-0006/0007).
    /// </summary>
    private static List<string> ForbiddenCrossModuleReferences(string module)
    {
        var sanctioned = SanctionedReferences
            .Where(row => row.Data.Item1 == module)
            .Select(row => row.Data.Item2)
            .ToHashSet(StringComparer.Ordinal);
        List<string> violations = [];

        foreach (var assemblyPath in Directory.EnumerateFiles(
                     AppContext.BaseDirectory, $"EwaysVNext.Modules.{module}.*.dll"))
        {
            var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);

            foreach (var reference in Assembly.LoadFrom(assemblyPath).GetReferencedAssemblies())
            {
                var match = Regex.Match(reference.Name ?? string.Empty, @"^EwaysVNext\.Modules\.([^.]+)\.(.+)$");
                if (!match.Success) continue;

                var other = match.Groups[1].Value;
                var layer = match.Groups[2].Value;
                if (other == module) continue;

                var isSanctionedGateway =
                    assemblyName == $"EwaysVNext.Modules.{module}.Adapter.Outbound"
                    && layer == "Application"
                    && sanctioned.Contains(other);

                var isAdminComposition =
                    module == "Admin"
                    && assemblyName == "EwaysVNext.Modules.Admin.Adapter.Outbound"
                    && layer is "QueryApplication" or "Application";

                if (!isSanctionedGateway && !isAdminComposition)
                    violations.Add($"{assemblyName} -> {reference.Name}");
            }
        }

        return violations;
    }

    /// <summary>Every other module this one assembly references, sanctioned or not.</summary>
    private static List<string> OtherModuleReferences(string module, string assemblyName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, $"{assemblyName}.dll");
        if (!File.Exists(path)) return [$"{assemblyName}.dll was not built"];

        return Assembly.LoadFrom(path).GetReferencedAssemblies()
            .Select(reference => Regex.Match(reference.Name ?? string.Empty, @"^EwaysVNext\.Modules\.([^.]+)\."))
            .Where(match => match.Success && match.Groups[1].Value != module)
            .Select(match => match.Groups[1].Value)
            .ToList();
    }

    private static List<string> ForbiddenProjectReferences(string module)
    {
        var sanctioned = SanctionedReferences
            .Where(row => row.Data.Item1 == module)
            .Select(row => row.Data.Item2)
            .ToHashSet(StringComparer.Ordinal);

        var moduleDirectory = Path.Combine(Root, "src", "Modules", module);
        List<string> violations = [];

        foreach (var projectPath in Directory.EnumerateFiles(
                     moduleDirectory, "*.csproj", SearchOption.AllDirectories))
        {
            var projectName = Path.GetFileNameWithoutExtension(projectPath);

            foreach (var referenced in Regex.Matches(
                         File.ReadAllText(projectPath), @"ProjectReference\s+Include=""([^""]+)""")
                     .Select(match => Path.GetFileNameWithoutExtension(match.Groups[1].Value)))
            {
                var match = Regex.Match(referenced, @"^EwaysVNext\.Modules\.([^.]+)\.(.+)$");
                if (!match.Success) continue;

                var other = match.Groups[1].Value;
                var layer = match.Groups[2].Value;
                if (other == module) continue;

                var isSanctionedGateway =
                    projectName == $"EwaysVNext.Modules.{module}.Adapter.Outbound"
                    && layer == "Application"
                    && sanctioned.Contains(other);

                var isAdminComposition =
                    module == "Admin"
                    && projectName == "EwaysVNext.Modules.Admin.Adapter.Outbound"
                    && layer is "QueryApplication" or "Application";

                if (!isSanctionedGateway && !isAdminComposition)
                    violations.Add($"{projectName} -> {referenced}");
            }
        }

        return violations;
    }

    private static Assembly Load(string assemblyName) =>
        Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, $"{assemblyName}.dll"));

    private static IEnumerable<string> SourceFiles(string relativeDirectory)
    {
        var directory = Path.Combine(Root, relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories).Where(NotInBinObj)
            : [];
    }

    private static bool NotInBinObj(string path) =>
        !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    /// <summary>The solution directory. Internal so SalesArchitectureTests can scan sources too.</summary>
    internal static string Root
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
