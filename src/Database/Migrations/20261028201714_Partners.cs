using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EwaysVNext.Database.Migrations
{
    /// <inheritdoc />
    public partial class Partners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "partners");

            migrationBuilder.CreateTable(
                name: "partners",
                schema: "partners",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    scopes = table.Column<string[]>(type: "text[]", nullable: false),
                    rate_limit_per_minute = table.Column<int>(type: "integer", nullable: false),
                    bulk_max_items = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_partners", x => x.id);
                    table.CheckConstraint("ck_partners_bulk_max_items", "bulk_max_items BETWEEN 1 AND 1000");
                    table.CheckConstraint("ck_partners_rate_limit", "rate_limit_per_minute BETWEEN 1 AND 6000");
                });

            migrationBuilder.CreateTable(
                name: "api_keys",
                schema: "partners",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    partner_id = table.Column<long>(type: "bigint", nullable: false),
                    prefix = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    secret_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    scopes = table.Column<string[]>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_keys", x => x.id);
                    table.CheckConstraint("ck_api_keys_expiry_after_creation", "expires_at IS NULL OR expires_at > created_at");
                    table.ForeignKey(
                        name: "fk_api_keys_partners_partner_id",
                        column: x => x.partner_id,
                        principalSchema: "partners",
                        principalTable: "partners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_partner_id",
                schema: "partners",
                table: "api_keys",
                column: "partner_id");

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_prefix",
                schema: "partners",
                table: "api_keys",
                column: "prefix",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_keys",
                schema: "partners");

            migrationBuilder.DropTable(
                name: "partners",
                schema: "partners");
        }
    }
}
