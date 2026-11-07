using System.Text.Json;
using EwaysVNext.Modules.Sales.Application.Domain.Documents;
using EwaysVNext.Modules.Sales.Application.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Sales.Configurations;

/// <summary>
/// The order aggregate's tables, and the constraints that hold its invariants in the database.
/// </summary>
/// <remarks>
/// The aggregate already enforces every rule here. They are repeated as check constraints
/// because the database is what survives a bug in the aggregate, a hand-written UPDATE during an
/// incident, or a future code path nobody has written yet — and an order whose lines do not total
/// what was charged is not something to discover from a customer.
/// </remarks>
internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders", table =>
        {
            table.HasCheckConstraint("ck_orders_total_positive", "total_minor > 0");

            table.HasCheckConstraint(
                "ck_orders_totals_add_up", "subtotal_minor + tax_minor = total_minor");

            // The partner fields are all-or-nothing. A partner order missing its fingerprint
            // could be charged twice; a website order carrying a partner id would be billed to
            // the wrong account.
            table.HasCheckConstraint(
                "ck_orders_partner_fields",
                "(channel = 'PartnerApi') = (partner_id IS NOT NULL AND external_order_id IS NOT NULL "
                + "AND request_fingerprint IS NOT NULL AND external_customer_id IS NOT NULL)");
        });

        builder.HasKey(order => order.Id);
        builder.Property(order => order.Id).HasColumnType("bigint").ValueGeneratedNever();

        builder.Property(order => order.Number).HasMaxLength(32).IsRequired();

        // Stored by name, so the value is readable in the check constraints above and in any
        // query someone writes during an incident.
        builder.Property(order => order.Channel).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(order => order.Status)
            .HasColumnName("order_status").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(order => order.PaymentStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(order => order.PaymentSource).HasConversion<string>().HasMaxLength(32);
        builder.Property(order => order.FulfillmentStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(order => order.FulfillmentSource).HasConversion<string>().HasMaxLength(32);

        builder.Property(order => order.PartnerId).HasColumnType("bigint");
        builder.Property(order => order.ExternalOrderId).HasMaxLength(128);
        builder.Property(order => order.ExternalCustomerId).HasMaxLength(128);
        builder.Property(order => order.RequestFingerprint).HasMaxLength(64);
        builder.Property(order => order.CustomerId).HasColumnType("bigint").IsRequired();
        builder.Property(order => order.Currency).HasColumnType("char(3)").IsRequired();

        builder.Property(order => order.TotalMinor).HasColumnType("bigint").IsRequired();
        builder.Property(order => order.SubtotalMinor).HasColumnType("bigint").IsRequired();
        builder.Property(order => order.TaxMinor).HasColumnType("bigint").IsRequired();
        builder.Property(order => order.UsdEquivalentMinor).HasColumnType("bigint").IsRequired();

        builder.Property(order => order.CreatedAt).IsRequired();
        builder.Property(order => order.CorrelationId).HasMaxLength(128).IsRequired();

        // The buyer as they were, stored whole. Joining to the customer at print time would make
        // an old invoice show a new address.
        builder.Property(order => order.Buyer)
            .HasConversion(
                buyer => JsonSerializer.Serialize(buyer, DocumentContentJson.Options),
                json => JsonSerializer.Deserialize<BuyerBlock>(json, DocumentContentJson.Options)!)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Ignore(order => order.DocumentStatus);
        builder.Ignore(order => order.CommercialInvoice);
        builder.Ignore(order => order.DomainEvents);

        builder.HasIndex(order => order.Number).IsUnique().HasDatabaseName("ux_orders_number");

        // The idempotency key. Filtered to partner orders because a website order has no
        // external id, and NULLs would otherwise collide or not, depending on the server's mood.
        builder.HasIndex(order => new { order.PartnerId, order.ExternalOrderId })
            .IsUnique()
            .HasFilter("channel = 'PartnerApi'")
            .HasDatabaseName("ux_orders_partner_external");

        builder.HasIndex(order => new { order.PartnerId, order.Id })
            .IsDescending(false, true)
            .HasFilter("channel = 'PartnerApi'")
            .HasDatabaseName("ix_orders_partner");

        builder.HasIndex(order => new { order.CustomerId, order.Channel, order.Id })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_orders_customer");

        builder.HasIndex(order => order.CreatedAt)
            .HasFilter("order_status = 'PendingPayment'")
            .HasDatabaseName("ix_orders_pending");

        builder.HasIndex(order => new { order.CreatedAt, order.Channel, order.Currency })
            .HasDatabaseName("ix_orders_created");

        ConfigureLines(builder);
        ConfigurePayments(builder);
        ConfigureDocuments(builder);
        ConfigureRefunds(builder);
        ConfigureTrace(builder);
    }

    private static void ConfigureLines(EntityTypeBuilder<Order> builder) =>
        builder.OwnsMany(order => order.Lines, line =>
        {
            line.ToTable("order_lines", table =>
            {
                table.HasCheckConstraint("ck_order_lines_quantity_positive", "quantity > 0");
                table.HasCheckConstraint(
                    "ck_order_lines_total", "line_total_minor = quantity * unit_price_minor");

                // The ±5% band, in the database. This is the one constraint here that protects
                // against something other than a bug: it is also what a hand-written UPDATE
                // during an incident runs into.
                table.HasCheckConstraint(
                    "ck_order_lines_partner_band",
                    "NOT band_enforced OR (unit_price_minor * 100 >= reference_price_minor * 95 "
                    + "AND unit_price_minor * 100 <= reference_price_minor * 105)");
            });

            line.WithOwner().HasForeignKey("order_id");
            line.HasKey(l => l.Id);
            line.Property(l => l.Id).HasColumnType("bigint").ValueGeneratedNever();

            line.Property(l => l.LineNo).IsRequired();
            line.Property(l => l.VariantId).HasColumnType("bigint").IsRequired();
            line.Property(l => l.ProductId).HasColumnType("bigint").IsRequired();
            line.Property(l => l.Sku).HasMaxLength(128).IsRequired();
            line.Property(l => l.ProductTitle).HasMaxLength(512).IsRequired();
            line.Property(l => l.BrandName).HasMaxLength(256).IsRequired();

            line.PrimitiveCollection(l => l.CategoryPath).HasColumnName("category_path").IsRequired();

            line.Property(l => l.Attributes)
                .HasConversion(
                    values => JsonSerializer.Serialize(values, DocumentContentJson.Options),
                    json => JsonSerializer.Deserialize<Dictionary<string, string>>(
                        json, DocumentContentJson.Options)!)
                .HasColumnType("jsonb")
                .IsRequired();

            line.Property(l => l.Quantity).IsRequired();
            line.Property(l => l.UnitPriceMinor).HasColumnType("bigint").IsRequired();
            line.Property(l => l.LineTotalMinor).HasColumnType("bigint").IsRequired();
            line.Property(l => l.DeviationBps).IsRequired();
            line.Property(l => l.BandEnforced).IsRequired();

            line.OwnsOne(l => l.Price, price =>
            {
                price.Property(p => p.ReferenceMinor).HasColumnName("reference_price_minor")
                    .HasColumnType("bigint").IsRequired();
                price.Property(p => p.Source).HasColumnName("price_source")
                    .HasConversion<string>().HasMaxLength(32).IsRequired();
                price.Property(p => p.FxRate).HasColumnName("fx_rate").HasColumnType("numeric(18,10)");
                price.Property(p => p.FxRateId).HasColumnName("fx_rate_id").HasColumnType("bigint");
                price.Property(p => p.FxOverrideId).HasColumnName("fx_override_id").HasColumnType("bigint");
                price.Property(p => p.FxRateAt).HasColumnName("fx_rate_at");
                price.Property(p => p.FxProvenance).HasColumnName("fx_provenance").HasMaxLength(64);
                price.Property(p => p.BaseAmountMinor).HasColumnName("base_amount_minor")
                    .HasColumnType("bigint").IsRequired();
                price.Property(p => p.BaseCurrency).HasColumnName("base_currency")
                    .HasColumnType("char(3)").IsRequired();
            });

            // Phase 4's popularity view aggregates sold lines by product.
            line.HasIndex(l => l.ProductId).HasDatabaseName("ix_order_lines_product");
        });

    private static void ConfigurePayments(EntityTypeBuilder<Order> builder) =>
        builder.OwnsMany(order => order.Payments, payment =>
        {
            payment.ToTable("order_payments", table =>
                table.HasCheckConstraint("ck_order_payments_amount_positive", "amount_minor > 0"));

            payment.WithOwner().HasForeignKey("order_id");
            payment.HasKey(p => p.Id);
            payment.Property(p => p.Id).HasColumnType("bigint").ValueGeneratedNever();

            payment.Property(p => p.Source).HasConversion<string>().HasMaxLength(32).IsRequired();
            payment.Property(p => p.Method).HasMaxLength(64);
            payment.Property(p => p.Reference).HasMaxLength(128);
            payment.Property(p => p.VerifiedBy);
            payment.Property(p => p.RecordedAt).IsRequired();
            payment.Property(p => p.AmountMinor).HasColumnType("bigint").IsRequired();
        });

    private static void ConfigureDocuments(EntityTypeBuilder<Order> builder) =>
        builder.OwnsMany(order => order.Documents, document =>
        {
            document.ToTable("order_documents", table =>
                // A credit note without the invoice it credits is unexplainable; an invoice with
                // one is a wiring mistake.
                table.HasCheckConstraint(
                    "ck_order_documents_credit_note_link",
                    "(type = 'CreditNote') = (related_document_id IS NOT NULL)"));

            document.WithOwner().HasForeignKey("order_id");
            document.HasKey(d => d.Id);
            document.Property(d => d.Id).HasColumnType("bigint").ValueGeneratedNever();

            document.Property(d => d.Type).HasConversion<string>().HasMaxLength(32).IsRequired();
            document.Property(d => d.Number).HasMaxLength(32).IsRequired();
            document.Property(d => d.IssuedAt).IsRequired();
            document.Property(d => d.RelatedDocumentId).HasColumnType("bigint");
            document.Property(d => d.PdfObjectKey).HasMaxLength(256).IsRequired();

            // Stored with the document's own serializer settings, not an ambient default: this
            // JSON has to deserialize to the same invoice years from now.
            document.Property(d => d.Content)
                .HasConversion(
                    content => JsonSerializer.Serialize(content, DocumentContentJson.Options),
                    json => JsonSerializer.Deserialize<DocumentContent>(
                        json, DocumentContentJson.Options)!)
                .HasColumnType("jsonb")
                .IsRequired();

            document.HasIndex(d => new { d.Type, d.Number })
                .IsUnique()
                .HasDatabaseName("ux_order_documents_type_number");
        });

    private static void ConfigureRefunds(EntityTypeBuilder<Order> builder) =>
        builder.OwnsMany(order => order.Refunds, refund =>
        {
            refund.ToTable("order_refunds", table =>
                table.HasCheckConstraint("ck_order_refunds_amount_positive", "amount_minor > 0"));

            refund.WithOwner().HasForeignKey("order_id");
            refund.HasKey(r => r.Id);
            refund.Property(r => r.Id).HasColumnType("bigint").ValueGeneratedNever();

            refund.Property(r => r.AmountMinor).HasColumnType("bigint").IsRequired();
            refund.Property(r => r.Reason).HasMaxLength(512).IsRequired();
            refund.Property(r => r.RecordedBy).IsRequired();
            refund.Property(r => r.RecordedAt).IsRequired();
            refund.Property(r => r.ExternalReference).HasMaxLength(128);
            refund.Property(r => r.CreditNoteDocumentId).HasColumnType("bigint").IsRequired();
        });

    private static void ConfigureTrace(EntityTypeBuilder<Order> builder) =>
        builder.OwnsOne(order => order.CompositionTrace, trace =>
        {
            trace.ToTable("order_composition_traces");
            trace.WithOwner().HasForeignKey("order_id");

            trace.Property(t => t.Seed).HasColumnType("bigint").IsRequired();
            trace.Property(t => t.StrategyVersion).HasMaxLength(16).IsRequired();
            trace.Property(t => t.Iterations).IsRequired();
            trace.Property(t => t.DurationMs).HasColumnType("bigint").IsRequired();
            trace.Property(t => t.CandidatePoolSize).IsRequired();
            trace.Property(t => t.Attempts).HasColumnType("jsonb").IsRequired();
        });
}
