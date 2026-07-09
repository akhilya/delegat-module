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
}
