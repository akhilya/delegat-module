using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    public partial class CategoryAttributeValueConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "maxLength",
                table: "categoryAttributes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "maxValue",
                table: "categoryAttributes",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "minValue",
                table: "categoryAttributes",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "validationPattern",
                table: "categoryAttributes",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "maxLength",
                table: "categoryAttributes");

            migrationBuilder.DropColumn(
                name: "maxValue",
                table: "categoryAttributes");

            migrationBuilder.DropColumn(
                name: "minValue",
                table: "categoryAttributes");

            migrationBuilder.DropColumn(
                name: "validationPattern",
                table: "categoryAttributes");
        }
    }
}
