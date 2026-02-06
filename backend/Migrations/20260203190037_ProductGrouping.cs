using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintO.Migrations
{
    /// <inheritdoc />
    public partial class ProductGrouping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "productGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    storeId = table.Column<int>(type: "int", nullable: false),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_productGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_productGroups_stores_storeId",
                        column: x => x.storeId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "productGroupMarketplaceLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    productGroupId = table.Column<int>(type: "int", nullable: false),
                    connectionId = table.Column<int>(type: "int", nullable: false),
                    externalGroupId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    externalGroupName = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<int>(type: "int", nullable: false),
                    lastErrorMessage = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    lastSyncAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_productGroupMarketplaceLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_productGroupMarketplaceLinks_marketplaceConnections_connecti~",
                        column: x => x.connectionId,
                        principalTable: "marketplaceConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_productGroupMarketplaceLinks_productGroups_productGroupId",
                        column: x => x.productGroupId,
                        principalTable: "productGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "productGroupMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    groupId = table.Column<int>(type: "int", nullable: false),
                    productId = table.Column<int>(type: "int", nullable: false),
                    sortOrder = table.Column<int>(type: "int", nullable: false),
                    isVariantLeader = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    addedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_productGroupMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_productGroupMembers_productGroups_groupId",
                        column: x => x.groupId,
                        principalTable: "productGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_productGroupMembers_products_productId",
                        column: x => x.productId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_productGroupMarketplaceLinks_connectionId",
                table: "productGroupMarketplaceLinks",
                column: "connectionId");

            migrationBuilder.CreateIndex(
                name: "IX_productGroupMarketplaceLinks_externalGroupId_connectionId",
                table: "productGroupMarketplaceLinks",
                columns: new[] { "externalGroupId", "connectionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_productGroupMarketplaceLinks_productGroupId_connectionId",
                table: "productGroupMarketplaceLinks",
                columns: new[] { "productGroupId", "connectionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_productGroupMembers_groupId_productId",
                table: "productGroupMembers",
                columns: new[] { "groupId", "productId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_productGroupMembers_productId",
                table: "productGroupMembers",
                column: "productId");

            migrationBuilder.CreateIndex(
                name: "IX_productGroups_storeId",
                table: "productGroups",
                column: "storeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "productGroupMarketplaceLinks");

            migrationBuilder.DropTable(
                name: "productGroupMembers");

            migrationBuilder.DropTable(
                name: "productGroups");
        }
    }
}
