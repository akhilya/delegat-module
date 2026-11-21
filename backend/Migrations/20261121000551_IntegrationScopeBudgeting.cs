using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    public partial class IntegrationScopeBudgeting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "activeLeases",
                table: "integrationScopeStates",
                type: "text",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<long>(
                name: "observedAtTicks",
                table: "integrationScopeStates",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "observedLimit",
                table: "integrationScopeStates",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "observedRemaining",
                table: "integrationScopeStates",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "usedUnits",
                table: "integrationScopeStates",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "windowEndTicks",
                table: "integrationScopeStates",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "windowStartTicks",
                table: "integrationScopeStates",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "activeLeases",
                table: "integrationScopeStates");

            migrationBuilder.DropColumn(
                name: "observedAtTicks",
                table: "integrationScopeStates");

            migrationBuilder.DropColumn(
                name: "observedLimit",
                table: "integrationScopeStates");

            migrationBuilder.DropColumn(
                name: "observedRemaining",
                table: "integrationScopeStates");

            migrationBuilder.DropColumn(
                name: "usedUnits",
                table: "integrationScopeStates");

            migrationBuilder.DropColumn(
                name: "windowEndTicks",
                table: "integrationScopeStates");

            migrationBuilder.DropColumn(
                name: "windowStartTicks",
                table: "integrationScopeStates");
        }
    }
}
