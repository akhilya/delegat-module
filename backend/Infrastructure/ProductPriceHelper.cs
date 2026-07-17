namespace Delegat.Infrastructure;

public static class ProductPriceHelper
{
    public static int? ComputeDiscountPercent(decimal? oldPrice, decimal? sellingPrice)
    {
        if (!oldPrice.HasValue || !sellingPrice.HasValue || oldPrice.Value <= 0)
            return null;

        var pct = (int)Math.Round((1 - sellingPrice.Value / oldPrice.Value) * 100);
        return Math.Clamp(pct, 0, 99);
    }

    /// <summary>
    /// Drop strike-through (old) price when it does not represent a real discount.
    /// Marketplaces such as Yandex reject oldPrice == price as a 0% discount.
    /// </summary>
    public static decimal? SanitizeOldPrice(decimal? price, decimal? oldPrice)
    {
        if (oldPrice is null or <= 0)
            return null;
        if (price is null)
            return oldPrice;
        return oldPrice.Value > price.Value ? oldPrice : null;
    }
}