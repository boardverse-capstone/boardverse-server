using BoardVerse.Core.Data;

namespace BoardVerse.Tests.Integration.Infrastructure;

public static class IntegrationTestAuth
{
    private static readonly SemaphoreSlim LoginLock = new(1, 1);
    private static readonly Dictionary<string, string> TokenCache = new(StringComparer.Ordinal);

    public static Task<string> AsAdminAsync(HttpClient client) =>
        LoginCachedAsync(client, "admin", DevSeedConstants.AdminUsername, DevSeedConstants.AdminPassword);

    public static Task<string> AsManagerAsync(HttpClient client) =>
        LoginCachedAsync(client, "manager", DevSeedConstants.ManagerUsername, DevSeedConstants.ManagerPassword);

    public static Task<string> AsPlayer1Async(HttpClient client) =>
        LoginCachedAsync(client, "player1", DevSeedConstants.Player1Username, DevSeedConstants.DemoPlayerPassword);

    public static Task<string> AsPlayer2Async(HttpClient client) =>
        LoginCachedAsync(client, "player2", DevSeedConstants.Player2Username, DevSeedConstants.DemoPlayerPassword);

    public static Task<string> AsPlayer3Async(HttpClient client) =>
        LoginCachedAsync(client, "player3", DevSeedConstants.Player3Username, DevSeedConstants.DemoPlayerPassword);

    private static async Task<string> LoginCachedAsync(
        HttpClient client,
        string cacheKey,
        string usernameOrEmail,
        string password)
    {
        await LoginLock.WaitAsync();
        try
        {
            if (TokenCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            var token = await ApiTestClient.LoginAsync(client, usernameOrEmail, password);
            TokenCache[cacheKey] = token;
            return token;
        }
        finally
        {
            LoginLock.Release();
        }
    }
}
