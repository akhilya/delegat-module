using Delegat.Marketplace.Ozon;
using Delegat.Marketplace.SDK;
using Delegat.Marketplace.SDK.DTOs;
using Delegat.Services.Marketplace;
using Microsoft.Extensions.Caching.Memory;

namespace Delegat.Tests;

public class AdapterMetadataCacheTests
{
    private sealed class MemoryMetadataCache : IMarketplaceMetadataCache
    {
        private readonly Dictionary<string, object> _store = new(StringComparer.Ordinal);

        public bool TryGetValue<T>(string key, out T? value)
        {
            if (_store.TryGetValue(key, out var boxed) && boxed is T typed)
            {
                value = typed;
                return true;
            }

            value = default;
            return false;
        }

        public void Set<T>(string key, T value, TimeSpan ttl)
        {
            _store[key] = value!;
        }

        public void Remove(string key) => _store.Remove(key);

        public int RemoveByPrefix(string prefix)
        {
            var keys = _store.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            foreach (var key in keys)
                _store.Remove(key);
            return keys.Count;
        }

        public int Count => _store.Count;
    }

    [Fact]
    public void Ozon_ShouldContinueAttributeValuesPagination()
    {
        Assert.True(OzonPlugin.ShouldContinueAttributeValuesPagination(hasNext: true, pageCount: 2000));
        Assert.False(OzonPlugin.ShouldContinueAttributeValuesPagination(hasNext: false, pageCount: 2000));
        Assert.False(OzonPlugin.ShouldContinueAttributeValuesPagination(hasNext: true, pageCount: 0));
    }

    [Fact]
    public void Ozon_CacheKeys_AreStable()
    {
        Assert.Equal("ozon:fields:1:2", $"ozon:fields:{1}:{2}");
        Assert.Equal("ozon:attr-values:1:2:85", $"ozon:attr-values:{1}:{2}:{85}");
        Assert.Equal("ozon:quotas:7", $"ozon:quotas:{7}");
    }

    [Fact]
    public void Yandex_TreeAndParamsKeys()
    {
        var apiKey = "abcdefghijklmnop";
        Assert.Equal("yandex:category-tree:abcdefgh", $"yandex:category-tree:{apiKey[..8]}");
        Assert.Equal("yandex:params:123:456", $"yandex:params:{123}:{456}");
    }

    [Fact]
    public void OpenCart_Keys_AreConnectionScoped()
    {
        Assert.Equal("opencart:cats:42:root:", $"opencart:cats:{42}:root:");
        Assert.Equal("opencart:fields:42:15", $"opencart:fields:{42}:15");
    }

    [Fact]
    public void MetadataCache_Hit_KeepsSingleEntry()
    {
        var cache = new MemoryMetadataCache();
        cache.Set("ozon:fields:1:2", new List<ExternalField>
        {
            new("sku", "SKU", true, "string")
        }, TimeSpan.FromHours(1));

        Assert.True(cache.TryGetValue("ozon:fields:1:2", out List<ExternalField>? fields));
        Assert.Single(fields!);
        cache.Set("ozon:fields:1:2", fields!, TimeSpan.FromHours(1));
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void HostMarketplaceMetadataCache_CategoryFieldsKey()
    {
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var cache = new MarketplaceMetadataCache(memory);
        var key = MarketplaceMetadataCache.CategoryFieldsKey("ozon", 9, "1:2");
        cache.Set(key, new List<string> { "a" }, TimeSpan.FromMinutes(1));
        Assert.True(cache.TryGetValue(key, out List<string>? values));
        Assert.Equal(new[] { "a" }, values);
    }

    [Fact]
    public void HostMarketplaceMetadataCache_RemoveByPrefix()
    {
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var cache = new MarketplaceMetadataCache(memory);
        cache.Set("ozon:fields:1:2", new List<string> { "a" }, TimeSpan.FromMinutes(1));
        cache.Set("ozon:attr-values:1:2:85", new List<string> { "b" }, TimeSpan.FromMinutes(1));
        cache.Set("wb:dir:global", new List<string> { "c" }, TimeSpan.FromMinutes(1));

        Assert.Equal(2, cache.RemoveByPrefix("ozon:"));
        Assert.False(cache.TryGetValue("ozon:fields:1:2", out List<string>? _));
        Assert.True(cache.TryGetValue("wb:dir:global", out List<string>? wb));
        Assert.Equal(new[] { "c" }, wb);
    }
}
