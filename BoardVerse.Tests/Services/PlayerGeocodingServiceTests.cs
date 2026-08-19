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
        // Pre-seed cache — phải dùng cùng quantization với service settings
        // để key khớp với key được generate trong ReverseGeocodeAsync.
        var key = PlayerGeocodingService.BuildCacheKey(
            10.776889,
            106.700806,
            TestSettings.CoordinateQuantization);
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
    public async Task ReverseGeocodeAsync_ClientReturnsEmpty_NegativeCacheSkipsSecondCall()
    {
        // Behavior sau fix: client trả null/empty → cache NEGATIVE marker 5 phút
        // để tránh spam Nominatim/Photon khi upstream rate-limit.
        // Call thứ 2 trong TTL window sẽ skip client hoàn toàn (trả null nhưng không call).
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
        // Negative cache: call thứ 2 skip client → CallCount = 1.
        Assert.Equal(1, fakeClient.CallCount);
        Assert.Null(second);
    }

    [Fact]
    public async Task ReverseGeocodeAsync_ClientThrows_NegativeCacheSkipsRetry()
    {
        // Exception cũng trigger negative cache để không gọi lại liên tục.
        var throwingClient = new FakeGeocodingClient(throwOnCall: true);
        var cache = new InMemoryCacheAdapter();

        var service = new PlayerGeocodingService(
            throwingClient,
            Options.Create(TestSettings),
            cache,
            NullLogger<PlayerGeocodingService>.Instance);

        var first = await service.ReverseGeocodeAsync(10.0, 106.0);
        var second = await service.ReverseGeocodeAsync(10.0, 106.0);

        Assert.Null(first);
        Assert.Null(second);
        // Negative cache: exception lần 1 vẫn gọi client, lần 2 skip.
        Assert.Equal(1, throwingClient.CallCount);
    }

    [Fact]
    public async Task ReverseGeocodeAsync_PhotonWrapper_ParsesAndStores()
    {
        // Wrapper {"_source":"photon", ...} do FallbackGeocodingClient chèn vào
        // khi Nominatim fail → Photon success. Test parser route đúng.
        var photonWrapper = """
        {"_source":"photon","type":"FeatureCollection","features":[{"type":"Feature","properties":{"osm_id":123,"city":"Thành phố Hồ Chí Minh","district":"Quận 1","country":"Việt Nam","countrycode":"vn"}}]}
        """;

        var fakeClient = new FakeGeocodingClient(photonWrapper);
        var cache = new InMemoryCacheAdapter();

        var service = new PlayerGeocodingService(
            fakeClient,
            Options.Create(TestSettings),
            cache,
            NullLogger<PlayerGeocodingService>.Instance);

        var result = await service.ReverseGeocodeAsync(10.7769, 106.7008);

        Assert.NotNull(result);
        Assert.Equal("Quận 1", result!.District);
        Assert.Equal("Thành phố Hồ Chí Minh", result.City);
        Assert.Equal("Việt Nam", result.Country);
    }

    [Fact]
    public async Task ReverseGeocodeAsync_PhotonWrapper_FieldInMiddle_StillParses()
    {
        // Wrapper có thể có field "_source" ở giữa (không phải đầu tiên), vẫn phải parse được.
        // Dùng JSON parser để strip an toàn thay vì string IndexOf.
        var photonWrapper = """
        {"type":"FeatureCollection","_source":"photon","features":[{"properties":{"city":"Hà Nội","country":"Việt Nam"}}]}
        """;

        var fakeClient = new FakeGeocodingClient(photonWrapper);
        var cache = new InMemoryCacheAdapter();

        var service = new PlayerGeocodingService(
            fakeClient,
            Options.Create(TestSettings),
            cache,
            NullLogger<PlayerGeocodingService>.Instance);

        var result = await service.ReverseGeocodeAsync(21.0285, 105.8542);

        Assert.NotNull(result);
        Assert.Equal("Hà Nội", result!.City);
        Assert.Equal("Việt Nam", result.Country);
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

    [Fact]
    public void BuildCacheKey_DifferentQuantization_ProducesDifferentKey()
    {
        // 0.00001 (1.1m) và 0.001 (100m) phải ra khác key cho cùng coord
        // để tránh collision giữa 2 cache instance chạy khác precision.
        var precise = PlayerGeocodingService.BuildCacheKey(10.776889, 106.700806, 0.00001);
        var coarse = PlayerGeocodingService.BuildCacheKey(10.776889, 106.700806, 0.001);

        Assert.NotEqual(precise, coarse);
        // Coarse key format F3: "10.777:106.701"
        Assert.Contains("10.777", coarse);
        Assert.Contains("106.701", coarse);
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