using System.Net;
using BoardVerse.Core.DTOs.Auth.Requests;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public class AuthIntegrationTests
{
    private readonly HttpClient _client;

    public AuthIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [IntegrationFact]
    public async Task Login_DevPlayer_ReturnsToken()
    {
        var token = await ApiTestClient.LoginAsync(
            _client,
            IntegrationTestFixtures.Player1Email,
            IntegrationTestFixtures.PlayerPassword);

        Assert.False(string.IsNullOrWhiteSpace(token));
    }

    [IntegrationFact]
    public async Task Login_InvalidPassword_Returns401()
    {
        var response = await ApiTestClient.PostJsonAsync(_client, "/api/auth/login", new LoginRequestDto
        {
            UsernameOrEmail = IntegrationTestFixtures.Player1Email,
            Password = "WrongPassword123!"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [IntegrationFact]
    public async Task Register_NewUser_Returns200()
    {
        var response = await ApiTestClient.PostJsonAsync(_client, "/api/auth/register", new RegisterRequestDto
        {
            Username = ApiTestClient.UniqueUsername(),
            Email = ApiTestClient.UniqueEmail(),
            Password = "Register@123"
        });

        response.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task RefreshToken_AfterLogin_ReturnsNewTokens()
    {
        var (_, _, refresh) = await ApiTestClient.LoginWithRefreshAsync(
            _client,
            IntegrationTestFixtures.Player1Email,
            IntegrationTestFixtures.PlayerPassword);

        var response = await ApiTestClient.PostJsonAsync(_client, "/api/auth/refresh-token", new RefreshTokenRequestDto
        {
            RefreshToken = refresh
        });

        response.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task Logout_AfterLogin_Returns200()
    {
        var (_, _, refresh) = await ApiTestClient.LoginWithRefreshAsync(
            _client,
            IntegrationTestFixtures.Player1Email,
            IntegrationTestFixtures.PlayerPassword);

        var response = await ApiTestClient.PostJsonAsync(_client, "/api/auth/logout", new RefreshTokenRequestDto
        {
            RefreshToken = refresh
        });

        response.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task GoogleLogin_InvalidToken_Returns401()
    {
        var response = await ApiTestClient.PostJsonAsync(_client, "/api/auth/google-login", new
        {
            idToken = "invalid-google-token"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [IntegrationFact]
    public async Task SendEmailVerification_ValidEmail_Returns200()
    {
        var response = await ApiTestClient.PostJsonAsync(_client, "/api/auth/send-email-verification", new
        {
            email = IntegrationTestFixtures.Player1Email
        });

        await ApiTestClient.AssertStatusOneOfAsync(response, HttpStatusCode.OK, HttpStatusCode.TooManyRequests);
    }

    [IntegrationFact]
    public async Task VerifyEmail_InvalidCode_Returns400()
    {
        var response = await ApiTestClient.PostJsonAsync(_client, "/api/auth/verify-email", new
        {
            email = IntegrationTestFixtures.Player1Email,
            code = "000000"
        });

        await ApiTestClient.AssertStatusOneOfAsync(response, HttpStatusCode.BadRequest, HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task RequestPasswordReset_KnownEmail_Returns200()
    {
        var response = await ApiTestClient.PostJsonAsync(_client, "/api/auth/request-password-reset", new
        {
            email = IntegrationTestFixtures.Player1Email
        });

        response.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task ResetPassword_InvalidCode_Returns400()
    {
        var response = await ApiTestClient.PostJsonAsync(_client, "/api/auth/reset-password", new
        {
            email = IntegrationTestFixtures.Player1Email,
            code = "000000",
            newPassword = "NewPassword@123"
        });

        await ApiTestClient.AssertStatusOneOfAsync(response, HttpStatusCode.BadRequest, HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task ChangePassword_WrongCurrent_Returns400()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await ApiTestClient.PostJsonAsync(_client, "/api/auth/change-password", new
        {
            currentPassword = "WrongCurrent@123",
            newPassword = "NewPassword@123"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [IntegrationFact]
    public async Task LinkGoogle_InvalidToken_Returns401()
    {
        var response = await ApiTestClient.PostJsonAsync(_client, "/api/auth/link-google", new
        {
            idToken = "invalid-google-token"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
