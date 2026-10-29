using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;

namespace Delegat.Services;

public class CurrencyService
{
    private readonly DataContext _context;

    public CurrencyService(DataContext context)
    {
        _context = context;
    }

    public async Task<string> GetStoreBaseCurrencyAsync(int storeId, CancellationToken ct = default)
    {
        var code = await _context.stores
            .AsNoTracking()
            .Where(s => s.Id == storeId)
            .Select(s => s.baseCurrency)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(code))
            throw ApiException.BadRequest(code: "base_currency_unconfigured");

        return NormalizeCode(code);
    }

    public async Task<decimal> GetRateToBaseAsync(int storeId, string currencyCode, CancellationToken ct = default)
    {
        var baseCurrency = await GetStoreBaseCurrencyAsync(storeId, ct);
        var code = NormalizeCode(currencyCode);
        if (code == baseCurrency)
            return 1m;

        var row = await _context.storeCurrencies
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.storeId == storeId && c.code == code && c.isEnabled, ct);

        if (row == null)
            throw ApiException.BadRequest(code: "currency_unavailable");

        if (row.rateToBase <= 0)
            throw ApiException.BadRequest(code: "currency_rate_invalid");

        return row.rateToBase;
    }

    public async Task<decimal> ConvertToBaseAsync(decimal amount, string currencyCode, int storeId, CancellationToken ct = default)
    {
        var rate = await GetRateToBaseAsync(storeId, currencyCode, ct);
        return Math.Round(amount * rate, 2, MidpointRounding.AwayFromZero);
    }

    public async Task<decimal> ConvertFromBaseAsync(decimal baseAmount, string currencyCode, int storeId, CancellationToken ct = default)
    {
        var baseCurrency = await GetStoreBaseCurrencyAsync(storeId, ct);
        var code = NormalizeCode(currencyCode);
        if (code == baseCurrency)
            return baseAmount;

        var rate = await GetRateToBaseAsync(storeId, code, ct);
        return Math.Round(baseAmount / rate, 2, MidpointRounding.AwayFromZero);
    }

    public async Task<decimal> ConvertAsync(decimal amount, string fromCode, string toCode, int storeId, CancellationToken ct = default)
    {
        var from = NormalizeCode(fromCode);
        var to = NormalizeCode(toCode);
        if (from == to)
            return amount;

        var inBase = await ConvertToBaseAsync(amount, from, storeId, ct);
        return await ConvertFromBaseAsync(inBase, to, storeId, ct);
    }

    public async Task<List<StoreCurrencyDto>> GetStoreCurrenciesAsync(int storeId, CancellationToken ct = default)
    {
        var store = await _context.stores
            .AsNoTracking()
            .Include(s => s.currencies)
            .FirstOrDefaultAsync(s => s.Id == storeId, ct);

        if (store == null)
            throw ApiException.NotFound(code: "store_not_found");

        return BuildCurrencyList(store);
    }

    public static List<StoreCurrencyDto> BuildCurrencyList(Store store)
    {
        var baseCode = NormalizeCode(store.baseCurrency);
        var result = new List<StoreCurrencyDto>
        {
            new(baseCode, 1m, true, DateTime.UtcNow, true)
        };

        if (store.currencies != null)
        {
            result.AddRange(store.currencies
                .OrderBy(c => c.code)
                .Select(c => new StoreCurrencyDto(c.code, c.rateToBase, c.isEnabled, c.updatedAt, false)));
        }

        return result;
    }

    public static string NormalizeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw ApiException.BadRequest(code: "currency_code_required");
        return code.Trim().ToUpperInvariant();
    }

    public static string NormalizeCodeOrBase(string? code, string baseCurrency)
    {
        if (string.IsNullOrWhiteSpace(code))
            return NormalizeCode(baseCurrency);
        return NormalizeCode(code);
    }

    public static bool IsValidCurrencyCode(string code)
    {
        return code.Length == 3 && code.All(char.IsLetter);
    }
}

public record StoreCurrencyDto(
    string Code,
    decimal RateToBase,
    bool IsEnabled,
    DateTime UpdatedAt,
    bool IsBase);
