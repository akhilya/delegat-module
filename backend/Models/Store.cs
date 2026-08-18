using Delegat.Infrastructure.Interfaces;
using Delegat.Models.Marketplace;
using Delegat.Services;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Delegat.Models;

/// <summary>
/// Represents a store (workspace) that contains products, categories, and marketplace connections.
/// </summary>
public class Store : IEntity, IMapToDTO<Store.StoreDTO>, IAddable<Store.AddForm>, IUpdateable<Store.UserJoinForm>
{
    /// <summary>
    /// Unique store identifier.
    /// </summary>
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Store display name.
    /// </summary>
    [StringLength(STORE_NAME_MAX_LENGTH)]
    public string name { get; set; } = null!;

    /// <summary>
    /// IANA timezone for display and scheduling context.
    /// </summary>
    [StringLength(TIMEZONE_MAX_LENGTH)]
    public string timezone { get; set; } = DEFAULT_TIMEZONE;

    /// <summary>
    /// When true, extended price fields are available on products and in mapping palette.
    /// </summary>
    public bool extendedPriceParameters { get; set; }

    /// <summary>
    /// ISO 4217 code of the store base currency. All product prices are stored in this currency.
    /// </summary>
    [StringLength(BASE_CURRENCY_MAX_LENGTH)]
    public string baseCurrency { get; set; } = DEFAULT_BASE_CURRENCY;

    /// <summary>
    /// Additional currencies with exchange rates to the store base currency.
    /// </summary>
    public virtual ICollection<StoreCurrency> currencies { get; set; } = new List<StoreCurrency>();

    /// <summary>
    /// Collection of users who are members of this store.
    /// </summary>
    [InverseProperty(nameof(User.memberships))]
    public virtual ICollection<User> members { get; set; } = new List<User>();

    /// <summary>
    /// Collection of products belonging to this store.
    /// </summary>
    public virtual ICollection<Products.Product> products { get; set; } = new List<Products.Product>();

    /// <summary>
    /// Collection of categories defined in this store.
    /// </summary>
    public virtual ICollection<Category> categories { get; set; } = new List<Category>();

    /// <summary>
    /// Collection of store-level custom attributes.
    /// </summary>
    public virtual ICollection<StoreAttribute> storeAttributes { get; set; } = new List<StoreAttribute>();

    /// <summary>
    /// Collection of marketplace connections for this store.
    /// </summary>
    public virtual ICollection<MarketplaceConnection> marketplaceConnections { get; set; } = new List<MarketplaceConnection>();

    /// <summary>
    /// Collection of product groups in this store.
    /// </summary>
    public virtual ICollection<ProductGroup> productGroups { get; set; } = new List<ProductGroup>();

    /// <summary>
    /// When set, first-run Setup is finished for this store.
    /// Null means the wizard may resume (adapters / catalog connect).
    /// </summary>
    public DateTime? onboardingCompletedAt { get; set; }

