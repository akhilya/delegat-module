using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    public partial class AddInstancePlatformEntitlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "instancePlatformEntitlements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    linked = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    planName = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    planSlug = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    subscriptionStatus = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    overagePurgeAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    updatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instancePlatformEntitlements", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "instancePlatformEntitlements");
        }
    }
}
