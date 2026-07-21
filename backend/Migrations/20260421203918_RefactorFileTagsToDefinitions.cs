using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    public partial class RefactorFileTagsToDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Clear old FileTag rows - they have inline tag+color that are incompatible
            // with the new tagDefinitionId FK. No data loss: the old tag feature had
            // no frontend UI, so any existing rows are test/leftover data.
            migrationBuilder.Sql("DELETE FROM `tags`;");

            // MySQL requires dropping FK constraint before its backing index.
            // The FK was auto-created by EF for the old FileTag.fileId column.
            migrationBuilder.Sql("ALTER TABLE `tags` DROP FOREIGN KEY `FK_tags_files_fileId`;");

            migrationBuilder.DropIndex(
                name: "IX_tags_fileId",
                table: "tags");

            migrationBuilder.DropColumn(
                name: "tag",
                table: "tags");

            migrationBuilder.RenameColumn(
                name: "color",
                table: "tags",
                newName: "tagDefinitionId");

            migrationBuilder.CreateTable(
                name: "tagDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    color = table.Column<int>(type: "int", nullable: false),
                    storeId = table.Column<int>(type: "int", nullable: false),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tagDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tagDefinitions_stores_storeId",
                        column: x => x.storeId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_tags_fileId_tagDefinitionId",
                table: "tags",
                columns: new[] { "fileId", "tagDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tags_tagDefinitionId",
                table: "tags",
                column: "tagDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_tagDefinitions_storeId",
                table: "tagDefinitions",
                column: "storeId");

            // Re-create FK on fileId (was dropped to allow index removal)
            migrationBuilder.AddForeignKey(
                name: "FK_tags_files_fileId",
                table: "tags",
                column: "fileId",
                principalTable: "files",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_tags_tagDefinitions_tagDefinitionId",
                table: "tags",
                column: "tagDefinitionId",
                principalTable: "tagDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_tags_tagDefinitions_tagDefinitionId",
                table: "tags");

            migrationBuilder.DropTable(
                name: "tagDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_tags_fileId_tagDefinitionId",
                table: "tags");

            migrationBuilder.DropIndex(
                name: "IX_tags_tagDefinitionId",
                table: "tags");

            migrationBuilder.RenameColumn(
                name: "tagDefinitionId",
                table: "tags",
                newName: "color");

            migrationBuilder.AddColumn<string>(
                name: "tag",
                table: "tags",
                type: "varchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_tags_fileId",
                table: "tags",
                column: "fileId");
        }
    }
}
