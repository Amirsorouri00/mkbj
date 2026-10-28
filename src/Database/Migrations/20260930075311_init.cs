using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace EwaysVNext.Database.Migrations
{
    /// <inheritdoc />
    public partial class init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "location");

            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.EnsureSchema(
                name: "customer");

            migrationBuilder.CreateTable(
                name: "AddressStatus",
                schema: "location",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AddressStatus", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AttributeBlueprints",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DataType = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PreviousStatus = table.Column<int>(type: "int", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttributeBlueprints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Category",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    ParentId = table.Column<long>(type: "bigint", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Category", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Category_Category_ParentId",
                        column: x => x.ParentId,
                        principalSchema: "catalog",
                        principalTable: "Category",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CategoryAttributeAssignments",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    CategoryId = table.Column<long>(type: "bigint", nullable: false),
                    AttributeBlueprintId = table.Column<long>(type: "bigint", nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    IsFilterable = table.Column<bool>(type: "bit", nullable: false),
                    IsSearchable = table.Column<bool>(type: "bit", nullable: false),
                    IsInTitle = table.Column<bool>(type: "bit", nullable: false),
                    TitleOrder = table.Column<int>(type: "int", nullable: true),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    IsVariantGenerating = table.Column<bool>(type: "bit", nullable: false),
                    RemovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryAttributeAssignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomerStatus",
                schema: "customer",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerStatus", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MeasurementUnits",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeasurementUnits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Product",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    PriceAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PriceCurrency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Stock = table.Column<int>(type: "int", nullable: false),
                    ReservedStock = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Product", x => x.Id);
                    table.CheckConstraint("CK_Product_ReservedStock_NonNegative", "[ReservedStock] >= 0");
                    table.CheckConstraint("CK_Product_Stock_NonNegative", "[Stock] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "Province",
                schema: "location",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Province", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TeamMember",
                schema: "customer",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    IdentityUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Gender = table.Column<int>(type: "int", nullable: false),
                    NationalCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: false),
                    MobileNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PersonnelCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Department = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    JobTitle = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamMember", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VerificationMethod",
                schema: "customer",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerificationMethod", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VerificationStatus",
                schema: "customer",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerificationStatus", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Address",
                schema: "location",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    DistrictId = table.Column<int>(type: "int", nullable: true),
                    CityId = table.Column<int>(type: "int", nullable: false),
                    AddressText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    RecipientName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RecipientMobile = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    StatusId = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "getdate()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Address", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Address_AddressStatus_StatusId",
                        column: x => x.StatusId,
                        principalSchema: "location",
                        principalTable: "AddressStatus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CategoryProduct",
                schema: "catalog",
                columns: table => new
                {
                    CategoryId = table.Column<long>(type: "bigint", nullable: false),
                    ProductId = table.Column<long>(type: "bigint", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryProduct", x => new { x.CategoryId, x.ProductId });
                    table.ForeignKey(
                        name: "FK_CategoryProduct_Category_CategoryId",
                        column: x => x.CategoryId,
                        principalSchema: "catalog",
                        principalTable: "Category",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AttributeBlueprintOptions",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    AttributeBlueprintId = table.Column<long>(type: "bigint", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    MeasurementUnitId = table.Column<long>(type: "bigint", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttributeBlueprintOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttributeBlueprintOptions_AttributeBlueprints_AttributeBlueprintId",
                        column: x => x.AttributeBlueprintId,
                        principalSchema: "catalog",
                        principalTable: "AttributeBlueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AttributeBlueprintOptions_MeasurementUnits_MeasurementUnitId",
                        column: x => x.MeasurementUnitId,
                        principalSchema: "catalog",
                        principalTable: "MeasurementUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductReservation",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    ProductId = table.Column<long>(type: "bigint", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsCommitted = table.Column<bool>(type: "bit", nullable: false),
                    CommittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductReservation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductReservation_Product_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "City",
                schema: "location",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProvinceId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_City", x => x.Id);
                    table.ForeignKey(
                        name: "FK_City_Province_ProvinceId",
                        column: x => x.ProvinceId,
                        principalSchema: "location",
                        principalTable: "Province",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Customer",
                schema: "customer",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    IdentityUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MobileNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    NationalCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Gender = table.Column<int>(type: "int", nullable: true),
                    BirthDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EconomicCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    StatusId = table.Column<int>(type: "int", nullable: true),
                    VerificationStatusId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "date", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customer", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Customer_CustomerStatus_StatusId",
                        column: x => x.StatusId,
                        principalSchema: "customer",
                        principalTable: "CustomerStatus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Customer_VerificationStatus_VerificationStatusId",
                        column: x => x.VerificationStatusId,
                        principalSchema: "customer",
                        principalTable: "VerificationStatus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "District",
                schema: "location",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CityId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_District", x => x.Id);
                    table.ForeignKey(
                        name: "FK_District_City_CityId",
                        column: x => x.CityId,
                        principalSchema: "location",
                        principalTable: "City",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerAddress",
                schema: "customer",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    CustomerId = table.Column<long>(type: "bigint", nullable: false),
                    AddressId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerAddress", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerAddress_Address_AddressId",
                        column: x => x.AddressId,
                        principalSchema: "location",
                        principalTable: "Address",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerAddress_Customer_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "customer",
                        principalTable: "Customer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CustomerVerification",
                schema: "customer",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    CustomerId = table.Column<long>(type: "bigint", nullable: false),
                    NationalId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BirthDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    VerificationMethod = table.Column<int>(type: "int", nullable: true),
                    ShahkarReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ExpertId = table.Column<int>(type: "int", nullable: true),
                    NationalCardReceived = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    NationalCardReviewedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    VerifiedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "sysutcdatetime()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerVerification", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerVerification_Customer_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "customer",
                        principalTable: "Customer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerVerification_VerificationMethod_VerificationMethod",
                        column: x => x.VerificationMethod,
                        principalSchema: "customer",
                        principalTable: "VerificationMethod",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerVerificationDetail",
                schema: "customer",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    VerificationId = table.Column<long>(type: "bigint", nullable: false),
                    Firstname = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Lastname = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Fathername = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Gender = table.Column<int>(type: "int", nullable: false),
                    Liveness = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "sysutcdatetime()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerVerificationDetail", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerVerificationDetail_CustomerVerification_VerificationId",
                        column: x => x.VerificationId,
                        principalSchema: "customer",
                        principalTable: "CustomerVerification",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "location",
                table: "AddressStatus",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { 1, "Active" },
                    { 2, "Inactive" },
                    { 3, "Deleted" },
                    { 4, "Archived" }
                });

            migrationBuilder.InsertData(
                schema: "customer",
                table: "CustomerStatus",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { 1, "Active" },
                    { 2, "Inactive" },
                    { 3, "Suspended" },
                    { 4, "PendingApproval" },
                    { 5, "Approved" },
                    { 6, "Rejected" }
                });

            migrationBuilder.InsertData(
                schema: "customer",
                table: "VerificationMethod",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { 1, "None" },
                    { 2, "Shahkar" },
                    { 3, "Agent" }
                });

            migrationBuilder.InsertData(
                schema: "customer",
                table: "VerificationStatus",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { 1, "NotVerified" },
                    { 2, "Verified" },
                    { 3, "VerifiedByAgent" },
                    { 4, "Pending" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Address_StatusId",
                schema: "location",
                table: "Address",
                column: "StatusId");

            migrationBuilder.CreateIndex(
                name: "IX_AttributeBlueprintOptions_AttributeBlueprintId",
                schema: "catalog",
                table: "AttributeBlueprintOptions",
                column: "AttributeBlueprintId");

            migrationBuilder.CreateIndex(
                name: "IX_AttributeBlueprintOptions_MeasurementUnitId",
                schema: "catalog",
                table: "AttributeBlueprintOptions",
                column: "MeasurementUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_AttributeBlueprints_Name",
                schema: "catalog",
                table: "AttributeBlueprints",
                column: "Name",
                unique: true,
                filter: "[DeletedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Category_Hierarchy",
                schema: "catalog",
                table: "Category",
                columns: new[] { "ParentId", "Status", "DisplayOrder", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Category_ParentId_NormalizedName",
                schema: "catalog",
                table: "Category",
                columns: new[] { "ParentId", "NormalizedName" },
                unique: true,
                filter: "[Status] <> 2");

            migrationBuilder.CreateIndex(
                name: "IX_Category_Slug",
                schema: "catalog",
                table: "Category",
                column: "Slug",
                unique: true,
                filter: "[Status] <> 2");

            migrationBuilder.CreateIndex(
                name: "IX_CategoryAttributeAssignments_Category_Attribute_Active",
                schema: "catalog",
                table: "CategoryAttributeAssignments",
                columns: new[] { "CategoryId", "AttributeBlueprintId" },
                unique: true,
                filter: "[RemovedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CategoryAttributeAssignments_Category_Primary_Active",
                schema: "catalog",
                table: "CategoryAttributeAssignments",
                columns: new[] { "CategoryId", "IsPrimary" },
                filter: "[RemovedAt] IS NULL AND [IsPrimary] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_CategoryAttributeAssignments_CategoryId",
                schema: "catalog",
                table: "CategoryAttributeAssignments",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_CategoryProduct_ProductId_CategoryId",
                schema: "catalog",
                table: "CategoryProduct",
                columns: new[] { "ProductId", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_City_ProvinceId_Name",
                schema: "location",
                table: "City",
                columns: new[] { "ProvinceId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customer_MobileNumber",
                schema: "customer",
                table: "Customer",
                column: "MobileNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customer_StatusId",
                schema: "customer",
                table: "Customer",
                column: "StatusId");

            migrationBuilder.CreateIndex(
                name: "IX_Customer_VerificationStatusId",
                schema: "customer",
                table: "Customer",
                column: "VerificationStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAddress_AddressId",
                schema: "customer",
                table: "CustomerAddress",
                column: "AddressId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAddress_CustomerId",
                schema: "customer",
                table: "CustomerAddress",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerVerification_CustomerId",
                schema: "customer",
                table: "CustomerVerification",
                column: "CustomerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerVerification_VerificationMethod",
                schema: "customer",
                table: "CustomerVerification",
                column: "VerificationMethod");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerVerificationDetail_VerificationId",
                schema: "customer",
                table: "CustomerVerificationDetail",
                column: "VerificationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_District_CityId_Name",
                schema: "location",
                table: "District",
                columns: new[] { "CityId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementUnits_Name",
                schema: "catalog",
                table: "MeasurementUnits",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementUnits_Symbol",
                schema: "catalog",
                table: "MeasurementUnits",
                column: "Symbol",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Product_CreatedAt",
                schema: "catalog",
                table: "Product",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Product_Sku",
                schema: "catalog",
                table: "Product",
                column: "Sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductReservation_IsCommitted_CommittedAt",
                schema: "catalog",
                table: "ProductReservation",
                columns: new[] { "IsCommitted", "CommittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductReservation_IsCommitted_ExpiresAt",
                schema: "catalog",
                table: "ProductReservation",
                columns: new[] { "IsCommitted", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductReservation_ProductId",
                schema: "catalog",
                table: "ProductReservation",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Province_Name",
                schema: "location",
                table: "Province",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamMember_MobileNumber",
                schema: "customer",
                table: "TeamMember",
                column: "MobileNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamMember_NationalCode",
                schema: "customer",
                table: "TeamMember",
                column: "NationalCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamMember_PersonnelCode",
                schema: "customer",
                table: "TeamMember",
                column: "PersonnelCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttributeBlueprintOptions",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "CategoryAttributeAssignments",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "CategoryProduct",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "CustomerAddress",
                schema: "customer");

            migrationBuilder.DropTable(
                name: "CustomerVerificationDetail",
                schema: "customer");

            migrationBuilder.DropTable(
                name: "District",
                schema: "location");

            migrationBuilder.DropTable(
                name: "ProductReservation",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "TeamMember",
                schema: "customer");

            migrationBuilder.DropTable(
                name: "AttributeBlueprints",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "MeasurementUnits",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "Category",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "Address",
                schema: "location");

            migrationBuilder.DropTable(
                name: "CustomerVerification",
                schema: "customer");

            migrationBuilder.DropTable(
                name: "City",
                schema: "location");

            migrationBuilder.DropTable(
                name: "Product",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "AddressStatus",
                schema: "location");

            migrationBuilder.DropTable(
                name: "Customer",
                schema: "customer");

            migrationBuilder.DropTable(
                name: "VerificationMethod",
                schema: "customer");

            migrationBuilder.DropTable(
                name: "Province",
                schema: "location");

            migrationBuilder.DropTable(
                name: "CustomerStatus",
                schema: "customer");

            migrationBuilder.DropTable(
                name: "VerificationStatus",
                schema: "customer");
        }
    }
}
