using System.Net;
using BoardVerse.Core.Data;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public class CafeIntegrationTests
{
    private readonly HttpClient _client;

    public CafeIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [IntegrationFact]
    public async Task NearbyCafes_NearDemoLocation_Returns200()
    {
        var gameId = await IntegrationCatalog.GetCatanGameIdAsync(_client);
        var response = await _client.GetAsync(
            $"/api/cafes/nearby?latitude={DevSeedConstants.DemoCafeLatitude}" +
            $"&longitude={DevSeedConstants.DemoCafeLongitude}" +
            $"&gameTemplateId={gameId}&radiusKm=15&pageSize=5");

        response.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task NearbyMe_WithSavedLocation_Returns200()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);
        var gameId = await IntegrationCatalog.GetCatanGameIdAsync(_client);

        var locationResponse = await ApiTestClient.PutJsonAsync(_client, "/api/userprofile/me/location", new
        {
            latitude = DevSeedConstants.DemoCafeLatitude,
            longitude = DevSeedConstants.DemoCafeLongitude,
            source = "Gps"
        });
        locationResponse.EnsureSuccessStatusCode();

        var response = await _client.GetAsync($"/api/cafes/nearby/me?gameTemplateId={gameId}&radiusKm=15");
        response.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task GetCafeById_Returns200()
    {
        var response = await _client.GetAsync($"/api/cafes/{DevSeedConstants.DemoCafeId}");
        response.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task UpdateCafe_AsManager_Returns200()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await ApiTestClient.PutJsonAsync(_client, $"/api/cafes/{DevSeedConstants.DemoCafeId}", new
        {
            description = "Integration test update"
        });

        response.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task ListStaff_AsManager_Returns200()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync($"/api/cafes/{DevSeedConstants.DemoCafeId}/staff?pageSize=20");
        response.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task PromoteStaff_AsManager_Returns200OrConflict()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await ApiTestClient.PostJsonAsync(
            _client,
            $"/api/cafes/{DevSeedConstants.DemoCafeId}/staff/promote",
            new { email = DevSeedConstants.Player2Email });

        await ApiTestClient.AssertStatusOneOfAsync(response, HttpStatusCode.OK, HttpStatusCode.Conflict);
    }

    [IntegrationFact]
    public async Task AddStaff_AsManager_Returns200OrConflict()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await ApiTestClient.PostJsonAsync(
            _client,
            $"/api/cafes/{DevSeedConstants.DemoCafeId}/staff",
            new
            {
                email = ApiTestClient.UniqueEmail("staff"),
                username = ApiTestClient.UniqueUsername("staff"),
                password = "StaffUser@123"
            });

        await ApiTestClient.AssertStatusOneOfAsync(response, HttpStatusCode.OK, HttpStatusCode.Created, HttpStatusCode.Conflict);
    }

    [IntegrationFact]
    public async Task RemoveStaff_AsManager_Returns200Or404()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var listResponse = await _client.GetAsync($"/api/cafes/{DevSeedConstants.DemoCafeId}/staff?pageSize=50");
        listResponse.EnsureSuccessStatusCode();
        var staff = (await ApiTestClient.ReadApiResponseAsync<StaffListDto>(listResponse)).Data?.Data ?? [];
        Assert.NotEmpty(staff);

        var staffId = staff[0].Id;
        var response = await ApiTestClient.DeleteAsync(_client, $"/api/cafes/{DevSeedConstants.DemoCafeId}/staff/{staffId}");
        await ApiTestClient.AssertStatusOneOfAsync(response, HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    private sealed class StaffListDto
    {
        public List<StaffMemberDto> Data { get; set; } = [];
    }

    private sealed class StaffMemberDto
    {
        public Guid Id { get; set; }
    }
}
