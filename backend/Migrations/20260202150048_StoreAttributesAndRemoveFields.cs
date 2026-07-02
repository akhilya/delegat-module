using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    public partial class StoreAttributesAndRemoveFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "explicitContent",
                table: "products");

            migrationBuilder.DropColumn(
                name: "series",
                table: "products");

            migrationBuilder.DropColumn(
                name: "warehouseStorageNumber",
                table: "products");

            migrationBuilder.CreateTable(
                name: "storeAttributes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    key = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    type = table.Column<int>(type: "int", nullable: false),
                    isRequired = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    sortOrder = table.Column<int>(type: "int", nullable: false),
                    options = table.Column<string>(type: "varchar(5000)", maxLength: 5000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    defaultValue = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    storeId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storeAttributes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_storeAttributes_stores_storeId",
                        column: x => x.storeId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "storeProductAttributeValues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    productId = table.Column<int>(type: "int", nullable: false),
                    storeAttributeId = table.Column<int>(type: "int", nullable: false),
                    value = table.Column<string>(type: "varchar(10000)", maxLength: 10000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storeProductAttributeValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_storeProductAttributeValues_products_productId",
                        column: x => x.productId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_storeProductAttributeValues_storeAttributes_storeAttributeId",
                        column: x => x.storeAttributeId,
                        principalTable: "storeAttributes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_storeAttributes_storeId",
                table: "storeAttributes",
                column: "storeId");

            migrationBuilder.CreateIndex(
                name: "IX_storeProductAttributeValues_productId",
                table: "storeProductAttributeValues",
                column: "productId");

            migrationBuilder.CreateIndex(
                name: "IX_storeProductAttributeValues_storeAttributeId",
                table: "storeProductAttributeValues",
                column: "storeAttributeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "storeProductAttributeValues");

            migrationBuilder.DropTable(
                name: "storeAttributes");

            migrationBuilder.AddColumn<bool>(
                name: "explicitContent",
                table: "products",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "series",
                table: "products",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "warehouseStorageNumber",
                table: "products",
                type: "int",
                nullable: true);
        }
    }
}
