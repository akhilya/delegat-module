using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    public partial class AddProductMediaExportGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "productMediaExportGrants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    fileId = table.Column<int>(type: "int", nullable: false),
                    connectionId = table.Column<int>(type: "int", nullable: false),
                    token = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    revision = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    revokedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_productMediaExportGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_productMediaExportGrants_files_fileId",
                        column: x => x.fileId,
                        principalTable: "files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_productMediaExportGrants_marketplaceConnections_connectionId",
                        column: x => x.connectionId,
                        principalTable: "marketplaceConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_productMediaExportGrants_connectionId",
                table: "productMediaExportGrants",
                column: "connectionId");

            migrationBuilder.CreateIndex(
                name: "IX_productMediaExportGrants_fileId_connectionId",
                table: "productMediaExportGrants",
                columns: new[] { "fileId", "connectionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_productMediaExportGrants_token",
                table: "productMediaExportGrants",
                column: "token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "productMediaExportGrants");
        }
    }
}
