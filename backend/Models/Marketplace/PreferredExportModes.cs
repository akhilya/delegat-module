namespace Delegat.Models.Marketplace;

/// <summary>
/// Allowed preferred export mode values for product adapter UI.
/// </summary>
public static class PreferredExportModes
{
    public const string Full = "full";
    public const string Price = "price";
    public const string Stock = "stock";

    public const int MaxLength = 16;

    public static bool IsValid(string? mode) =>
        mode == Full || mode == Price || mode == Stock;

    public static string Normalize(string? mode) =>
        IsValid(mode) ? mode! : Full;
}
