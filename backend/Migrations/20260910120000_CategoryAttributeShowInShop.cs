using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(DataContext))]
    [Migration("20260910120000_CategoryAttributeShowInShop")]
    public partial class CategoryAttributeShowInShop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "showInShop",
                table: "categoryAttributes",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "showInShop",
                table: "categoryAttributes");
        }
    }
}
