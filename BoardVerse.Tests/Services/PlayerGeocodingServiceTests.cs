using BoardVerse.Core.Settings;
using BoardVerse.Services.Services.Geocoding;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BoardVerse.Tests.Services;

public class PlayerGeocodingServiceTests
{
    private static readonly NominatimSettings TestSettings = new()
    {
        EnableCache = true,
        CacheTtlHours = 1,
        CoordinateQuantization = 0.00001
    };

    [Fact]
    public async Task ReverseGeocodeAsync_NoCache_FetchesAndStores()
    {
        var fakeClient = new FakeGeocodingClient(
            """{"lat":"10.0","lon":"106.0","address":{"city_district":"Quận 1","city":"HCM","country":"VN"}}""");
        var cache = new InMemoryCacheAdapter();

        var service = new PlayerGeocodingService(
            fakeClient,
            Options.Create(TestSettings),
            cache,
            NullLogger<PlayerGeocodingService>.Instance);

        var result = await service.ReverseGeocodeAsync(10.776889, 106.700806);

        Assert.NotNull(result);
        Assert.Equal("Quận 1", result!.District);
        Assert.Equal("HCM", result.City);
        Assert.Equal(1, fakeClient.CallCount);
    }

    [Fact]
    public async Task ReverseGeocodeAsync_CacheHit_DoesNotCallClient()
    {
        var fakeClient = new FakeGeocodingClient(
            """{"lat":"10.0","lon":"106.0","address":{"city":"HCM"}}""");
        var cache = new InMemoryCacheAdapter();
        // Pre-seed cache
        var key = PlayerGeocodingService.BuildCacheKey(10.776889, 106.700806);
        cache.Set(key, new ReverseGeocodeResult { City = "CachedCity" }, TimeSpan.FromMinutes(10));

        var service = new PlayerGeocodingService(
            fakeClient,
            Options.Create(TestSettings),
            cache,
            NullLogger<PlayerGeocodingService>.Instance);

        var result = await service.ReverseGeocodeAsync(10.776889, 106.700806);

        Assert.NotNull(result);
        Assert.Equal("CachedCity", result!.City);
        Assert.Equal(0, fakeClient.CallCount);
    }

    [Fact]
    public async Task ReverseGeocodeAsync_ClientThrows_ReturnsNullWithoutThrowing()
    {
        var throwingClient = new FakeGeocodingClient(throwOnCall: true);
        var cache = new InMemoryCacheAdapter();

        var service = new PlayerGeocodingService(
            throwingClient,
            Options.Create(TestSettings),
            cache,
            NullLogger<PlayerGeocodingService>.Instance);

        var result = await service.ReverseGeocodeAsync(10.776889, 106.700806);

        Assert.Null(result);
    }

    [Fact]
    public async Task ReverseGeocodeAsync_ClientReturnsEmpty_DoesNotCacheNull()
    {
        // Behavior sau fix: null KHÔNG được cache để tránh poison cache 30 ngày.
        // Mỗi request sẽ gọi lại client.
        var fakeClient = new FakeGeocodingClient("");
        var cache = new InMemoryCacheAdapter();

        var service = new PlayerGeocodingService(
            fakeClient,
            Options.Create(TestSettings),
            cache,
            NullLogger<PlayerGeocodingService>.Instance);

        var first = await service.ReverseGeocodeAsync(10.0, 106.0);
        var second = await service.ReverseGeocodeAsync(10.0, 106.0);

        Assert.Null(first);
        // Second call phải gọi lại client (cache không lưu null)
        Assert.Equal(2, fakeClient.CallCount);
        Assert.Null(second);
    }

    [Fact]
    public void BuildCacheKey_QuantizesCoordinates()
    {
        // Hai toạ độ cách nhau ~0.5m vẫn ra cùng key
        var key1 = PlayerGeocodingService.BuildCacheKey(10.7768890, 106.7008060);
        var key2 = PlayerGeocodingService.BuildCacheKey(10.7768895, 106.7008065);
        Assert.Equal(key1, key2);

        // Cách nhau ~1.1km → khác key
        var key3 = PlayerGeocodingService.BuildCacheKey(10.786889, 106.700806);
        Assert.NotEqual(key1, key3);
    }

    private sealed class FakeGeocodingClient : IGeocodingClient
    {
        private readonly string? _raw;
        private readonly bool _throw;

        public int CallCount { get; private set; }

        public FakeGeocodingClient(string? raw = null, bool throwOnCall = false)
        {
            _raw = raw;
            _throw = throwOnCall;
        }

        public Task<string?> ReverseGeocodeRawAsync(
            double latitude,
            double longitude,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (_throw)
            {
                throw new HttpRequestException("simulated");
            }
            return Task.FromResult(_raw);
        }
    }

    private sealed class InMemoryCacheAdapter : IMemoryCacheGeocoding
    {
        private readonly Dictionary<string, object?> _store = new();

        public bool TryGetValue<T>(string key, out T? value)
        {
            if (_store.TryGetValue(key, out var raw))
            {
                if (raw is null)
                {
                    // Cache miss-after-set of null value → treat as cached null (valid hit).
                    value = default;
                    return true;
                }
                if (raw is T typed)
                {
                    value = typed;
                    return true;
                }
            }
            value = default;
            return false;
        }

        public void Set<T>(string key, T value, TimeSpan ttl)
        {
            _store[key] = value;
        }
    }
}