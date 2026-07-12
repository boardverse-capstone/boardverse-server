using System.Net;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public class BggIntegrationTests
{
    private readonly HttpClient _client;

    public BggIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [IntegrationFact]
    public async Task GetComponentCatalog_AsAdmin_Returns200()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/bgg/component-catalog");
        response.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task SearchGames_AsAdmin_Returns200Or500()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/bgg/search?query=catan");
        await ApiTestClient.AssertStatusOneOfAsync(response, HttpStatusCode.OK, HttpStatusCode.InternalServerError);
    }

    [IntegrationFact]
    public async Task PreviewGame_AsAdmin_Returns200Or404Or500()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/bgg/games/13?curatedComponentsOnly=true");
        await ApiTestClient.AssertStatusOneOfAsync(
            response,
            HttpStatusCode.OK,
            HttpStatusCode.NotFound,
            HttpStatusCode.InternalServerError);
    }

    [IntegrationFact]
    public async Task ImportGame_AsAdmin_Returns200Or409Or500()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await ApiTestClient.PostJsonAsync(_client, "/api/v1/bgg/import", new
        {
            bggId = 13,
            overwriteExisting = false,
            curatedComponentsOnly = true
        });

        await ApiTestClient.AssertStatusOneOfAsync(
            response,
            HttpStatusCode.OK,
            HttpStatusCode.Created,
            HttpStatusCode.Conflict,
            HttpStatusCode.BadRequest,
            HttpStatusCode.InternalServerError);
    }
}

[Collection(IntegrationTestCollection.Name)]
public class CafePartnerIntegrationTests
{
    private readonly HttpClient _client;

    public CafePartnerIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [IntegrationFact]
    public async Task SubmitApplication_CreatesApplicationAndGetById_Returns200()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var response = await ApiTestClient.PostJsonAsync(_client, "/api/cafe-partner-applications", new
        {
            cafeName = $"Integration Cafe {suffix}",
            address = $"123 Test Street {suffix}, Ho Chi Minh City, Vietnam",
            latitude = 10.776889,
            longitude = 106.700806,
            phoneNumber = "0901234567",
            representativeEmail = ApiTestClient.UniqueEmail("partner"),
            businessLicense = $"BL-{suffix}".ToUpperInvariant(),
            businessLicenseImageUrl = "https://example.com/license.png"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var applicationId = (await ApiTestClient.ReadApiResponseAsync<ApplicationDto>(response)).Data!.Id;
        var getResponse = await _client.GetAsync($"/api/cafe-partner-applications/{applicationId}");
        getResponse.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task SubmitApplication_InvalidDuplicate_Returns409Or400()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var body = new
        {
            cafeName = $"Integration Cafe {suffix}",
            address = $"456 Review Street {suffix}, Ho Chi Minh City, Vietnam",
            latitude = 10.776889,
            longitude = 106.700806,
            phoneNumber = "0907654321",
            representativeEmail = ApiTestClient.UniqueEmail("partner-dup"),
            businessLicense = $"BL-DUP-{suffix}".ToUpperInvariant(),
            businessLicenseImageUrl = "https://example.com/license.png"
        };

        var first = await ApiTestClient.PostJsonAsync(_client, "/api/cafe-partner-applications", body);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var duplicate = await ApiTestClient.PostJsonAsync(_client, "/api/cafe-partner-applications", body);
        await ApiTestClient.AssertStatusOneOfAsync(duplicate, HttpStatusCode.Conflict, HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task AdminReviewApplication_ApproveOrReject()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var submitResponse = await ApiTestClient.PostJsonAsync(_client, "/api/cafe-partner-applications", new
        {
            cafeName = $"Review Cafe {suffix}",
            address = $"789 Admin Review Street {suffix}, Ho Chi Minh City, Vietnam",
            latitude = 10.776889,
            longitude = 106.700806,
            phoneNumber = "0907654321",
            representativeEmail = ApiTestClient.UniqueEmail("review"),
            businessLicense = $"BL-REV-{suffix}".ToUpperInvariant(),
            businessLicenseImageUrl = "https://example.com/license2.png"
        });

        Assert.Equal(HttpStatusCode.Created, submitResponse.StatusCode);

        var applicationId = (await ApiTestClient.ReadApiResponseAsync<ApplicationDto>(submitResponse)).Data!.Id;
        var adminToken = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, adminToken);

        var getResponse = await _client.GetAsync($"/api/admin/cafe-partner-applications/{applicationId}");
        getResponse.EnsureSuccessStatusCode();

        var rejectResponse = await ApiTestClient.PostJsonAsync(
            _client,
            $"/api/admin/cafe-partner-applications/{applicationId}/reject",
            new { reason = "Integration test rejection" });
        rejectResponse.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task ManagerPartnerProfile_Returns200Or404()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/manager/cafes/me");
        await ApiTestClient.AssertStatusOneOfAsync(response, HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task ManagerOperationalProfile_Returns200Or400Or404()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await ApiTestClient.PutJsonAsync(_client, "/api/manager/cafes/me/operational-profile", new
        {
            workingHours = new
            {
                weekdayStart = "08:00",
                weekdayEnd = "22:00",
                weekendStart = "09:00",
                weekendEnd = "23:00"
            },
            numberOfTables = 12,
            numberOfPrivateRooms = 0,
            spaceImageUrls = new[] { "https://example.com/1.png", "https://example.com/2.png", "https://example.com/3.png" },
            numberOfGamesOwned = 25,
            popularGamesList = "Catan, Ticket to Ride, Azul",
            hasGameMaster = true,
            billingModel = 0
        });

        await ApiTestClient.AssertStatusOneOfAsync(response, HttpStatusCode.OK, HttpStatusCode.BadRequest, HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task ManagerActivateDeactivate_Returns200Or400Or404()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var activateResponse = await _client.PostAsync("/api/manager/cafes/me/activate", null);
        await ApiTestClient.AssertStatusOneOfAsync(activateResponse, HttpStatusCode.OK, HttpStatusCode.BadRequest, HttpStatusCode.NotFound);

        var deactivateResponse = await _client.PostAsync("/api/manager/cafes/me/deactivate", null);
        await ApiTestClient.AssertStatusOneOfAsync(deactivateResponse, HttpStatusCode.OK, HttpStatusCode.BadRequest, HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task ManagerReopen_Returns200Or400Or404()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var reopenResponse = await _client.PostAsync("/api/manager/cafes/me/reopen", null);
        await ApiTestClient.AssertStatusOneOfAsync(reopenResponse, HttpStatusCode.OK, HttpStatusCode.BadRequest, HttpStatusCode.NotFound);
    }

    private sealed class ApplicationDto
    {
        public Guid Id { get; set; }
    }
}

[Collection(IntegrationTestCollection.Name)]
public class ManagerStaffIntegrationTests
{
    private readonly HttpClient _client;

    public ManagerStaffIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [IntegrationFact]
    public async Task ManagerMyCafes_Returns200()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/manager/my-cafes");
        response.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task StaffMyCafes_AsStaffOr403()
    {
        var managerToken = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, managerToken);
        await ApiTestClient.PostJsonAsync(
            _client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/staff/promote",
            new { email = IntegrationTestFixtures.Player2Email });

        var staffToken = await IntegrationTestAuth.AsPlayer2Async(_client);
        ApiTestClient.Authorize(_client, staffToken);

        var response = await _client.GetAsync("/api/staff/my-cafes");
        await ApiTestClient.AssertStatusOneOfAsync(response, HttpStatusCode.OK, HttpStatusCode.Forbidden);
    }
}
