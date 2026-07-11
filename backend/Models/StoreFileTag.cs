using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Delegat.Models;

public class StoreFileTag
{
    [Key]
    public int Id { get; set; }

    [ForeignKey(nameof(file))]
    public int fileId { get; set; }

    [InverseProperty(nameof(StoreFile.tags))]
    public StoreFile file { get; set; } = null!;

    [ForeignKey(nameof(tagDefinition))]
    public int tagDefinitionId { get; set; }

    public TagDefinition tagDefinition { get; set; } = null!;
}
