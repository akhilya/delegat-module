using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "scheduledTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    storeId = table.Column<int>(type: "int", nullable: false),
                    authorUserId = table.Column<int>(type: "int", nullable: false),
                    name = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    actionId = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    parametersJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    intervalMinutes = table.Column<int>(type: "int", nullable: false),
                    timeZoneId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    isEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    nextRunAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scheduledTasks", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "scheduledTaskRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    scheduledTaskId = table.Column<int>(type: "int", nullable: false),
                    storeId = table.Column<int>(type: "int", nullable: false),
                    actionId = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    error = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    scheduledFor = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    startedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    finishedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scheduledTaskRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_scheduledTaskRuns_scheduledTasks_scheduledTaskId",
                        column: x => x.scheduledTaskId,
                        principalTable: "scheduledTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_scheduledTaskRuns_scheduledTaskId_startedAt",
                table: "scheduledTaskRuns",
                columns: new[] { "scheduledTaskId", "startedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_scheduledTaskRuns_storeId_startedAt",
                table: "scheduledTaskRuns",
                columns: new[] { "storeId", "startedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_scheduledTasks_isEnabled_nextRunAt",
                table: "scheduledTasks",
                columns: new[] { "isEnabled", "nextRunAt" });

            migrationBuilder.CreateIndex(
                name: "IX_scheduledTasks_storeId_createdAt",
                table: "scheduledTasks",
                columns: new[] { "storeId", "createdAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scheduledTaskRuns");

            migrationBuilder.DropTable(
                name: "scheduledTasks");
        }
    }
}
