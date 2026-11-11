using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    public partial class AddFileAttributeSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "allowMultipleFiles",
                table: "storeAttributes",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "attributeDecisionsJson",
                table: "pendingCategoryImports",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "allowMultipleFiles",
                table: "categoryAttributes",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "productAttributeFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    attributeValueId = table.Column<int>(type: "int", nullable: false),
                    fileId = table.Column<int>(type: "int", nullable: false),
                    index = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_productAttributeFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_productAttributeFiles_files_fileId",
                        column: x => x.fileId,
                        principalTable: "files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_productAttributeFiles_productAttributeValues_attributeValueId",
                        column: x => x.attributeValueId,
                        principalTable: "productAttributeValues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "storeProductAttributeFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    attributeValueId = table.Column<int>(type: "int", nullable: false),
                    fileId = table.Column<int>(type: "int", nullable: false),
                    index = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storeProductAttributeFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_storeProductAttributeFiles_files_fileId",
                        column: x => x.fileId,
                        principalTable: "files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_storeProductAttributeFiles_storeProductAttributeValues_attri~",
                        column: x => x.attributeValueId,
                        principalTable: "storeProductAttributeValues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_productAttributeFiles_attributeValueId_fileId",
                table: "productAttributeFiles",
                columns: new[] { "attributeValueId", "fileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_productAttributeFiles_attributeValueId_index",
                table: "productAttributeFiles",
                columns: new[] { "attributeValueId", "index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_productAttributeFiles_fileId",
                table: "productAttributeFiles",
                column: "fileId");

            migrationBuilder.CreateIndex(
                name: "IX_storeProductAttributeFiles_attributeValueId_fileId",
                table: "storeProductAttributeFiles",
                columns: new[] { "attributeValueId", "fileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_storeProductAttributeFiles_attributeValueId_index",
                table: "storeProductAttributeFiles",
                columns: new[] { "attributeValueId", "index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_storeProductAttributeFiles_fileId",
                table: "storeProductAttributeFiles",
                column: "fileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "productAttributeFiles");

            migrationBuilder.DropTable(
                name: "storeProductAttributeFiles");

            migrationBuilder.DropColumn(
                name: "allowMultipleFiles",
                table: "storeAttributes");

            migrationBuilder.DropColumn(
                name: "attributeDecisionsJson",
                table: "pendingCategoryImports");

            migrationBuilder.DropColumn(
                name: "allowMultipleFiles",
                table: "categoryAttributes");
        }
    }
}
