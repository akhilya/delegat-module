using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    public partial class AddStoreSettingsAndProductPriceFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "extendedPriceParameters",
                table: "stores",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "timezone",
                table: "stores",
                type: "varchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "currency",
                table: "products",
                type: "varchar(3)",
                maxLength: 3,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "discount",
                table: "products",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "discountedPrice",
                table: "products",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "minPrice",
                table: "products",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "netPrice",
                table: "products",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "oldPrice",
                table: "products",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "storeCurrencies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    storeId = table.Column<int>(type: "int", nullable: false),
                    code = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    rateToRub = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    isEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    updatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storeCurrencies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_storeCurrencies_stores_storeId",
                        column: x => x.storeId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_storeCurrencies_storeId_code",
                table: "storeCurrencies",
                columns: new[] { "storeId", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "storeCurrencies");

            migrationBuilder.DropColumn(
                name: "extendedPriceParameters",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "timezone",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "currency",
                table: "products");

            migrationBuilder.DropColumn(
                name: "discount",
                table: "products");

            migrationBuilder.DropColumn(
                name: "discountedPrice",
                table: "products");

            migrationBuilder.DropColumn(
                name: "minPrice",
                table: "products");

            migrationBuilder.DropColumn(
                name: "netPrice",
                table: "products");

            migrationBuilder.DropColumn(
                name: "oldPrice",
                table: "products");
        }
    }
}
