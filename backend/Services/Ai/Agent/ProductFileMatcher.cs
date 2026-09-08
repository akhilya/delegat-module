namespace Delegat.Services.Ai.Agent;

public sealed record ProductFileMatchProduct(int Id, string Sku, string? Barcode, string Name);

public sealed record ProductFileMatchInput(string RelativePath, string FileName);

public sealed record ProductFileMatchResult(
    string RelativePath,
    int? ProductId,
    string? Sku,
    string? Name,
    double Confidence,
    string Reason);

public static class ProductFileMatcher
{
    public static IReadOnlyList<ProductFileMatchResult> Match(
        IReadOnlyList<ProductFileMatchInput> files,
        IReadOnlyList<ProductFileMatchProduct> products)
    {
        var results = new List<ProductFileMatchResult>(files.Count);
        foreach (var file in files)
        {
            var stem = Path.GetFileNameWithoutExtension(file.FileName);
            var hay = file.FileName;

            ProductFileMatchProduct? hit = null;
            var reason = "none";
            var confidence = 0d;

            foreach (var p in products)
            {
                if (!string.IsNullOrWhiteSpace(p.Sku)
                    && hay.Contains(p.Sku, StringComparison.OrdinalIgnoreCase))
                {
                    hit = p;
                    reason = "sku";
                    confidence = 0.95;
                    break;
                }
            }

            if (hit == null)
            {
                foreach (var p in products)
                {
                    if (!string.IsNullOrWhiteSpace(p.Barcode)
                        && hay.Contains(p.Barcode, StringComparison.OrdinalIgnoreCase))
                    {
                        hit = p;
                        reason = "barcode";
                        confidence = 0.9;
                        break;
                    }
                }
            }

            if (hit == null)
            {
                foreach (var p in products)
                {
                    if (!string.IsNullOrWhiteSpace(p.Name)
                        && stem.Equals(p.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        hit = p;
                        reason = "name";
                        confidence = 0.8;
                        break;
                    }
                }
            }

            results.Add(new ProductFileMatchResult(
                file.RelativePath,
                hit?.Id,
                hit?.Sku,
                hit?.Name,
                confidence,
                reason));
        }

        return results;
    }
}
