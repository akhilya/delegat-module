using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    public partial class AddStoreBaseCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "rateToRub",
                table: "storeCurrencies",
                newName: "rateToBase");

            migrationBuilder.AddColumn<string>(
                name: "baseCurrency",
                table: "stores",
                type: "varchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "baseCurrency",
                table: "stores");

            migrationBuilder.RenameColumn(
                name: "rateToBase",
                table: "storeCurrencies",
                newName: "rateToRub");
        }
    }
}
