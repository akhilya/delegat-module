using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintO.Migrations
{
    /// <inheritdoc />
    public partial class AddCategoryAttributeGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "group",
                table: "categoryAttributes",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_pendingImageDownloads_status_storeId",
                table: "pendingImageDownloads",
                columns: new[] { "status", "storeId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "group",
                table: "categoryAttributes");

            migrationBuilder.DropIndex(
                name: "IX_pendingImageDownloads_status_storeId",
                table: "pendingImageDownloads");
        }
    }
}
