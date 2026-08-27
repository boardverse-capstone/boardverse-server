using BoardVerse.Tests.Helpers;
using Xunit;

namespace BoardVerse.Tests.Fixtures;

/// <summary>
/// IClassFixture chạy TRƯỚC mỗi test class để reset test data trên testing DB.
/// Kết hợp với FakeDbContext.ResetTestDataAsync().
/// </summary>
public class DatabaseResetFixture : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        // Reset data TRƯỚC mỗi test class chạy.
        await FakeDbContext.ResetTestDataAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}
