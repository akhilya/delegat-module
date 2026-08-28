using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    public partial class StoreAttributeValueConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "allowCustomValues",
                table: "storeAttributes",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "maxLength",
                table: "storeAttributes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "maxValue",
                table: "storeAttributes",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "maxValues",
                table: "storeAttributes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "minValue",
                table: "storeAttributes",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "validationPattern",
                table: "storeAttributes",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "allowCustomValues",
                table: "storeAttributes");

            migrationBuilder.DropColumn(
                name: "maxLength",
                table: "storeAttributes");

            migrationBuilder.DropColumn(
                name: "maxValue",
                table: "storeAttributes");

            migrationBuilder.DropColumn(
                name: "maxValues",
                table: "storeAttributes");

            migrationBuilder.DropColumn(
                name: "minValue",
                table: "storeAttributes");

            migrationBuilder.DropColumn(
                name: "validationPattern",
                table: "storeAttributes");
        }
    }
}
