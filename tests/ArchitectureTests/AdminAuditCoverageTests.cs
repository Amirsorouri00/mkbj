using System.Reflection;
using AwesomeAssertions;
using EwaysVNext.Audit;
using Xunit;

namespace EwaysVNext.ArchitectureTests;

/// <summary>
/// Every service an administrator can change something through can write an audit entry.
/// </summary>
/// <remarks>
/// <para>
/// The failure this prevents is slow and silent. Someone adds a method to an admin service, or a
/// whole new admin service, and it works perfectly — the change lands, the response is 200, and
/// no test anywhere fails. Months later somebody asks who deactivated a product in March and the
/// answer is that nobody knows.
/// </para>
/// <para>
/// This is a structural rule, not a behavioural one: it says the service <b>holds</b> the audit
/// trail, which is the part a compiler can check. That one of them is called on the right path,
/// before the save and in the same transaction, is a question for each service's own tests —
/// <c>OrderAdminServiceTests</c> asserts exactly that ordering for Sales.
/// </para>
/// </remarks>
public sealed class AdminAuditCoverageTests
{
    /// <summary>
    /// Services whose name ends in AdminService but which write nothing.
    /// </summary>
    /// <remarks>
    /// Empty today, and that is the point: every entry here has to be argued for, because an
    /// admin service that writes nothing is unusual enough to be worth saying out loud.
    /// </remarks>
    private static readonly string[] ReadOnlyAdminServices = [];

    [Fact]
    public void Every_Admin_Service_Holds_The_Audit_Trail()
    {
        var offenders = AdminServices()
            .Where(type => !ReadOnlyAdminServices.Contains(type.Name, StringComparer.Ordinal))
            .Where(type => !TakesAuditTrail(type))
            .Select(type => type.FullName!)
            .ToList();

        offenders.Should().BeEmpty(
            "an admin service that cannot write an audit entry is one whose changes nobody can "
            + "attribute later; take IAuditTrail or argue it onto ReadOnlyAdminServices");
    }

    /// <summary>
    /// The rule has something to check.
    /// </summary>
    /// <remarks>
    /// Without this, deleting every admin service — or renaming the convention — would leave the
    /// rule above passing over an empty set, which is the quietest way for a guard to stop
    /// guarding anything.
    /// </remarks>
    [Fact]
    public void The_Admin_Services_Are_Found_At_All() =>
        AdminServices().Select(type => type.Name).Should().Contain(
            [
                "CatalogAdminService",
                "PricingAdminService",
                "SettingsAdminService",
                "PartnerAdminService",
                "CustomerAdminService",
                "OrderAdminService",
            ]);

    private static List<Type> AdminServices() =>
        [.. Directory
            .EnumerateFiles(AppContext.BaseDirectory, "EwaysVNext.Modules.*.Application.dll")
            .Select(Assembly.LoadFrom)
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(type => type.Name.EndsWith("AdminService", StringComparison.Ordinal))];

    private static bool TakesAuditTrail(Type type) =>
        type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .SelectMany(constructor => constructor.GetParameters())
            .Any(parameter => parameter.ParameterType == typeof(IAuditTrail));
}
