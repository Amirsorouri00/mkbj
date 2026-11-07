using Microsoft.EntityFrameworkCore;

namespace EwaysVNext.Database;

public sealed class EwaysVNextDbContext(DbContextOptions<EwaysVNextDbContext> options) : DbContext(options)
{
    public DbSet<EwaysVNext.Modules.Catalog.Application.Products.Domain.Product> Products
        => Set<EwaysVNext.Modules.Catalog.Application.Products.Domain.Product>();

    public DbSet<EwaysVNext.Modules.Catalog.Application.Categories.Domain.Category> Categories
        => Set<EwaysVNext.Modules.Catalog.Application.Categories.Domain.Category>();

    public DbSet<EwaysVNext.Modules.Catalog.Application.Categories.Domain.CategoryProduct> CategoryProducts
        => Set<EwaysVNext.Modules.Catalog.Application.Categories.Domain.CategoryProduct>();

    public DbSet<EwaysVNext.Modules.Catalog.Application.Brands.Domain.Brand> Brands
        => Set<EwaysVNext.Modules.Catalog.Application.Brands.Domain.Brand>();

    public DbSet<EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Domain.AttributeBlueprint> AttributeBlueprints
        => Set<EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Domain.AttributeBlueprint>();

    public DbSet<EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Domain.MeasurementUnit> MeasurementUnits
        => Set<EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Domain.MeasurementUnit>();
    public DbSet<EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.Domain.CategoryAttributeAssignment> CategoryAttributeAssignments
        => Set<EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.Domain.CategoryAttributeAssignment>();
    public DbSet<EwaysVNext.Modules.Catalog.Application.CategoryAttributeExclusions.Domain.CategoryAttributeExclusion> CategoryAttributeExclusions
        => Set<EwaysVNext.Modules.Catalog.Application.CategoryAttributeExclusions.Domain.CategoryAttributeExclusion>();

    public DbSet<EwaysVNext.Modules.Pricing.Application.Currencies.Domain.Currency> Currencies
        => Set<EwaysVNext.Modules.Pricing.Application.Currencies.Domain.Currency>();

    public DbSet<EwaysVNext.Modules.Pricing.Application.Fx.Domain.FxRate> FxRates
        => Set<EwaysVNext.Modules.Pricing.Application.Fx.Domain.FxRate>();

    public DbSet<EwaysVNext.Modules.Pricing.Application.Fx.Domain.FxRateOverride> FxRateOverrides
        => Set<EwaysVNext.Modules.Pricing.Application.Fx.Domain.FxRateOverride>();

    public DbSet<EwaysVNext.Modules.Pricing.Application.Prices.Domain.VariantPrice> VariantPrices
        => Set<EwaysVNext.Modules.Pricing.Application.Prices.Domain.VariantPrice>();

    public DbSet<EwaysVNext.Jobs.Implementation.JobRecord> Jobs
        => Set<EwaysVNext.Jobs.Implementation.JobRecord>();

    public DbSet<EwaysVNext.Jobs.Implementation.RecurringJobRecord> RecurringJobs
        => Set<EwaysVNext.Jobs.Implementation.RecurringJobRecord>();

    public DbSet<EwaysVNext.Modules.Partners.Application.Partners.Domain.Partner> Partners
        => Set<EwaysVNext.Modules.Partners.Application.Partners.Domain.Partner>();

    public DbSet<EwaysVNext.Modules.Partners.Application.Usage.ApiKeyUsage> ApiKeyUsages
        => Set<EwaysVNext.Modules.Partners.Application.Usage.ApiKeyUsage>();

    public DbSet<EwaysVNext.Modules.Settings.Application.Settings.Domain.SellerProfile> SellerProfiles
        => Set<EwaysVNext.Modules.Settings.Application.Settings.Domain.SellerProfile>();

    public DbSet<EwaysVNext.Modules.Settings.Application.Settings.Domain.PaymentInstruction> PaymentInstructions
        => Set<EwaysVNext.Modules.Settings.Application.Settings.Domain.PaymentInstruction>();

