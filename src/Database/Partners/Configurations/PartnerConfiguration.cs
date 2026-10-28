using EwaysVNext.Modules.Partners.Application.Partners.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Partners.Configurations;

/// <summary>
/// Scopes are a Postgres <c>text[]</c> rather than a join table: a closed set validated in the
/// domain, never queried across partners, and always read with the row that owns them.
/// <para>
/// Mapped as a primitive collection over a <c>List&lt;string&gt;</c> backing field, with no value
/// converter. A converter from <c>IReadOnlySet&lt;string&gt;</c> does work on Npgsql but breaks the
/// EF InMemory model outright — InMemory composes its own <c>IEnumerable&lt;string&gt; -&gt;
/// string</c> converter onto it and throws while building the model, taking the Development
/// fallback and most of the test suite with it. `PersistenceContractTests` runs both providers
/// precisely to catch that.
/// </para>
/// </summary>
internal static class ScopeMapping
{
    public static void MapScopes<T>(EntityTypeBuilder<T> builder) where T : class =>
        builder.PrimitiveCollection<IReadOnlyList<string>>("Scopes")
            .HasField("_scopes")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("scopes")
            .IsRequired();
}

internal sealed class PartnerConfiguration : IEntityTypeConfiguration<Partner>
{
    public void Configure(EntityTypeBuilder<Partner> builder)
    {
        builder.HasKey(partner => partner.Id);
        builder.Property(partner => partner.Id).ValueGeneratedNever();

        builder.Property(partner => partner.Name)
            .HasMaxLength(PartnerLimits.NameMaxLength)
            .IsRequired();

        // Stored as its name, not its ordinal: a dump stays readable, and inserting a new enum
        // member in the middle later cannot silently re-label existing rows.
        builder.Property(partner => partner.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(partner => partner.RateLimitPerMinute).IsRequired();
        builder.Property(partner => partner.BulkMaxItems).IsRequired();
        builder.Property(partner => partner.CreatedAt).IsRequired();

        ScopeMapping.MapScopes(builder);

        // The domain enforces these too. They are repeated here because the domain only guards
        // the path through the application: a migration, a fix-up script or a future bug writing
        // the row directly would not pass through it.
        builder.ToTable("partners", table =>
        {
            table.HasCheckConstraint("ck_partners_rate_limit",
                $"rate_limit_per_minute BETWEEN {PartnerLimits.RateLimitMin} AND {PartnerLimits.RateLimitMax}");
            table.HasCheckConstraint("ck_partners_bulk_max_items",
                $"bulk_max_items BETWEEN {PartnerLimits.BulkMin} AND {PartnerLimits.BulkMax}");
        });

        builder.HasMany(partner => partner.Keys)
            .WithOne()
            .HasForeignKey(key => key.PartnerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(partner => partner.Keys)
            .HasField("_keys")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.HasKey(key => key.Id);
        builder.Property(key => key.Id).ValueGeneratedNever();

        builder.Property(key => key.PartnerId).IsRequired();

        builder.Property(key => key.Prefix)
            .HasMaxLength(8)
            .IsRequired();

        // Unique because the prefix is the lookup handle: two keys sharing one would make
        // FindByPrefixAsync ambiguous, and the authenticator would verify against whichever row
        // the database happened to return.
        builder.HasIndex(key => key.Prefix).IsUnique();

        builder.Property(key => key.SecretHash)
            .HasColumnType("bytea")
            .IsRequired();

        ScopeMapping.MapScopes(builder);

        builder.Property(key => key.CreatedAt).IsRequired();
        builder.Property(key => key.ExpiresAt);
        builder.Property(key => key.RevokedAt);

        builder.ToTable("api_keys", table =>
            table.HasCheckConstraint("ck_api_keys_expiry_after_creation",
                "expires_at IS NULL OR expires_at > created_at"));
    }
}
