namespace Delegat.Models.Ai;

public class AiPageContext
{
    public string Page { get; set; } = "";
    public int? MappingId { get; set; }
    public string? MappingName { get; set; }
    public int? ConnectionId { get; set; }
    public string? ConnectionName { get; set; }
    public int? ProductId { get; set; }
    public string? ProductName { get; set; }
    public int? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    /// <summary>Product selected for mapping preview on fieldMapping page.</summary>
    public int? PreviewProductId { get; set; }

    /// <summary>Background operation id when explaining operation logs.</summary>
    public string? OperationId { get; set; }

    /// <summary>Background operation title when explaining operation logs.</summary>
    public string? OperationTitle { get; set; }

    /// <summary>Background operation type (e.g. batch-export, folder-archive).</summary>
    public string? OperationType { get; set; }
}
