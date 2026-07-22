using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    public partial class AddInstanceLoggingSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "instanceLoggingSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    logErrorsEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    logApiRequestsEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    logAdapterHttpEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    updatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instanceLoggingSettings", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "instanceLoggingSettings");
        }
    }
}
