using System.Text;
using System.Text.Json;
using Delegat.Services.Ai.Agent;

namespace Delegat.Tests;

public class AgentToolResultTests
{
    [Fact]
    public void IsFailure_CabinetQuotaBodyWithoutErrorProperty_IsFailed()
    {
        var body = new
        {
            title = "Product limit reached (MAX_PRODUCTS=11)",
            status = 409,
            code = "product_limit_reached",
            data = new { limit = 11, used = 11, remaining = 0 },
        };

        Assert.True(AgentToolResult.IsFailure(body, "HTTP 409"));
    }

    [Fact]
    public void IsFailure_Http400Summary_IsFailed()
    {
        Assert.True(AgentToolResult.IsFailure(new { name = "X" }, "HTTP 400"));
        Assert.True(AgentToolResult.IsFailure(new { }, "HTTP 400 too large (5241532 bytes)"));
    }

    [Fact]
    public void IsFailure_OkCatalogPayload_IsNotFailed()
    {
        Assert.False(AgentToolResult.IsFailure(new { items = new[] { new { id = 1 } }, totalCount = 1 }, "HTTP 200"));
        Assert.False(AgentToolResult.IsFailure(new { id = 12, sku = "A" }, "HTTP 200"));
    }

    [Fact]
    public void IsFailure_QueuedCatalogConnectJob_IsNotFailed()
    {
        var json = """{"operationId":"abc","connectionId":1,"kind":"import","status":"pending","queuedCount":0,"error":null}""";
        var (result, summary) = CabinetRequestInvoker.InterpretResponse(200, Encoding.UTF8.GetBytes(json));
        Assert.Equal("HTTP 200 queued", summary);
        Assert.False(AgentToolResult.IsFailure(result, summary));
        Assert.True(AgentToolResult.IsQueuedJob(result));
    }

    [Fact]
    public void IsQueuedJob_OnlyTreatsActiveJobStatusesAsQueued()
    {
        Assert.True(AgentToolResult.IsQueuedJob(new { status = "running" }));
        Assert.True(AgentToolResult.IsQueuedJob(new { status = "queued" }));
        Assert.False(AgentToolResult.IsQueuedJob(new { status = "completed" }));
        Assert.False(AgentToolResult.IsQueuedJob(new { status = "failed" }));
        Assert.False(AgentToolResult.IsQueuedJob(new { message = "ok" }));
    }

    [Fact]
    public void IsFailure_PendingJobWithDomainError_IsNotFailed()
    {
        var json = """{"operationId":"abc","connectionId":1,"kind":"import","status":"pending","error":"Timed out"}""";
        var (result, summary) = CabinetRequestInvoker.InterpretResponse(200, Encoding.UTF8.GetBytes(json));
        Assert.False(AgentToolResult.IsFailure(result, summary));
        Assert.False(AgentToolResult.IsFailure(
            JsonDocument.Parse(json).RootElement, "HTTP 200"));
    }

    [Fact]
    public void IsFailure_FailedJobStatus_IsFailed()
    {
        var json = """{"operationId":"abc","status":"failed","error":"Timed out"}""";
        Assert.True(AgentToolResult.IsFailure(
            JsonDocument.Parse(json).RootElement, "HTTP 200"));
    }

