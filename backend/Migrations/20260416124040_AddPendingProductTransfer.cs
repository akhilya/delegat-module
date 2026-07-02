using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingProductTransfer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pendingProductTransfers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    sourceProductId = table.Column<int>(type: "int", nullable: false),
                    sourceStoreId = table.Column<int>(type: "int", nullable: false),
                    targetStoreId = table.Column<int>(type: "int", nullable: false),
                    mode = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    newSku = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    categoryAction = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    categoryId = table.Column<int>(type: "int", nullable: true),
                    groupAction = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    groupId = table.Column<int>(type: "int", nullable: true),
                    groupName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    confirmBreakLinks = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    authorUserId = table.Column<int>(type: "int", nullable: false),
                    batchId = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<int>(type: "int", nullable: false),
                    lastError = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    newProductId = table.Column<int>(type: "int", nullable: true),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    startedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    finishedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pendingProductTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pendingProductTransfers_products_newProductId",
                        column: x => x.newProductId,
                        principalTable: "products",
                        principalColumn: "Id");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_pendingProductTransfers_batchId",
                table: "pendingProductTransfers",
                column: "batchId");

            migrationBuilder.CreateIndex(
                name: "IX_pendingProductTransfers_newProductId",
                table: "pendingProductTransfers",
                column: "newProductId");

            migrationBuilder.CreateIndex(
                name: "IX_pendingProductTransfers_status_createdAt",
                table: "pendingProductTransfers",
                columns: new[] { "status", "createdAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pendingProductTransfers");
        }
    }
}
