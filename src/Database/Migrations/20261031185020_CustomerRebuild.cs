using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EwaysVNext.Database.Migrations
{
    /// <inheritdoc />
    public partial class CustomerRebuild : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "customer");

            migrationBuilder.CreateTable(
                name: "customers",
                schema: "customer",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    identity_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    partner_id = table.Column<long>(type: "bigint", nullable: true),
                    external_customer_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    email_verified = table.Column<bool>(type: "boolean", nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    company_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    billing_line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    billing_line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    billing_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    billing_postal_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    billing_country_iso2 = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    registration_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customers", x => x.id);
                    table.CheckConstraint("ck_customers_kind_keys", "(kind = 'Website' AND identity_user_id IS NOT NULL AND partner_id IS NULL AND external_customer_id IS NULL) OR (kind = 'PartnerLinked' AND partner_id IS NOT NULL AND external_customer_id IS NOT NULL AND identity_user_id IS NULL)");
                });

            migrationBuilder.CreateTable(
                name: "contact_changes",
                schema: "customer",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    field = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    old_value = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    new_value = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source_partner_id = table.Column<long>(type: "bigint", nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contact_changes", x => x.id);
                    table.ForeignKey(
                        name: "fk_contact_changes_customers_customer_id",
                        column: x => x.customer_id,
                        principalSchema: "customer",
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contact_changes_customer_id_changed_at",
                schema: "customer",
                table: "contact_changes",
                columns: new[] { "customer_id", "changed_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_customers_email",
                schema: "customer",
                table: "customers",
                column: "email");

            migrationBuilder.CreateIndex(
                name: "ux_customers_identity_user",
                schema: "customer",
                table: "customers",
                column: "identity_user_id",
                unique: true,
                filter: "kind = 'Website'");

            migrationBuilder.CreateIndex(
                name: "ux_customers_partner_external",
                schema: "customer",
                table: "customers",
                columns: new[] { "partner_id", "external_customer_id" },
                unique: true,
                filter: "kind = 'PartnerLinked'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contact_changes",
                schema: "customer");

            migrationBuilder.DropTable(
                name: "customers",
                schema: "customer");
        }
    }
}
