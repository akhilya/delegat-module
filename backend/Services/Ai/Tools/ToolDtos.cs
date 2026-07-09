namespace Delegat.Services.Ai.Tools;

// Shared input DTOs used by multiple handler classes.

internal class IdInput { public int Id { get; set; } }
internal class IdsInput { public List<int> Ids { get; set; } = []; }
internal class SearchInput { public string? Search { get; set; } }
internal class ConnectionIdInput { public int? ConnectionId { get; set; } }
internal class OptionalIdInput { public int? Id { get; set; } }
internal class BatchIdInput { public string BatchId { get; set; } = ""; }
