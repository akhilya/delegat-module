using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    public partial class AddAppSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "appSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    key = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    label = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    value = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    valueType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    isEditable = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    updatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updatedByUserId = table.Column<int>(type: "int", nullable: true)
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
                name: "IX_appSettings_key",
                table: "appSettings",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_appSettings_updatedByUserId",
                table: "appSettings",
                column: "updatedByUserId");

            migrationBuilder.Sql(@"
INSERT INTO `appSettings`
(`key`, `label`, `value`, `valueType`, `description`, `isEditable`, `updatedAt`, `updatedByUserId`)
VALUES
('MAX_PRODUCTS', 'Product limit', NULL, 'integer', 'Maximum number of products in a single store. Empty - no limit or value from MAX_PRODUCTS.', 1, UTC_TIMESTAMP(6), NULL),
('MAX_STORAGE_GB', 'Storage limit, GB', NULL, 'decimal', 'Maximum total size of store and product files. Empty - no limit or value from MAX_STORAGE_GB.', 1, UTC_TIMESTAMP(6), NULL),
('MAX_FILES', 'File limit', NULL, 'integer', 'Maximum number of store and product files. Empty - no limit or value from MAX_FILES.', 1, UTC_TIMESTAMP(6), NULL),
('MAX_CONNECTIONS', 'Connection limit', NULL, 'integer', 'Maximum number of active store adapters. Empty - no limit or value from MAX_CONNECTIONS.', 1, UTC_TIMESTAMP(6), NULL),
('GLOBAL_RATE_LIMIT', 'Global rate limit', NULL, 'integer', 'Number of requests per minute per IP. The current middleware applies the value after the application restarts.', 1, UTC_TIMESTAMP(6), NULL),
('AUTH_RATE_LIMIT', 'Auth rate limit', NULL, 'integer', 'Number of auth requests per minute per IP. The current middleware applies the value after the application restarts.', 1, UTC_TIMESTAMP(6), NULL);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "appSettings");
        }
    }
}
