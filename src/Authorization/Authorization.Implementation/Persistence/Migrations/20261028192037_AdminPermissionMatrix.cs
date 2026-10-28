using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AuthorizationService.Implementation.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdminPermissionMatrix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "authorization",
                table: "permissions",
                columns: new[] { "id", "code", "created_at_utc", "description", "name", "updated_at_utc" },
                values: new object[,]
                {
                    { new Guid("1b1bc629-d7be-4ae9-8379-a4a96e01f2d4"), "customers.manage", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc), null, "Manage customers", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("23c77d5c-58d0-49b5-818e-83a23d108d38"), "orders.manage", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc), null, "Manage orders", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("37e59e3a-94fe-43d1-bf8f-d0f57cfc82d7"), "partners.manage", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc), null, "Manage partners", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("3e2384fc-8eb3-492b-bd4b-1519aa403015"), "currencies.manage", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc), null, "Manage currencies", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("456b4292-5990-4b68-8fd7-1a43193e2d38"), "catalog.manage", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc), null, "Manage catalog", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("630e0097-bb6c-4587-a8e1-fd0a561cdf32"), "admin-accounts.manage", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc), null, "Manage admin accounts", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("638688a3-9a51-4d23-8ee0-58e2efbfa773"), "imports.manage", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc), null, "Manage imports", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("6e892f33-023e-429b-b697-4ad522a6615a"), "settings.manage", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc), null, "Manage settings", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("740123bd-baa3-4f9b-9e3f-4d0b960aaab7"), "refunds.manage", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc), null, "Manage refunds", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("a445cf70-2f1e-498a-8282-f1e2881e84bc"), "fx.manage", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc), null, "Manage FX rates", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("d71fac8d-4fb0-4cda-bd94-482e056a01c9"), "payments.confirm", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc), null, "Confirm payments", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.InsertData(
                schema: "authorization",
                table: "roles",
                columns: new[] { "id", "code", "created_at_utc", "name", "rank", "updated_at_utc" },
                values: new object[,]
                {
                    { new Guid("1a52e0a6-982a-4eac-843d-cf357b081742"), "super_admin", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc), "Super Admin", 20, new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("7237c944-c6d2-4dcf-b0e4-ecf2768ce08d"), "staff", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc), "Staff", 10, new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.InsertData(
                schema: "authorization",
                table: "permission_rules",
                columns: new[] { "id", "permission_id", "role_id" },
                values: new object[,]
                {
                    { new Guid("33a6ce58-a48f-45cc-a481-5dd1ffc44af0"), new Guid("1b1bc629-d7be-4ae9-8379-a4a96e01f2d4"), new Guid("7237c944-c6d2-4dcf-b0e4-ecf2768ce08d") },
                    { new Guid("769fa513-a0db-42bc-8a67-0fcc5f6c3073"), new Guid("37e59e3a-94fe-43d1-bf8f-d0f57cfc82d7"), new Guid("1a52e0a6-982a-4eac-843d-cf357b081742") },
                    { new Guid("7a376c6a-5026-4fb1-ae11-e1abf946f0a9"), new Guid("740123bd-baa3-4f9b-9e3f-4d0b960aaab7"), new Guid("1a52e0a6-982a-4eac-843d-cf357b081742") },
                    { new Guid("82bae564-7381-47fd-b277-d5f232733acb"), new Guid("3e2384fc-8eb3-492b-bd4b-1519aa403015"), new Guid("1a52e0a6-982a-4eac-843d-cf357b081742") },
                    { new Guid("93883648-7f7f-4d32-b5fd-9ea6146f5fbf"), new Guid("456b4292-5990-4b68-8fd7-1a43193e2d38"), new Guid("7237c944-c6d2-4dcf-b0e4-ecf2768ce08d") },
                    { new Guid("9c44f926-9820-4e3d-89c5-18d07620a677"), new Guid("a445cf70-2f1e-498a-8282-f1e2881e84bc"), new Guid("1a52e0a6-982a-4eac-843d-cf357b081742") },
                    { new Guid("9f51a166-39c3-4eb8-a0c7-d1b7f877fe5f"), new Guid("23c77d5c-58d0-49b5-818e-83a23d108d38"), new Guid("7237c944-c6d2-4dcf-b0e4-ecf2768ce08d") },
                    { new Guid("ac294bdd-0ea0-4bce-b96a-4ce936236978"), new Guid("6e892f33-023e-429b-b697-4ad522a6615a"), new Guid("1a52e0a6-982a-4eac-843d-cf357b081742") },
                    { new Guid("e71446da-cab8-4db4-9ca9-d8407e505026"), new Guid("638688a3-9a51-4d23-8ee0-58e2efbfa773"), new Guid("7237c944-c6d2-4dcf-b0e4-ecf2768ce08d") },
                    { new Guid("ec3c722e-b2b4-4d5a-b9c9-04fbb5985862"), new Guid("630e0097-bb6c-4587-a8e1-fd0a561cdf32"), new Guid("1a52e0a6-982a-4eac-843d-cf357b081742") },
                    { new Guid("f6c02d4a-1b22-4ae3-baff-141a8960e3f9"), new Guid("d71fac8d-4fb0-4cda-bd94-482e056a01c9"), new Guid("7237c944-c6d2-4dcf-b0e4-ecf2768ce08d") }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "authorization",
                table: "permission_rules",
                keyColumn: "id",
                keyValue: new Guid("33a6ce58-a48f-45cc-a481-5dd1ffc44af0"));

            migrationBuilder.DeleteData(
                schema: "authorization",
                table: "permission_rules",
                keyColumn: "id",
                keyValue: new Guid("769fa513-a0db-42bc-8a67-0fcc5f6c3073"));

            migrationBuilder.DeleteData(
                schema: "authorization",
                table: "permission_rules",
                keyColumn: "id",
                keyValue: new Guid("7a376c6a-5026-4fb1-ae11-e1abf946f0a9"));

            migrationBuilder.DeleteData(
                schema: "authorization",
                table: "permission_rules",
                keyColumn: "id",
                keyValue: new Guid("82bae564-7381-47fd-b277-d5f232733acb"));

            migrationBuilder.DeleteData(
                schema: "authorization",
                table: "permission_rules",
                keyColumn: "id",
                keyValue: new Guid("93883648-7f7f-4d32-b5fd-9ea6146f5fbf"));

            migrationBuilder.DeleteData(
                schema: "authorization",
                table: "permission_rules",
                keyColumn: "id",
                keyValue: new Guid("9c44f926-9820-4e3d-89c5-18d07620a677"));

            migrationBuilder.DeleteData(
                schema: "authorization",
                table: "permission_rules",
                keyColumn: "id",
                keyValue: new Guid("9f51a166-39c3-4eb8-a0c7-d1b7f877fe5f"));

            migrationBuilder.DeleteData(
                schema: "authorization",
                table: "permission_rules",
                keyColumn: "id",
                keyValue: new Guid("ac294bdd-0ea0-4bce-b96a-4ce936236978"));

            migrationBuilder.DeleteData(
                schema: "authorization",
                table: "permission_rules",
                keyColumn: "id",
                keyValue: new Guid("e71446da-cab8-4db4-9ca9-d8407e505026"));

            migrationBuilder.DeleteData(
                schema: "authorization",
                table: "permission_rules",
                keyColumn: "id",
                keyValue: new Guid("ec3c722e-b2b4-4d5a-b9c9-04fbb5985862"));

            migrationBuilder.DeleteData(
                schema: "authorization",
                table: "permission_rules",
                keyColumn: "id",
                keyValue: new Guid("f6c02d4a-1b22-4ae3-baff-141a8960e3f9"));

            migrationBuilder.DeleteData(
                schema: "authorization",
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("1b1bc629-d7be-4ae9-8379-a4a96e01f2d4"));

            migrationBuilder.DeleteData(
                schema: "authorization",
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("23c77d5c-58d0-49b5-818e-83a23d108d38"));

            migrationBuilder.DeleteData(
                schema: "authorization",
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("37e59e3a-94fe-43d1-bf8f-d0f57cfc82d7"));

            migrationBuilder.DeleteData(
                schema: "authorization",
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("3e2384fc-8eb3-492b-bd4b-1519aa403015"));

            migrationBuilder.DeleteData(
                schema: "authorization",
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("456b4292-5990-4b68-8fd7-1a43193e2d38"));

            migrationBuilder.DeleteData(
                schema: "authorization",
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("630e0097-bb6c-4587-a8e1-fd0a561cdf32"));

            migrationBuilder.DeleteData(
                schema: "authorization",
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("638688a3-9a51-4d23-8ee0-58e2efbfa773"));

            migrationBuilder.DeleteData(
                schema: "authorization",
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("6e892f33-023e-429b-b697-4ad522a6615a"));

            migrationBuilder.DeleteData(
                schema: "authorization",
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("740123bd-baa3-4f9b-9e3f-4d0b960aaab7"));

            migrationBuilder.DeleteData(
                schema: "authorization",
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("a445cf70-2f1e-498a-8282-f1e2881e84bc"));

            migrationBuilder.DeleteData(
                schema: "authorization",
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("d71fac8d-4fb0-4cda-bd94-482e056a01c9"));

            migrationBuilder.DeleteData(
                schema: "authorization",
                table: "roles",
                keyColumn: "id",
                keyValue: new Guid("1a52e0a6-982a-4eac-843d-cf357b081742"));

            migrationBuilder.DeleteData(
                schema: "authorization",
                table: "roles",
                keyColumn: "id",
                keyValue: new Guid("7237c944-c6d2-4dcf-b0e4-ecf2768ce08d"));
        }
    }
}
