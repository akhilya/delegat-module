using Delegat;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(DataContext))]
    [Migration("20260718120000_AddPublicFileTokens")]
    public partial class AddPublicFileTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "publicToken",
                table: "files",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "publicToken",
                table: "storeFiles",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_files_publicToken",
                table: "files",
                column: "publicToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_storeFiles_publicToken",
                table: "storeFiles",
                column: "publicToken",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_files_publicToken",
                table: "files");

            migrationBuilder.DropIndex(
                name: "IX_storeFiles_publicToken",
                table: "storeFiles");

            migrationBuilder.DropColumn(
                name: "publicToken",
                table: "files");

            migrationBuilder.DropColumn(
                name: "publicToken",
                table: "storeFiles");
        }
    }
}
