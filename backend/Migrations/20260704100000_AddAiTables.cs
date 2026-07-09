using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    public partial class AddAiTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS `aiModelConfigs` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `name` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
    `apiUrl` varchar(500) CHARACTER SET utf8mb4 NOT NULL,
    `apiKeyEncrypted` longtext CHARACTER SET utf8mb4 NOT NULL,
    `modelId` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
    `isDefault` tinyint(1) NOT NULL DEFAULT 0,
    `provider` int NOT NULL DEFAULT 0,
    `maxTokens` int NOT NULL DEFAULT 8192,
    `createdAt` datetime(6) NOT NULL DEFAULT '0001-01-01 00:00:00',
    PRIMARY KEY (`Id`),
    KEY `IX_aiModelConfigs_isDefault` (`isDefault`)
) CHARACTER SET=utf8mb4;");

            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS `aiOperations` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `storeId` int NOT NULL,
    `userId` int NOT NULL,
    `operationType` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
    `description` varchar(500) CHARACTER SET utf8mb4 NOT NULL,
    `rollbackDataJson` longtext CHARACTER SET utf8mb4 NOT NULL,
    `canRollback` tinyint(1) NOT NULL DEFAULT 0,
    `status` int NOT NULL DEFAULT 0,
    `createdAt` datetime(6) NOT NULL DEFAULT '0001-01-01 00:00:00',
    PRIMARY KEY (`Id`),
    KEY `IX_aiOperations_userId_storeId_createdAt` (`userId`, `storeId`, `createdAt`)
) CHARACTER SET=utf8mb4;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS `aiModelConfigs`;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS `aiOperations`;");
        }
    }
}
