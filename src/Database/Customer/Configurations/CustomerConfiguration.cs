using EwaysVNext.Modules.Customer.Application.Customers.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using CustomerAggregate = EwaysVNext.Modules.Customer.Application.Customers.Domain.Customer;

namespace EwaysVNext.Database.Customer.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<CustomerAggregate>
{
    public void Configure(EntityTypeBuilder<CustomerAggregate> builder)
    {
        builder.HasKey(customer => customer.Id);
        builder.Property(customer => customer.Id).ValueGeneratedNever();

        // Stored as names: the filtered indexes and the check constraint below are written
        // against these values, and an ordinal would make that SQL unreadable and fragile to a
        // new enum member being inserted in the middle.
        builder.Property(customer => customer.Kind)
            .HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Property(customer => customer.Status)
            .HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Property(customer => customer.IdentityUserId);
        builder.Property(customer => customer.PartnerId);
        builder.Property(customer => customer.ExternalCustomerId).HasMaxLength(100);

        builder.Property(customer => customer.Email).HasMaxLength(256);
        builder.Property(customer => customer.Phone).HasMaxLength(20);
        builder.Property(customer => customer.EmailVerified).IsRequired();

        builder.Property(customer => customer.FullName).HasMaxLength(200);
        builder.Property(customer => customer.CompanyName).HasMaxLength(200);
        builder.Property(customer => customer.RegistrationNumber).HasMaxLength(100);
        builder.Property(customer => customer.CreatedAt).IsRequired();

        builder.OwnsOne(customer => customer.BillingAddress, address =>
        {
            address.Property(value => value.Line1).HasColumnName("billing_line1").HasMaxLength(200);
            address.Property(value => value.Line2).HasColumnName("billing_line2").HasMaxLength(200);
            address.Property(value => value.City).HasColumnName("billing_city").HasMaxLength(100);
            address.Property(value => value.PostalCode).HasColumnName("billing_postal_code").HasMaxLength(20);
            address.Property(value => value.CountryIso2).HasColumnName("billing_country_iso2").HasMaxLength(2);
        });

        // Filtered, because the uniqueness only applies within a kind. Two partners may each use
        // "456" for their own customer, and a website customer has no external id at all — an
        // unfiltered index would collide on the nulls.
        builder.HasIndex(customer => new { customer.PartnerId, customer.ExternalCustomerId })
            .IsUnique()
            .HasFilter("kind = 'PartnerLinked'")
            .HasDatabaseName("ux_customers_partner_external");

        builder.HasIndex(customer => customer.IdentityUserId)
            .IsUnique()
            .HasFilter("kind = 'Website'")
            .HasDatabaseName("ux_customers_identity_user");

        // Deliberately NO unique index on email or phone. Two customers may share an address —
        // a company placing orders through a partner and the same person buying directly are two
        // records by design (PRD §3), and merging them is not something this system does.
        builder.HasIndex(customer => customer.Email);

        builder.HasMany(customer => customer.ContactChanges)
            .WithOne()
            .HasForeignKey(change => change.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(customer => customer.ContactChanges)
            .HasField("_contactChanges")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.ToTable("customers", table =>
            // The two kinds use different keys, and a row carrying both — or neither — would be
            // reachable by one lookup and invisible to the other. The domain cannot be bypassed
            // through the application, but a migration or fix-up script can.
            table.HasCheckConstraint("ck_customers_kind_keys",
                "(kind = 'Website' AND identity_user_id IS NOT NULL AND partner_id IS NULL AND external_customer_id IS NULL) OR "
                + "(kind = 'PartnerLinked' AND partner_id IS NOT NULL AND external_customer_id IS NOT NULL AND identity_user_id IS NULL)"));
    }
}

internal sealed class ContactChangeConfiguration : IEntityTypeConfiguration<ContactChange>
{
    public void Configure(EntityTypeBuilder<ContactChange> builder)
    {
        builder.HasKey(change => change.Id);
        builder.Property(change => change.Id).ValueGeneratedNever();

        builder.Property(change => change.CustomerId).IsRequired();
        builder.Property(change => change.Field).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(change => change.OldValue).HasMaxLength(256);
        builder.Property(change => change.NewValue).HasMaxLength(256);
        builder.Property(change => change.ChangedAt).IsRequired();
        builder.Property(change => change.SourcePartnerId);
        builder.Property(change => change.CorrelationId).HasMaxLength(128).IsRequired();

        // The question this table answers is "what happened to this customer, most recent first".
        builder.HasIndex(change => new { change.CustomerId, change.ChangedAt })
            .IsDescending(false, true);

        builder.ToTable("contact_changes");
    }
}
