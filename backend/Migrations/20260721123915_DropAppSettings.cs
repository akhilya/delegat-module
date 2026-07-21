using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    public partial class DropAppSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "appSettings");

            migrationBuilder.DropIndex(
                name: "IX_storeFiles_publicToken",
                table: "storeFiles");

            migrationBuilder.DropIndex(
                name: "IX_files_publicToken",
                table: "files");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "appSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    updatedByUserId = table.Column<int>(type: "int", nullable: true),
                    description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    isEditable = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    key = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    label = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    updatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    value = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    valueType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_appSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_appSettings_Users_updatedByUserId",
                        column: x => x.updatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_storeFiles_publicToken",
                table: "storeFiles",
                column: "publicToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_files_publicToken",
                table: "files",
                column: "publicToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_appSettings_key",
                table: "appSettings",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_appSettings_updatedByUserId",
                table: "appSettings",
                column: "updatedByUserId");
        }
    }
}
