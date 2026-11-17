using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Delegat.Services.Ai.Agent;

/// <summary>
/// Structured failure for the generated-artifact pipeline. <see cref="Code"/> is the
/// only client/model-visible token; internal exception text never leaves the service.
/// </summary>
public sealed class GeneratedArtifactException(string code, bool outcomeUnknown = false)
    : Exception(code)
{
    public string Code { get; } = code;
    public bool OutcomeUnknown { get; } = outcomeUnknown;
}

/// <summary>One file the script declares it will produce inside the sandbox.</summary>
public sealed record GeneratedOutputSpec(string FileName, string ContentType);

/// <summary>Metadata-only handle returned to the model; never bytes, keys, or paths.</summary>
public sealed record GeneratedArtifactMetadata(
    string ArtifactId,
    string FileName,
    string ContentType,
    long Length,
    string Sha256,
    DateTime ExpiresAtUtc);

/// <summary>Exact effective arguments of a confirmed import_generated_file step.</summary>
public sealed record GeneratedFileImportRequest(
    string ArtifactId,
    string Sha256,
    long Length,
    DateTime ExpiresAtUtc,
    string Destination,
    int? ProductId,
    int? FolderId);

/// <summary>
/// Server-verified import detail attached to a Plan event. The client only displays
/// these fields; it can never mint or substitute them.
/// </summary>
public sealed record GeneratedFileImportApproval(
    int Step,
    string ArtifactId,
    string FileName,
    long Length,
    string Sha256,
    DateTime ExpiresAtUtc,
    string Destination,
    int StoreId,
    int? ProductId,
    int? FolderId);

/// <summary>
/// Proposal validation shared by both engines: <see cref="Rejection"/> is the
/// serialized safe error returned to the model; <see cref="GeneratedFileImports"/>
/// carries the server-verified detail attached to the Plan event.
/// </summary>
internal sealed record GeneratedPlanValidation(
    string? Rejection,
    IReadOnlyList<GeneratedFileImportApproval> GeneratedFileImports);

/// <summary>Result of a confirmed import step.</summary>
public sealed record GeneratedFileImportResult(
    string Destination,
    int FileId,
    int? ProductId,
    int? FolderId,
    string FileName,
    string ContentType,
    long Length,
    string Sha256,
    string DownloadUrl,
    bool Idempotent,
    int WrittenCount);

/// <summary>
/// Limits and defensive parsers shared by the sandbox client, the artifact service,
/// the plan validator and the tool host. Every reader uses TryGetValue-style checks:
/// malformed tool JSON yields a structured rejection, never a 500.
/// </summary>
public static class GeneratedFileContract
{
    public const int MaxOutputs = 4;
    public const long MaxFileBytes = 32L * 1024 * 1024;
    public const long MaxBatchBytes = 64L * 1024 * 1024;
    public const int MaxManifestBytes = 2 * 1024 * 1024;
    public const long MaxArchiveBytes = MaxBatchBytes + MaxManifestBytes + 65536;
    public const int ProtocolVersion = 1;
    public static readonly TimeSpan ArtifactLifetime = TimeSpan.FromHours(24);

    public const string DestinationStore = "store";
    public const string DestinationProduct = "product";
    public const string ImportToolName = "import_generated_file";
    public const string PythonToolName = "run_python";

    public const int MaxFileNameChars = 180;
    public const int MaxFileNameUtf8Bytes = 255;
    public const int MaxContentTypeChars = 200;
    public const string DefaultContentType = "application/octet-stream";

    private static readonly Regex ArtifactIdRx = new("^[0-9a-f]{32}$", RegexOptions.Compiled);
    private static readonly Regex Sha256Rx = new("^[0-9a-f]{64}$", RegexOptions.Compiled);
    private static readonly Regex MediaTypeRx = new(
        @"^[a-zA-Z0-9!#$&^_.+\-]+/[a-zA-Z0-9!#$&^_.+\-]+$", RegexOptions.Compiled);
    private static readonly Encoding OutputNameEncoding = new UTF8Encoding(false, true);

    public static string NewArtifactId() =>
        Guid.NewGuid().ToString("N");

