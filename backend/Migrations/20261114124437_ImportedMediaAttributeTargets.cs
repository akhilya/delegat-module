using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    public partial class ImportedMediaAttributeTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "skipGallery",
                table: "pendingImageDownloads",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "targetAttributeId",
                table: "pendingImageDownloads",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "targetStoreAttributeId",
                table: "pendingImageDownloads",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "skipGallery",
                table: "pendingImageDownloads");

            migrationBuilder.DropColumn(
                name: "targetAttributeId",
                table: "pendingImageDownloads");

            migrationBuilder.DropColumn(
                name: "targetStoreAttributeId",
                table: "pendingImageDownloads");
        }
    }
}
