using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    public partial class AddFolderShareTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "folderShareTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    productId = table.Column<int>(type: "int", nullable: false),
                    folderId = table.Column<int>(type: "int", nullable: true),
                    token = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    expiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    createdByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_folderShareTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_folderShareTokens_Users_createdByUserId",
                        column: x => x.createdByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_folderShareTokens_productFolders_folderId",
                        column: x => x.folderId,
                        principalTable: "productFolders",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_folderShareTokens_products_productId",
                        column: x => x.productId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_folderShareTokens_createdByUserId",
                table: "folderShareTokens",
                column: "createdByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_folderShareTokens_folderId",
                table: "folderShareTokens",
                column: "folderId");

            migrationBuilder.CreateIndex(
                name: "IX_folderShareTokens_productId",
                table: "folderShareTokens",
                column: "productId");

            migrationBuilder.CreateIndex(
                name: "IX_folderShareTokens_token",
                table: "folderShareTokens",
                column: "token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "folderShareTokens");
        }
    }
}
