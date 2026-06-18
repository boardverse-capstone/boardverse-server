using System.Net;
using BoardVerse.Core.Data;
using BoardVerse.Core.Enum;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public class AdminIntegrationTests
{
    private readonly HttpClient _client;

    public AdminIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [IntegrationFact]
    public async Task GetKarmaLogs_AsAdmin_Returns200()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/admin/karma-logs?pageSize=10");
        response.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task GetUserKarmaAlerts_AsAdmin_Returns200()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/admin/users/alerts");
        response.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task PunishUser_Warning_AsAdmin_Returns200()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/v1/admin/users/{DevSeedConstants.DemoPlayer3UserId}/punish",
            new
            {
                actionType = AdminPunishmentActionType.Warning,
                reason = "Integration test warning"
            });

        response.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task AdjustKarma_AsAdmin_Returns200()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/v1/admin/users/{DevSeedConstants.DemoPlayer3UserId}/adjust-karma",
            new { amount = 1, reason = "Integration test bump" });

        response.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task GetSystemConfigs_AsAdmin_Returns200()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/admin/configs");
        response.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task BulkUpdateConfigs_AsAdmin_Returns200()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var getResponse = await _client.GetAsync("/api/v1/admin/configs");
        getResponse.EnsureSuccessStatusCode();
        var configMap = (await ApiTestClient.ReadApiResponseAsync<Dictionary<string, string>>(getResponse)).Data ?? [];
        Assert.NotEmpty(configMap);

        var first = configMap.First();
        var response = await ApiTestClient.PutJsonAsync(_client, "/api/v1/admin/configs", new
        {
            configs = new[] { new { configKey = first.Key, configValue = first.Value } }
        });

        response.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task ListUsers_AsAdmin_Returns200()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/usermanagement/users?pageSize=10");
        response.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task GetUserById_AsAdmin_Returns200()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync($"/api/usermanagement/{DevSeedConstants.DemoPlayer1UserId}");
        response.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task CreateUpdateDisableUser_AsAdmin_Flow()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var email = ApiTestClient.UniqueEmail("adminuser");
        var createResponse = await ApiTestClient.PostJsonAsync(_client, "/api/usermanagement", new
        {
            username = ApiTestClient.UniqueUsername("adminuser"),
            email,
            password = "AdminUser@123",
            role = "Player"
        });
        createResponse.EnsureSuccessStatusCode();
        var userId = (await ApiTestClient.ReadApiResponseAsync<CreatedUserDto>(createResponse)).Data!.Id;

        var updateResponse = await ApiTestClient.PutJsonAsync(_client, $"/api/usermanagement/{userId}", new
        {
            username = ApiTestClient.UniqueUsername("adminuser2")
        });
        updateResponse.EnsureSuccessStatusCode();

        var blockResponse = await ApiTestClient.PostJsonAsync(_client, $"/api/usermanagement/users/{userId}/block", new
        {
            reason = "Integration test block"
        });
        blockResponse.EnsureSuccessStatusCode();

        var unblockResponse = await _client.PostAsync($"/api/usermanagement/users/{userId}/unblock", null);
        unblockResponse.EnsureSuccessStatusCode();

        var roleResponse = await ApiTestClient.PutJsonAsync(_client, $"/api/usermanagement/users/{userId}/role", new
        {
            role = "Player"
        });
        roleResponse.EnsureSuccessStatusCode();

        var disableResponse = await ApiTestClient.DeleteAsync(_client, $"/api/usermanagement/{userId}");
        disableResponse.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task ListCafePartnerApplications_AsAdmin_Returns200()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/admin/cafe-partner-applications?pageSize=10");
        response.EnsureSuccessStatusCode();
    }

    private sealed class CreatedUserDto
    {
        public Guid Id { get; set; }
    }
}
