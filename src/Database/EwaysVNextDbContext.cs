using Microsoft.EntityFrameworkCore;

namespace EwaysVNext.Database;

public sealed class EwaysVNextDbContext(DbContextOptions<EwaysVNextDbContext> options) : DbContext(options)
{
    public DbSet<EwaysVNext.Modules.Customer.Application.Domain.Customers.Customer> Customers
        => Set<EwaysVNext.Modules.Customer.Application.Domain.Customers.Customer>();
    public DbSet<EwaysVNext.Modules.Customer.Application.Domain.TeamMembers.TeamMember> TeamMembers
        => Set<EwaysVNext.Modules.Customer.Application.Domain.TeamMembers.TeamMember>();
    public DbSet<EwaysVNext.Modules.Location.Application.Domain.Locations.Province> Provinces
        => Set<EwaysVNext.Modules.Location.Application.Domain.Locations.Province>();
    public DbSet<EwaysVNext.Modules.Location.Application.Domain.Locations.Address> Addresses
        => Set<EwaysVNext.Modules.Location.Application.Domain.Locations.Address>();

    public DbSet<EwaysVNext.Modules.Catalog.Application.Products.Domain.Product> Products
        => Set<EwaysVNext.Modules.Catalog.Application.Products.Domain.Product>();

    public DbSet<EwaysVNext.Modules.Catalog.Application.Categories.Domain.Category> Categories
        => Set<EwaysVNext.Modules.Catalog.Application.Categories.Domain.Category>();

    public DbSet<EwaysVNext.Modules.Catalog.Application.Categories.Domain.CategoryProduct> CategoryProducts
        => Set<EwaysVNext.Modules.Catalog.Application.Categories.Domain.CategoryProduct>();

    public DbSet<EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Domain.AttributeBlueprint> AttributeBlueprints
        => Set<EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Domain.AttributeBlueprint>();

    public DbSet<EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Domain.MeasurementUnit> MeasurementUnits
        => Set<EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Domain.MeasurementUnit>();
    public DbSet<EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.Domain.CategoryAttributeAssignment> CategoryAttributeAssignments
        => Set<EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.Domain.CategoryAttributeAssignment>();
    public DbSet<EwaysVNext.Modules.Catalog.Application.CategoryAttributeExclusions.Domain.CategoryAttributeExclusion> CategoryAttributeExclusions
        => Set<EwaysVNext.Modules.Catalog.Application.CategoryAttributeExclusions.Domain.CategoryAttributeExclusion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EwaysVNextDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var rootEntityType = entityType;
            while (rootEntityType.FindOwnership() is { } ownership)
            {
                rootEntityType = ownership.PrincipalEntityType;
            }

            var namespaceName = rootEntityType.ClrType?.Namespace ?? string.Empty;
            entityType.SetSchema(ResolveModuleSchema(namespaceName) ?? "dbo");
        }

        modelBuilder.HasDefaultSchema("dbo");

        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
        {
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
                foreach (var property in entityType.GetProperties().Where(p => p.Name == "RowVersion"))
                {
                    property.IsConcurrencyToken = false;
                    property.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
                }
        }
    }

    private static string? ResolveModuleSchema(string namespaceName)
    {
        const string marker = "Modules.";
        var markerIndex = namespaceName.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return null;
        }

        var afterMarker = namespaceName[(markerIndex + marker.Length)..];
        var dotIndex = afterMarker.IndexOf('.');
        var moduleName = dotIndex < 0 ? afterMarker : afterMarker[..dotIndex];

        return moduleName.ToLowerInvariant();
    }
}
