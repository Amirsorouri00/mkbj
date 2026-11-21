using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mkbj.Database.Migrations
{
    /// <inheritdoc />
    public partial class PartnerOrderRequestJournal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "partner_order_requests",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    partner_id = table.Column<long>(type: "bigint", nullable: false),
                    key_id = table.Column<long>(type: "bigint", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    path = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    request_body = table.Column<string>(type: "text", nullable: false),
                    external_order_ids = table.Column<string[]>(type: "text[]", nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: true),
                    response_body = table.Column<string>(type: "text", nullable: true),
                    replayed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_partner_order_requests", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_partner_order_requests_correlation_id",
                schema: "sales",
                table: "partner_order_requests",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "ix_partner_order_requests_partner_id_id",
                schema: "sales",
                table: "partner_order_requests",
                columns: new[] { "partner_id", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "partner_order_requests",
                schema: "sales");
        }
    }
}
