using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    public partial class AddAiGeneratedArtifacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "aiGeneratedArtifactImports",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    artifactId = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    storeId = table.Column<int>(type: "int", nullable: false),
                    userId = table.Column<int>(type: "int", nullable: false),
                    destination = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    productId = table.Column<int>(type: "int", nullable: true),
                    folderId = table.Column<int>(type: "int", nullable: true),
                    length = table.Column<long>(type: "bigint", nullable: false),
                    destinationKey = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    createdAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    state = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    fileId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aiGeneratedArtifactImports", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "aiGeneratedArtifacts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    storeId = table.Column<int>(type: "int", nullable: false),
                    userId = table.Column<int>(type: "int", nullable: false),
                    sandboxRunId = table.Column<int>(type: "int", nullable: false),
                    fileName = table.Column<string>(type: "varchar(180)", maxLength: 180, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    contentType = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    length = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    storageKey = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    createdAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    expiresAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    state = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aiGeneratedArtifacts", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_aiGeneratedArtifactImports_artifactId",
                table: "aiGeneratedArtifactImports",
                column: "artifactId");

            migrationBuilder.CreateIndex(
                name: "IX_aiGeneratedArtifactImports_storeId_userId_state",
                table: "aiGeneratedArtifactImports",
                columns: new[] { "storeId", "userId", "state" });

            migrationBuilder.CreateIndex(
                name: "IX_aiGeneratedArtifacts_storeId_userId_expiresAtUtc",
                table: "aiGeneratedArtifacts",
                columns: new[] { "storeId", "userId", "expiresAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "aiGeneratedArtifactImports");

            migrationBuilder.DropTable(
                name: "aiGeneratedArtifacts");
        }
    }
}
