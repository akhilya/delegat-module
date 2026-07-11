using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    public partial class AddAiModelIsEnabled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Guarded AddColumn: tolerate the column already existing (desynced history
            // after previous recovery runs that created tables/columns via raw SQL).
            // Uses information_schema check so the migration can succeed and record its ID.
            migrationBuilder.Sql(@"
SET @dbname = DATABASE();
SET @tablename = 'aiModelConfigs';
SET @columnname = 'isEnabled';
SET @preparedStatement = (SELECT IF(
  (
    SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
    WHERE table_name = @tablename
      AND table_schema = @dbname
      AND column_name = @columnname
  ) > 0,
  'SELECT 1',
  'ALTER TABLE `aiModelConfigs` ADD COLUMN `isEnabled` tinyint(1) NOT NULL DEFAULT 0'
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "isEnabled",
                table: "aiModelConfigs");
        }
    }
}
