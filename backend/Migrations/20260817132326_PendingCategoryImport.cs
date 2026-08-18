using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    public partial class PendingCategoryImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "externalCategoryId",
                table: "pendingProductImports",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "forceImportWithoutMapping",
                table: "pendingProductImports",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "pendingCategoryImports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    connectionId = table.Column<int>(type: "int", nullable: false),
                    storeId = table.Column<int>(type: "int", nullable: false),
                    authorUserId = table.Column<int>(type: "int", nullable: false),
                    operationId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    externalCategoryId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    externalCategoryName = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    localCategoryName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    localCategoryId = table.Column<int>(type: "int", nullable: true),
                    selectedFieldKeysJson = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<int>(type: "int", nullable: false),
                    lastError = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    retryCount = table.Column<int>(type: "int", nullable: false),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    lastAttemptAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    completedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    resultLocalCategoryId = table.Column<int>(type: "int", nullable: true),
                    resultMappingId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pendingCategoryImports", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_pendingProductImports_connectionId_externalCategoryId",
                table: "pendingProductImports",
                columns: new[] { "connectionId", "externalCategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_pendingCategoryImports_connectionId_externalCategoryId_status",
                table: "pendingCategoryImports",
                columns: new[] { "connectionId", "externalCategoryId", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_pendingCategoryImports_operationId",
                table: "pendingCategoryImports",
                column: "operationId");

            migrationBuilder.CreateIndex(
                name: "IX_pendingCategoryImports_status_createdAt",
                table: "pendingCategoryImports",
                columns: new[] { "status", "createdAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pendingCategoryImports");

            migrationBuilder.DropIndex(
                name: "IX_pendingProductImports_connectionId_externalCategoryId",
                table: "pendingProductImports");

            migrationBuilder.DropColumn(
                name: "externalCategoryId",
                table: "pendingProductImports");

            migrationBuilder.DropColumn(
                name: "forceImportWithoutMapping",
                table: "pendingProductImports");
        }
    }
}
