using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    public partial class PendingCatalogConnectJob : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pendingCatalogConnectJobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    storeId = table.Column<int>(type: "int", nullable: false),
                    connectionId = table.Column<int>(type: "int", nullable: false),
                    authorUserId = table.Column<int>(type: "int", nullable: false),
                    operationId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    kind = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    mode = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    totalItems = table.Column<int>(type: "int", nullable: false),
                    processedItems = table.Column<int>(type: "int", nullable: false),
                    queuedCount = table.Column<int>(type: "int", nullable: false),
                    payloadJson = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    resultJson = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    lastError = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    lastAttemptAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    completedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pendingCatalogConnectJobs", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_pendingCatalogConnectJobs_connectionId_kind_status",
                table: "pendingCatalogConnectJobs",
                columns: new[] { "connectionId", "kind", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_pendingCatalogConnectJobs_operationId",
                table: "pendingCatalogConnectJobs",
                column: "operationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pendingCatalogConnectJobs_status_createdAt",
                table: "pendingCatalogConnectJobs",
                columns: new[] { "status", "createdAt" });

            migrationBuilder.CreateIndex(
                name: "IX_pendingCatalogConnectJobs_storeId",
                table: "pendingCatalogConnectJobs",
                column: "storeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pendingCatalogConnectJobs");
        }
    }
}
