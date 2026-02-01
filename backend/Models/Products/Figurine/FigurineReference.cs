using PrintO.Infrastructure.Interfaces;
using PrintO.Interfaces;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PrintO.Models.Products.Figurine;

public class FigurineReference : IEntity, IProductReference<FigurineVariation>, IMapToDTO<object>, IAddable<FigurineReference.AddForm>
{
    [Key]
    public int Id { get; set; }

    [ForeignKey(nameof(product))]
    public int productId { get; set; }
    public Product product { get; set; } = null!;

    public virtual ICollection<FigurineVariation> variations { get; set; } = new List<FigurineVariation>();

    public bool AddFill(AddForm form)
    {
        productId = form.productId;

        return true;
    }

    public object MapToDTO(IServiceProvider serviceProvider)
    {
        return new
        {
            Id,
            product = ((IMapToDTO<object>)product).MapToDTO(serviceProvider),
            variations = variations.Select(v => v.MapToDTO(serviceProvider)),
        };
    }

    public struct AddForm
    {
        public int productId { get; set; }

        public AddForm(int productId)
        {
            this.productId = productId;
        }
    }
}