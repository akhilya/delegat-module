using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketplaceOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "marketplaceOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    storeId = table.Column<int>(type: "int", nullable: false),
                    connectionId = table.Column<int>(type: "int", nullable: false),
                    marketplaceOrderId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    displayId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    channel = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, defaultValue: "shop")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    currency = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    orderCreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    orderUpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    syncedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    payloadJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketplaceOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_marketplaceOrders_marketplaceConnections_connectionId",
                        column: x => x.connectionId,
                        principalTable: "marketplaceConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "marketplaceOrderSyncStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    connectionId = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    lastPulledAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    lastError = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketplaceOrderSyncStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_marketplaceOrderSyncStates_marketplaceConnections_connection~",
                        column: x => x.connectionId,
                        principalTable: "marketplaceConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_marketplaceOrders_connectionId_status",
                table: "marketplaceOrders",
                columns: new[] { "connectionId", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_marketplaceOrders_displayId",
                table: "marketplaceOrders",
                column: "displayId");

            migrationBuilder.CreateIndex(
                name: "IX_marketplaceOrders_storeId_connectionId_marketplaceOrderId",
                table: "marketplaceOrders",
                columns: new[] { "storeId", "connectionId", "marketplaceOrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketplaceOrders_storeId_status",
                table: "marketplaceOrders",
                columns: new[] { "storeId", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_marketplaceOrderSyncStates_connectionId_status",
                table: "marketplaceOrderSyncStates",
                columns: new[] { "connectionId", "status" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "marketplaceOrders");

            migrationBuilder.DropTable(
                name: "marketplaceOrderSyncStates");
        }
    }
}
