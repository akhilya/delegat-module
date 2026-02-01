using Microsoft.AspNetCore.Identity;
using PrintO.Infrastructure.Interfaces;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PrintO.Models;

public class User : IdentityUser<int>, IEntity, IMapToDTO<User.UserDTO>, IUpdateable<User.SelectStoreForm>
{
    int IEntity.Id
    {
        get => Id;
        set => Id = value;
    }

    public int? selectedStoreId { get; set; }
    public Store? selectedStore { get; set; }

    [InverseProperty(nameof(Store.members))]
    public virtual ICollection<Store> memberships { get; set; } = new List<Store>();

    [StringLength(USER_NAME_MAX_LENGTH)]
    public override string? UserName { get => base.UserName; set => base.UserName = value; }

    public bool isAdmin { get; set; }

    public bool UpdateFill(SelectStoreForm form)
    {
        selectedStoreId = form.storeId;
        return true;
    }

    public UserDTO MapToDTO(IServiceProvider serviceProvider)
    {
        return new UserDTO
        {
            id = Id,
            userName = UserName,
            selectedStoreId = selectedStoreId,
            isAdmin = isAdmin
        };
    }

    public const int USER_NAME_MAX_LENGTH = 100;

    public struct UserDTO
    {
        public int id { get; set; }
        public string? userName { get; set; }
        public int? selectedStoreId { get; set; }
        public bool isAdmin { get; set; }
    }

    public struct SelectStoreForm
    {
        public int storeId { get; set; }

        public SelectStoreForm(int storeId)
        {
            this.storeId = storeId;
        }
    }
}
