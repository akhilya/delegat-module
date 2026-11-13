using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    public partial class AtomicCatalogImportAndBulkCheckpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "processedCount",
                table: "pendingProductBulkUpdates",
                type: "int",
                nullable: false,
                defaultValue: -1);

            migrationBuilder.CreateTable(
                name: "ProductImportReceipt",
                columns: table => new
                {
                    key = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    productId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductImportReceipt", x => x.key);
                    table.ForeignKey(
                        name: "FK_ProductImportReceipt_products_productId",
                        column: x => x.productId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ProductImportReceipt_productId",
                table: "ProductImportReceipt",
                column: "productId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductImportReceipt");

            migrationBuilder.DropColumn(
                name: "processedCount",
                table: "pendingProductBulkUpdates");
        }
    }
}
