using Delegat;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(DataContext))]
    [Migration("20260730120000_AddPreferredExportModes")]
    public partial class AddPreferredExportModes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "preferredExportMode",
                table: "productMarketplaceLinks",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "full")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "preferredExportAllMode",
                table: "products",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "full")
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "preferredExportMode",
                table: "productMarketplaceLinks");

            migrationBuilder.DropColumn(
                name: "preferredExportAllMode",
                table: "products");
        }
    }
}
