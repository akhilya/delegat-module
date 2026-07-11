using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    public partial class AddModifiersJsonToFieldMapping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent: column may already exist from a partial migration run
            migrationBuilder.Sql("""
                SET @col_exists = (SELECT COUNT(*) FROM information_schema.columns
                    WHERE table_schema = DATABASE() AND table_name = 'fieldMappings' AND column_name = 'modifiersJson');
                SET @sql = IF(@col_exists = 0,
                    'ALTER TABLE `fieldMappings` ADD `modifiersJson` longtext CHARACTER SET utf8mb4 NULL',
                    'SELECT 1');
                PREPARE stmt FROM @sql;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;
                """);

            // Data migration: convert existing transformExpression values to modifiersJson format.
            // Only converts rows that have a transformExpression but no modifiersJson yet.

            // multiply:N → [{"type":"multiply","params":{"factor":N}}]
            migrationBuilder.Sql("""
                UPDATE `fieldMappings`
                SET `modifiersJson` = CONCAT('[{"type":"multiply","params":{"factor":', SUBSTRING(`transformExpression`, 10), '}}]')
                WHERE `transformExpression` LIKE 'multiply:%'
                  AND (`modifiersJson` IS NULL OR `modifiersJson` = '');
                """);

            // divide:N → [{"type":"divide","params":{"divisor":N}}]
            migrationBuilder.Sql("""
                UPDATE `fieldMappings`
                SET `modifiersJson` = CONCAT('[{"type":"divide","params":{"divisor":', SUBSTRING(`transformExpression`, 8), '}}]')
                WHERE `transformExpression` LIKE 'divide:%'
                  AND (`modifiersJson` IS NULL OR `modifiersJson` = '');
                """);

            // add:N → [{"type":"add","params":{"addend":N}}]
            migrationBuilder.Sql("""
                UPDATE `fieldMappings`
                SET `modifiersJson` = CONCAT('[{"type":"add","params":{"addend":', SUBSTRING(`transformExpression`, 5), '}}]')
                WHERE `transformExpression` LIKE 'add:%'
                  AND (`modifiersJson` IS NULL OR `modifiersJson` = '');
                """);

            // truncate:N → [{"type":"truncate","params":{"maxLength":N}}]
            migrationBuilder.Sql("""
                UPDATE `fieldMappings`
                SET `modifiersJson` = CONCAT('[{"type":"truncate","params":{"maxLength":', SUBSTRING(`transformExpression`, 10), '}}]')
                WHERE `transformExpression` LIKE 'truncate:%'
                  AND (`modifiersJson` IS NULL OR `modifiersJson` = '');
                """);

            // round:N → [{"type":"round","params":{"decimals":N}}]
            migrationBuilder.Sql("""
                UPDATE `fieldMappings`
                SET `modifiersJson` = CONCAT('[{"type":"round","params":{"decimals":', SUBSTRING(`transformExpression`, 7), '}}]')
                WHERE `transformExpression` LIKE 'round:%'
                  AND (`modifiersJson` IS NULL OR `modifiersJson` = '');
                """);

            // uppercase → [{"type":"uppercase","params":{}}]
            migrationBuilder.Sql("""
                UPDATE `fieldMappings`
                SET `modifiersJson` = '[{"type":"uppercase","params":{}}]'
                WHERE `transformExpression` = 'uppercase'
                  AND (`modifiersJson` IS NULL OR `modifiersJson` = '');
                """);

            // lowercase → [{"type":"lowercase","params":{}}]
            migrationBuilder.Sql("""
                UPDATE `fieldMappings`
                SET `modifiersJson` = '[{"type":"lowercase","params":{}}]'
                WHERE `transformExpression` = 'lowercase'
                  AND (`modifiersJson` IS NULL OR `modifiersJson` = '');
                """);

            // trim → [{"type":"trim","params":{}}]
            migrationBuilder.Sql("""
                UPDATE `fieldMappings`
                SET `modifiersJson` = '[{"type":"trim","params":{}}]'
                WHERE `transformExpression` = 'trim'
                  AND (`modifiersJson` IS NULL OR `modifiersJson` = '');
                """);

            // floor → [{"type":"floor","params":{}}]
            migrationBuilder.Sql("""
                UPDATE `fieldMappings`
                SET `modifiersJson` = '[{"type":"floor","params":{}}]'
                WHERE `transformExpression` = 'floor'
                  AND (`modifiersJson` IS NULL OR `modifiersJson` = '');
                """);

            // ceiling → [{"type":"ceiling","params":{}}]
            migrationBuilder.Sql("""
                UPDATE `fieldMappings`
                SET `modifiersJson` = '[{"type":"ceiling","params":{}}]'
                WHERE `transformExpression` = 'ceiling'
                  AND (`modifiersJson` IS NULL OR `modifiersJson` = '');
                """);

            // append:X → [{"type":"append","params":{"suffix":"X"}}]
            migrationBuilder.Sql("""
                UPDATE `fieldMappings`
                SET `modifiersJson` = CONCAT('[{"type":"append","params":{"suffix":"', REPLACE(SUBSTRING(`transformExpression`, 8), '"', '\\"'), '"}}]')
                WHERE `transformExpression` LIKE 'append:%'
                  AND (`modifiersJson` IS NULL OR `modifiersJson` = '');
                """);

            // prepend:X → [{"type":"prepend","params":{"prefix":"X"}}]
            migrationBuilder.Sql("""
                UPDATE `fieldMappings`
                SET `modifiersJson` = CONCAT('[{"type":"prepend","params":{"prefix":"', REPLACE(SUBSTRING(`transformExpression`, 9), '"', '\\"'), '"}}]')
                WHERE `transformExpression` LIKE 'prepend:%'
                  AND (`modifiersJson` IS NULL OR `modifiersJson` = '');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "modifiersJson",
                table: "fieldMappings");
        }
    }
}
