using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EwaysVNext.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "sales");

            migrationBuilder.CreateSequence(
                name: "cn_number_seq",
                schema: "sales");

            migrationBuilder.CreateSequence(
                name: "inv_number_seq",
                schema: "sales");

            migrationBuilder.CreateSequence(
                name: "order_number_seq",
                schema: "sales");

            migrationBuilder.CreateSequence(
                name: "pf_number_seq",
                schema: "sales");

            migrationBuilder.CreateTable(
                name: "composition_attempts",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    partner_id = table.Column<long>(type: "bigint", nullable: true),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    target_minor = table.Column<long>(type: "bigint", nullable: false),
                    succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    error_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    duration_ms = table.Column<long>(type: "bigint", nullable: false),
                    lines = table.Column<int>(type: "integer", nullable: false),
                    iterations = table.Column<int>(type: "integer", nullable: false),
                    candidate_pool_size = table.Column<int>(type: "integer", nullable: false),
                    seed = table.Column<long>(type: "bigint", nullable: false),
                    strategy_version = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    recompositions = table.Column<int>(type: "integer", nullable: false),
                    order_id = table.Column<long>(type: "bigint", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_composition_attempts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "orders",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    partner_id = table.Column<long>(type: "bigint", nullable: true),
                    external_order_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    external_customer_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    request_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    total_minor = table.Column<long>(type: "bigint", nullable: false),
                    subtotal_minor = table.Column<long>(type: "bigint", nullable: false),
                    tax_minor = table.Column<long>(type: "bigint", nullable: false),
                    usd_equivalent_minor = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    order_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    payment_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    payment_source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    fulfillment_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    fulfillment_source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    buyer = table.Column<string>(type: "jsonb", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orders", x => x.id);
                    table.CheckConstraint("ck_orders_partner_fields", "(channel = 'PartnerApi') = (partner_id IS NOT NULL AND external_order_id IS NOT NULL AND request_fingerprint IS NOT NULL AND external_customer_id IS NOT NULL)");
                    table.CheckConstraint("ck_orders_total_positive", "total_minor > 0");
                    table.CheckConstraint("ck_orders_totals_add_up", "subtotal_minor + tax_minor = total_minor");
                });

            migrationBuilder.CreateTable(
                name: "order_composition_traces",
                schema: "sales",
                columns: table => new
                {
                    order_id = table.Column<long>(type: "bigint", nullable: false),
                    seed = table.Column<long>(type: "bigint", nullable: false),
                    strategy_version = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    iterations = table.Column<int>(type: "integer", nullable: false),
                    duration_ms = table.Column<long>(type: "bigint", nullable: false),
                    candidate_pool_size = table.Column<int>(type: "integer", nullable: false),
                    attempts = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_composition_traces", x => x.order_id);
                    table.ForeignKey(
                        name: "fk_order_composition_traces_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "sales",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_documents",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    content = table.Column<string>(type: "jsonb", nullable: false),
                    related_document_id = table.Column<long>(type: "bigint", nullable: true),
                    pdf_object_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    order_id = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_documents", x => x.id);
                    table.CheckConstraint("ck_order_documents_credit_note_link", "(type = 'CreditNote') = (related_document_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_order_documents_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "sales",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_lines",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    variant_id = table.Column<long>(type: "bigint", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    sku = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    product_title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    brand_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    category_path = table.Column<string[]>(type: "text[]", nullable: false),
                    attributes = table.Column<string>(type: "jsonb", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price_minor = table.Column<long>(type: "bigint", nullable: false),
                    line_total_minor = table.Column<long>(type: "bigint", nullable: false),
                    reference_price_minor = table.Column<long>(type: "bigint", nullable: false),
                    price_source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    fx_rate = table.Column<decimal>(type: "numeric(18,10)", nullable: true),
                    fx_rate_id = table.Column<long>(type: "bigint", nullable: true),
                    fx_override_id = table.Column<long>(type: "bigint", nullable: true),
                    fx_rate_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    fx_provenance = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    base_amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    base_currency = table.Column<string>(type: "char(3)", nullable: false),
                    deviation_bps = table.Column<int>(type: "integer", nullable: false),
                    band_enforced = table.Column<bool>(type: "boolean", nullable: false),
                    order_id = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_lines", x => x.id);
                    table.CheckConstraint("ck_order_lines_partner_band", "NOT band_enforced OR (unit_price_minor * 100 >= reference_price_minor * 95 AND unit_price_minor * 100 <= reference_price_minor * 105)");
                    table.CheckConstraint("ck_order_lines_quantity_positive", "quantity > 0");
                    table.CheckConstraint("ck_order_lines_total", "line_total_minor = quantity * unit_price_minor");
                    table.ForeignKey(
                        name: "fk_order_lines_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "sales",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_payments",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    method = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    verified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    order_id = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_payments", x => x.id);
                    table.CheckConstraint("ck_order_payments_amount_positive", "amount_minor > 0");
                    table.ForeignKey(
                        name: "fk_order_payments_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "sales",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_refunds",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    external_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    credit_note_document_id = table.Column<long>(type: "bigint", nullable: false),
                    order_id = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_refunds", x => x.id);
                    table.CheckConstraint("ck_order_refunds_amount_positive", "amount_minor > 0");
                    table.ForeignKey(
                        name: "fk_order_refunds_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "sales",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_composition_attempts_at",
                schema: "sales",
                table: "composition_attempts",
                column: "at",
                descending: []);

            migrationBuilder.CreateIndex(
                name: "ix_composition_attempts_failures",
                schema: "sales",
                table: "composition_attempts",
                column: "at",
                descending: [],
                filter: "NOT succeeded");

            migrationBuilder.CreateIndex(
                name: "ix_order_documents_order_id",
                schema: "sales",
                table: "order_documents",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ux_order_documents_type_number",
                schema: "sales",
                table: "order_documents",
                columns: new[] { "type", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_order_lines_order_id",
                schema: "sales",
                table: "order_lines",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_lines_product",
                schema: "sales",
                table: "order_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_payments_order_id",
                schema: "sales",
                table: "order_payments",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_refunds_order_id",
                schema: "sales",
                table: "order_refunds",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_orders_created",
                schema: "sales",
                table: "orders",
                columns: new[] { "created_at", "channel", "currency" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_customer",
                schema: "sales",
                table: "orders",
                columns: new[] { "customer_id", "channel", "id" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_orders_partner",
                schema: "sales",
                table: "orders",
                columns: new[] { "partner_id", "id" },
                descending: new[] { false, true },
                filter: "channel = 'PartnerApi'");

            migrationBuilder.CreateIndex(
                name: "ix_orders_pending",
                schema: "sales",
                table: "orders",
                column: "created_at",
                filter: "order_status = 'PendingPayment'");

            migrationBuilder.CreateIndex(
                name: "ux_orders_number",
                schema: "sales",
                table: "orders",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_orders_partner_external",
                schema: "sales",
                table: "orders",
                columns: new[] { "partner_id", "external_order_id" },
                unique: true,
                filter: "channel = 'PartnerApi'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "composition_attempts",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "order_composition_traces",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "order_documents",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "order_lines",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "order_payments",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "order_refunds",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "orders",
                schema: "sales");

            migrationBuilder.DropSequence(
                name: "cn_number_seq",
                schema: "sales");

            migrationBuilder.DropSequence(
                name: "inv_number_seq",
                schema: "sales");

            migrationBuilder.DropSequence(
                name: "order_number_seq",
                schema: "sales");

            migrationBuilder.DropSequence(
                name: "pf_number_seq",
                schema: "sales");
        }
    }
}
