using Microsoft.EntityFrameworkCore;

namespace Mkbj.Database;

public sealed class MkbjDbContext(DbContextOptions<MkbjDbContext> options) : DbContext(options)
{
    public DbSet<Mkbj.Modules.Sales.Application.Domain.Telemetry.PartnerOrderRequest> PartnerOrderRequests
        => Set<Mkbj.Modules.Sales.Application.Domain.Telemetry.PartnerOrderRequest>();
    public DbSet<Mkbj.Modules.Catalog.Application.Products.Domain.Product> Products
        => Set<Mkbj.Modules.Catalog.Application.Products.Domain.Product>();

    public DbSet<Mkbj.Modules.Catalog.Application.Categories.Domain.Category> Categories
        => Set<Mkbj.Modules.Catalog.Application.Categories.Domain.Category>();

    public DbSet<Mkbj.Modules.Catalog.Application.Categories.Domain.CategoryProduct> CategoryProducts
        => Set<Mkbj.Modules.Catalog.Application.Categories.Domain.CategoryProduct>();

    public DbSet<Mkbj.Modules.Catalog.Application.Brands.Domain.Brand> Brands
        => Set<Mkbj.Modules.Catalog.Application.Brands.Domain.Brand>();

    public DbSet<Mkbj.Modules.Catalog.Application.AttributeBlueprints.Domain.AttributeBlueprint> AttributeBlueprints
        => Set<Mkbj.Modules.Catalog.Application.AttributeBlueprints.Domain.AttributeBlueprint>();

    public DbSet<Mkbj.Modules.Catalog.Application.AttributeBlueprints.Domain.MeasurementUnit> MeasurementUnits
        => Set<Mkbj.Modules.Catalog.Application.AttributeBlueprints.Domain.MeasurementUnit>();
    public DbSet<Mkbj.Modules.Catalog.Application.CategoryAttributeAssignments.Domain.CategoryAttributeAssignment> CategoryAttributeAssignments
        => Set<Mkbj.Modules.Catalog.Application.CategoryAttributeAssignments.Domain.CategoryAttributeAssignment>();
    public DbSet<Mkbj.Modules.Catalog.Application.CategoryAttributeExclusions.Domain.CategoryAttributeExclusion> CategoryAttributeExclusions
        => Set<Mkbj.Modules.Catalog.Application.CategoryAttributeExclusions.Domain.CategoryAttributeExclusion>();

    public DbSet<Mkbj.Modules.Pricing.Application.Currencies.Domain.Currency> Currencies
        => Set<Mkbj.Modules.Pricing.Application.Currencies.Domain.Currency>();

    public DbSet<Mkbj.Modules.Pricing.Application.Fx.Domain.FxRate> FxRates
        => Set<Mkbj.Modules.Pricing.Application.Fx.Domain.FxRate>();

    public DbSet<Mkbj.Modules.Pricing.Application.Fx.Domain.FxRateOverride> FxRateOverrides
        => Set<Mkbj.Modules.Pricing.Application.Fx.Domain.FxRateOverride>();

    public DbSet<Mkbj.Modules.Pricing.Application.Prices.Domain.VariantPrice> VariantPrices
        => Set<Mkbj.Modules.Pricing.Application.Prices.Domain.VariantPrice>();

    public DbSet<Mkbj.Jobs.Implementation.JobRecord> Jobs
        => Set<Mkbj.Jobs.Implementation.JobRecord>();

    public DbSet<Mkbj.Jobs.Implementation.RecurringJobRecord> RecurringJobs
        => Set<Mkbj.Jobs.Implementation.RecurringJobRecord>();

    public DbSet<Mkbj.Modules.Partners.Application.Partners.Domain.Partner> Partners
        => Set<Mkbj.Modules.Partners.Application.Partners.Domain.Partner>();

    public DbSet<Mkbj.Modules.Partners.Application.Usage.ApiKeyUsage> ApiKeyUsages
        => Set<Mkbj.Modules.Partners.Application.Usage.ApiKeyUsage>();

    public DbSet<Mkbj.Modules.Settings.Application.Settings.Domain.SellerProfile> SellerProfiles
        => Set<Mkbj.Modules.Settings.Application.Settings.Domain.SellerProfile>();

    public DbSet<Mkbj.Modules.Settings.Application.Settings.Domain.PaymentInstruction> PaymentInstructions
        => Set<Mkbj.Modules.Settings.Application.Settings.Domain.PaymentInstruction>();

    public DbSet<Mkbj.Modules.Settings.Application.Settings.Domain.CommerceLimits> CommerceLimits
        => Set<Mkbj.Modules.Settings.Application.Settings.Domain.CommerceLimits>();

    public DbSet<Mkbj.Modules.Customer.Application.Customers.Domain.Customer> Customers
        => Set<Mkbj.Modules.Customer.Application.Customers.Domain.Customer>();

    public DbSet<Mkbj.Modules.Sales.Application.Domain.Orders.Order> Orders
        => Set<Mkbj.Modules.Sales.Application.Domain.Orders.Order>();

    public DbSet<Mkbj.Modules.Sales.Application.Domain.Carts.Cart> Carts
        => Set<Mkbj.Modules.Sales.Application.Domain.Carts.Cart>();

    public DbSet<Mkbj.Modules.Sales.Application.Domain.Telemetry.CompositionAttempt> CompositionAttempts
        => Set<Mkbj.Modules.Sales.Application.Domain.Telemetry.CompositionAttempt>();

    public DbSet<Mkbj.Modules.Sourcing.Application.Domain.ImportJobs.ImportJob> ImportJobs
        => Set<Mkbj.Modules.Sourcing.Application.Domain.ImportJobs.ImportJob>();

    public DbSet<Mkbj.Modules.Sourcing.Application.Domain.StagedRecords.StagedRecord> StagedRecords
        => Set<Mkbj.Modules.Sourcing.Application.Domain.StagedRecords.StagedRecord>();

    public DbSet<Mkbj.Modules.Sourcing.Application.Domain.SourceListings.SourceListing> SourceListings
        => Set<Mkbj.Modules.Sourcing.Application.Domain.SourceListings.SourceListing>();

    public DbSet<Mkbj.Modules.Sourcing.Application.Domain.SourcePolicies.SourcePolicy> SourcePolicies
        => Set<Mkbj.Modules.Sourcing.Application.Domain.SourcePolicies.SourcePolicy>();

    public DbSet<Mkbj.Audit.Implementation.AuditEvent> AuditEvents
        => Set<Mkbj.Audit.Implementation.AuditEvent>();

    public DbSet<Mkbj.Audit.Implementation.AuditSeal> AuditSeals
        => Set<Mkbj.Audit.Implementation.AuditSeal>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MkbjDbContext).Assembly);

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

        if (namespaceName.StartsWith("Mkbj.Jobs", StringComparison.Ordinal)) return "jobs";
        if (namespaceName.StartsWith("Mkbj.Audit", StringComparison.Ordinal)) return "audit";

        throw new InvalidOperationException(
            $"No schema is defined for '{clrType?.FullName ?? "<unknown>"}'. Entities belong to a module " +
            "(Mkbj.Modules.{Module}.*), to Mkbj.Jobs or to Mkbj.Audit.");
    }
}
