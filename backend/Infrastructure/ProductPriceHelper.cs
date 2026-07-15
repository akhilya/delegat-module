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
}