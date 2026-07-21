namespace Delegat.Models;

/// <summary>
/// Legacy store-admin join rows. Kept mappable for EF data migration to StoreManager role;
/// excluded from schema migrations (table created by AddStoreAdministrators).
/// </summary>
public class StoreAdministrator
{
    public int StoreId { get; set; }
    public int UserId { get; set; }
}
