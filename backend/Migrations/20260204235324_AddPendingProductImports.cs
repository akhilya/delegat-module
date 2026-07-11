using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingProductImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pendingProductImports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    connectionId = table.Column<int>(type: "int", nullable: false),
                    externalProductId = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    targetCategoryId = table.Column<int>(type: "int", nullable: true),
                    authorUserId = table.Column<int>(type: "int", nullable: false),
                    storeId = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    importedProductId = table.Column<int>(type: "int", nullable: true),
                    lastError = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    retryCount = table.Column<int>(type: "int", nullable: false),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    lastAttemptAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    completedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    batchId = table.Column<string>(type: "varchar(255)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    productName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pendingProductImports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pendingProductImports_products_importedProductId",
                        column: x => x.importedProductId,
                        principalTable: "products",
                        principalColumn: "Id");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_pendingProductImports_batchId",
                table: "pendingProductImports",
                column: "batchId");

            migrationBuilder.CreateIndex(
                name: "IX_pendingProductImports_importedProductId",
                table: "pendingProductImports",
                column: "importedProductId");

            migrationBuilder.CreateIndex(
                name: "IX_pendingProductImports_status_createdAt",
                table: "pendingProductImports",
                columns: new[] { "status", "createdAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pendingProductImports");
        }
    }
}
