using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AuthorizationService.Implementation.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "authorization");

            migrationBuilder.CreateTable(
                name: "permissions",
                schema: "authorization",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permissions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "authorization",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    rank = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "scopes",
                schema: "authorization",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scopes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "permission_rules",
                schema: "authorization",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permission_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_permission_rules_permissions_permission_id",
                        column: x => x.permission_id,
                        principalSchema: "authorization",
                        principalTable: "permissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_permission_rules_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "authorization",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_role_scopes",
                schema: "authorization",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_role_scopes", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_role_scopes_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "authorization",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_role_scopes_scopes_scope_id",
                        column: x => x.scope_id,
                        principalSchema: "authorization",
                        principalTable: "scopes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "permission_rule_scopes",
                schema: "authorization",
                columns: table => new
                {
                    permission_rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permission_rule_scopes", x => new { x.permission_rule_id, x.scope_id });
                    table.ForeignKey(
                        name: "fk_permission_rule_scopes_permission_rules_permission_rule_id",
                        column: x => x.permission_rule_id,
                        principalSchema: "authorization",
                        principalTable: "permission_rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_permission_rule_scopes_scopes_scope_id",
                        column: x => x.scope_id,
                        principalSchema: "authorization",
                        principalTable: "scopes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "authorization",
                table: "roles",
                columns: new[] { "id", "code", "created_at_utc", "name", "rank", "updated_at_utc" },
                values: new object[,]
                {
                    { new Guid("9452269e-9362-41a7-904f-5f3240403f66"), "Manager", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc), "Manager", 2, new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("aae2a4d1-aed0-4a94-8651-ab03f8c4f8da"), "User", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc), "User", 1, new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("dd981160-15d1-455c-b608-d5dc72b77629"), "Admin", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc), "Admin", 3, new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.InsertData(
                schema: "authorization",
                table: "scopes",
                columns: new[] { "id", "code", "created_at_utc", "name", "updated_at_utc" },
                values: new object[,]
                {
                    { new Guid("0f8e8424-10a1-4381-91dc-34b5a71be06e"), "Technology", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc), "Technology", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("6e82a8c8-6591-4967-9bca-a6f2beadfea4"), "Finance", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc), "Finance", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("c92e7cca-205d-4acd-964f-4a3362ae6ea8"), "Sell", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc), "Sell", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.CreateIndex(
                name: "ix_permission_rule_scopes_scope_id",
                schema: "authorization",
                table: "permission_rule_scopes",
                column: "scope_id");

            migrationBuilder.CreateIndex(
                name: "ix_permission_rules_permission_id",
                schema: "authorization",
                table: "permission_rules",
                column: "permission_id");

            migrationBuilder.CreateIndex(
                name: "ix_permission_rules_role_id",
                schema: "authorization",
                table: "permission_rules",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_permissions_code",
                schema: "authorization",
                table: "permissions",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_roles_code",
                schema: "authorization",
                table: "roles",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_scopes_code",
                schema: "authorization",
                table: "scopes",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_role_scopes_role_id",
                schema: "authorization",
                table: "user_role_scopes",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_role_scopes_scope_id",
                schema: "authorization",
                table: "user_role_scopes",
                column: "scope_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_role_scopes_user_id_role_id_scope_id",
                schema: "authorization",
                table: "user_role_scopes",
                columns: new[] { "user_id", "role_id", "scope_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "permission_rule_scopes",
                schema: "authorization");

            migrationBuilder.DropTable(
                name: "user_role_scopes",
                schema: "authorization");

            migrationBuilder.DropTable(
                name: "permission_rules",
                schema: "authorization");

            migrationBuilder.DropTable(
                name: "scopes",
                schema: "authorization");

            migrationBuilder.DropTable(
                name: "permissions",
                schema: "authorization");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "authorization");
        }
    }
}
