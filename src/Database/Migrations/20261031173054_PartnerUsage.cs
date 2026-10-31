using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EwaysVNext.Database.Migrations
{
    /// <inheritdoc />
    public partial class PartnerUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "api_key_usage",
                schema: "partners",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    partner_id = table.Column<long>(type: "bigint", nullable: false),
                    key_id = table.Column<long>(type: "bigint", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    path = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_key_usage", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_api_key_usage_at",
                schema: "partners",
                table: "api_key_usage",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "ix_api_key_usage_partner_id_at",
                schema: "partners",
                table: "api_key_usage",
                columns: new[] { "partner_id", "at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_key_usage",
                schema: "partners");
        }
    }
}
