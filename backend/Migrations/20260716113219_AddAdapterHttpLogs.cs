using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    public partial class AddAdapterHttpLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "adapterHttpLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    storeId = table.Column<int>(type: "int", nullable: false),
                    connectionId = table.Column<int>(type: "int", nullable: false),
                    adapterKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    method = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    url = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    path = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    statusCode = table.Column<int>(type: "int", nullable: false),
                    durationMs = table.Column<int>(type: "int", nullable: false),
                    requestHeadersJson = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    requestBody = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    responseHeadersJson = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    responseBody = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    errorMessage = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    operationId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_adapterHttpLogs", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_adapterHttpLogs_adapterKey_createdAt",
                table: "adapterHttpLogs",
                columns: new[] { "adapterKey", "createdAt" });

            migrationBuilder.CreateIndex(
                name: "IX_adapterHttpLogs_connectionId_createdAt",
                table: "adapterHttpLogs",
                columns: new[] { "connectionId", "createdAt" });

            migrationBuilder.CreateIndex(
                name: "IX_adapterHttpLogs_statusCode_createdAt",
                table: "adapterHttpLogs",
                columns: new[] { "statusCode", "createdAt" });

            migrationBuilder.CreateIndex(
                name: "IX_adapterHttpLogs_storeId_createdAt",
                table: "adapterHttpLogs",
                columns: new[] { "storeId", "createdAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "adapterHttpLogs");
        }
    }
}
