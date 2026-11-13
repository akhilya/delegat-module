using System.Collections.Concurrent;
using Delegat.Marketplace.SDK;
using Microsoft.Extensions.Caching.Memory;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Process-wide metadata cache shared by marketplace adapters.
/// Tracks keys so adapter-scoped prefixes can be cleared without wiping other IMemoryCache entries.
/// </summary>
public sealed class MarketplaceMetadataCache : IMarketplaceMetadataCache
{
    public static readonly TimeSpan CategoryFieldsTtl = TimeSpan.FromHours(1);

    private readonly IMemoryCache _cache;
    private readonly ConcurrentDictionary<string, byte> _keys = new(StringComparer.Ordinal);

    public MarketplaceMetadataCache(IMemoryCache cache)
    {
        _cache = cache;
    }

    public static string CategoryFieldsKey(string adapterKey, int connectionId, string externalCategoryId)
        => $"category-fields:{adapterKey}:{connectionId}:{externalCategoryId}";

    public static string PriceFieldsKey(string adapterKey, int connectionId, string externalCategoryId)
        => $"price-fields:{adapterKey}:{connectionId}:{externalCategoryId}";

    public bool TryGetValue<T>(string key, out T? value)
    {
        if (_cache.TryGetValue(key, out T? cached) && cached is not null)
        {
            value = cached;
            return true;
        }

        value = default;
        return false;
    }

    public void Set<T>(string key, T value, TimeSpan ttl)
    {
        _cache.Set(key, value, ttl);
        _keys[key] = 0;
    }

    public void Remove(string key)
    {
        _cache.Remove(key);
        _keys.TryRemove(key, out _);
    }

    public int RemoveByPrefix(string prefix)
    {
        if (string.IsNullOrEmpty(prefix))
            return 0;

        var removed = 0;
        foreach (var key in _keys.Keys)
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            _cache.Remove(key);
            if (_keys.TryRemove(key, out _))
                removed++;
        }

        return removed;
    }
}
