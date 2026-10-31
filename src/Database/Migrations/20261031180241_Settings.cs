using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EwaysVNext.Database.Migrations
{
    /// <inheritdoc />
    public partial class Settings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "settings");

            migrationBuilder.CreateTable(
                name: "commerce_limits",
                schema: "settings",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    min_usd_minor = table.Column<long>(type: "bigint", nullable: false),
                    max_usd_minor = table.Column<long>(type: "bigint", nullable: false),
                    bulk_default_max = table.Column<int>(type: "integer", nullable: false),
                    composition_deadline_ms = table.Column<int>(type: "integer", nullable: false),
                    composition_max_lines = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_commerce_limits", x => x.id);
                    table.CheckConstraint("ck_commerce_limits_deadline", "composition_deadline_ms BETWEEN 1000 AND 9500");
                    table.CheckConstraint("ck_commerce_limits_range", "min_usd_minor > 0 AND max_usd_minor > min_usd_minor");
                    table.CheckConstraint("ck_commerce_limits_singleton", "id = 1");
                });

            migrationBuilder.CreateTable(
                name: "payment_instruction",
                schema: "settings",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    active_version = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_instruction", x => x.id);
                    table.CheckConstraint("ck_payment_instruction_singleton", "id = 1");
                });

            migrationBuilder.CreateTable(
                name: "seller_profile",
                schema: "settings",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    legal_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    uen = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    gst_registered = table.Column<bool>(type: "boolean", nullable: false),
                    invoice_footer = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    required_buyer_fields = table.Column<string[]>(type: "text[]", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seller_profile", x => x.id);
                    table.CheckConstraint("ck_seller_profile_singleton", "id = 1");
                });

            migrationBuilder.CreateTable(
                name: "payment_instruction_versions",
                schema: "settings",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    payment_instruction_id = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_instruction_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_payment_instruction_versions_payment_instruction_payment_in",
                        column: x => x.payment_instruction_id,
                        principalSchema: "settings",
                        principalTable: "payment_instruction",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "settings",
                table: "commerce_limits",
                columns: new[] { "id", "bulk_default_max", "composition_deadline_ms", "composition_max_lines", "max_usd_minor", "min_usd_minor" },
                values: new object[] { 1L, 100, 8000, 200, 100000000L, 1000L });

            migrationBuilder.InsertData(
                schema: "settings",
                table: "payment_instruction",
                columns: new[] { "id", "active_version" },
                values: new object[] { 1L, 1 });

            migrationBuilder.InsertData(
                schema: "settings",
                table: "seller_profile",
                columns: new[] { "id", "address", "email", "gst_registered", "invoice_footer", "legal_name", "required_buyer_fields", "uen" },
                values: new object[] { 1L, "Singapore", "billing@example.invalid", false, "", "Configure seller legal name", new[] { "email|phone" }, "UNSET" });

            migrationBuilder.InsertData(
                schema: "settings",
                table: "payment_instruction_versions",
                columns: new[] { "id", "created_at", "created_by", "payment_instruction_id", "text", "version" },
                values: new object[] { 1L, new DateTimeOffset(new DateTime(2026, 10, 28, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1L, "Pay by bank transfer using the details on your pro forma invoice. Quote the pro forma number as the payment reference.", 1 });

            migrationBuilder.CreateIndex(
                name: "ix_payment_instruction_versions_payment_instruction_id_version",
                schema: "settings",
                table: "payment_instruction_versions",
                columns: new[] { "payment_instruction_id", "version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "commerce_limits",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "payment_instruction_versions",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "seller_profile",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "payment_instruction",
                schema: "settings");
        }
    }
}
