using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketplaceIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "price",
                table: "products",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "marketplaceConnections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    adapterKey = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    credentialsJson = table.Column<string>(type: "varchar(5000)", maxLength: 5000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    settingsJson = table.Column<string>(type: "varchar(10000)", maxLength: 10000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    isActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    lastSyncAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    storeId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketplaceConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_marketplaceConnections_stores_storeId",
                        column: x => x.storeId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "categoryMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    connectionId = table.Column<int>(type: "int", nullable: false),
                    localCategoryId = table.Column<int>(type: "int", nullable: false),
                    externalCategoryId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    externalCategoryName = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_categoryMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_categoryMappings_categories_localCategoryId",
                        column: x => x.localCategoryId,
                        principalTable: "categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_categoryMappings_marketplaceConnections_connectionId",
                        column: x => x.connectionId,
                        principalTable: "marketplaceConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "productMarketplaceLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    productId = table.Column<int>(type: "int", nullable: false),
                    connectionId = table.Column<int>(type: "int", nullable: false),
                    externalProductId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    externalSku = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<int>(type: "int", nullable: false),
                    lastErrorMessage = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    lastSyncAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    externalPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    externalStock = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_productMarketplaceLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_productMarketplaceLinks_marketplaceConnections_connectionId",
                        column: x => x.connectionId,
                        principalTable: "marketplaceConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_productMarketplaceLinks_products_productId",
                        column: x => x.productId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "fieldMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    categoryMappingId = table.Column<int>(type: "int", nullable: false),
                    externalFieldKey = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    externalFieldName = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    isRequired = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    isEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    sourceType = table.Column<int>(type: "int", nullable: false),
                    sourceField = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    sourceAttributeId = table.Column<int>(type: "int", nullable: true),
                    staticValue = table.Column<string>(type: "varchar(5000)", maxLength: 5000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    transformExpression = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    sortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fieldMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fieldMappings_categoryMappings_categoryMappingId",
                        column: x => x.categoryMappingId,
                        principalTable: "categoryMappings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_categoryMappings_connectionId",
                table: "categoryMappings",
                column: "connectionId");

            migrationBuilder.CreateIndex(
                name: "IX_categoryMappings_localCategoryId_connectionId",
                table: "categoryMappings",
                columns: new[] { "localCategoryId", "connectionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fieldMappings_categoryMappingId",
                table: "fieldMappings",
                column: "categoryMappingId");

            migrationBuilder.CreateIndex(
                name: "IX_marketplaceConnections_storeId",
                table: "marketplaceConnections",
                column: "storeId");

            migrationBuilder.CreateIndex(
                name: "IX_productMarketplaceLinks_connectionId",
                table: "productMarketplaceLinks",
                column: "connectionId");

            migrationBuilder.CreateIndex(
                name: "IX_productMarketplaceLinks_externalProductId_connectionId",
                table: "productMarketplaceLinks",
                columns: new[] { "externalProductId", "connectionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_productMarketplaceLinks_productId_connectionId",
                table: "productMarketplaceLinks",
                columns: new[] { "productId", "connectionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fieldMappings");

            migrationBuilder.DropTable(
                name: "productMarketplaceLinks");

            migrationBuilder.DropTable(
                name: "categoryMappings");

            migrationBuilder.DropTable(
                name: "marketplaceConnections");

            migrationBuilder.DropColumn(
                name: "price",
                table: "products");
        }
    }
}
