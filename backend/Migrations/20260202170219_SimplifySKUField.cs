using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    public partial class SimplifySKUField : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // First, copy newSKU values to SKU column (newSKU was the primary field)
            migrationBuilder.Sql("UPDATE products SET SKU = newSKU WHERE newSKU IS NOT NULL AND newSKU != ''");

            // Drop the newSKU column (data is now in SKU)
            migrationBuilder.DropColumn(
                name: "newSKU",
                table: "products");

            // Rename SKU to sku (lowercase)
            migrationBuilder.RenameColumn(
                name: "SKU",
                table: "products",
                newName: "sku");

            // Set empty string for any null values
            migrationBuilder.Sql("UPDATE products SET sku = '' WHERE sku IS NULL");

            // Make the column non-nullable
            migrationBuilder.AlterColumn<string>(
                name: "sku",
                table: "products",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(20)",
                oldMaxLength: 20,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "sku",
                table: "products",
                newName: "SKU");

            migrationBuilder.AlterColumn<string>(
                name: "SKU",
                table: "products",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(20)",
                oldMaxLength: 20)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "newSKU",
                table: "products",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");
        }
    }
}
