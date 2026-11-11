using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Delegat.Models;

/// <summary>Ordered link between a store attribute value and a product-owned file.</summary>
public class StoreProductAttributeFile
{
    [Key]
    public int Id { get; set; }

    [ForeignKey(nameof(attributeValue))]
    public int attributeValueId { get; set; }
    public StoreProductAttributeValue attributeValue { get; set; } = null!;

    [ForeignKey(nameof(file))]
    public int fileId { get; set; }
    public File file { get; set; } = null!;

    public int index { get; set; }
}
