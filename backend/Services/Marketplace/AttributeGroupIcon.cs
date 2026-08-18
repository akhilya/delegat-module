namespace Delegat.Services.Marketplace;

/// <summary>
/// Stored <see cref="Models.CategoryAttribute.groupIcon"/> values.
/// Marketplace groups use <c>adapter:{adapterKey}</c>; other groups store a Lucide-compatible icon name.
/// </summary>
public static class AttributeGroupIconValue
{
    public const string AdapterPrefix = "adapter:";

    public static string ForAdapter(string adapterKey)
    {
        var key = (adapterKey ?? "").Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(key) ? "" : AdapterPrefix + key;
    }
}
