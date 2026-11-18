using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    public partial class AddAiDurableTurns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "aiTurnJobs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    runId = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    conversationId = table.Column<int>(type: "int", nullable: false),
                    sessionId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    activeSessionId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    storeId = table.Column<int>(type: "int", nullable: false),
                    userId = table.Column<int>(type: "int", nullable: false),
                    payloadVersion = table.Column<int>(type: "int", nullable: false),
                    kind = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    engine = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    phase = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    requestJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    checkpointsJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    bindingJson = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    startedStep = table.Column<int>(type: "int", nullable: true),
                    executionEnded = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    cancelRequested = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    lastEventType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    lastSequence = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    leaseOwner = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    leaseExpiresAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    attempts = table.Column<int>(type: "int", nullable: false),
                    createdAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aiTurnJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_aiTurnJobs_aiConversations_conversationId",
                        column: x => x.conversationId,
                        principalTable: "aiConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "aiTurnJournalEvents",
                columns: table => new
                {
                    jobId = table.Column<long>(type: "bigint", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    eventJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aiTurnJournalEvents", x => new { x.jobId, x.sequence });
                    table.ForeignKey(
                        name: "FK_aiTurnJournalEvents_aiTurnJobs_jobId",
                        column: x => x.jobId,
                        principalTable: "aiTurnJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_aiTurnJobs_activeSessionId",
                table: "aiTurnJobs",
                column: "activeSessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_aiTurnJobs_conversationId",
                table: "aiTurnJobs",
                column: "conversationId");

            migrationBuilder.CreateIndex(
                name: "IX_aiTurnJobs_runId",
                table: "aiTurnJobs",
                column: "runId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_aiTurnJobs_sessionId_storeId_userId_Id",
                table: "aiTurnJobs",
                columns: new[] { "sessionId", "storeId", "userId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_aiTurnJobs_status_leaseExpiresAtUtc_Id",
                table: "aiTurnJobs",
                columns: new[] { "status", "leaseExpiresAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "aiTurnJournalEvents");

            migrationBuilder.DropTable(
                name: "aiTurnJobs");
        }
    }
}
