using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeAdapterHttpLogCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_adapterHttpLogs_storeId_statusCode",
                table: "adapterHttpLogs",
                columns: new[] { "storeId", "statusCode" });

            migrationBuilder.CreateIndex(
                name: "IX_adapterHttpLogs_createdAt",
                table: "adapterHttpLogs",
                column: "createdAt");

            migrationBuilder.CreateIndex(
                name: "IX_apiRequestLogs_createdAt",
                table: "apiRequestLogs",
                column: "createdAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_adapterHttpLogs_storeId_statusCode",
                table: "adapterHttpLogs");

            migrationBuilder.DropIndex(name: "IX_adapterHttpLogs_createdAt", table: "adapterHttpLogs");
            migrationBuilder.DropIndex(name: "IX_apiRequestLogs_createdAt", table: "apiRequestLogs");
        }
    }
}
