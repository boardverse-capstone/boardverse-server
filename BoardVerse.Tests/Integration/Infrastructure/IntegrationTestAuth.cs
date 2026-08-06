using BoardVerse.Core.Data;

namespace BoardVerse.Tests.Integration.Infrastructure;

public static class IntegrationTestAuth
{
    private static readonly SemaphoreSlim LoginLock = new(1, 1);
    private static readonly Dictionary<string, (string Token, Guid UserId)> TokenCache = new(StringComparer.Ordinal);

    /// <summary>
    /// Xóa cache token. Gọi khi bootstrapper regenerate IDs (test fixture thay đổi).
    /// Tránh stale token chứa user ID cũ không còn tồn tại trong database.
    /// </summary>
    public static void ClearCache()
    {
        lock (LoginLock)
        {
            TokenCache.Clear();
        }
    }

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
            // Check if cached token's user ID matches the current fixture ID.
            // If not, invalidate to force re-login (avoid stale token for old user).
            if (TokenCache.TryGetValue(cacheKey, out var cached))
            {
                var (cachedToken, cachedUserId) = cached;
                var currentUserId = ResolveExpectedUserId(cacheKey);
                if (currentUserId != Guid.Empty && cachedUserId != currentUserId)
                {
                    TokenCache.Remove(cacheKey);
                }
                else
                {
                    return cachedToken;
                }
            }

            var token = await ApiTestClient.LoginAsync(client, usernameOrEmail, password);
            var userId = ExtractUserIdFromToken(token);
            TokenCache[cacheKey] = (token, userId);
            return token;
        }
        finally
        {
            LoginLock.Release();
        }
    }

    private static Guid ResolveExpectedUserId(string cacheKey) => cacheKey switch
    {
        "admin" => IntegrationTestFixtures.AdminUserId,
        "manager" => IntegrationTestFixtures.ManagerUserId,
        "player1" => IntegrationTestFixtures.DemoPlayer1UserId,
        "player2" => IntegrationTestFixtures.DemoPlayer2UserId,
        "player3" => IntegrationTestFixtures.DemoPlayer3UserId,
        _ => Guid.Empty
    };

    private static Guid ExtractUserIdFromToken(string token)
    {
        try
        {
            var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);
            var userIdClaim = jwt.Claims.FirstOrDefault(c =>
                c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(userIdClaim, out var id) ? id : Guid.Empty;
        }
        catch
        {
            return Guid.Empty;
        }
    }
}