    /// <summary>MySQL datetime(6) precision: exact expiry must survive save/reload.</summary>
    public static DateTime TruncateToMicroseconds(DateTime utc) =>
        new(utc.Ticks - utc.Ticks % 10, DateTimeKind.Utc);

    /// <summary>
    /// Strict output basename: at most <see cref="MaxFileNameChars"/> UTF-16 code
    /// units and <see cref="MaxFileNameUtf8Bytes"/> UTF-8 bytes, no separators, no
    /// control characters, no dot-only segments, no surrounding whitespace, no
    /// malformed Unicode. Filenames are never sanitized - an invalid name is a
    /// rejection, not a rewrite.
    /// </summary>
    public static bool IsValidOutputFileName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxFileNameChars)
            return false;
        if (name != name.Trim() || name is "." or "..")
            return false;
        foreach (var ch in name)
        {
            if (char.IsControl(ch) || ch is '/' or '\\')
                return false;
        }
        try
        {
            if (OutputNameEncoding.GetByteCount(name) > MaxFileNameUtf8Bytes)
                return false;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
        return true;
    }

    /// <summary>
    /// Explicit single media type (no parameters, no CRLF); lowercase normalized.
    /// Omitted content type defaults to <see cref="DefaultContentType"/>.
    /// </summary>
    public static bool TryNormalizeContentType(string? raw, out string contentType)
    {
        contentType = DefaultContentType;
        if (raw == null)
            return true;
        var trimmed = raw.Trim();
        if (trimmed.Length == 0 || trimmed.Length > MaxContentTypeChars
            || trimmed != raw || !MediaTypeRx.IsMatch(trimmed))
            return false;
        contentType = trimmed.ToLowerInvariant();
        return true;
    }

    public static bool IsValidArtifactId(string? id) => id != null && ArtifactIdRx.IsMatch(id);
    public static bool IsValidSha256(string? sha) => sha != null && Sha256Rx.IsMatch(sha);

    /// <summary>
    /// Parses the optional <c>outputs</c> declaration of a run_python step. Rejects
    /// malformed types, unknown fields, duplicates, an explicitly empty list, and
    /// more than <see cref="MaxOutputs"/> entries.
    /// </summary>
    public static bool TryParseOutputs(JsonElement args, out IReadOnlyList<GeneratedOutputSpec>? outputs)
    {
        try
        {
            return TryParseOutputsCore(args, out outputs);
        }
        catch (InvalidOperationException)
        {
            // JSON unescaping can reject an unmatched UTF-16 surrogate before validation.
            outputs = null;
            return false;
        }
    }

    private static bool TryParseOutputsCore(JsonElement args, out IReadOnlyList<GeneratedOutputSpec>? outputs)
    {
        outputs = null;
        if (args.ValueKind != JsonValueKind.Object
            || !args.TryGetProperty("outputs", out var raw))
            return true;
        if (raw.ValueKind != JsonValueKind.Array)
            return false;
        var specs = new List<GeneratedOutputSpec>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in raw.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                return false;
            string? fileName = null;
            string? contentType = null;
            var hasFileName = false;
            foreach (var prop in item.EnumerateObject())
            {
                switch (prop.Name)
                {
                    case "fileName":
                        if (prop.Value.ValueKind != JsonValueKind.String)
                            return false;
                        fileName = prop.Value.GetString();
                        hasFileName = true;
                        break;
                    case "contentType":
                        if (prop.Value.ValueKind != JsonValueKind.String)
                            return false;
                        contentType = prop.Value.GetString();
                        break;
                    default:
                        return false; // unknown field
                }
            }
            if (!hasFileName || !IsValidOutputFileName(fileName)
                || !TryNormalizeContentType(contentType, out var normalized))
                return false;
            if (!seen.Add(fileName!))
                return false; // duplicate declared name
            specs.Add(new GeneratedOutputSpec(fileName!, normalized));
        }
        if (specs.Count == 0 || specs.Count > MaxOutputs)
            return false;
        outputs = specs;
        return true;
    }

    /// <summary>True when the step declares captured outputs (valid or invalid shape).</summary>
    public static bool HasOutputsArgument(JsonElement args) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty("outputs", out _);

    /// <summary>
    /// Parses the exact arguments of an import_generated_file step. Every field must
    /// be present with the exact shape; productId/folderId are explicit JSON null when
    /// not applicable. Unknown fields are rejected.
    /// </summary>
    public static bool TryParseImportRequest(
        JsonElement args,
        out GeneratedFileImportRequest? request)
    {
        request = null;
        if (args.ValueKind != JsonValueKind.Object)
            return false;

        string? artifactId = null;
        string? sha256 = null;
        string? destination = null;
        long? length = null;
        DateTime? expiresAtUtc = null;
        int? productId = null;
        int? folderId = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var prop in args.EnumerateObject())
        {
            if (!seen.Add(prop.Name))
                return false; // duplicate JSON key
            switch (prop.Name)
            {
                case "artifactId":
                    if (prop.Value.ValueKind != JsonValueKind.String)
                        return false;
                    artifactId = prop.Value.GetString();
                    break;
                case "sha256":
                    if (prop.Value.ValueKind != JsonValueKind.String)
                        return false;
                    sha256 = prop.Value.GetString();
                    break;
                case "length":
                    if (prop.Value.ValueKind != JsonValueKind.Number
                        || !prop.Value.TryGetInt64(out var parsed))
                        return false;
                    length = parsed;
                    break;
                case "expiresAtUtc":
                    if (prop.Value.ValueKind != JsonValueKind.String
                        || !TryParseInstant(prop.Value.GetString(), out var instant))
                        return false;
                    expiresAtUtc = instant;
                    break;
                case "destination":
                    if (prop.Value.ValueKind != JsonValueKind.String)
                        return false;
                    destination = prop.Value.GetString();
                    break;
                case "productId":
                    if (!TryReadNullableInt(prop.Value, out productId))
                        return false;
                    break;
                case "folderId":
                    if (!TryReadNullableInt(prop.Value, out folderId))
                        return false;
                    break;
                default:
                    return false; // unknown field
            }
        }

        const string required = "artifactId,sha256,length,expiresAtUtc,destination,productId,folderId";
        if (seen.Count != required.Split(',').Length)
            return false;

        if (!IsValidArtifactId(artifactId)
            || !IsValidSha256(sha256)
            || length is null or < 0 || length > MaxFileBytes
            || expiresAtUtc == null)
            return false;

        if (destination == DestinationStore)
        {
            if (productId != null)
                return false;
        }
        else if (destination == DestinationProduct)
        {
            if (productId is null or <= 0)
                return false;
        }
        else
        {
            return false;
        }
        if (folderId is <= 0)
            return false;

        request = new GeneratedFileImportRequest(
            artifactId!, sha256!, length.Value,
            TruncateToMicroseconds(expiresAtUtc.Value),
            destination!, productId, folderId);
        return true;
    }

    private static bool TryReadNullableInt(JsonElement value, out int? result)
    {
        result = null;
        if (value.ValueKind == JsonValueKind.Null)
            return true;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed))
        {
            result = parsed;
            return true;
        }
        return false;
    }

    internal static bool TryParseInstant(string? raw, out DateTime instant)
    {
        instant = default;
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 64)
            return false;
        if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind | DateTimeStyles.AllowWhiteSpaces, out var parsed))
            return false;
        if (parsed.Kind == DateTimeKind.Unspecified)
            return false; // an expiry must be an unambiguous UTC-equivalent instant
        instant = parsed.ToUniversalTime();
        return true;
    }

    /// <summary>Safe structured error payload: the code is data, not model prose.</summary>
    public static object ErrorResult(GeneratedArtifactException ex) => new
    {
        error = ex.Code,
        errorClass = "generated_artifact",
        retryable = false,
        outcomeUnknown = ex.OutcomeUnknown,
    };

    public static object ErrorResult(string code, bool outcomeUnknown = false) => new
    {
        error = code,
        errorClass = "generated_artifact",
        retryable = false,
        outcomeUnknown,
    };
}
