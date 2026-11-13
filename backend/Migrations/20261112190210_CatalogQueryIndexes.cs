using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    public partial class CatalogQueryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_productVariants_barcode_productId",
                table: "productVariants",
                columns: new[] { "barcode", "productId" });

            migrationBuilder.CreateIndex(
                name: "IX_products_storeId_barcode",
                table: "products",
                columns: new[] { "storeId", "barcode" });

            migrationBuilder.CreateIndex(
                name: "IX_products_storeId_deletedAt_categoryId_Id",
                table: "products",
                columns: new[] { "storeId", "deletedAt", "categoryId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_pendingImageDownloads_batchId",
                table: "pendingImageDownloads",
                column: "batchId")
                .Annotation("MySql:IndexPrefixLength", new[] { 191 });

            migrationBuilder.CreateIndex(
                name: "IX_pendingImageDownloads_status_createdAt",
                table: "pendingImageDownloads",
                columns: new[] { "status", "createdAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_productVariants_barcode_productId",
                table: "productVariants");

            migrationBuilder.DropIndex(
                name: "IX_products_storeId_barcode",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_products_storeId_deletedAt_categoryId_Id",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_pendingImageDownloads_batchId",
                table: "pendingImageDownloads");

            migrationBuilder.DropIndex(
                name: "IX_pendingImageDownloads_status_createdAt",
                table: "pendingImageDownloads");
        }
    }
}
