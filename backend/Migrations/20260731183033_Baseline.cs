using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delegat.Migrations
{
    /// <inheritdoc />
    public partial class Baseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "adapterHttpLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    storeId = table.Column<int>(type: "int", nullable: false),
                    connectionId = table.Column<int>(type: "int", nullable: false),
                    adapterKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    method = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    url = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    path = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    statusCode = table.Column<int>(type: "int", nullable: false),
                    durationMs = table.Column<int>(type: "int", nullable: false),
                    requestHeadersJson = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    requestBody = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    responseHeadersJson = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    responseBody = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    errorMessage = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    operationId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_adapterHttpLogs", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "aiConversations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    sessionId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    storeId = table.Column<int>(type: "int", nullable: false),
                    userId = table.Column<int>(type: "int", nullable: false),
                    title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    modelName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    lastMessageAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    messageCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aiConversations", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "aiModelConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    apiUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    apiKeyEncrypted = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    modelId = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    isDefault = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    isEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    provider = table.Column<int>(type: "int", nullable: false),
                    maxTokens = table.Column<int>(type: "int", nullable: false),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    proxyUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aiModelConfigs", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "aiOperations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    storeId = table.Column<int>(type: "int", nullable: false),
                    userId = table.Column<int>(type: "int", nullable: false),
                    operationType = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    rollbackDataJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    canRollback = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aiOperations", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "aiStoreSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    StoreId = table.Column<int>(type: "int", nullable: false),
                    StoreDescription = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CommunicationStyle = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ResponseBrevity = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EmojiUsage = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aiStoreSettings", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "apiRequestLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    apiKeyId = table.Column<int>(type: "int", nullable: true),
                    userId = table.Column<int>(type: "int", nullable: true),
                    keyDisplayHint = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    method = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    path = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    queryString = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    statusCode = table.Column<int>(type: "int", nullable: false),
                    durationMs = table.Column<int>(type: "int", nullable: false),
                    remoteIpAddress = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    userAgent = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    requestBody = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    responseBody = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    errorMessage = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_apiRequestLogs", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "errorLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    statusCode = table.Column<int>(type: "int", nullable: false),
                    method = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    path = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    queryString = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    exceptionType = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    message = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    stackTrace = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    traceIdentifier = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    userId = table.Column<int>(type: "int", nullable: true),
                    userName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    remoteIpAddress = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_errorLogs", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "instanceLoggingSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    logErrorsEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    logApiRequestsEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    logAdapterHttpEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    updatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instanceLoggingSettings", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "instancePublicApiSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    publicApiEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    updatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instancePublicApiSettings", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "mappingGenerativeCaches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    mappingId = table.Column<int>(type: "int", nullable: false),
                    nodeId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    productId = table.Column<int>(type: "int", nullable: true),
                    promptHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    seed = table.Column<int>(type: "int", nullable: true),
                    modelConfigId = table.Column<int>(type: "int", nullable: false),
                    inputHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    output = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    operationId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    errorMessage = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    completedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mappingGenerativeCaches", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "operationLogEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    operationId = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    level = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    message = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    timestamp = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operationLogEntries", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "operations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    operationId = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    storeId = table.Column<int>(type: "int", nullable: false),
                    authorUserId = table.Column<int>(type: "int", nullable: false),
                    type = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    title = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    connectionId = table.Column<int>(type: "int", nullable: true),
                    connectionName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    batchId = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    batchType = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    totalItems = table.Column<int>(type: "int", nullable: true),
                    completedItems = table.Column<int>(type: "int", nullable: true),
                    failedItems = table.Column<int>(type: "int", nullable: true),
                    summary = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    finishedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operations", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "pendingFolderJobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    storeId = table.Column<int>(type: "int", nullable: false),
                    authorUserId = table.Column<int>(type: "int", nullable: false),
                    operationId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    jobType = table.Column<int>(type: "int", nullable: false),
                    scope = table.Column<int>(type: "int", nullable: false),
                    productId = table.Column<int>(type: "int", nullable: true),
                    folderId = table.Column<int>(type: "int", nullable: true),
                    status = table.Column<int>(type: "int", nullable: false),
                    lastError = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    lastAttemptAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    completedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    resultObjectKey = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    resultFileName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    resultBytes = table.Column<long>(type: "bigint", nullable: true),
                    totalItems = table.Column<int>(type: "int", nullable: false),
                    processedItems = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pendingFolderJobs", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "pendingProductBulkUpdates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    storeId = table.Column<int>(type: "int", nullable: false),
                    authorUserId = table.Column<int>(type: "int", nullable: false),
                    batchId = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    productIdsJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    updatesJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<int>(type: "int", nullable: false),
                    lastError = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    completedCount = table.Column<int>(type: "int", nullable: false),
                    failedCount = table.Column<int>(type: "int", nullable: false),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    startedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    finishedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pendingProductBulkUpdates", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "pendingProductExports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    connectionId = table.Column<int>(type: "int", nullable: false),
                    productId = table.Column<int>(type: "int", nullable: false),
                    productName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    authorUserId = table.Column<int>(type: "int", nullable: false),
                    storeId = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    externalProductId = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    linkId = table.Column<int>(type: "int", nullable: true),
                    lastError = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    retryCount = table.Column<int>(type: "int", nullable: false),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    lastAttemptAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    completedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    batchId = table.Column<string>(type: "varchar(255)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pendingProductExports", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "referenceCurrencies",
                columns: table => new
                {
                    code = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    label = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    symbol = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    syncedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_referenceCurrencies", x => x.code);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "referencePlugins",
                columns: table => new
                {
                    key = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    displayName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    latestVersion = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    minPlatformVersion = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    artifactUrl = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    changelog = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    syncedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_referencePlugins", x => x.key);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "referenceTimezones",
                columns: table => new
                {
                    id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    label = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    syncedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_referenceTimezones", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "RoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    ClaimType = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ClaimValue = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleClaims", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NormalizedName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConcurrencyStamp = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "stores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    timezone = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    extendedPriceParameters = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    baseCurrency = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stores", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "systemVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    key = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    version = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    appliedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_systemVersions", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "UserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ClaimType = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ClaimValue = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserClaims", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "UserRoles",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    RoleId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => new { x.UserId, x.RoleId });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "aiConversationMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    conversationId = table.Column<int>(type: "int", nullable: false),
                    role = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    content = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    toolCallsJson = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    feedbackRating = table.Column<int>(type: "int", nullable: true),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aiConversationMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_aiConversationMessages_aiConversations_conversationId",
                        column: x => x.conversationId,
                        principalTable: "aiConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "categories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    hasSizes = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    storeId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_categories_stores_storeId",
                        column: x => x.storeId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "marketplaceConnections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    adapterKey = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    credentialsJson = table.Column<string>(type: "varchar(5000)", maxLength: 5000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    settingsJson = table.Column<string>(type: "varchar(10000)", maxLength: 10000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    isActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    lastSyncAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    storeId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketplaceConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_marketplaceConnections_stores_storeId",
                        column: x => x.storeId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "productGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    storeId = table.Column<int>(type: "int", nullable: false),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_productGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_productGroups_stores_storeId",
                        column: x => x.storeId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "storeAttributes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    key = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    type = table.Column<int>(type: "int", nullable: false),
                    isRequired = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    isUnique = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    isMultiline = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    showOnProductPage = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    sortOrder = table.Column<int>(type: "int", nullable: false),
                    options = table.Column<string>(type: "varchar(5000)", maxLength: 5000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    defaultValue = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    descriptionInTooltip = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    storeId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storeAttributes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_storeAttributes_stores_storeId",
                        column: x => x.storeId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "storeCurrencies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    storeId = table.Column<int>(type: "int", nullable: false),
                    code = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    rateToBase = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    isEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    updatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storeCurrencies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_storeCurrencies_stores_storeId",
                        column: x => x.storeId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "storeFolders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    storeId = table.Column<int>(type: "int", nullable: false),
                    parentFolderId = table.Column<int>(type: "int", nullable: true),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storeFolders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_storeFolders_storeFolders_parentFolderId",
                        column: x => x.parentFolderId,
                        principalTable: "storeFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_storeFolders_stores_storeId",
                        column: x => x.storeId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tagDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    color = table.Column<int>(type: "int", nullable: false),
                    storeId = table.Column<int>(type: "int", nullable: false),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tagDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tagDefinitions_stores_storeId",
                        column: x => x.storeId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    selectedStoreId = table.Column<int>(type: "int", nullable: true),
                    UserName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    isAdmin = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    isActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    NormalizedUserName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Email = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NormalizedEmail = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EmailConfirmed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    PasswordHash = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SecurityStamp = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConcurrencyStamp = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PhoneNumber = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PhoneNumberConfirmed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Users_stores_selectedStoreId",
                        column: x => x.selectedStoreId,
                        principalTable: "stores",
                        principalColumn: "Id");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "categoryAttributes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    key = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    type = table.Column<int>(type: "int", nullable: false),
                    isRequired = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    isUnique = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    isMultiline = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    showOnProductPage = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    sortOrder = table.Column<int>(type: "int", nullable: false),
                    options = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    defaultValue = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    descriptionInTooltip = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    group = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    categoryId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_categoryAttributes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_categoryAttributes_categories_categoryId",
                        column: x => x.categoryId,
                        principalTable: "categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    sku = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    barcode = table.Column<string>(type: "varchar(13)", maxLength: 13, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "varchar(5000)", maxLength: 5000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    storageQuantity = table.Column<int>(type: "int", nullable: true),
                    price = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    oldPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    minPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    netPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    discountedPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    discount = table.Column<int>(type: "int", nullable: true),
                    currency = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    deletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    storeId = table.Column<int>(type: "int", nullable: false),
                    categoryId = table.Column<int>(type: "int", nullable: true),
                    preferredExportAllMode = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.Id);
                    table.ForeignKey(
                        name: "FK_products_categories_categoryId",
                        column: x => x.categoryId,
                        principalTable: "categories",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_products_stores_storeId",
                        column: x => x.storeId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "categoryMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    connectionId = table.Column<int>(type: "int", nullable: false),
                    localCategoryId = table.Column<int>(type: "int", nullable: false),
                    externalCategoryId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    externalCategoryName = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    graphJson = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_categoryMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_categoryMappings_categories_localCategoryId",
                        column: x => x.localCategoryId,
                        principalTable: "categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_categoryMappings_marketplaceConnections_connectionId",
                        column: x => x.connectionId,
                        principalTable: "marketplaceConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "groupValueMarketplaceLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    connectionId = table.Column<int>(type: "int", nullable: false),
                    groupValue = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    externalGroupId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<int>(type: "int", nullable: false),
                    lastErrorMessage = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    lastSyncAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_groupValueMarketplaceLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_groupValueMarketplaceLinks_marketplaceConnections_connection~",
                        column: x => x.connectionId,
                        principalTable: "marketplaceConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "productGroupMarketplaceLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    productGroupId = table.Column<int>(type: "int", nullable: false),
                    connectionId = table.Column<int>(type: "int", nullable: false),
                    externalGroupId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    externalGroupName = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<int>(type: "int", nullable: false),
                    lastErrorMessage = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    lastSyncAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_productGroupMarketplaceLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_productGroupMarketplaceLinks_marketplaceConnections_connecti~",
                        column: x => x.connectionId,
                        principalTable: "marketplaceConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_productGroupMarketplaceLinks_productGroups_productGroupId",
                        column: x => x.productGroupId,
                        principalTable: "productGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "invitationTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    token = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    used = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    usedByUserId = table.Column<int>(type: "int", nullable: true),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    usedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invitationTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_invitationTokens_Users_usedByUserId",
                        column: x => x.usedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "refreshTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    userId = table.Column<int>(type: "int", nullable: false),
                    token = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    expiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    revokedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_refreshTokens_Users_userId",
                        column: x => x.userId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "storeFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    authorUserId = table.Column<int>(type: "int", nullable: false),
                    storeId = table.Column<int>(type: "int", nullable: false),
                    filePath = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    displayName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    uploadDateTime = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    fileType = table.Column<int>(type: "int", nullable: false),
                    contentType = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    length = table.Column<long>(type: "bigint", nullable: false),
                    isPublic = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    publicToken = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    folderId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storeFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_storeFiles_Users_authorUserId",
                        column: x => x.authorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_storeFiles_storeFolders_folderId",
                        column: x => x.folderId,
                        principalTable: "storeFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_storeFiles_stores_storeId",
                        column: x => x.storeId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "storeFolderShareTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    storeId = table.Column<int>(type: "int", nullable: false),
                    folderId = table.Column<int>(type: "int", nullable: true),
                    token = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    expiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    createdByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storeFolderShareTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_storeFolderShareTokens_Users_createdByUserId",
                        column: x => x.createdByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_storeFolderShareTokens_storeFolders_folderId",
                        column: x => x.folderId,
                        principalTable: "storeFolders",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_storeFolderShareTokens_stores_storeId",
                        column: x => x.storeId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StoreUser",
                columns: table => new
                {
                    membersId = table.Column<int>(type: "int", nullable: false),
                    membershipsId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreUser", x => new { x.membersId, x.membershipsId });
                    table.ForeignKey(
                        name: "FK_StoreUser_Users_membersId",
                        column: x => x.membersId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StoreUser_stores_membershipsId",
                        column: x => x.membershipsId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "userApiKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    userId = table.Column<int>(type: "int", nullable: false),
                    name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    keyPrefix = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    keyHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    last4 = table.Column<string>(type: "varchar(4)", maxLength: 4, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    lastUsedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    revokedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_userApiKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_userApiKeys_Users_userId",
                        column: x => x.userId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "notes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    text = table.Column<string>(type: "varchar(10000)", maxLength: 10000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    importance = table.Column<int>(type: "int", nullable: false),
                    productId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notes_products_productId",
                        column: x => x.productId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "pendingImageDownloads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    productId = table.Column<int>(type: "int", nullable: false),
                    storeId = table.Column<int>(type: "int", nullable: false),
                    authorUserId = table.Column<int>(type: "int", nullable: false),
                    imageUrl = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    imageIndex = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    retryCount = table.Column<int>(type: "int", nullable: false),
                    lastError = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    lastAttemptAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    completedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    batchId = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pendingImageDownloads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pendingImageDownloads_products_productId",
                        column: x => x.productId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "pendingProductImports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    connectionId = table.Column<int>(type: "int", nullable: false),
                    externalProductId = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    targetCategoryId = table.Column<int>(type: "int", nullable: true),
                    authorUserId = table.Column<int>(type: "int", nullable: false),
                    storeId = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    importedProductId = table.Column<int>(type: "int", nullable: true),
                    lastError = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    retryCount = table.Column<int>(type: "int", nullable: false),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    lastAttemptAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    completedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    batchId = table.Column<string>(type: "varchar(255)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    productName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pendingProductImports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pendingProductImports_products_importedProductId",
                        column: x => x.importedProductId,
                        principalTable: "products",
                        principalColumn: "Id");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "pendingProductTransfers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    sourceProductId = table.Column<int>(type: "int", nullable: false),
                    sourceStoreId = table.Column<int>(type: "int", nullable: false),
                    targetStoreId = table.Column<int>(type: "int", nullable: false),
                    mode = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    newSku = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    categoryAction = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    categoryId = table.Column<int>(type: "int", nullable: true),
                    groupAction = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    groupId = table.Column<int>(type: "int", nullable: true),
                    groupName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    confirmBreakLinks = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    syncMissingCategoryAttributes = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    authorUserId = table.Column<int>(type: "int", nullable: false),
                    batchId = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<int>(type: "int", nullable: false),
                    lastError = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    newProductId = table.Column<int>(type: "int", nullable: true),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    startedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    finishedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pendingProductTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pendingProductTransfers_products_newProductId",
                        column: x => x.newProductId,
                        principalTable: "products",
                        principalColumn: "Id");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "productAttributeValues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    productId = table.Column<int>(type: "int", nullable: false),
                    attributeId = table.Column<int>(type: "int", nullable: false),
                    value = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_productAttributeValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_productAttributeValues_categoryAttributes_attributeId",
                        column: x => x.attributeId,
                        principalTable: "categoryAttributes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_productAttributeValues_products_productId",
                        column: x => x.productId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "productFolders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    productId = table.Column<int>(type: "int", nullable: false),
                    parentFolderId = table.Column<int>(type: "int", nullable: true),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_productFolders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_productFolders_productFolders_parentFolderId",
                        column: x => x.parentFolderId,
                        principalTable: "productFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_productFolders_products_productId",
                        column: x => x.productId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "productGroupMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    groupId = table.Column<int>(type: "int", nullable: false),
                    productId = table.Column<int>(type: "int", nullable: false),
                    sortOrder = table.Column<int>(type: "int", nullable: false),
                    isVariantLeader = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    addedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_productGroupMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_productGroupMembers_productGroups_groupId",
                        column: x => x.groupId,
                        principalTable: "productGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_productGroupMembers_products_productId",
                        column: x => x.productId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "productVariants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    productId = table.Column<int>(type: "int", nullable: false),
                    sku = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    barcode = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    size = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    price = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    quantity = table.Column<int>(type: "int", nullable: true),
                    sortOrder = table.Column<int>(type: "int", nullable: false),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_productVariants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_productVariants_products_productId",
                        column: x => x.productId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "storeProductAttributeValues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    productId = table.Column<int>(type: "int", nullable: false),
                    storeAttributeId = table.Column<int>(type: "int", nullable: false),
                    value = table.Column<string>(type: "varchar(10000)", maxLength: 10000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storeProductAttributeValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_storeProductAttributeValues_products_productId",
                        column: x => x.productId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_storeProductAttributeValues_storeAttributes_storeAttributeId",
                        column: x => x.storeAttributeId,
                        principalTable: "storeAttributes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "productMarketplaceLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    productId = table.Column<int>(type: "int", nullable: false),
                    connectionId = table.Column<int>(type: "int", nullable: false),
                    externalProductId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    externalSku = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<int>(type: "int", nullable: false),
                    lastErrorMessage = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    lastSyncAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    externalPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    externalStock = table.Column<int>(type: "int", nullable: true),
                    categoryMappingId = table.Column<int>(type: "int", nullable: true),
                    preferredExportMode = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_productMarketplaceLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_productMarketplaceLinks_categoryMappings_categoryMappingId",
                        column: x => x.categoryMappingId,
                        principalTable: "categoryMappings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_productMarketplaceLinks_marketplaceConnections_connectionId",
                        column: x => x.connectionId,
                        principalTable: "marketplaceConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_productMarketplaceLinks_products_productId",
                        column: x => x.productId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "storeFileShareTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    fileId = table.Column<int>(type: "int", nullable: false),
                    token = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    expiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    createdByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storeFileShareTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_storeFileShareTokens_Users_createdByUserId",
                        column: x => x.createdByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_storeFileShareTokens_storeFiles_fileId",
                        column: x => x.fileId,
                        principalTable: "storeFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "storeFileTags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    fileId = table.Column<int>(type: "int", nullable: false),
                    tagDefinitionId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storeFileTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_storeFileTags_storeFiles_fileId",
                        column: x => x.fileId,
                        principalTable: "storeFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_storeFileTags_tagDefinitions_tagDefinitionId",
                        column: x => x.tagDefinitionId,
                        principalTable: "tagDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "files",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    authorUserId = table.Column<int>(type: "int", nullable: false),
                    productId = table.Column<int>(type: "int", nullable: false),
                    filePath = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    displayName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    uploadDateTime = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    fileType = table.Column<int>(type: "int", nullable: false),
                    contentType = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    length = table.Column<long>(type: "bigint", nullable: false),
                    isPublic = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    publicToken = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    folderId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_files", x => x.Id);
                    table.ForeignKey(
                        name: "FK_files_Users_authorUserId",
                        column: x => x.authorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_files_productFolders_folderId",
                        column: x => x.folderId,
                        principalTable: "productFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_files_products_productId",
                        column: x => x.productId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "folderShareTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    productId = table.Column<int>(type: "int", nullable: false),
                    folderId = table.Column<int>(type: "int", nullable: true),
                    token = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    expiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    createdByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_folderShareTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_folderShareTokens_Users_createdByUserId",
                        column: x => x.createdByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_folderShareTokens_productFolders_folderId",
                        column: x => x.folderId,
                        principalTable: "productFolders",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_folderShareTokens_products_productId",
                        column: x => x.productId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "productVariantLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    variantId = table.Column<int>(type: "int", nullable: false),
                    connectionId = table.Column<int>(type: "int", nullable: false),
                    externalVariantId = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    marketplaceSize = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    rawData = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_productVariantLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_productVariantLinks_marketplaceConnections_connectionId",
                        column: x => x.connectionId,
                        principalTable: "marketplaceConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_productVariantLinks_productVariants_variantId",
                        column: x => x.variantId,
                        principalTable: "productVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "fileShareTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    fileId = table.Column<int>(type: "int", nullable: false),
                    token = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    expiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    createdAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    createdByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fileShareTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fileShareTokens_Users_createdByUserId",
                        column: x => x.createdByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_fileShareTokens_files_fileId",
                        column: x => x.fileId,
                        principalTable: "files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "images",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    fileId = table.Column<int>(type: "int", nullable: false),
                    productId = table.Column<int>(type: "int", nullable: false),
                    index = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_images", x => x.Id);
                    table.ForeignKey(
                        name: "FK_images_files_fileId",
                        column: x => x.fileId,
                        principalTable: "files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_images_products_productId",
                        column: x => x.productId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    fileId = table.Column<int>(type: "int", nullable: false),
                    tagDefinitionId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tags_files_fileId",
                        column: x => x.fileId,
                        principalTable: "files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_tags_tagDefinitions_tagDefinitionId",
                        column: x => x.tagDefinitionId,
                        principalTable: "tagDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_adapterHttpLogs_adapterKey_createdAt",
                table: "adapterHttpLogs",
                columns: new[] { "adapterKey", "createdAt" });

            migrationBuilder.CreateIndex(
                name: "IX_adapterHttpLogs_connectionId_createdAt",
                table: "adapterHttpLogs",
                columns: new[] { "connectionId", "createdAt" });

            migrationBuilder.CreateIndex(
                name: "IX_adapterHttpLogs_statusCode_createdAt",
                table: "adapterHttpLogs",
                columns: new[] { "statusCode", "createdAt" });

            migrationBuilder.CreateIndex(
                name: "IX_adapterHttpLogs_storeId_createdAt",
                table: "adapterHttpLogs",
                columns: new[] { "storeId", "createdAt" });

            migrationBuilder.CreateIndex(
                name: "IX_aiConversationMessages_conversationId_createdAt",
                table: "aiConversationMessages",
                columns: new[] { "conversationId", "createdAt" });

            migrationBuilder.CreateIndex(
                name: "IX_aiConversations_sessionId",
                table: "aiConversations",
                column: "sessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_aiConversations_storeId_userId",
                table: "aiConversations",
                columns: new[] { "storeId", "userId" });

            migrationBuilder.CreateIndex(
                name: "IX_aiModelConfigs_isDefault",
                table: "aiModelConfigs",
                column: "isDefault");

            migrationBuilder.CreateIndex(
                name: "IX_aiOperations_userId_storeId_createdAt",
                table: "aiOperations",
                columns: new[] { "userId", "storeId", "createdAt" });

            migrationBuilder.CreateIndex(
                name: "IX_aiStoreSettings_StoreId",
                table: "aiStoreSettings",
                column: "StoreId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_apiRequestLogs_apiKeyId_createdAt",
                table: "apiRequestLogs",
                columns: new[] { "apiKeyId", "createdAt" });

            migrationBuilder.CreateIndex(
                name: "IX_apiRequestLogs_statusCode_createdAt",
                table: "apiRequestLogs",
                columns: new[] { "statusCode", "createdAt" });

            migrationBuilder.CreateIndex(
                name: "IX_apiRequestLogs_userId_createdAt",
                table: "apiRequestLogs",
                columns: new[] { "userId", "createdAt" });

            migrationBuilder.CreateIndex(
                name: "IX_categories_storeId",
                table: "categories",
                column: "storeId");

            migrationBuilder.CreateIndex(
                name: "IX_categoryAttributes_categoryId",
                table: "categoryAttributes",
                column: "categoryId");

            migrationBuilder.CreateIndex(
                name: "IX_categoryMappings_connectionId",
                table: "categoryMappings",
                column: "connectionId");

            migrationBuilder.CreateIndex(
                name: "IX_categoryMappings_localCategoryId_connectionId",
                table: "categoryMappings",
                columns: new[] { "localCategoryId", "connectionId" });

            migrationBuilder.CreateIndex(
                name: "IX_errorLogs_createdAt",
                table: "errorLogs",
                column: "createdAt");

            migrationBuilder.CreateIndex(
                name: "IX_errorLogs_statusCode",
                table: "errorLogs",
                column: "statusCode");

            migrationBuilder.CreateIndex(
                name: "IX_files_authorUserId",
                table: "files",
                column: "authorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_files_folderId",
                table: "files",
                column: "folderId");

            migrationBuilder.CreateIndex(
                name: "IX_files_productId",
                table: "files",
                column: "productId");

            migrationBuilder.CreateIndex(
                name: "IX_fileShareTokens_createdByUserId",
                table: "fileShareTokens",
                column: "createdByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_fileShareTokens_fileId",
                table: "fileShareTokens",
                column: "fileId");

            migrationBuilder.CreateIndex(
                name: "IX_fileShareTokens_token",
                table: "fileShareTokens",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_folderShareTokens_createdByUserId",
                table: "folderShareTokens",
                column: "createdByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_folderShareTokens_folderId",
                table: "folderShareTokens",
                column: "folderId");

            migrationBuilder.CreateIndex(
                name: "IX_folderShareTokens_productId",
                table: "folderShareTokens",
                column: "productId");

            migrationBuilder.CreateIndex(
                name: "IX_folderShareTokens_token",
                table: "folderShareTokens",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_groupValueMarketplaceLinks_connectionId_groupValue",
                table: "groupValueMarketplaceLinks",
                columns: new[] { "connectionId", "groupValue" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_images_fileId",
                table: "images",
                column: "fileId");

            migrationBuilder.CreateIndex(
                name: "IX_images_productId",
                table: "images",
                column: "productId");

            migrationBuilder.CreateIndex(
                name: "IX_invitationTokens_usedByUserId",
                table: "invitationTokens",
                column: "usedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_mapGenCache_lookup",
                table: "mappingGenerativeCaches",
                columns: new[] { "mappingId", "nodeId", "productId", "promptHash", "seed", "modelConfigId", "inputHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketplaceConnections_storeId",
                table: "marketplaceConnections",
                column: "storeId");

            migrationBuilder.CreateIndex(
                name: "IX_notes_productId",
                table: "notes",
                column: "productId");

            migrationBuilder.CreateIndex(
                name: "IX_operationLogEntries_operationId",
                table: "operationLogEntries",
                column: "operationId");

            migrationBuilder.CreateIndex(
                name: "IX_operations_operationId",
                table: "operations",
                column: "operationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_operations_storeId_createdAt",
                table: "operations",
                columns: new[] { "storeId", "createdAt" });

            migrationBuilder.CreateIndex(
                name: "IX_pendingFolderJobs_operationId",
                table: "pendingFolderJobs",
                column: "operationId");

            migrationBuilder.CreateIndex(
                name: "IX_pendingFolderJobs_status_createdAt",
                table: "pendingFolderJobs",
                columns: new[] { "status", "createdAt" });

            migrationBuilder.CreateIndex(
                name: "IX_pendingFolderJobs_storeId",
                table: "pendingFolderJobs",
                column: "storeId");

            migrationBuilder.CreateIndex(
                name: "IX_pendingImageDownloads_productId",
                table: "pendingImageDownloads",
                column: "productId");

            migrationBuilder.CreateIndex(
                name: "IX_pendingImageDownloads_status_storeId",
                table: "pendingImageDownloads",
                columns: new[] { "status", "storeId" });

            migrationBuilder.CreateIndex(
                name: "IX_pendingProductBulkUpdates_batchId",
                table: "pendingProductBulkUpdates",
                column: "batchId");

            migrationBuilder.CreateIndex(
                name: "IX_pendingProductBulkUpdates_status_createdAt",
                table: "pendingProductBulkUpdates",
                columns: new[] { "status", "createdAt" });

            migrationBuilder.CreateIndex(
                name: "IX_pendingProductExports_batchId",
                table: "pendingProductExports",
                column: "batchId");

            migrationBuilder.CreateIndex(
                name: "IX_pendingProductExports_status_createdAt",
                table: "pendingProductExports",
                columns: new[] { "status", "createdAt" });

            migrationBuilder.CreateIndex(
                name: "IX_pendingProductImports_batchId",
                table: "pendingProductImports",
                column: "batchId");

            migrationBuilder.CreateIndex(
                name: "IX_pendingProductImports_importedProductId",
                table: "pendingProductImports",
                column: "importedProductId");

            migrationBuilder.CreateIndex(
                name: "IX_pendingProductImports_status_createdAt",
                table: "pendingProductImports",
                columns: new[] { "status", "createdAt" });

            migrationBuilder.CreateIndex(
                name: "IX_pendingProductTransfers_batchId",
                table: "pendingProductTransfers",
                column: "batchId");

            migrationBuilder.CreateIndex(
                name: "IX_pendingProductTransfers_newProductId",
                table: "pendingProductTransfers",
                column: "newProductId");

            migrationBuilder.CreateIndex(
                name: "IX_pendingProductTransfers_status_createdAt",
                table: "pendingProductTransfers",
                columns: new[] { "status", "createdAt" });

            migrationBuilder.CreateIndex(
                name: "IX_productAttributeValues_attributeId",
                table: "productAttributeValues",
                column: "attributeId");

            migrationBuilder.CreateIndex(
                name: "IX_productAttributeValues_productId",
                table: "productAttributeValues",
                column: "productId");

            migrationBuilder.CreateIndex(
                name: "IX_productFolders_parentFolderId",
                table: "productFolders",
                column: "parentFolderId");

            migrationBuilder.CreateIndex(
                name: "IX_productFolders_productId",
                table: "productFolders",
                column: "productId");

            migrationBuilder.CreateIndex(
                name: "IX_productGroupMarketplaceLinks_connectionId",
                table: "productGroupMarketplaceLinks",
                column: "connectionId");

            migrationBuilder.CreateIndex(
                name: "IX_productGroupMarketplaceLinks_externalGroupId_connectionId",
                table: "productGroupMarketplaceLinks",
                columns: new[] { "externalGroupId", "connectionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_productGroupMarketplaceLinks_productGroupId_connectionId",
                table: "productGroupMarketplaceLinks",
                columns: new[] { "productGroupId", "connectionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_productGroupMembers_groupId_productId",
                table: "productGroupMembers",
                columns: new[] { "groupId", "productId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_productGroupMembers_productId",
                table: "productGroupMembers",
                column: "productId");

            migrationBuilder.CreateIndex(
                name: "IX_productGroups_storeId",
                table: "productGroups",
                column: "storeId");

            migrationBuilder.CreateIndex(
                name: "IX_productMarketplaceLinks_categoryMappingId",
                table: "productMarketplaceLinks",
                column: "categoryMappingId");

            migrationBuilder.CreateIndex(
                name: "IX_productMarketplaceLinks_connectionId",
                table: "productMarketplaceLinks",
                column: "connectionId");

            migrationBuilder.CreateIndex(
                name: "IX_productMarketplaceLinks_externalProductId_connectionId",
                table: "productMarketplaceLinks",
                columns: new[] { "externalProductId", "connectionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_productMarketplaceLinks_productId_connectionId",
                table: "productMarketplaceLinks",
                columns: new[] { "productId", "connectionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_products_categoryId",
                table: "products",
                column: "categoryId");

            migrationBuilder.CreateIndex(
                name: "IX_products_storeId",
                table: "products",
                column: "storeId");

            migrationBuilder.CreateIndex(
                name: "IX_products_storeId_deletedAt",
                table: "products",
                columns: new[] { "storeId", "deletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_productVariantLinks_connectionId",
                table: "productVariantLinks",
                column: "connectionId");

            migrationBuilder.CreateIndex(
                name: "IX_productVariantLinks_variantId_connectionId",
                table: "productVariantLinks",
                columns: new[] { "variantId", "connectionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_productVariants_productId",
                table: "productVariants",
                column: "productId");

            migrationBuilder.CreateIndex(
                name: "IX_refreshTokens_token",
                table: "refreshTokens",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refreshTokens_userId",
                table: "refreshTokens",
                column: "userId");

            migrationBuilder.CreateIndex(
                name: "IX_storeAttributes_storeId",
                table: "storeAttributes",
                column: "storeId");

            migrationBuilder.CreateIndex(
                name: "IX_storeCurrencies_storeId_code",
                table: "storeCurrencies",
                columns: new[] { "storeId", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_storeFiles_authorUserId",
                table: "storeFiles",
                column: "authorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_storeFiles_folderId",
                table: "storeFiles",
                column: "folderId");

            migrationBuilder.CreateIndex(
                name: "IX_storeFiles_storeId",
                table: "storeFiles",
                column: "storeId");

            migrationBuilder.CreateIndex(
                name: "IX_storeFileShareTokens_createdByUserId",
                table: "storeFileShareTokens",
                column: "createdByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_storeFileShareTokens_fileId",
                table: "storeFileShareTokens",
                column: "fileId");

            migrationBuilder.CreateIndex(
                name: "IX_storeFileShareTokens_token",
                table: "storeFileShareTokens",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_storeFileTags_fileId_tagDefinitionId",
                table: "storeFileTags",
                columns: new[] { "fileId", "tagDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_storeFileTags_tagDefinitionId",
                table: "storeFileTags",
                column: "tagDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_storeFolders_parentFolderId",
                table: "storeFolders",
                column: "parentFolderId");

            migrationBuilder.CreateIndex(
                name: "IX_storeFolders_storeId",
                table: "storeFolders",
                column: "storeId");

            migrationBuilder.CreateIndex(
                name: "IX_storeFolderShareTokens_createdByUserId",
                table: "storeFolderShareTokens",
                column: "createdByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_storeFolderShareTokens_folderId",
                table: "storeFolderShareTokens",
                column: "folderId");

            migrationBuilder.CreateIndex(
                name: "IX_storeFolderShareTokens_storeId",
                table: "storeFolderShareTokens",
                column: "storeId");

            migrationBuilder.CreateIndex(
                name: "IX_storeFolderShareTokens_token",
                table: "storeFolderShareTokens",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_storeProductAttributeValues_productId",
                table: "storeProductAttributeValues",
                column: "productId");

            migrationBuilder.CreateIndex(
                name: "IX_storeProductAttributeValues_storeAttributeId",
                table: "storeProductAttributeValues",
                column: "storeAttributeId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreUser_membershipsId",
                table: "StoreUser",
                column: "membershipsId");

            migrationBuilder.CreateIndex(
                name: "IX_systemVersions_key",
                table: "systemVersions",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tagDefinitions_storeId",
                table: "tagDefinitions",
                column: "storeId");

            migrationBuilder.CreateIndex(
                name: "IX_tags_fileId_tagDefinitionId",
                table: "tags",
                columns: new[] { "fileId", "tagDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tags_tagDefinitionId",
                table: "tags",
                column: "tagDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_userApiKeys_keyHash",
                table: "userApiKeys",
                column: "keyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_userApiKeys_userId",
                table: "userApiKeys",
                column: "userId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_selectedStoreId",
                table: "Users",
                column: "selectedStoreId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "adapterHttpLogs");

            migrationBuilder.DropTable(
                name: "aiConversationMessages");

            migrationBuilder.DropTable(
                name: "aiModelConfigs");

            migrationBuilder.DropTable(
                name: "aiOperations");

            migrationBuilder.DropTable(
                name: "aiStoreSettings");

            migrationBuilder.DropTable(
                name: "apiRequestLogs");

            migrationBuilder.DropTable(
                name: "errorLogs");

            migrationBuilder.DropTable(
                name: "fileShareTokens");

            migrationBuilder.DropTable(
                name: "folderShareTokens");

            migrationBuilder.DropTable(
                name: "groupValueMarketplaceLinks");

            migrationBuilder.DropTable(
                name: "images");

            migrationBuilder.DropTable(
                name: "instanceLoggingSettings");

            migrationBuilder.DropTable(
                name: "instancePublicApiSettings");

            migrationBuilder.DropTable(
                name: "invitationTokens");

            migrationBuilder.DropTable(
                name: "mappingGenerativeCaches");

            migrationBuilder.DropTable(
                name: "notes");

            migrationBuilder.DropTable(
                name: "operationLogEntries");

            migrationBuilder.DropTable(
                name: "operations");

            migrationBuilder.DropTable(
                name: "pendingFolderJobs");

            migrationBuilder.DropTable(
                name: "pendingImageDownloads");

            migrationBuilder.DropTable(
                name: "pendingProductBulkUpdates");

            migrationBuilder.DropTable(
                name: "pendingProductExports");

            migrationBuilder.DropTable(
                name: "pendingProductImports");

            migrationBuilder.DropTable(
                name: "pendingProductTransfers");

            migrationBuilder.DropTable(
                name: "productAttributeValues");

            migrationBuilder.DropTable(
                name: "productGroupMarketplaceLinks");

            migrationBuilder.DropTable(
                name: "productGroupMembers");

            migrationBuilder.DropTable(
                name: "productMarketplaceLinks");

            migrationBuilder.DropTable(
                name: "productVariantLinks");

            migrationBuilder.DropTable(
                name: "referenceCurrencies");

            migrationBuilder.DropTable(
                name: "referencePlugins");

            migrationBuilder.DropTable(
                name: "referenceTimezones");

            migrationBuilder.DropTable(
                name: "refreshTokens");

            migrationBuilder.DropTable(
                name: "RoleClaims");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "storeCurrencies");

            migrationBuilder.DropTable(
                name: "storeFileShareTokens");

            migrationBuilder.DropTable(
                name: "storeFileTags");

            migrationBuilder.DropTable(
                name: "storeFolderShareTokens");

            migrationBuilder.DropTable(
                name: "storeProductAttributeValues");

            migrationBuilder.DropTable(
                name: "StoreUser");

            migrationBuilder.DropTable(
                name: "systemVersions");

            migrationBuilder.DropTable(
                name: "tags");

            migrationBuilder.DropTable(
                name: "userApiKeys");

            migrationBuilder.DropTable(
                name: "UserClaims");

            migrationBuilder.DropTable(
                name: "UserRoles");

            migrationBuilder.DropTable(
                name: "aiConversations");

            migrationBuilder.DropTable(
                name: "categoryAttributes");

            migrationBuilder.DropTable(
                name: "productGroups");

            migrationBuilder.DropTable(
                name: "categoryMappings");

            migrationBuilder.DropTable(
                name: "productVariants");

            migrationBuilder.DropTable(
                name: "storeFiles");

            migrationBuilder.DropTable(
                name: "storeAttributes");

            migrationBuilder.DropTable(
                name: "files");

            migrationBuilder.DropTable(
                name: "tagDefinitions");

            migrationBuilder.DropTable(
                name: "marketplaceConnections");

            migrationBuilder.DropTable(
                name: "storeFolders");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "productFolders");

            migrationBuilder.DropTable(
                name: "products");

            migrationBuilder.DropTable(
                name: "categories");

            migrationBuilder.DropTable(
                name: "stores");
        }
    }
}
