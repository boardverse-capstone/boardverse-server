using BoardVerse.Core.IRepositories;
using Moq;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho HealthService — basic user count check (smoke test cho API health endpoint).
/// </summary>
public class HealthServiceTests
{
    [Fact]
    public async Task GetUserCountAsync_ReturnsCountFromRepository()
    {
        var repo = new Mock<IHealthRepository>();
        repo.Setup(r => r.CountUsersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(42);

        var sut = new HealthService(repo.Object);
        var count = await sut.GetUserCountAsync();

        Assert.Equal(42, count);
        repo.Verify(r => r.CountUsersAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetUserCountAsync_ZeroUsers_ReturnsZero()
    {
        var repo = new Mock<IHealthRepository>();
        repo.Setup(r => r.CountUsersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var sut = new HealthService(repo.Object);
        var count = await sut.GetUserCountAsync();

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task GetUserCountAsync_PassesCancellationToken()
    {
        var repo = new Mock<IHealthRepository>();
        repo.Setup(r => r.CountUsersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        using var cts = new CancellationTokenSource();

        var sut = new HealthService(repo.Object);
        await sut.GetUserCountAsync(cts.Token);

        Assert.False(cts.IsCancellationRequested);
    }
}