    public DbSet<EwaysVNext.Modules.Settings.Application.Settings.Domain.CommerceLimits> CommerceLimits
        => Set<EwaysVNext.Modules.Settings.Application.Settings.Domain.CommerceLimits>();

    public DbSet<EwaysVNext.Modules.Customer.Application.Customers.Domain.Customer> Customers
        => Set<EwaysVNext.Modules.Customer.Application.Customers.Domain.Customer>();

    public DbSet<EwaysVNext.Modules.Sales.Application.Domain.Orders.Order> Orders
        => Set<EwaysVNext.Modules.Sales.Application.Domain.Orders.Order>();

    public DbSet<EwaysVNext.Modules.Sales.Application.Domain.Carts.Cart> Carts
        => Set<EwaysVNext.Modules.Sales.Application.Domain.Carts.Cart>();

    public DbSet<EwaysVNext.Modules.Sales.Application.Domain.Telemetry.CompositionAttempt> CompositionAttempts
        => Set<EwaysVNext.Modules.Sales.Application.Domain.Telemetry.CompositionAttempt>();

    public DbSet<EwaysVNext.Audit.Implementation.AuditEvent> AuditEvents
        => Set<EwaysVNext.Audit.Implementation.AuditEvent>();

    public DbSet<EwaysVNext.Audit.Implementation.AuditSeal> AuditSeals
        => Set<EwaysVNext.Audit.Implementation.AuditSeal>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EwaysVNextDbContext).Assembly);

        modelBuilder.HasPostgresExtension("pg_trgm");
        modelBuilder.HasPostgresExtension("unaccent");

        Sales.Configurations.SalesSequences.Configure(modelBuilder);

        var isInMemory = Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory";

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var rootEntityType = entityType;
            while (rootEntityType.FindOwnership() is { } ownership)
            {
                rootEntityType = ownership.PrincipalEntityType;
            }

            entityType.SetSchema(ResolveSchema(rootEntityType.ClrType));

            foreach (var property in entityType.GetProperties().Where(p => p.Name == "RowVersion"))
            {
                if (isInMemory)
                {
                    // InMemory has no xmin and no concurrency story; keep it an inert property.
                    property.IsConcurrencyToken = false;

                    // OnAdd, not Never. Against Npgsql this column is xmin, which the database
                    // supplies, so seed data never carries a value for it. Marking it Never here
                    // makes it a required property with no value, and every HasData row in the
                    // model fails validation — which is how seeded settings broke 54 tests that
                    // have nothing to do with settings.
                    property.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAdd;
                }
                else
                {
                    property.SetColumnName("xmin");
                    property.SetColumnType("xid");
                    property.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAddOrUpdate;
                    property.IsConcurrencyToken = true;
                }
            }
        }
    }

    /// <summary>
    /// Schema per module, derived from the aggregate's CLR namespace. There is deliberately no
    /// fallback: a type whose home cannot be named would otherwise land in a shared default schema
    /// silently, which is how two modules end up sharing a table nobody meant to share.
    /// </summary>
    private static string ResolveSchema(Type? clrType)
    {
        var namespaceName = clrType?.Namespace ?? string.Empty;

        const string marker = "Modules.";
        var markerIndex = namespaceName.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex >= 0)
        {
            var afterMarker = namespaceName[(markerIndex + marker.Length)..];
            var dotIndex = afterMarker.IndexOf('.');
            return (dotIndex < 0 ? afterMarker : afterMarker[..dotIndex]).ToLowerInvariant();
        }

        if (namespaceName.StartsWith("EwaysVNext.Jobs", StringComparison.Ordinal)) return "jobs";
        if (namespaceName.StartsWith("EwaysVNext.Audit", StringComparison.Ordinal)) return "audit";

        throw new InvalidOperationException(
            $"No schema is defined for '{clrType?.FullName ?? "<unknown>"}'. Entities belong to a module " +
            "(EwaysVNext.Modules.{Module}.*), to EwaysVNext.Jobs or to EwaysVNext.Audit.");
    }
}
