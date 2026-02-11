using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintO.Migrations
{
    /// <inheritdoc />
    public partial class AddProductFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "productFolders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    productId = table.Column<int>(type: "int", nullable: false),
                    parentFolderId = table.Column<int>(type: "int", nullable: true),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_productFolders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_productFolders_productFolders_parentFolderId",
                        column: x => x.parentFolderId,
                        principalTable: "productFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_productFolders_products_productId",
                        column: x => x.productId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "folderId",
                table: "files",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_productFolders_productId",
                table: "productFolders",
                column: "productId");

            migrationBuilder.CreateIndex(
                name: "IX_productFolders_parentFolderId",
                table: "productFolders",
                column: "parentFolderId");

            migrationBuilder.CreateIndex(
                name: "IX_files_folderId",
                table: "files",
                column: "folderId");

            migrationBuilder.AddForeignKey(
                name: "FK_files_productFolders_folderId",
                table: "files",
                column: "folderId",
                principalTable: "productFolders",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_files_productFolders_folderId",
                table: "files");

            migrationBuilder.DropIndex(
                name: "IX_files_folderId",
                table: "files");

            migrationBuilder.DropColumn(
                name: "folderId",
                table: "files");

            migrationBuilder.DropTable(
                name: "productFolders");
        }
    }
}
