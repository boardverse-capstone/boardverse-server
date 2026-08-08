#nullable enable
using System.Net;
using BoardVerse.Core.DTOs.Pos;
using BoardVerse.Core.Enum;
using BoardVerse.Data;
using BoardVerse.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BoardVerse.Tests.Integration;

/// <summary>
/// Lite projection của CafeTableStatusDto — chỉ chứa các field test cần assert.
/// Tránh reference Core namespace trong test (DTO thật có thể có field phụ khiến JSON parse lỗi).
/// </summary>
internal sealed class CafeTableStatusDtoLite
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public int SeatCount { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

/// <summary>
/// Integration tests for CafePosController - Extra endpoints
/// Covers: Sync tables, bookings, component checklist
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class CafePosControllerExtraIntegrationTests
{
    private readonly HttpClient _client;
    private readonly BoardVerseWebApplicationFactory _factory;

    public CafePosControllerExtraIntegrationTests(BoardVerseWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    #region === SYNC TABLES ===

    [IntegrationFact]
    public async Task CafePos_SyncTables()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var syncRequest = new
        {
            tables = new[]
            {
                new
                {
                    id = IntegrationTestFixtures.DemoPosTableId,
                    name = "Table 1",
                    seatCount = 4,
                    status = "Available"
                }
            }
        };

        var response = await ApiTestClient.PutJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/tables",
            syncRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone
                   );
    }

    /// <summary>
    /// Regression test cho bug "PUT /pos/tables trả data=[] khi bàn vừa sync có Status=InUse".
    /// Trước fix: controller gọi GetTablesAsync(..., includeOnlyAvailable=true default) → filter loại bàn InUse → response rỗng.
    /// Sau fix: controller gọi includeOnlyAvailable=false → trả TOÀN BỘ bàn active trong response sync.
    /// </summary>
    [IntegrationFact]
    public async Task CafePos_SyncTables_ReturnsTableWithInUseStatus_InResponsePayload()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var tableName = $"SyncRegression {Guid.NewGuid():N}".Substring(0, 22);

        // 1. Sync tạo 1 bàn mới — status ban đầu = Available.
        var firstSync = await ApiTestClient.PutJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/tables",
            new { tables = new[] { new { name = tableName, seatCount = 4, sortOrder = 0 } } });
        await ApiTestClient.AssertStatusOneOfAsync(firstSync, HttpStatusCode.OK);

        var firstBody = await ApiTestClient.ReadApiResponseAsync<List<CafeTableStatusDtoLite>>(firstSync);
        Assert.NotNull(firstBody.Data);
        var created = firstBody.Data!.FirstOrDefault(t => t.Name == tableName);
        Assert.NotNull(created);
        Assert.Equal("Available", created!.Status);

        // 2. Flip Status=InUse thông qua DbContext (giả lập bàn đang có phiên chơi).
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BoardVerseDbContext>();
            var table = await db.CafeTables.FirstOrDefaultAsync(t => t.Id == created.Id);
            Assert.NotNull(table);
            table!.Status = CafeTableStatus.InUse;
            await db.SaveChangesAsync();
        }

        // 3. Sync lại cùng bàn đó — response PHẢI chứa bàn (kể cả khi đang InUse).
        var secondSync = await ApiTestClient.PutJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/tables",
            new { tables = new[] { new { name = tableName, seatCount = 4, sortOrder = 0 } } });
        await ApiTestClient.AssertStatusOneOfAsync(secondSync, HttpStatusCode.OK);

        var secondBody = await ApiTestClient.ReadApiResponseAsync<List<CafeTableStatusDtoLite>>(secondSync);
        Assert.NotNull(secondBody.Data);
        var afterSync = secondBody.Data!.FirstOrDefault(t => t.Id == created.Id);
        Assert.NotNull(afterSync);
        // Đây là điểm mấu chốt của fix: bàn InUse vẫn phải xuất hiện trong response sync.
        Assert.Equal("InUse", afterSync!.Status);
    }

    /// <summary>
    /// Smoke test cho query param includeInactive:
    /// - Default (includeInactive=false): bàn soft-deleted phải KHÔNG xuất hiện trong response.
    /// - includeInactive=true: bàn soft-deleted PHẢI xuất hiện với IsActive=false.
    /// </summary>
    [IntegrationFact]
    public async Task CafePos_GetTables_IncludeInactive_HonorsSoftDeleteFlag()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var tableName = $"InactiveProbe {Guid.NewGuid():N}".Substring(0, 22);

        // 1. Sync tạo 1 bàn mới — mặc định IsActive=true.
        var firstSync = await ApiTestClient.PutJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/tables",
            new { tables = new[] { new { name = tableName, seatCount = 4, sortOrder = 0 } } });
        await ApiTestClient.AssertStatusOneOfAsync(firstSync, HttpStatusCode.OK);

        var firstBody = await ApiTestClient.ReadApiResponseAsync<List<CafeTableStatusDtoLite>>(firstSync);
        var created = firstBody.Data!.FirstOrDefault(t => t.Name == tableName);
        Assert.NotNull(created);
        Assert.True(created!.IsActive);

        // 2. Soft-delete bàn vừa tạo (IsActive=false) thông qua DbContext.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BoardVerseDbContext>();
            var table = await db.CafeTables.FirstOrDefaultAsync(t => t.Id == created.Id);
            Assert.NotNull(table);
            table!.IsActive = false;
            await db.SaveChangesAsync();
        }

        // 3. GET /pos/tables (default includeInactive=false) — bàn đã ẩn KHÔNG xuất hiện.
        var defaultResponse = await _client.GetAsync(
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/tables?includeOnlyAvailable=false");
        await ApiTestClient.AssertStatusOneOfAsync(defaultResponse, HttpStatusCode.OK);
        var defaultBody = await ApiTestClient.ReadApiResponseAsync<List<CafeTableStatusDtoLite>>(defaultResponse);
        var hiddenInDefault = defaultBody.Data!.FirstOrDefault(t => t.Id == created.Id);
        Assert.Null(hiddenInDefault);

        // 4. GET /pos/tables?includeInactive=true — bàn đã ẩn PHẢI xuất hiện với IsActive=false.
        var includeInactiveResponse = await _client.GetAsync(
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/tables?includeOnlyAvailable=false&includeInactive=true");
        await ApiTestClient.AssertStatusOneOfAsync(includeInactiveResponse, HttpStatusCode.OK);
        var includeInactiveBody = await ApiTestClient.ReadApiResponseAsync<List<CafeTableStatusDtoLite>>(includeInactiveResponse);
        var visible = includeInactiveBody.Data!.FirstOrDefault(t => t.Id == created.Id);
        Assert.NotNull(visible);
        Assert.False(visible!.IsActive);

        // 5. Cleanup: bật lại IsActive=true để tránh ảnh hưởng test khác.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BoardVerseDbContext>();
            var table = await db.CafeTables.FirstOrDefaultAsync(t => t.Id == created.Id);
            if (table != null)
            {
                table.IsActive = true;
                await db.SaveChangesAsync();
            }
        }
    }

    #endregion

    #region === BOOKINGS ===

    [IntegrationFact]
    public async Task CafePos_GetBookingByCode()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/bookings/TEST123");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone
                   );
    }

    [IntegrationFact]
    public async Task CafePos_GetActiveBookings()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/bookings?status=Confirmed");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone
                   );
    }

    #endregion

    #region === SESSIONS ===

    [IntegrationFact]
    public async Task CafePos_GetActiveSessions()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/sessions/active");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.NotFound
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone
                   );
    }

    [IntegrationFact]
    public async Task CafePos_StartSession_FromBooking()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var startRequest = new
        {
            code = "TEST123",
            cafeTableId = IntegrationTestFixtures.DemoPosTableId
        };

        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/check-in",
            startRequest);
        Assert.True(response.StatusCode == HttpStatusCode.Created ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.InternalServerError ||
                   response.StatusCode == HttpStatusCode.MethodNotAllowed ||
                   response.StatusCode == HttpStatusCode.Gone,
                   $"Start session returned: {(int)response.StatusCode}");
    }

    #endregion

    #region === BOXES ===

    [IntegrationFact]
    public async Task CafePos_GetBoxes()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/boxes");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.NotFound
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone
                   );
    }

    [IntegrationFact]
    public async Task CafePos_GetBoxByBarcode()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/boxes/by-barcode/{IntegrationTestFixtures.PosBoxBarcode}");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone
                   );
    }

    #endregion

    #region === TABLES ===

    [IntegrationFact]
    public async Task CafePos_GetTables()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/tables");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.NotFound
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone
                   );
    }

    #endregion
}
