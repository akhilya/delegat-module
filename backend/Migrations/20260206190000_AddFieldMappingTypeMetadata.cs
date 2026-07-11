using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    public partial class AddFieldMappingTypeMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent: columns may already exist from a partial migration run
            migrationBuilder.Sql("""
                SET @col_exists = (SELECT COUNT(*) FROM information_schema.columns
                    WHERE table_schema = DATABASE() AND table_name = 'fieldMappings' AND column_name = 'externalFieldType');
                SET @sql = IF(@col_exists = 0,
                    'ALTER TABLE `fieldMappings` ADD `externalFieldType` varchar(50) CHARACTER SET utf8mb4 NULL',
                    'SELECT 1');
                PREPARE stmt FROM @sql;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;
                """);

            migrationBuilder.Sql("""
                SET @col_exists = (SELECT COUNT(*) FROM information_schema.columns
                    WHERE table_schema = DATABASE() AND table_name = 'fieldMappings' AND column_name = 'externalFieldOptions');
                SET @sql = IF(@col_exists = 0,
                    'ALTER TABLE `fieldMappings` ADD `externalFieldOptions` longtext CHARACTER SET utf8mb4 NULL',
                    'SELECT 1');
                PREPARE stmt FROM @sql;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "externalFieldType",
                table: "fieldMappings");

            migrationBuilder.DropColumn(
                name: "externalFieldOptions",
                table: "fieldMappings");
        }
    }
}
