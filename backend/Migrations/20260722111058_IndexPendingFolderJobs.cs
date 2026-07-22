using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    public partial class IndexPendingFolderJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "operationId",
                table: "pendingFolderJobs",
                type: "varchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_pendingFolderJobs_operationId",
                table: "pendingFolderJobs",
                column: "operationId");

            migrationBuilder.CreateIndex(
                name: "IX_pendingFolderJobs_status_createdAt",
                table: "pendingFolderJobs",
                columns: new[] { "status", "createdAt" });

            migrationBuilder.CreateIndex(
                name: "IX_pendingFolderJobs_storeId",
                table: "pendingFolderJobs",
                column: "storeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_pendingFolderJobs_operationId",
                table: "pendingFolderJobs");

            migrationBuilder.DropIndex(
                name: "IX_pendingFolderJobs_status_createdAt",
                table: "pendingFolderJobs");

            migrationBuilder.DropIndex(
                name: "IX_pendingFolderJobs_storeId",
                table: "pendingFolderJobs");

            migrationBuilder.AlterColumn<string>(
                name: "operationId",
                table: "pendingFolderJobs",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(64)",
                oldMaxLength: 64)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }
    }
}
