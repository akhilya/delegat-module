using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Delegat.Models;

/// <summary>Ordered link between a category attribute value and a product-owned file.</summary>
public class ProductAttributeFile
{
    [Key]
    public int Id { get; set; }

    [ForeignKey(nameof(attributeValue))]
    public int attributeValueId { get; set; }
    public ProductAttributeValue attributeValue { get; set; } = null!;

    [ForeignKey(nameof(file))]
    public int fileId { get; set; }
    public File file { get; set; } = null!;

    public int index { get; set; }
}
