using Delegat.Models.Ai;

namespace Delegat.Services.Ai.Agent;

public sealed class AgentUploadedFile
{
    public int Id { get; init; }
    public string FileName { get; init; } = "";
    public long Length { get; init; }
}

public sealed class StagedChatAttachment
{
    public int Id { get; init; }
    public string RelativePath { get; init; } = "";
    public string FileName { get; init; } = "";
    public long Length { get; init; }
    public DateTime ExpiresAt { get; init; }
}

public static class AgentChatAttachmentContext
{
    public static string? UserMessage(IReadOnlyList<AiChatAttachment> items)
    {
        if (items.Count == 0) return null;
        var lines = string.Join("\n", items.Take(AgentFileUploadService.MaxChatAttachments).Select(a =>
            $"- id={a.Id} relativePath={a.relativePath} name={a.fileName} size={a.length} type={a.contentType}"));
        return
            "The user attached files to this chat (already stored on the server). Decide from their request what to do with them — put them on products, use them another way, or ignore them. Internal: match_files is optional for name matching; writes use propose_plan then platform_request POST /products/{id}/files with attachmentIds and optional folderId.\n"
            + lines;
    }
}
