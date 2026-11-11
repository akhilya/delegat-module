using Delegat.Services.Marketplace;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Tests;

public class AdapterHttpLogServiceTests
{
    [Fact]
    public void HostStorageBudget_DoesNotInheritTheLargerSdkCaptureLimit()
    {
        // These are documented persistence budgets, independent of SDK download/capture support.
        Assert.InRange(AdapterHttpLogService.MaxStoredBodyChars, 1, 64 * 1024);
        Assert.InRange(AdapterHttpLogService.MaxDetailedRowsSafety, 1, 5_000);
        Assert.True(AdapterHttpLogService.MaxStoredBodyChars <
            global::Delegat.Marketplace.SDK.AdapterHttpLogHelper.MaxStoredBodyChars);
    }

    [Fact]
    public void DetailProjection_TruncatesBodiesInMySqlRatherThanClientMemory()
    {
        // SQL generation only: this test never opens a database connection.
        var options = new DbContextOptionsBuilder<DataContext>()
            .UseMySql("Server=localhost;Database=unused;User=unused;Password=unused",
                new MySqlServerVersion(new Version(8, 4, 0))).Options;
        using var db = new DataContext(options);
        var sql = AdapterHttpLogService.ProjectDetails(db.adapterHttpLogs.AsNoTracking()
            .Where(l => l.storeId == 1 && l.Id == 2), DateTime.UtcNow.AddHours(-24)).ToQueryString();

        Assert.Contains("SUBSTRING(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(AdapterHttpLogService.MaxPreviewBodyChars.ToString(), sql);
        Assert.Contains("CHAR_LENGTH(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CASE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SELECT `a`.`Id`, `a`.`createdAt`, `a`.`storeId`, `a`.`connectionId`, `a`.`adapterKey`, `a`.`method`, `a`.`url`, `a`.`path`, `a`.`statusCode`, `a`.`durationMs`, `a`.`requestHeadersJson`, `a`.`requestBody`", sql);
    }

    [Fact]
    public void RedactHeadersJson_MasksSensitiveHeaders_CaseInsensitive()
    {
        var headers = new[]
        {
            new KeyValuePair<string, IEnumerable<string>>("Authorization", new[] { "Bearer secret-token" }),
            new KeyValuePair<string, IEnumerable<string>>("api-key", new[] { "abc123" }),
            new KeyValuePair<string, IEnumerable<string>>("X-Request-Id", new[] { "req-1" }),
            new KeyValuePair<string, IEnumerable<string>>("Content-Type", new[] { "application/json" }),
        };

        var json = AdapterHttpLogService.RedactHeadersJson(headers);

        Assert.Contains("\"***\"", json);
        Assert.DoesNotContain("secret-token", json);
        Assert.DoesNotContain("abc123", json);
        Assert.Contains("req-1", json);
        Assert.Contains("application/json", json);
    }

    [Fact]
    public void RedactBody_MasksSensitiveJsonFields()
    {
        const string body = """{"api_key":"live-secret","password":"p@ss","name":"ok","token":"t1"}""";

        var redacted = AdapterHttpLogService.RedactBody(body);

        Assert.DoesNotContain("live-secret", redacted);
        Assert.DoesNotContain("p@ss", redacted);
        Assert.DoesNotContain("\"t1\"", redacted);
        Assert.Contains("\"name\":\"ok\"", redacted);
        Assert.Contains("\"***\"", redacted);
    }

    [Fact]
    public void RedactBody_LeavesNonSensitiveJsonUntouched()
    {
        const string body = """{"offerId":"SKU-1","price":100}""";
        Assert.Equal(body, AdapterHttpLogService.RedactBody(body));
    }

    [Fact]
    public void RedactBody_HidesDurableMediaGrantTokensInPayloads()
    {
        const string token = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var body = $$"""{"videos":["https://media.example.test/api/pub/media-export/{{token}}"]}""";

        var redacted = AdapterHttpLogService.RedactBody(body);

        Assert.DoesNotContain(token, redacted);
        Assert.Contains("/api/pub/media-export/[redacted]", redacted);
    }

    [Fact]
    public void TruncateBody_ShortBody_StillRedacts()
    {
        const string body = """{"secret":"should-hide","x":1}""";
        var result = AdapterHttpLogService.TruncateBody(body);

        Assert.DoesNotContain("should-hide", result);
        Assert.DoesNotContain("…[truncated]", result);
    }

    [Fact]
    public void TruncateBody_LongBody_TruncatesAndMarks()
    {
        var prefix = "{\"apiKey\":\"leak-me\",\"data\":\"";
        var filler = new string('x', AdapterHttpLogService.MaxStoredBodyChars);
        var body = prefix + filler + "\"}";

        var result = AdapterHttpLogService.TruncateBody(body);

        Assert.EndsWith(AdapterHttpLogService.TruncationMarker, result);
        Assert.True(result.Length < body.Length);
        Assert.DoesNotContain("leak-me", result);
    }

    [Fact]
    public void PreviewBody_LongStoredBody_TruncatesForUi()
    {
        var stored = new string('a', AdapterHttpLogService.MaxPreviewBodyChars + 100);
        var (preview, truncated, length) = AdapterHttpLogService.PreviewBody(stored);

        Assert.True(truncated);
        Assert.Equal(stored.Length, length);
        Assert.EndsWith(AdapterHttpLogService.TruncationMarker, preview);
        Assert.True(preview.Length < stored.Length);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void TruncateBody_NullOrEmpty_ReturnsEmpty(string? body, string expected)
    {
        Assert.Equal(expected, AdapterHttpLogService.TruncateBody(body));
    }
}
