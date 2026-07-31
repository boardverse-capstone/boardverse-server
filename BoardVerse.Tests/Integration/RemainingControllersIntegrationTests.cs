#nullable enable
using System.Net;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

/// <summary>
/// Integration tests for remaining controllers
/// Covers: CafePartnerApplicationController, StaffController, ManagerController, LobbyInviteController, UserRatingController
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class RemainingControllersIntegrationTests
{
    private readonly HttpClient _client;

    public RemainingControllersIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    #region === CAFE PARTNER APPLICATION ===

    [IntegrationFact]
    public async Task CafePartnerApplication_Submit()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new
        {
            businessName = "Test Cafe",
            businessAddress = "123 Test Street",
            contactEmail = "cafe@test.com",
            contactPhone = "0123456789",
            description = "Test cafe description"
        };

        var response = await ApiTestClient.PostJsonAsync(_client, "/api/v1/cafe-partner-applications", request);
        Assert.True(response.StatusCode == HttpStatusCode.Created ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task CafePartnerApplication_GetMyApplication()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/cafe-partner-applications/me");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    #endregion

    #region === ADMIN CAFE PARTNER APPLICATION ===

    [IntegrationFact]
    public async Task AdminCafePartnerApplication_GetAll()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/admin/cafe-partner-applications");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Forbidden);
    }

    [IntegrationFact]
    public async Task AdminCafePartnerApplication_GetById()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync($"/api/v1/admin/cafe-partner-applications/{Guid.NewGuid()}");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task AdminCafePartnerApplication_Approve()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var approveRequest = new
        {
            notes = "Approved for testing"
        };

        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/v1/admin/cafe-partner-applications/{Guid.NewGuid()}/approve",
            approveRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task AdminCafePartnerApplication_Reject()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var rejectRequest = new
        {
            reason = "Test rejection"
        };

        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/v1/admin/cafe-partner-applications/{Guid.NewGuid()}/reject",
            rejectRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    #endregion

    #region === STAFF CONTROLLER ===

    [IntegrationFact]
    public async Task Staff_GetMyProfile()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync($"/api/v1/staff/me");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task Staff_UpdateProfile()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var updateRequest = new
        {
            displayName = "Updated Staff Name"
        };

        var response = await ApiTestClient.PutJsonAsync(_client, "/api/v1/staff/me", updateRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    #endregion

    #region === MANAGER CONTROLLER ===

    [IntegrationFact]
    public async Task Manager_GetMyCafes()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/manager/cafes");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Forbidden);
    }

    [IntegrationFact]
    public async Task Manager_GetCafeStats()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/v1/manager/cafes/{IntegrationTestFixtures.DemoCafeId}/stats");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Forbidden);
    }

    [IntegrationFact]
    public async Task Manager_GetCafeReports()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/v1/manager/cafes/{IntegrationTestFixtures.DemoCafeId}/reports?period=weekly");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Forbidden);
    }

    #endregion

    #region === LOBBY INVITE CONTROLLER ===

    [IntegrationFact]
    public async Task LobbyInvite_GetMyInvites()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/lobby-invites/received");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task LobbyInvite_Accept()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.PostAsync($"/api/v1/lobby-invites/{Guid.NewGuid()}/accept", null);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task LobbyInvite_Decline()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.PostAsync($"/api/v1/lobby-invites/{Guid.NewGuid()}/decline", null);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    #endregion

    #region === USER RATING CONTROLLER ===

    [IntegrationFact]
    public async Task UserRating_GetRatings()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/v1/users/{IntegrationTestFixtures.DemoPlayer1UserId}/ratings");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task UserRating_GetAverageRating()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/v1/users/{IntegrationTestFixtures.DemoPlayer1UserId}/ratings/average");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    #endregion

    #region === MANAGER CAFE PROFILE CONTROLLER ===

    [IntegrationFact]
    public async Task ManagerCafeProfile_GetProfile()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/v1/manager/cafes/{IntegrationTestFixtures.DemoCafeId}/profile");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Forbidden);
    }

    [IntegrationFact]
    public async Task ManagerCafeProfile_UpdateOperationalStatus()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var updateRequest = new
        {
            status = "Open",
            openingHours = "09:00-22:00"
        };

        var response = await ApiTestClient.PutJsonAsync(_client,
            $"/api/v1/manager/cafes/{IntegrationTestFixtures.DemoCafeId}/operational-status",
            updateRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task ManagerCafeProfile_GetInventory()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/v1/manager/cafes/{IntegrationTestFixtures.DemoCafeId}/inventory");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Forbidden);
    }

    #endregion

    #region === CAFE INVENTORY CONTROLLER ===

    [IntegrationFact]
    public async Task CafeInventory_GetAll()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/v1/cafes/{IntegrationTestFixtures.DemoCafeId}/inventory");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Forbidden);
    }

    [IntegrationFact]
    public async Task CafeInventory_GetBox()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/v1/cafes/{IntegrationTestFixtures.DemoCafeId}/inventory/boxes/{IntegrationTestFixtures.DemoCatanInventoryId}");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task CafeInventory_UpdateBox()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var updateRequest = new
        {
            status = "Available",
            condition = "Good"
        };

        var response = await ApiTestClient.PutJsonAsync(_client,
            $"/api/v1/cafes/{IntegrationTestFixtures.DemoCafeId}/inventory/boxes/{IntegrationTestFixtures.DemoCatanInventoryId}",
            updateRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    #endregion

    #region === BGG CONTROLLER ===

    [IntegrationFact]
    public async Task Bgg_Search()
    {
        var response = await _client.GetAsync("/api/v1/bgg/search?query=Catan");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task Bgg_GetGameDetails()
    {
        var response = await _client.GetAsync("/api/v1/bgg/games/13"); // Catan BGG ID
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    #endregion
}
