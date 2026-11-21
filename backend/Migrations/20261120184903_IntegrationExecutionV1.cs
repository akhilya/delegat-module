using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    public partial class IntegrationExecutionV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "integrationAttempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    commandId = table.Column<int>(type: "int", nullable: false),
                    stageId = table.Column<int>(type: "int", nullable: false),
                    stageKey = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    dispatchId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    attemptNumber = table.Column<int>(type: "int", nullable: false),
                    requestKey = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    requestHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    outcome = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    effectCertainty = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    responseSummary = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    issuesJson = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    completedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integrationAttempts", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "integrationCommands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    commandId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    operationId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    storeId = table.Column<int>(type: "int", nullable: false),
                    authorUserId = table.Column<int>(type: "int", nullable: false),
                    authorApiKeyId = table.Column<int>(type: "int", nullable: true),
                    connectionId = table.Column<int>(type: "int", nullable: true),
                    scopeKey = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    kind = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    requestKey = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    requestHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    inputJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    inputVersion = table.Column<int>(type: "int", nullable: false),
                    recipeVersion = table.Column<int>(type: "int", nullable: false),
                    state = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    stopRequested = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    title = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    summary = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    finishedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integrationCommands", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "integrationStages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    commandId = table.Column<int>(type: "int", nullable: false),
                    itemKey = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    stageKey = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    kind = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    position = table.Column<int>(type: "int", nullable: false),
                    payloadJson = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    payloadFileKey = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    dependsOnJson = table.Column<string>(type: "text", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    batchKey = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    isEffectful = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    state = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    receiptJson = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    evidenceJson = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    issuesJson = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    nextCheckAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    attemptCount = table.Column<int>(type: "int", nullable: false),
                    claimVersion = table.Column<int>(type: "int", nullable: false),
                    claimedBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    claimedUntil = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    finishedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integrationStages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_integrationStages_integrationCommands_commandId",
                        column: x => x.commandId,
                        principalTable: "integrationCommands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_integrationAttempts_commandId",
                table: "integrationAttempts",
                column: "commandId");

            migrationBuilder.CreateIndex(
                name: "IX_integrationAttempts_dispatchId",
                table: "integrationAttempts",
                column: "dispatchId");

            migrationBuilder.CreateIndex(
                name: "IX_integrationAttempts_requestKey",
                table: "integrationAttempts",
                column: "requestKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_integrationAttempts_stageId_attemptNumber",
                table: "integrationAttempts",
                columns: new[] { "stageId", "attemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_integrationCommands_commandId",
                table: "integrationCommands",
                column: "commandId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_integrationCommands_operationId",
                table: "integrationCommands",
                column: "operationId");

            migrationBuilder.CreateIndex(
                name: "IX_integrationCommands_scopeKey_requestKey",
                table: "integrationCommands",
                columns: new[] { "scopeKey", "requestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_integrationCommands_storeId_state",
                table: "integrationCommands",
                columns: new[] { "storeId", "state" });

            migrationBuilder.CreateIndex(
                name: "IX_integrationStages_claimedUntil",
                table: "integrationStages",
                column: "claimedUntil");

            migrationBuilder.CreateIndex(
                name: "IX_integrationStages_commandId_itemKey_stageKey",
                table: "integrationStages",
                columns: new[] { "commandId", "itemKey", "stageKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_integrationStages_state_nextCheckAt",
                table: "integrationStages",
                columns: new[] { "state", "nextCheckAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "integrationAttempts");

            migrationBuilder.DropTable(
                name: "integrationStages");

            migrationBuilder.DropTable(
                name: "integrationCommands");
        }
    }
}
