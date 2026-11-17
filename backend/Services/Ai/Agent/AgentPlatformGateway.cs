using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Delegat.Services.Ai.Agent;

public sealed class AgentPlatformGateway
{
    private readonly AgentFileUploadService _uploads;
    private readonly ICabinetRequestInvoker _invoker;
    private readonly CabinetCapabilityIndex? _capabilities;

    public AgentPlatformGateway(
        AgentFileUploadService uploads,
        ICabinetRequestInvoker invoker,
        CabinetCapabilityIndex? capabilities = null)
    {
        _uploads = uploads;
        _invoker = invoker;
        _capabilities = capabilities;
    }

    public async Task<(object result, string summary)> ExecuteAsync(
        int storeId,
        int userId,
        string method,
        string path,
        JsonElement? body,
        IReadOnlySet<string> permissions,
        CancellationToken ct)
    {
        // Yield so a caller that returns this Task without awaiting cannot keep a JsonDocument
        // alive across the first database await (InMemory EF often completes synchronously).
        await Task.Yield();

        var deny = PlatformRequestPolicy.DenyReason(method, path);
        if (deny != null)
            return (new { error = deny, errorClass = "forbidden", retryable = false, path, method },
                "Route is not allowed for the assistant");

        var mutating = PlatformRequestPolicy.IsMutating(method);
        if (mutating && AgentAccess.IsReadOnly(permissions))
            return (new { error = "forbidden", errorClass = "forbidden", retryable = false },
                "This account is read-only");

        var (apiPath, query) = PlatformRequestPolicy.SplitPathAndQuery(path);
        if (_capabilities != null)
        {
            var validation = _capabilities.Validate(method, apiPath);
            if (validation.Status != CabinetCapabilityIndex.StatusOk)
                return (PreflightError(validation, method, path),
                    $"Route rejected before dispatch: {validation.ErrorClass}");
        }

        var mismatch = AssistantBodyGuard.RejectReason(method, apiPath, body);
        if (mismatch != null)
            return (new { error = "body_mismatch", message = mismatch }, mismatch);

        if (IsAttachStagedFiles(method, apiPath, body, out var productId, out var attachmentIds, out var folderId))
        {
            try
            {
                var saved = await _uploads.AssignChatAttachmentsToProductAsync(
                    storeId, userId, productId, attachmentIds, folderId, ct);
                return (new { count = saved.Count, items = saved }, $"Attached {saved.Count} files");
            }
            catch (InvalidOperationException ex)
            {
                return (new { error = ex.Message }, ex.Message);
            }
        }

        return await _invoker.InvokeAsync(storeId, userId, method, apiPath, query, body, ct);
    }

    private static object PreflightError(CabinetCapabilityIndex.Validation validation, string method, string path) =>
        new
        {
            error = validation.Status,
            errorClass = validation.ErrorClass,
            retryable = false,
            method,
            path,
            callableAlternatives = validation.Alternatives
                .Select(a => new { method = a.Method, path = a.Path })
                .ToArray(),
            details = validation.Details,
        };

    private static bool IsAttachStagedFiles(
        string method,
        string apiPath,
        JsonElement? body,
        out int productId,
        out List<int> attachmentIds,
        out int? folderId)
    {
        productId = 0;
        attachmentIds = [];
        folderId = null;
        if (!HttpMethods.IsPost(method))
            return false;
        var parts = apiPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4
            || !parts[0].Equals("api", StringComparison.OrdinalIgnoreCase)
            || !parts[1].Equals("products", StringComparison.OrdinalIgnoreCase)
            || !parts[3].Equals("files", StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(parts[2], out productId))
            return false;

        attachmentIds = ReadIntArray(body, "attachmentIds");
        if (attachmentIds.Count == 0)
            return false;
        folderId = ReadInt(body, "folderId");
        return true;
    }

    private static List<int> ReadIntArray(JsonElement? body, string name)
    {
        var ids = new List<int>();
        if (body is not { ValueKind: JsonValueKind.Object } el) return ids;
        if (!el.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array) return ids;
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var n) && n > 0)
                ids.Add(n);
            else if (item.ValueKind == JsonValueKind.String && int.TryParse(item.GetString(), out var s) && s > 0)
                ids.Add(s);
        }

        return ids;
    }

    private static int? ReadInt(JsonElement? body, string name)
    {
        if (body is not { ValueKind: JsonValueKind.Object } el) return null;
        if (!el.TryGetProperty(name, out var p)) return null;
        if (p.ValueKind == JsonValueKind.Null) return null;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n)) return n;
        return int.TryParse(p.GetString(), out var s) ? s : null;
    }
}
