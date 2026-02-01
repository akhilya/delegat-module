using PrintO.Infrastructure.Interfaces;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PrintO.Models;

public class Store : IEntity, IMapToDTO<Store.StoreDTO>, IAddable<Store.AddForm>, IUpdateable<Store.UserJoinForm>
{
    [Key]
    public int Id { get; set; }

    [StringLength(STORE_NAME_MAX_LENGTH)]
    public string name { get; set; } = null!;

    [InverseProperty(nameof(User.memberships))]
    public virtual ICollection<User> members { get; set; } = new List<User>();

    public virtual ICollection<Products.Product> products { get; set; } = new List<Products.Product>();

    public bool AddFill(AddForm form)
    {
        name = form.name;
        return true;
    }

    public bool UpdateFill(UserJoinForm form)
    {
        members.Add(form.user);
        return true;
    }

    public StoreDTO MapToDTO(IServiceProvider serviceProvider)
    {
        return new StoreDTO
        {
            id = Id,
            name = name,
            membersCount = members?.Count ?? 0,
        };
    }

    public StoreDetailDTO MapToDetailDTO(IServiceProvider serviceProvider)
    {
        return new StoreDetailDTO
        {
            id = Id,
            name = name,
            members = members?.Select(m => new MemberDTO
            {
                id = m.Id,
                userName = m.UserName ?? ""
            }).ToList() ?? new List<MemberDTO>()
        };
    }

    public const int STORE_NAME_MAX_LENGTH = 40;

    public struct AddForm
    {
        public string name { get; set; }
    }

    public struct UserJoinForm
    {
        public User user { get; set; }

        public UserJoinForm(User user)
        {
            this.user = user;
        }
    }

    public struct StoreDTO
    {
        public int id { get; set; }
        public string name { get; set; }
        public int membersCount { get; set; }
    }

    public struct StoreDetailDTO
    {
        public int id { get; set; }
        public string name { get; set; }
        public List<MemberDTO> members { get; set; }
    }

    public struct MemberDTO
    {
        public int id { get; set; }
        public string userName { get; set; }
    }
}