    [Fact]
    public void IsFailure_RequestFailedWithoutHttpStatus_IsFailed()
    {
        Assert.True(AgentToolResult.IsFailure(new { error = "request_failed", message = "canceled" }, "canceled"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(124)]
    public void IsFailure_NonZeroPythonExitCode_IsFailed(int exitCode)
    {
        Assert.True(AgentToolResult.IsFailure(
            new { exitCode, stdout = "partial", stderr = "timeout", error = "timeout" },
            $"Python run failed (exit {exitCode})"));
    }

    [Fact]
    public void IsFailure_ZeroPythonExitCode_IsSuccess()
    {
        Assert.False(AgentToolResult.IsFailure(
            new { exitCode = 0, stdout = "ok", stderr = "", error = (string?)null },
            "Python run completed (exit 0)"));
    }

    [Fact]
    public void InterpretResponse_ImportPreview_TruncatesItemsKeepsCounts()
    {
        var items = Enumerable.Range(1, 50).Select(i => new
        {
            externalId = $"wb-{i}",
            name = $"Product {i}",
            hasMapping = true,
            isAlreadyLinked = false,
        });
        var json = JsonSerializer.Serialize(new
        {
            items,
            hasMore = true,
            totalNew = 50,
            totalAlreadyLinked = 0,
            totalWithMapping = 50,
            totalWithoutMapping = 0,
        });
        var (result, summary) = CabinetRequestInvoker.InterpretResponse(200, Encoding.UTF8.GetBytes(json));
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.Equal(CabinetRequestInvoker.MaxListItems, doc.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(50, doc.RootElement.GetProperty("totalNew").GetInt32());
        Assert.True(doc.RootElement.GetProperty("itemsTruncated").GetInt32() > 0);
        Assert.Contains("trimmed", summary, StringComparison.OrdinalIgnoreCase);
        Assert.False(AgentToolResult.IsFailure(result, summary));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(60)]
    [InlineData(217)]
    public void InterpretResponse_CappedTerminalPagesRemainTraversableUntilEveryRowIsRead(int total)
    {
        var received = new List<int>();
        var start = 0;
        for (var round = 0; round < 30; round++)
        {
            var json = JsonSerializer.Serialize(new
            {
                items = Enumerable.Range(1, total).Skip(start).Take(100).Select(id => new { id }),
                totalCount = total,
                lastPage = start + 100 >= total,
            });
            var (result, _) = CabinetRequestInvoker.InterpretResponse(200, Encoding.UTF8.GetBytes(json));
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            var page = doc.RootElement.GetProperty("items").EnumerateArray().ToList();
            received.AddRange(page.Select(p => p.GetProperty("id").GetInt32()));
            start += page.Count;
            if (doc.RootElement.GetProperty("lastPage").GetBoolean()) break;
            Assert.NotEmpty(page);
            Assert.True(start < total);
        }
        Assert.Equal(Enumerable.Range(1, total), received);
        Assert.Equal(total, received.Distinct().Count());
    }

    [Fact]
    public void TruncateForModel_CapsHugeToolResult()
    {
        var huge = new string('x', AgentToolResult.MaxModelResultChars + 4000);
        var clipped = AgentToolResult.TruncateForModel(huge);
        Assert.True(clipped.Length < huge.Length);
        using var doc = JsonDocument.Parse(clipped);
        Assert.True(doc.RootElement.GetProperty("truncated").GetBoolean());
        Assert.Contains("truncated", clipped, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TruncateForModel_OversizedJsonStaysValidAndPreservesFailureFieldsAtTheEnd()
    {
        var huge = JsonSerializer.Serialize(new
        {
            items = new string('x', AgentToolResult.MaxModelResultChars + 1000),
            status = 409,
            error = "product_limit_reached",
        });

        var clipped = AgentToolResult.TruncateForModel(huge);
        using var doc = JsonDocument.Parse(clipped);

        Assert.True(clipped.Length <= AgentToolResult.MaxModelResultChars);
        Assert.True(doc.RootElement.GetProperty("truncated").GetBoolean());
        Assert.Equal(409, doc.RootElement.GetProperty("important").GetProperty("status").GetInt32());
        Assert.Equal("product_limit_reached", doc.RootElement.GetProperty("important").GetProperty("error").GetString());
        Assert.True(doc.RootElement.GetProperty("preview").GetString()!.Length > 0);
    }

    [Fact]
    public void TruncateForModel_PreservesNonZeroPythonExitCode()
    {
        var huge = JsonSerializer.Serialize(new
        {
            exitCode = 124,
            error = "timeout",
            stdout = new string('x', AgentToolResult.MaxModelResultChars + 1000),
        });

        using var doc = JsonDocument.Parse(AgentToolResult.TruncateForModel(huge));

        Assert.Equal(124, doc.RootElement.GetProperty("important").GetProperty("exitCode").GetInt32());
        Assert.Equal("timeout", doc.RootElement.GetProperty("important").GetProperty("error").GetString());
    }

    [Fact]
    public void TruncateForModel_PreservesAttachmentPaginationMetadata()
    {
        var huge = JsonSerializer.Serialize(new
        {
            fileName = "records.txt",
            nextStartOffset = 12_000,
            totalCharacters = 50_000,
            truncated = true,
            text = new string('x', AgentToolResult.MaxModelResultChars + 1000),
        });

        using var doc = JsonDocument.Parse(AgentToolResult.TruncateForModel(huge));
        var important = doc.RootElement.GetProperty("important");

        Assert.Equal(12_000, important.GetProperty("nextStartOffset").GetInt32());
        Assert.Equal(50_000, important.GetProperty("totalCharacters").GetInt32());
        Assert.True(important.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public void InterpretResponse_404_IsResourceNotFoundWithoutRouteHints()
    {
        var json = """{"title":"Product not found","status":404}""";
        var (result, summary) = CabinetRequestInvoker.InterpretResponse(404, Encoding.UTF8.GetBytes(json));
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.Equal("not_found", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("resource_not_found", doc.RootElement.GetProperty("errorClass").GetString());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("hint").ValueKind);
        // A resource 404 keeps the response's own title and never prescribes routes.
        Assert.Equal("Product not found", summary);
        var serialized = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("do not invent", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mappings/{mappingId}/graph", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("Request failed (404)", summary, StringComparison.Ordinal);
        Assert.True(AgentToolResult.IsFailure(result, summary));
    }

    [Fact]
    public void InterpretResponse_404_WithoutBodyTitle_UsesNeutralNotFound()
    {
        var (result, summary) = CabinetRequestInvoker.InterpretResponse(404, Encoding.UTF8.GetBytes("{}"));
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.Equal("not_found", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("resource_not_found", doc.RootElement.GetProperty("errorClass").GetString());
        Assert.Equal("Not found", doc.RootElement.GetProperty("title").GetString());
        Assert.Equal("Not found", summary);
    }

    [Theory]
    [InlineData(401, "forbidden")]
    [InlineData(403, "forbidden")]
    [InlineData(409, "conflict")]
    [InlineData(429, "quota")]
    [InlineData(500, "upstream_error")]
    [InlineData(503, "upstream_error")]
    public void InterpretResponse_MapsHttpStatusToErrorClass(int status, string errorClass)
    {
        var json = """{"title":"failed","status":400}""";
        var (result, _) = CabinetRequestInvoker.InterpretResponse(status, Encoding.UTF8.GetBytes(json));
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.Equal(errorClass, doc.RootElement.GetProperty("errorClass").GetString());
        Assert.True(AgentToolResult.IsFailure(result, ""));
    }

    [Fact]
    public void InterpretResponse_Conflict_SetsErrorFromCode()
    {
        var json = """{"title":"Product limit reached (MAX_PRODUCTS=11)","status":409,"code":"product_limit_reached","data":{"limit":11,"used":11,"remaining":0}}""";
        var (result, summary) = CabinetRequestInvoker.InterpretResponse(409, Encoding.UTF8.GetBytes(json));
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.Equal("product_limit_reached", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal(409, doc.RootElement.GetProperty("status").GetInt32());
        Assert.True(AgentToolResult.IsFailure(result, summary));
        Assert.Contains("nothing was created", summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HTTP 409", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void InterpretResponse_Validation400_FlattensFieldMessages()
    {
        var json = """
            {"title":"One or more validation errors occurred.","status":400,"errors":{"$.attributeValues[1].value":["The JSON value could not be converted to System.String. Path: $.attributeValues[1].value | LineNumber: 0 | BytePositionInLine: 12."]}}
            """;
        var (result, summary) = CabinetRequestInvoker.InterpretResponse(400, Encoding.UTF8.GetBytes(json));
        Assert.Contains("Characteristic values must be text", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("One or more validation errors occurred", summary, StringComparison.Ordinal);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.Equal("validation_failed", doc.RootElement.GetProperty("errorClass").GetString());
    }

    [Fact]
    public void InterpretResponse_Validation400_SetsErrorAndFailsPlan()
    {
        var json = """{"title":"One or more validation errors occurred.","status":400,"code":"validation_failed"}""";
        var (result, summary) = CabinetRequestInvoker.InterpretResponse(400, Encoding.UTF8.GetBytes(json));
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.Equal("validation_failed", doc.RootElement.GetProperty("error").GetString());
        // Without a flattened errors object the class is a generic bad request.
        Assert.Equal("bad_request", doc.RootElement.GetProperty("errorClass").GetString());
        Assert.Equal(400, doc.RootElement.GetProperty("status").GetInt32());
        Assert.True(AgentToolResult.IsFailure(result, summary));
        Assert.DoesNotContain("Plan executed", AgentToolResult.FormatPlanOutcome(0, 1, [summary], "en"), StringComparison.Ordinal);
    }

    [Fact]
    public void InterpretResponse_OversizedProduct_SlmsFilesInsteadOfDropping()
    {
        var huge = new string('x', 300_000);
        var json = JsonSerializer.Serialize(new
        {
            id = 1,
            name = "Tank",
            sku = "_WA121",
            files = new[] { new { id = 9, data = huge } },
            attributeValues = new[] { new { attributeId = 146, value = "Brand" } },
        });
        Assert.True(json.Length > CabinetRequestInvoker.MaxResponseBytes);

        var (result, summary) = CabinetRequestInvoker.InterpretResponse(200, Encoding.UTF8.GetBytes(json));
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.False(doc.RootElement.TryGetProperty("error", out _));
        Assert.Equal(1, doc.RootElement.GetProperty("id").GetInt32());
        Assert.Equal("Brand", doc.RootElement.GetProperty("attributeValues")[0].GetProperty("value").GetString());
        Assert.False(doc.RootElement.TryGetProperty("files", out _));
        Assert.Contains("trimmed", summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FormatPlanOutcome_FailedCreate_DoesNotClaimSuccess()
    {
        var ru = AgentToolResult.FormatPlanOutcome(0, 1, ["Product limit reached — nothing was created."], "ru");
        Assert.Contains("Не удалось завершить", ru, StringComparison.Ordinal);
        Assert.DoesNotContain("ничего не изменилось", ru, StringComparison.Ordinal);
        Assert.Contains("проверьте результат", ru, StringComparison.Ordinal);
        Assert.DoesNotContain("Plan executed", ru, StringComparison.Ordinal);

        var en = AgentToolResult.FormatPlanOutcome(0, 1, ["Product limit reached — nothing was created."], "en");
        Assert.Contains("No steps completed successfully", en, StringComparison.Ordinal);
        Assert.DoesNotContain("Nothing was changed", en, StringComparison.Ordinal);
        Assert.DoesNotContain("Plan executed: 1 of 1", en, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatPlanOutcome_PartialSuccess_DoesNotClaimCatalogUnchanged()
    {
        var ru = AgentToolResult.FormatPlanOutcome(1, 2, ["Invalid barcode. EAN-8 (8 digits) or EAN-13 (13 digits) are allowed"], "ru");
        Assert.Contains("Сделано частично", ru, StringComparison.Ordinal);
        Assert.Contains("корзин", ru, StringComparison.Ordinal);
        Assert.DoesNotContain("ничего не изменилось", ru, StringComparison.Ordinal);

        var en = AgentToolResult.FormatPlanOutcome(1, 2, ["Invalid barcode. EAN-8 (8 digits) or EAN-13 (13 digits) are allowed"], "en");
        Assert.Contains("Partly done", en, StringComparison.Ordinal);
        Assert.Contains("trash", en, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Nothing was changed", en, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatPlanOutcome_QueuedWork_DoesNotClaimBackgroundCompletion()
    {
        var ru = AgentToolResult.FormatPlanOutcome(1, 1, [], "ru", queued: 1);
        Assert.Contains("поставлено в очередь", ru, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("отслеживается отдельно", ru, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("выполнены все шаги", ru, StringComparison.OrdinalIgnoreCase);

        var en = AgentToolResult.FormatPlanOutcome(1, 1, [], "en", queued: 1);
        Assert.Contains("background operations queued", en, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tracked separately", en, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("All steps finished", en, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatPlanOutcome_QueuedThenFailure_DoesNotClaimQueuedWriteWasApplied()
    {
        var ru = AgentToolResult.FormatPlanOutcome(1, 2, ["HTTP 400"], "ru", queued: 1);
        Assert.Contains("поставлено в очередь", ru, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("уже применена", ru, StringComparison.OrdinalIgnoreCase);

        var en = AgentToolResult.FormatPlanOutcome(1, 2, ["HTTP 400"], "en", queued: 1);
        Assert.Contains("background operations queued", en, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Some steps were applied", en, StringComparison.Ordinal);
    }

    [Fact]
    public void TruncateForModel_OversizedCaptureResult_PreservesArtifactHandles()
    {
        var expiry = DateTime.UtcNow.AddHours(24);
        var expiryText = expiry.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'");
        var artifacts = Enumerable.Range(0, 4).Select(i => new
        {
            artifactId = new string((char)('a' + i), 32),
            fileName = $"очень-длинное-имя-файла-отчёта-{i}-подробный-итоговый.csv",
            contentType = "text/csv",
            length = 10 + i,
            sha256 = new string((char)('a' + i), 64),
            expiresAtUtc = expiryText,
        }).ToArray();
        var payload = JsonSerializer.Serialize(new
        {
            data = new { stdout = new string('x', 80_000), artifacts },
            summary = "done",
        });

        var truncated = AgentToolResult.TruncateForModel(payload);
        Assert.True(truncated.Length <= AgentToolResult.MaxModelResultChars);

        using var doc = JsonDocument.Parse(truncated);
        var kept = doc.RootElement.GetProperty("artifacts");
        Assert.Equal(4, kept.GetArrayLength());
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(artifacts[i].artifactId, kept[i].GetProperty("artifactId").GetString());
            Assert.Equal(artifacts[i].fileName, kept[i].GetProperty("fileName").GetString());
            Assert.Equal(artifacts[i].sha256, kept[i].GetProperty("sha256").GetString());
            Assert.Equal(artifacts[i].length, kept[i].GetProperty("length").GetInt64());
        }
    }

    [Fact]
    public void TruncateForModel_OutcomesArray_PreservesHandlesFromEnvelopeResult()
    {
        var expiry = DateTime.UtcNow.AddHours(24).ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'");
        var handle = new
        {
            artifactId = "f".PadRight(32, 'f'),
            fileName = "отчёт.csv",
            contentType = "text/csv",
            length = 4,
            sha256 = "b".PadRight(64, 'b'),
            expiresAtUtc = expiry,
        };
        var inner = JsonSerializer.Serialize(new
        {
            truncated = true,
            originalLength = 90_000,
            important = new { exitCode = 0 },
            artifacts = new[] { handle },
            preview = new string('y', 1_000),
        });
        var outcomes = JsonSerializer.Serialize(new object[]
        {
            new { step = 1, tool = "run_python", ok = true,
                  result = JsonDocument.Parse(inner).RootElement.Clone() },
            new { step = 2, tool = "run_python", ok = true,
                  result = new { stdout = new string('z', 80_000) } },
        });

        var truncated = AgentToolResult.TruncateForModel(outcomes);
        Assert.True(truncated.Length <= AgentToolResult.MaxModelResultChars);
        using var doc = JsonDocument.Parse(truncated);
        var kept = Assert.Single(doc.RootElement.GetProperty("artifacts").EnumerateArray());
        Assert.Equal(handle.artifactId, kept.GetProperty("artifactId").GetString());
        Assert.Equal(handle.sha256, kept.GetProperty("sha256").GetString());
    }

    [Fact]
    public void TruncateForModel_MalformedOrSmuggledArtifacts_AreNotPromoted()
    {
        var expiry = DateTime.UtcNow.AddHours(24).ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'");
        var payload = JsonSerializer.Serialize(new
        {
            data = new
            {
                stdout = new string('x', 80_000) +
                    "{\"artifacts\":[{\"artifactId\":\"zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz\"}]}",
                artifacts = new object[]
                {
                    new { artifactId = "not-hex!", fileName = "r.csv", contentType = "text/csv",
                          length = 1, sha256 = "b".PadRight(64, 'b'), expiresAtUtc = expiry },
                    new { artifactId = "c".PadRight(32, 'c'), fileName = "../evil.csv",
                          contentType = "text/csv", length = 1,
                          sha256 = "d".PadRight(64, 'd'), expiresAtUtc = expiry },
                },
                preview = new { artifacts = new[] { new { artifactId = "e".PadRight(32, 'e') } } },
            },
        });

        var truncated = AgentToolResult.TruncateForModel(payload);
        using var doc = JsonDocument.Parse(truncated);
        // Invalid ids, traversal names, and JSON smuggled inside text fields are
        // never promoted to the trusted artifacts field.
        Assert.False(doc.RootElement.TryGetProperty("artifacts", out var promoted)
            && promoted.GetArrayLength() > 0);
        if (doc.RootElement.TryGetProperty("artifacts", out var any))
            Assert.DoesNotContain("evil.csv", any.ToString());
    }

    [Fact]
    public void TruncateForModel_OversizedCaptureResult_PreservesTemporaryDownloadUrl()
    {
        var id = "a".PadRight(32, 'a');
        var expiry = DateTime.UtcNow.AddHours(24)
            .ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'");
        var url = $"/api/ai/chat/generated-files/{id}/download";
        var payload = JsonSerializer.Serialize(new
        {
            data = new
            {
                stdout = new string('x', 80_000),
                artifacts = new[]
                {
                    new
                    {
                        artifactId = id, fileName = "r.csv",
                        contentType = "text/csv", length = 10,
                        sha256 = "b".PadRight(64, 'b'), expiresAtUtc = expiry,
                        downloadUrl = url,
                    },
                },
            },
        });

        var truncated = AgentToolResult.TruncateForModel(payload);
        Assert.True(truncated.Length <= AgentToolResult.MaxModelResultChars);
        using var doc = JsonDocument.Parse(truncated);
        var kept = Assert.Single(doc.RootElement.GetProperty("artifacts").EnumerateArray());
        Assert.Equal(id, kept.GetProperty("artifactId").GetString());
        Assert.Equal(url, kept.GetProperty("downloadUrl").GetString());
    }

    [Fact]
    public void TruncateForModel_OutcomesEnvelopeResult_PreservesDownloadUrl()
    {
        // Handles nested inside a truncated outcome envelope keep their URL
        // through the second promotion level as well.
        var id = "f".PadRight(32, 'f');
        var url = $"/api/ai/chat/generated-files/{id}/download";
        var expiry = DateTime.UtcNow.AddHours(24)
            .ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'");
        var inner = JsonSerializer.Serialize(new
        {
            truncated = true,
            originalLength = 90_000,
            artifacts = new[]
            {
                new
                {
                    artifactId = id, fileName = "отчёт.csv",
                    contentType = "text/csv", length = 4,
                    sha256 = "b".PadRight(64, 'b'), expiresAtUtc = expiry,
                    downloadUrl = url,
                },
            },
            preview = new string('y', 1_000),
        });
        var outcomes = JsonSerializer.Serialize(new object[]
        {
            new
            {
                step = 1, tool = "run_python", ok = true,
                result = JsonDocument.Parse(inner).RootElement.Clone(),
            },
            new { step = 2, tool = "run_python", ok = true,
                  result = new { stdout = new string('z', 80_000) } },
        });

        var truncated = AgentToolResult.TruncateForModel(outcomes);
        Assert.True(truncated.Length <= AgentToolResult.MaxModelResultChars);
        using var doc = JsonDocument.Parse(truncated);
        var kept = Assert.Single(doc.RootElement.GetProperty("artifacts").EnumerateArray());
        Assert.Equal(url, kept.GetProperty("downloadUrl").GetString());
    }

    [Fact]
    public void TruncateForModel_ForeignOrMalformedDownloadUrl_DropsOnlyThatHandle()
    {
        var validId = "a".PadRight(32, 'a');
        var foreignId = "c".PadRight(32, 'c');
        var suffixedId = "d".PadRight(32, 'd');
        var legacyId = "e".PadRight(32, 'e');
        var expiry = DateTime.UtcNow.AddHours(24)
            .ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'");
        var sha = "b".PadRight(64, 'b');
        object Meta(string id, string name, string? url) => url == null
            ? new { artifactId = id, fileName = name, contentType = "text/csv",
                    length = 1, sha256 = sha, expiresAtUtc = expiry }
            : new { artifactId = id, fileName = name, contentType = "text/csv",
                    length = 1, sha256 = sha, expiresAtUtc = expiry,
                    downloadUrl = url };
        var payload = JsonSerializer.Serialize(new
        {
            data = new
            {
                stdout = new string('x', 80_000),
                artifacts = new object[]
                {
                    // Canonical URL survives.
                    Meta(validId, "ok.csv",
                        $"/api/ai/chat/generated-files/{validId}/download"),
                    // Foreign host and suffixed route are rejected wholesale;
                    // the handle itself is not promoted with a fake URL.
                    Meta(foreignId, "foreign.csv", "https://evil.example/x"),
                    Meta(suffixedId, "suffixed.csv",
                        $"/api/ai/chat/generated-files/{suffixedId}/download?x=1"),
                    // Old handles without any URL remain usable (import only).
                    Meta(legacyId, "old.csv", null),
                },
            },
        });

        var truncated = AgentToolResult.TruncateForModel(payload);
        using var doc = JsonDocument.Parse(truncated);
        var kept = doc.RootElement.GetProperty("artifacts").EnumerateArray().ToList();
        Assert.Equal(2, kept.Count);
        Assert.Equal(validId, kept[0].GetProperty("artifactId").GetString());
        Assert.Equal(
            $"/api/ai/chat/generated-files/{validId}/download",
            kept[0].GetProperty("downloadUrl").GetString());
        Assert.Equal(legacyId, kept[1].GetProperty("artifactId").GetString());
        Assert.False(kept[1].TryGetProperty("downloadUrl", out var old)
            && old.ValueKind == JsonValueKind.String);
    }

    [Fact]
    public void TruncateForModel_RootCaptureResult_PromotesVerdictBesideArtifacts()
    {
        var id = "a".PadRight(32, 'a');
        var url = $"/api/ai/chat/generated-files/{id}/download";
        var expiry = DateTime.UtcNow.AddHours(24)
            .ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'");
        // The real run_python result: verdict and handles live at the root.
        var payload = JsonSerializer.Serialize(new
        {
            exitCode = 0,
            error = (string?)null,
            stdout = new string('x', 80_000),
            artifacts = new[]
            {
                new
                {
                    artifactId = id, fileName = "r.csv",
                    contentType = "text/csv", length = 10,
                    sha256 = "b".PadRight(64, 'b'), expiresAtUtc = expiry,
                    downloadUrl = url,
                },
            },
        });

        var truncated = AgentToolResult.TruncateForModel(payload);
        Assert.True(truncated.Length <= AgentToolResult.MaxModelResultChars);
        using var doc = JsonDocument.Parse(truncated);
        Assert.Equal(0, doc.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("error").ValueKind);
        var kept = Assert.Single(
            doc.RootElement.GetProperty("artifacts").EnumerateArray());
        Assert.Equal(url, kept.GetProperty("downloadUrl").GetString());
    }

    [Fact]
    public void TruncateForModel_EnvelopeResult_KeepsRootVerdictThroughSecondPass()
    {
        var id = "a".PadRight(32, 'a');
        var url = $"/api/ai/chat/generated-files/{id}/download";
        var expiry = DateTime.UtcNow.AddHours(24)
            .ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'");
        var firstPass = AgentToolResult.TruncateForModel(JsonSerializer.Serialize(new
        {
            exitCode = 0,
            error = (string?)null,
            stdout = new string('x', 80_000),
            artifacts = new[]
            {
                new
                {
                    artifactId = id, fileName = "r.csv",
                    contentType = "text/csv", length = 10,
                    sha256 = "b".PadRight(64, 'b'), expiresAtUtc = expiry,
                    downloadUrl = url,
                },
            },
        }));

        // An oversized re-serialization of that envelope must keep the root
        // verdict - the cabinet never digs it out of important or preview.
        var rewrapped = JsonSerializer.Serialize(new
        {
            truncated = true,
            originalLength = 90_000,
            important = new { exitCode = 0, error = (string?)null },
            artifacts = JsonDocument.Parse(firstPass).RootElement
                .GetProperty("artifacts").Clone(),
            exitCode = 0,
            error = (string?)null,
            preview = new string('y', 80_000),
        });
        var retruncated = AgentToolResult.TruncateForModel(rewrapped);
        Assert.True(retruncated.Length <= AgentToolResult.MaxModelResultChars);
        using var doc = JsonDocument.Parse(retruncated);
        Assert.Equal(0, doc.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("error").ValueKind);
        var kept = Assert.Single(
            doc.RootElement.GetProperty("artifacts").EnumerateArray());
        Assert.Equal(url, kept.GetProperty("downloadUrl").GetString());
    }

    [Fact]
    public void TruncateForModel_FailedCaptureResult_KeepsContradictoryMarkersAtRoot()
    {
        var id = "c".PadRight(32, 'c');
        var url = $"/api/ai/chat/generated-files/{id}/download";
        var expiry = DateTime.UtcNow.AddHours(24)
            .ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'");
        // A failed run that still carries artifact rows must not truncate into
        // an apparently successful result.
        var payload = JsonSerializer.Serialize(new
        {
            exitCode = 1,
            error = "division by zero",
            errorClass = "runtime_error",
            outcomeUnknown = true,
            isError = true,
            status = "failed",
            stdout = new string('x', 80_000),
            artifacts = new[]
            {
                new
                {
                    artifactId = id, fileName = "r.csv",
                    contentType = "text/csv", length = 10,
                    sha256 = "b".PadRight(64, 'b'), expiresAtUtc = expiry,
                    downloadUrl = url,
                },
            },
        });

        var truncated = AgentToolResult.TruncateForModel(payload);
        Assert.True(truncated.Length <= AgentToolResult.MaxModelResultChars);
        using var doc = JsonDocument.Parse(truncated);
        Assert.Equal(1, doc.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal("division by zero", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("runtime_error",
            doc.RootElement.GetProperty("errorClass").GetString());
        Assert.True(doc.RootElement.GetProperty("outcomeUnknown").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("isError").GetBoolean());
        Assert.Equal("failed", doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public void TruncateForModel_WrappedResult_DoesNotFabricateRootVerdict()
    {
        var id = "a".PadRight(32, 'a');
        var expiry = DateTime.UtcNow.AddHours(24)
            .ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'");
        // exitCode nested under data is not promoted: only the real root
        // verdict may sit beside the promoted handles.
        var payload = JsonSerializer.Serialize(new
        {
            data = new
            {
                exitCode = 0,
                error = (string?)null,
                stdout = new string('x', 80_000),
                artifacts = new[]
                {
                    new
                    {
                        artifactId = id, fileName = "r.csv",
                        contentType = "text/csv", length = 10,
                        sha256 = "b".PadRight(64, 'b'), expiresAtUtc = expiry,
                        downloadUrl = $"/api/ai/chat/generated-files/{id}/download",
                    },
                },
            },
        });

        var truncated = AgentToolResult.TruncateForModel(payload);
        using var doc = JsonDocument.Parse(truncated);
        Assert.False(doc.RootElement.TryGetProperty("exitCode", out _));
        Assert.Single(doc.RootElement.GetProperty("artifacts").EnumerateArray());
    }
}
