using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    public partial class MigrateBrandingToStoreScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "storeBrandingSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    StoreId = table.Column<int>(type: "int", nullable: false),
                    name = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    logoObjectKey = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    logoContentType = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    logoUrl = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    faviconObjectKey = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    faviconContentType = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    faviconUrl = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    primaryColor = table.Column<string>(type: "varchar(7)", maxLength: 7, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    accentColor = table.Column<string>(type: "varchar(7)", maxLength: 7, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    updatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storeBrandingSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_storeBrandingSettings_stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_storeBrandingSettings_StoreId",
                table: "storeBrandingSettings",
                column: "StoreId",
                unique: true);

            // Copy singleton instance branding onto every existing store (omit description).
            migrationBuilder.Sql("""
                INSERT INTO storeBrandingSettings
                    (StoreId, name, logoObjectKey, logoContentType, logoUrl,
                     faviconObjectKey, faviconContentType, faviconUrl,
                     primaryColor, accentColor, updatedAt)
                SELECT
                    s.Id,
                    COALESCE(i.name, 'Delegat.Module'),
                    i.logoObjectKey,
                    i.logoContentType,
                    i.logoUrl,
                    i.faviconObjectKey,
                    i.faviconContentType,
                    i.faviconUrl,
                    i.primaryColor,
                    i.accentColor,
                    COALESCE(i.updatedAt, UTC_TIMESTAMP(6))
                FROM stores s
                INNER JOIN instanceBrandingSettings i ON i.Id = 1;
                """);

            migrationBuilder.DropTable(
                name: "instanceBrandingSettings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "instanceBrandingSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    accentColor = table.Column<string>(type: "varchar(7)", maxLength: 7, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    faviconContentType = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    faviconObjectKey = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    faviconUrl = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    logoContentType = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    logoObjectKey = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    logoUrl = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    name = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    primaryColor = table.Column<string>(type: "varchar(7)", maxLength: 7, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    updatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instanceBrandingSettings", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql("""
                INSERT INTO instanceBrandingSettings
                    (Id, name, description, logoObjectKey, logoContentType, logoUrl,
                     faviconObjectKey, faviconContentType, faviconUrl,
                     primaryColor, accentColor, updatedAt)
                SELECT
                    1,
                    s.name,
                    NULL,
                    s.logoObjectKey,
                    s.logoContentType,
                    s.logoUrl,
                    s.faviconObjectKey,
                    s.faviconContentType,
                    s.faviconUrl,
                    s.primaryColor,
                    s.accentColor,
                    s.updatedAt
                FROM storeBrandingSettings s
                ORDER BY s.Id
                LIMIT 1;
                """);

            migrationBuilder.DropTable(
                name: "storeBrandingSettings");
        }
    }
}
