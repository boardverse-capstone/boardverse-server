using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BoardVerse.Core.DTOs.Auth.Requests;
using BoardVerse.Core.DTOs.Auth.Responses;

namespace BoardVerse.Tests.Integration.Infrastructure;

public static class ApiTestClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static async Task<string> LoginAsync(HttpClient client, string email, string password)
    {
        var (_, token, _) = await LoginWithRefreshAsync(client, email, password);
        return token;
    }

    public static async Task<(LoginResponseDto Body, string Token, string RefreshToken)> LoginWithRefreshAsync(
        HttpClient client,
        string email,
        string password)
    {
        ClearAuth(client);

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            UsernameOrEmail = email,
            Password = password
        });

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"Login failed for '{email}': {(int)response.StatusCode} {response.StatusCode}. Body: {errorBody}");
        }

        var body = (await ReadApiResponseAsync<LoginResponseDto>(response)).Data!;
        return (body, body.Token, body.RefreshToken);
    }

    public static void Authorize(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    public static void ClearAuth(HttpClient client) =>
        client.DefaultRequestHeaders.Authorization = null;

    public static async Task<ApiResponseEnvelope<T>> ReadApiResponseAsync<T>(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        var parsed = JsonSerializer.Deserialize<ApiResponseEnvelope<T>>(json, JsonOptions);
        return parsed ?? throw new InvalidOperationException($"Could not parse API response: {json}");
    }

    public static async Task<HttpResponseMessage> PostJsonAsync<T>(HttpClient client, string path, T body) =>
        await client.PostAsJsonAsync(path, body);

    public static async Task<HttpResponseMessage> PutJsonAsync<T>(HttpClient client, string path, T body) =>
        await client.PutAsJsonAsync(path, body);

    public static async Task<HttpResponseMessage> DeleteAsync(HttpClient client, string path) =>
        await client.DeleteAsync(path);

    public static void AssertStatusOneOf(HttpResponseMessage response, params HttpStatusCode[] allowed)
    {
        if (!allowed.Contains(response.StatusCode))
        {
            throw new Xunit.Sdk.XunitException(
                $"Expected one of [{string.Join(", ", allowed)}] but got {(int)response.StatusCode} {response.StatusCode}. Body: {response.Content.ReadAsStringAsync().GetAwaiter().GetResult()}");
        }
    }

    public static async Task AssertStatusOneOfAsync(HttpResponseMessage response, params HttpStatusCode[] allowed)
    {
        if (!allowed.Contains(response.StatusCode))
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new Xunit.Sdk.XunitException(
                $"Expected one of [{string.Join(", ", allowed)}] but got {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
        }
    }

    public static string UniqueEmail(string prefix = "itest") =>
        $"{prefix}.{Guid.NewGuid():N}@boardverse.test";

    public static string UniqueUsername(string prefix = "itest")
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        return $"{prefix}_{suffix}";
    }
}

public sealed class ApiResponseEnvelope<T>
{
    public int StatusCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }
}
