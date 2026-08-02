#nullable enable
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BoardVerse.Core.DTOs.Wallet;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

/// <summary>
/// Integration tests cho WalletController (BVC + ledger).
/// Verify HTTP shape, auth, validation, idempotency, pagination.
/// Connection string từ <c>appsettings.local.json</c> trỏ về nhánh Neon testing
/// (<c>ep-morning-darkness</c>) theo <c>neon-database-workflow.mdc</c>.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class WalletControllerIntegrationTests
{
    private readonly HttpClient _client;

    public WalletControllerIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [IntegrationFact]
    public async Task GetWallet_WithoutAuth_Returns401()
    {
        var response = await _client.GetAsync("/api/v1/wallet");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [IntegrationFact]
    public async Task GetWallet_AsPlayer_AutoCreatesAndReturnsEmptyWallet()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/wallet?includeHeld=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ApiTestClient.ReadApiResponseAsync<WalletDto>(response);
        Assert.NotNull(body.Data);
        Assert.Equal(0, body.Data!.AvailableBalance);
        Assert.Equal(0, body.Data.HeldBalance ?? 0);
        Assert.Equal("low", body.Data.RiskLevel.ToString(),
            ignoreCase: true);
        Assert.Equal("active", body.Data.AccountStatus.ToString(),
            ignoreCase: true);
        // isCoolingOff mặc định false
        Assert.False(body.Data.IsCoolingOff);
    }

    [IntegrationFact]
    public async Task GetWallet_DefaultQuery_DoesNotExposeHeldBalance()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/wallet");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ApiTestClient.ReadApiResponseAsync<WalletDto>(response);
        Assert.NotNull(body.Data);
        // includeHeld=false → HeldBalance không hiển thị (DTO trả null)
        Assert.Null(body.Data!.HeldBalance);
    }

    [IntegrationFact]
    public async Task GetWallet_CalledTwice_ReturnsSameWalletNoDuplicate()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var r1 = await _client.GetAsync("/api/v1/wallet?includeHeld=true");
        var r2 = await _client.GetAsync("/api/v1/wallet?includeHeld=true");

        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);

        var w1 = (await ApiTestClient.ReadApiResponseAsync<WalletDto>(r1)).Data!;
        var w2 = (await ApiTestClient.ReadApiResponseAsync<WalletDto>(r2)).Data!;

        // Auto-create idempotent: 2 lần GET cùng 1 ví, không nhân balance
        Assert.Equal(w1.UserId, w2.UserId);
        Assert.Equal(w1.AvailableBalance, w2.AvailableBalance);
    }

    [IntegrationFact]
    public async Task GetTransactions_AsPlayer_ReturnsPagedEmptyForFreshWallet()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        // Ensure wallet exists
        await _client.GetAsync("/api/v1/wallet");

        var response = await _client.GetAsync("/api/v1/wallet/transactions?page=1&pageSize=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ApiTestClient.ReadApiResponseAsync<BvcTransactionPageDto>(response);
        Assert.NotNull(body.Data);
        Assert.Empty(body.Data!.Items);
        Assert.Equal(0, body.Data.TotalItems);
        Assert.Equal(1, body.Data.Page);
        Assert.Equal(20, body.Data.PageSize);
        Assert.False(body.Data.HasMore);
    }

    [IntegrationFact]
    public async Task GetTransactions_ClampsPageSizeToHundred()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/wallet/transactions?page=1&pageSize=9999");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ApiTestClient.ReadApiResponseAsync<BvcTransactionPageDto>(response);
        Assert.Equal(100, body.Data!.PageSize);
    }

    [IntegrationFact]
    public async Task CreateTopUp_WithoutAuth_Returns401()
    {
        var request = new TopUpRequestDto
        {
            AmountVnd = 50_000,
            IdempotencyKey = Guid.NewGuid().ToString("N")
        };

        var response = await _client.PostAsJsonAsync("/api/v1/wallet/topup", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [IntegrationFact]
    public async Task CreateTopUp_BelowMinimumVnd_Returns400()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new TopUpRequestDto
        {
            AmountVnd = 5_000,  // dưới 10.000 minimum
            IdempotencyKey = Guid.NewGuid().ToString("N")
        };

        var response = await _client.PostAsJsonAsync("/api/v1/wallet/topup", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [IntegrationFact]
    public async Task CreateTopUp_MissingIdempotencyKey_Returns400()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new TopUpRequestDto
        {
            AmountVnd = 50_000,
            IdempotencyKey = string.Empty
        };

        var response = await _client.PostAsJsonAsync("/api/v1/wallet/topup", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [IntegrationFact]
    public async Task CreateTopUp_AmountNotMultipleOfThousand_Returns400()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new TopUpRequestDto
        {
            AmountVnd = 15_500,  // không chia hết cho 1.000
            IdempotencyKey = Guid.NewGuid().ToString("N")
        };

        var response = await _client.PostAsJsonAsync("/api/v1/wallet/topup", request);

        // DTO [Range] chỉ chặn min/max. Bội số là service validate → 400.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
