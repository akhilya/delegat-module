using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    public partial class AddCategoryAttributeGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Column may already exist from a previous partial migration
            migrationBuilder.Sql("""
                SET @col_exists = (SELECT COUNT(*) FROM information_schema.columns
                    WHERE table_schema = DATABASE() AND table_name = 'categoryAttributes' AND column_name = 'group');
                SET @sql = IF(@col_exists = 0,
                    'ALTER TABLE `categoryAttributes` ADD `group` varchar(100) CHARACTER SET utf8mb4 NULL',
                    'SELECT 1');
                PREPARE stmt FROM @sql;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_pendingImageDownloads_status_storeId",
                table: "pendingImageDownloads",
                columns: new[] { "status", "storeId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "group",
                table: "categoryAttributes");

            migrationBuilder.DropIndex(
                name: "IX_pendingImageDownloads_status_storeId",
                table: "pendingImageDownloads");
        }
    }
}
