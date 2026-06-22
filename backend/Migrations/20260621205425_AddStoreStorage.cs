using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintO.Migrations
{
    /// <inheritdoc />
    public partial class AddStoreStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "storeFolders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    storeId = table.Column<int>(type: "int", nullable: false),
                    parentFolderId = table.Column<int>(type: "int", nullable: true),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storeFolders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_storeFolders_storeFolders_parentFolderId",
                        column: x => x.parentFolderId,
                        principalTable: "storeFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_storeFolders_stores_storeId",
                        column: x => x.storeId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "storeFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    authorUserId = table.Column<int>(type: "int", nullable: false),
                    storeId = table.Column<int>(type: "int", nullable: false),
                    filePath = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    displayName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    uploadDateTime = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    fileType = table.Column<int>(type: "int", nullable: false),
                    contentType = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    length = table.Column<long>(type: "bigint", nullable: false),
                    folderId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storeFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_storeFiles_Users_authorUserId",
                        column: x => x.authorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_storeFiles_storeFolders_folderId",
                        column: x => x.folderId,
                        principalTable: "storeFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_storeFiles_stores_storeId",
                        column: x => x.storeId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "storeFolderShareTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    storeId = table.Column<int>(type: "int", nullable: false),
                    folderId = table.Column<int>(type: "int", nullable: true),
                    token = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    expiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    createdByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storeFolderShareTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_storeFolderShareTokens_Users_createdByUserId",
                        column: x => x.createdByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_storeFolderShareTokens_storeFolders_folderId",
                        column: x => x.folderId,
                        principalTable: "storeFolders",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_storeFolderShareTokens_stores_storeId",
                        column: x => x.storeId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "storeFileShareTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    fileId = table.Column<int>(type: "int", nullable: false),
                    token = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    expiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    createdByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storeFileShareTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_storeFileShareTokens_Users_createdByUserId",
                        column: x => x.createdByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_storeFileShareTokens_storeFiles_fileId",
                        column: x => x.fileId,
                        principalTable: "storeFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "storeFileTags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    fileId = table.Column<int>(type: "int", nullable: false),
                    tagDefinitionId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storeFileTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_storeFileTags_storeFiles_fileId",
                        column: x => x.fileId,
                        principalTable: "storeFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_storeFileTags_tagDefinitions_tagDefinitionId",
                        column: x => x.tagDefinitionId,
                        principalTable: "tagDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_storeFiles_authorUserId",
                table: "storeFiles",
                column: "authorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_storeFiles_folderId",
                table: "storeFiles",
                column: "folderId");

            migrationBuilder.CreateIndex(
                name: "IX_storeFiles_storeId",
                table: "storeFiles",
                column: "storeId");

            migrationBuilder.CreateIndex(
                name: "IX_storeFileShareTokens_createdByUserId",
                table: "storeFileShareTokens",
                column: "createdByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_storeFileShareTokens_fileId",
                table: "storeFileShareTokens",
                column: "fileId");

            migrationBuilder.CreateIndex(
                name: "IX_storeFileShareTokens_token",
                table: "storeFileShareTokens",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_storeFileTags_fileId_tagDefinitionId",
                table: "storeFileTags",
                columns: new[] { "fileId", "tagDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_storeFileTags_tagDefinitionId",
                table: "storeFileTags",
                column: "tagDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_storeFolders_parentFolderId",
                table: "storeFolders",
                column: "parentFolderId");

            migrationBuilder.CreateIndex(
                name: "IX_storeFolders_storeId",
                table: "storeFolders",
                column: "storeId");

            migrationBuilder.CreateIndex(
                name: "IX_storeFolderShareTokens_createdByUserId",
                table: "storeFolderShareTokens",
                column: "createdByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_storeFolderShareTokens_folderId",
                table: "storeFolderShareTokens",
                column: "folderId");

            migrationBuilder.CreateIndex(
                name: "IX_storeFolderShareTokens_storeId",
                table: "storeFolderShareTokens",
                column: "storeId");

            migrationBuilder.CreateIndex(
                name: "IX_storeFolderShareTokens_token",
                table: "storeFolderShareTokens",
                column: "token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "storeFileShareTokens");

            migrationBuilder.DropTable(
                name: "storeFileTags");

            migrationBuilder.DropTable(
                name: "storeFolderShareTokens");

            migrationBuilder.DropTable(
                name: "storeFiles");

            migrationBuilder.DropTable(
                name: "storeFolders");
        }
    }
}
