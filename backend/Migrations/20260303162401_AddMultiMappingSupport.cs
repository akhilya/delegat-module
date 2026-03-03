using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintO.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiMappingSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // MySQL: must drop FK that depends on the index before dropping the index
            migrationBuilder.DropForeignKey(
                name: "FK_categoryMappings_categories_localCategoryId",
                table: "categoryMappings");

            migrationBuilder.DropIndex(
                name: "IX_categoryMappings_localCategoryId_connectionId",
                table: "categoryMappings");

            // Recreate as non-unique index (allow multiple mappings per category+connection)
            migrationBuilder.CreateIndex(
                name: "IX_categoryMappings_localCategoryId_connectionId",
                table: "categoryMappings",
                columns: new[] { "localCategoryId", "connectionId" });

            // Re-add the FK now that the new non-unique index exists
            migrationBuilder.AddForeignKey(
                name: "FK_categoryMappings_categories_localCategoryId",
                table: "categoryMappings",
                column: "localCategoryId",
                principalTable: "categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddColumn<int>(
                name: "categoryMappingId",
                table: "productMarketplaceLinks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "name",
                table: "categoryMappings",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_productMarketplaceLinks_categoryMappingId",
                table: "productMarketplaceLinks",
                column: "categoryMappingId");

            migrationBuilder.AddForeignKey(
                name: "FK_productMarketplaceLinks_categoryMappings_categoryMappingId",
                table: "productMarketplaceLinks",
                column: "categoryMappingId",
                principalTable: "categoryMappings",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_productMarketplaceLinks_categoryMappings_categoryMappingId",
                table: "productMarketplaceLinks");

            migrationBuilder.DropIndex(
                name: "IX_productMarketplaceLinks_categoryMappingId",
                table: "productMarketplaceLinks");

            migrationBuilder.DropColumn(
                name: "categoryMappingId",
                table: "productMarketplaceLinks");

            migrationBuilder.DropColumn(
                name: "name",
                table: "categoryMappings");

            // MySQL: drop FK before changing index back to unique
            migrationBuilder.DropForeignKey(
                name: "FK_categoryMappings_categories_localCategoryId",
                table: "categoryMappings");

            migrationBuilder.DropIndex(
                name: "IX_categoryMappings_localCategoryId_connectionId",
                table: "categoryMappings");

            migrationBuilder.CreateIndex(
                name: "IX_categoryMappings_localCategoryId_connectionId",
                table: "categoryMappings",
                columns: new[] { "localCategoryId", "connectionId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_categoryMappings_categories_localCategoryId",
                table: "categoryMappings",
                column: "localCategoryId",
                principalTable: "categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
