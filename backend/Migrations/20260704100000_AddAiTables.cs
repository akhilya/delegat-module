using System;
using Delegat;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    [DbContext(typeof(DataContext))]
    [Migration("20260704100000_AddAiTables")]
    public partial class AddAiTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent creation: tolerate the table already existing in the DB
            // (can happen due to prior raw "IF NOT EXISTS" guards in later migrations,
            // docker volume state, or partial history application during AI schema iteration).
            // Using raw SQL with IF NOT EXISTS lets EF still record this migration ID
            // in __EFMigrationsHistory without failing.
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

            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS `aiOperations` (
    `Id`               int          NOT NULL AUTO_INCREMENT,
    `storeId`          int          NOT NULL,
    `userId`           int          NOT NULL,
    `operationType`    varchar(100) CHARACTER SET utf8mb4 NOT NULL,
    `description`      varchar(500) CHARACTER SET utf8mb4 NOT NULL,
    `rollbackDataJson` longtext     CHARACTER SET utf8mb4 NOT NULL,
    `canRollback`      tinyint(1)   NOT NULL DEFAULT 0,
    `status`           int          NOT NULL DEFAULT 0,
    `createdAt`        datetime(6)  NOT NULL,
    PRIMARY KEY (`Id`),
    KEY `IX_aiOperations_userId_storeId_createdAt` (`userId`, `storeId`, `createdAt`)
) CHARACTER SET=utf8mb4;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "aiOperations");
            migrationBuilder.DropTable(name: "aiModelConfigs");
        }
    }
}
