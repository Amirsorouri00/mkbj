using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthorizationService.Implementation.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditVerifyGrantSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "authorization",
                table: "permissions",
                columns: new[] { "id", "code", "created_at_utc", "description", "name", "updated_at_utc" },
                values: new object[] { new Guid("0a5e9b4c-3d71-4f28-9c60-5e8b2a1d7f43"), "audit.verify", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc), null, "Verify the audit log", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                schema: "authorization",
                table: "permission_rules",
                columns: new[] { "id", "permission_id", "role_id" },
                values: new object[] { new Guid("c1b47d82-6e95-4a30-8f17-2d9c6b5e8a14"), new Guid("0a5e9b4c-3d71-4f28-9c60-5e8b2a1d7f43"), new Guid("1a52e0a6-982a-4eac-843d-cf357b081742") });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "authorization",
                table: "permission_rules",
                keyColumn: "id",
                keyValue: new Guid("c1b47d82-6e95-4a30-8f17-2d9c6b5e8a14"));

            migrationBuilder.DeleteData(
                schema: "authorization",
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("0a5e9b4c-3d71-4f28-9c60-5e8b2a1d7f43"));
        }
    }
}
