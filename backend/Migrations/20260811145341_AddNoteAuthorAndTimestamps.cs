using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    public partial class AddNoteAuthorAndTimestamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "authorUserId",
                table: "notes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "createdAt",
                table: "notes",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "updatedAt",
                table: "notes",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateIndex(
                name: "IX_notes_authorUserId",
                table: "notes",
                column: "authorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_notes_createdAt",
                table: "notes",
                column: "createdAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_notes_authorUserId",
                table: "notes");

            migrationBuilder.DropIndex(
                name: "IX_notes_createdAt",
                table: "notes");

            migrationBuilder.DropColumn(
                name: "authorUserId",
                table: "notes");

            migrationBuilder.DropColumn(
                name: "createdAt",
                table: "notes");

            migrationBuilder.DropColumn(
                name: "updatedAt",
                table: "notes");
        }
    }
}
