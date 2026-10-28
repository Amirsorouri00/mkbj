using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AuthorizationService.Implementation.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "authorization");

            migrationBuilder.CreateTable(
                name: "Permissions",
                schema: "authorization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                schema: "authorization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Scopes",
                schema: "authorization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Scopes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PermissionRules",
                schema: "authorization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermissionRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PermissionRules_Permissions_PermissionId",
                        column: x => x.PermissionId,
                        principalSchema: "authorization",
                        principalTable: "Permissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PermissionRules_Roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "authorization",
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserRoleScopes",
                schema: "authorization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScopeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoleScopes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserRoleScopes_Roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "authorization",
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserRoleScopes_Scopes_ScopeId",
                        column: x => x.ScopeId,
                        principalSchema: "authorization",
                        principalTable: "Scopes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PermissionRuleScopes",
                schema: "authorization",
                columns: table => new
                {
                    PermissionRuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScopeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermissionRuleScopes", x => new { x.PermissionRuleId, x.ScopeId });
                    table.ForeignKey(
                        name: "FK_PermissionRuleScopes_PermissionRules_PermissionRuleId",
                        column: x => x.PermissionRuleId,
                        principalSchema: "authorization",
                        principalTable: "PermissionRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PermissionRuleScopes_Scopes_ScopeId",
                        column: x => x.ScopeId,
                        principalSchema: "authorization",
                        principalTable: "Scopes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "authorization",
                table: "Roles",
                columns: new[] { "Id", "Code", "CreatedAtUtc", "Name", "Rank", "UpdatedAtUtc" },
                values: new object[,]
                {
                    { new Guid("9452269e-9362-41a7-904f-5f3240403f66"), "Manager", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc), "Manager", 2, new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("aae2a4d1-aed0-4a94-8651-ab03f8c4f8da"), "User", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc), "User", 1, new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("dd981160-15d1-455c-b608-d5dc72b77629"), "Admin", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc), "Admin", 3, new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.InsertData(
                schema: "authorization",
                table: "Scopes",
                columns: new[] { "Id", "Code", "CreatedAtUtc", "Name", "UpdatedAtUtc" },
                values: new object[,]
                {
                    { new Guid("0f8e8424-10a1-4381-91dc-34b5a71be06e"), "Technology", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc), "Technology", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("6e82a8c8-6591-4967-9bca-a6f2beadfea4"), "Finance", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc), "Finance", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("c92e7cca-205d-4acd-964f-4a3362ae6ea8"), "Sell", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc), "Sell", new DateTime(2026, 9, 23, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_PermissionRules_PermissionId",
                schema: "authorization",
                table: "PermissionRules",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_PermissionRules_RoleId",
                schema: "authorization",
                table: "PermissionRules",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_PermissionRuleScopes_ScopeId",
                schema: "authorization",
                table: "PermissionRuleScopes",
                column: "ScopeId");

            migrationBuilder.CreateIndex(
                name: "IX_Permissions_Code",
                schema: "authorization",
                table: "Permissions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Roles_Code",
                schema: "authorization",
                table: "Roles",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Scopes_Code",
                schema: "authorization",
                table: "Scopes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserRoleScopes_RoleId",
                schema: "authorization",
                table: "UserRoleScopes",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoleScopes_ScopeId",
                schema: "authorization",
                table: "UserRoleScopes",
                column: "ScopeId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoleScopes_UserId_RoleId_ScopeId",
                schema: "authorization",
                table: "UserRoleScopes",
                columns: new[] { "UserId", "RoleId", "ScopeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PermissionRuleScopes",
                schema: "authorization");

            migrationBuilder.DropTable(
                name: "UserRoleScopes",
                schema: "authorization");

            migrationBuilder.DropTable(
                name: "PermissionRules",
                schema: "authorization");

            migrationBuilder.DropTable(
                name: "Scopes",
                schema: "authorization");

            migrationBuilder.DropTable(
                name: "Permissions",
                schema: "authorization");

            migrationBuilder.DropTable(
                name: "Roles",
                schema: "authorization");
        }
    }
}
