using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    public partial class AddAiModelProxyUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Guard: if AddAiTables was recorded in __EFMigrationsHistory but the table
            // was never actually created (e.g. raw-SQL IF NOT EXISTS masked the failure),
            // create it now so the AddColumn below does not crash.
            // EF's CreateTable has no IF NOT EXISTS equivalent, so raw SQL is necessary here.
            //
            // Note: the base AddAiTables migration itself has also been made idempotent with
            // CREATE TABLE IF NOT EXISTS so that the opposite drift (table exists, history row missing)
            // no longer crashes on startup. See 20260704100000_AddAiTables.cs.
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS `aiModelConfigs` (
    `Id`               int          NOT NULL AUTO_INCREMENT,
    `name`             varchar(100) CHARACTER SET utf8mb4 NOT NULL,
    `apiUrl`           varchar(500) CHARACTER SET utf8mb4 NOT NULL,
    `apiKeyEncrypted`  longtext     CHARACTER SET utf8mb4 NOT NULL,
    `modelId`          varchar(200) CHARACTER SET utf8mb4 NOT NULL,
    `isDefault`        tinyint(1)   NOT NULL DEFAULT 0,
    `provider`         int          NOT NULL DEFAULT 0,
    `maxTokens`        int          NOT NULL DEFAULT 8192,
    `createdAt`        datetime(6)  NOT NULL,
    PRIMARY KEY (`Id`),
    KEY `IX_aiModelConfigs_isDefault` (`isDefault`)
) CHARACTER SET=utf8mb4;");

            migrationBuilder.AddColumn<string>(
                name: "proxyUrl",
                table: "aiModelConfigs",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "proxyUrl",
                table: "aiModelConfigs");
        }
    }
}