    public bool AddFill(AddForm form)
    {
        name = (form.name ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(form.timezone))
            timezone = form.timezone.Trim();
        else if (string.IsNullOrWhiteSpace(timezone))
            timezone = DEFAULT_TIMEZONE;

        if (!string.IsNullOrWhiteSpace(form.baseCurrency))
            baseCurrency = CurrencyService.NormalizeCode(form.baseCurrency);
        else if (string.IsNullOrWhiteSpace(baseCurrency))
            baseCurrency = DEFAULT_BASE_CURRENCY;

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
            timezone = timezone,
            extendedPriceParameters = extendedPriceParameters,
            baseCurrency = baseCurrency,
            currencies = MapCurrencyDtos(),
            membersCount = members?.Count(m => m.isActive) ?? 0,
            onboardingCompletedAt = onboardingCompletedAt,
        };
    }

    public StoreDetailDTO MapToDetailDTO(IServiceProvider serviceProvider)
    {
        return new StoreDetailDTO
        {
            id = Id,
            name = name,
            timezone = timezone,
            extendedPriceParameters = extendedPriceParameters,
            baseCurrency = baseCurrency,
            currencies = MapCurrencyDtos(),
            members = members?.Where(m => m.isActive).Select(m => new MemberDTO
            {
                id = m.Id,
                userName = m.UserName ?? "",
            }).ToList() ?? new List<MemberDTO>(),
            onboardingCompletedAt = onboardingCompletedAt,
        };
    }

    public List<CurrencyDTO> MapCurrencyDtos()
    {
        return CurrencyService.BuildCurrencyList(this)
            .Select(c => new CurrencyDTO
            {
                code = c.Code,
                rateToBase = c.RateToBase,
                isEnabled = c.IsEnabled,
                isBase = c.IsBase,
                updatedAt = c.UpdatedAt
            })
            .ToList();
    }

    /// <summary>
    /// Maximum length for store name.
    /// </summary>
    public const int STORE_NAME_MAX_LENGTH = 40;

    public const int TIMEZONE_MAX_LENGTH = 64;
    public const int BASE_CURRENCY_MAX_LENGTH = 3;
    public const string DEFAULT_TIMEZONE = "Europe/Moscow";
    public const string DEFAULT_BASE_CURRENCY = "RUB";

    /// <summary>
    /// Form for creating a new store.
    /// </summary>
    public struct AddForm
    {
        /// <summary>
        /// Store display name.
        /// </summary>
        public string name { get; set; }

        /// <summary>
        /// Add every currently active user as a member of the new store.
        /// </summary>
        public bool addAllUsers { get; set; }

        /// <summary>
        /// Optional IANA timezone (defaults to <see cref="DEFAULT_TIMEZONE"/>).
        /// </summary>
        public string? timezone { get; set; }

        /// <summary>
        /// Optional ISO 4217 base currency (defaults to <see cref="DEFAULT_BASE_CURRENCY"/>).
        /// </summary>
        public string? baseCurrency { get; set; }

        /// <summary>
        /// When true, leave <see cref="onboardingCompletedAt"/> null so Setup can resume.
        /// Settings-created stores omit this (default false) and are marked complete.
        /// </summary>
        public bool fromSetup { get; set; }
    }

    /// <summary>
    /// Form for adding a user to a store.
    /// </summary>
    public struct UserJoinForm
    {
        /// <summary>
        /// User to add to the store.
        /// </summary>
        public User user { get; set; }

        public UserJoinForm(User user)
        {
            this.user = user;
        }
    }

    /// <summary>
    /// Store summary DTO for list views.
    /// </summary>
    public struct StoreDTO
    {
        /// <summary>
        /// Unique store identifier.
        /// </summary>
        public int id { get; set; }

        /// <summary>
        /// Store display name.
        /// </summary>
        public string name { get; set; }

        public string timezone { get; set; }

        public bool extendedPriceParameters { get; set; }

        public string baseCurrency { get; set; }

        public List<CurrencyDTO> currencies { get; set; }

        /// <summary>
        /// Number of members in the store.
        /// </summary>
        public int membersCount { get; set; }

        /// <summary>
        /// Store favicon URL for switchers/lists (default CP square when unset).
        /// </summary>
        public string? faviconUrl { get; set; }

        /// <summary>
        /// When null, first-run Setup may resume on this store.
        /// </summary>
        public DateTime? onboardingCompletedAt { get; set; }
    }

    /// <summary>
    /// Detailed store DTO including member list.
    /// </summary>
    public struct StoreDetailDTO
    {
        /// <summary>
        /// Unique store identifier.
        /// </summary>
        public int id { get; set; }

        /// <summary>
        /// Store display name.
        /// </summary>
        public string name { get; set; }

        public string timezone { get; set; }

        public bool extendedPriceParameters { get; set; }

        public string baseCurrency { get; set; }

        public List<CurrencyDTO> currencies { get; set; }

        /// <summary>
        /// List of store members.
        /// </summary>
        public List<MemberDTO> members { get; set; }

        /// <summary>
        /// When null, first-run Setup may resume on this store.
        /// </summary>
        public DateTime? onboardingCompletedAt { get; set; }
    }

    public struct CurrencyDTO
    {
        public string code { get; set; }
        public decimal rateToBase { get; set; }
        public bool isEnabled { get; set; }
        public bool isBase { get; set; }
        public DateTime updatedAt { get; set; }
    }

    /// <summary>
    /// Store member data transfer object.
    /// </summary>
    public struct MemberDTO
    {
        /// <summary>
        /// User identifier.
        /// </summary>
        public int id { get; set; }

        /// <summary>
        /// Username of the member.
        /// </summary>
        public string userName { get; set; }
    }
}
