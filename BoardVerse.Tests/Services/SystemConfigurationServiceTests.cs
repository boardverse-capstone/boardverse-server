using System.Globalization;
using System.Text.Json;
using BoardVerse.Core.Data;
using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using Microsoft.Extensions.Caching.Distributed;
using Moq;

using System.Threading;
namespace BoardVerse.Tests.Services;

public class SystemConfigurationServiceTests
{
    [Fact]
    public async Task GetIntAsync_ReturnsParsedValueFromRepository()
    {
        var repo = new Mock<ISystemConfigurationRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<SystemConfiguration>
        {
            new()
            {
                ConfigKey = SystemConfigKeys.MatchmakingRadiusKm,
                ConfigValue = "20",
                Description = "radius"
            }
        });

        var service = BuildService(repo);

        var value = await service.GetIntAsync(SystemConfigKeys.MatchmakingRadiusKm, 10);

        Assert.Equal(20, value);
    }

    [Fact]
    public async Task GetDoubleAsync_UsesFallbackWhenMissing()
    {
        var repo = new Mock<ISystemConfigurationRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var service = BuildService(repo);

        var value = await service.GetDoubleAsync(SystemConfigKeys.MatchmakingRadiusKm, 15.0);

        Assert.Equal(15.0, value);
    }

        [Fact]
    public async Task IsDemoLoosenLobbyConstraintsEnabledAsync_ReturnsTrue_WhenConfigTrue()
    {
        var repo = new Mock<ISystemConfigurationRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<SystemConfiguration>
        {
            new()
            {
                ConfigKey = SystemConfigKeys.DemoLoosenLobbyConstraints,
                ConfigValue = "true",
                Description = "Demo mode toggle"
            }
        });

        var service = BuildService(repo);

        var enabled = await service.IsDemoLoosenLobbyConstraintsEnabledAsync();

        Assert.True(enabled);
    }

    [Fact]
    public async Task IsDemoLoosenLobbyConstraintsEnabledAsync_ReturnsFalse_WhenConfigFalse()
    {
        var repo = new Mock<ISystemConfigurationRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<SystemConfiguration>
        {
            new()
            {
                ConfigKey = SystemConfigKeys.DemoLoosenLobbyConstraints,
                ConfigValue = "false",
                Description = "Demo mode toggle"
            }
        });

        var service = BuildService(repo);

        var enabled = await service.IsDemoLoosenLobbyConstraintsEnabledAsync();

        Assert.False(enabled);
    }

    [Fact]
    public async Task IsDemoLoosenLobbyConstraintsEnabledAsync_ReturnsFalse_WhenConfigMissing()
    {
        var repo = new Mock<ISystemConfigurationRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var service = BuildService(repo);

        var enabled = await service.IsDemoLoosenLobbyConstraintsEnabledAsync();

        Assert.False(enabled);
    }

    [Fact]
    public async Task IsBypassTimeWindowEnabledAsync_ReturnsTrue_WhenConfigTrue()
    {
        var repo = new Mock<ISystemConfigurationRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<SystemConfiguration>
        {
            new()
            {
                ConfigKey = SystemConfigKeys.BypassTimeWindowValidations,
                ConfigValue = "true",
                Description = "Bypass toggle"
            }
        });

        var service = BuildService(repo);

        var enabled = await service.IsBypassTimeWindowEnabledAsync();

        Assert.True(enabled);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("TRUE", true)]
    [InlineData("1", true)]
    [InlineData("yes", true)]
    [InlineData("on", true)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData("0", false)]
    [InlineData("no", false)]
    [InlineData("off", false)]
    public async Task GetBoolAsync_ParsesCommonStringFormats(string raw, bool expected)
    {
        var repo = new Mock<ISystemConfigurationRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<SystemConfiguration>
        {
            new()
            {
                ConfigKey = "feature_flag_test",
                ConfigValue = raw,
                Description = "Test"
            }
        });

        var service = BuildService(repo);

        var value = await service.GetBoolAsync("feature_flag_test", fallback: false);

        Assert.Equal(expected, value);
    }

    [Fact]
    public async Task GetBoolAsync_ReturnsFallback_WhenConfigMissing()
    {
        var repo = new Mock<ISystemConfigurationRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var service = BuildService(repo);

        var valueTrue = await service.GetBoolAsync("missing_key", fallback: true);
        var valueFalse = await service.GetBoolAsync("missing_key", fallback: false);

        Assert.True(valueTrue);
        Assert.False(valueFalse);
    }

    [Fact]
    public async Task GetBoolAsync_ReturnsFallback_WhenValueIsUnparseable()
    {
        var repo = new Mock<ISystemConfigurationRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<SystemConfiguration>
        {
            new()
            {
                ConfigKey = "weird_flag",
                ConfigValue = "maybe",
                Description = "Invalid value"
            }
        });

        var service = BuildService(repo);

        var value = await service.GetBoolAsync("weird_flag", fallback: false);

        Assert.False(value);
    }

    private static SystemConfigurationService BuildService(Mock<ISystemConfigurationRepository> repo, BoardVerseDbContext? db = null)
    {
        var cache = new Mock<IDistributedCache>();
        cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((byte[]?)null);

        if (db == null)
        {
            // Default fallback — InMemory provider for tests that don't exercise transactions.
            // For tests that call BulkUpdateConfigsAsync, callers MUST pass a SQLite-backed context.
            var dbOptions = new DbContextOptionsBuilder<BoardVerseDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            db = new BoardVerseDbContext(dbOptions);
        }

        return new SystemConfigurationService(repo.Object, cache.Object, db);
    }
}

public class KarmaConfigurationServiceTests
{
    [Fact]
    public async Task GetNoShowPenaltyAsync_DelegatesToProvider()
    {
        var provider = new Mock<ISystemConfigurationProvider>();
        provider.Setup(p => p.GetIntAsync(SystemConfigKeys.KarmaPenaltyNoshow, -5, It.IsAny<CancellationToken>())).ReturnsAsync(-7);

        var service = new KarmaConfigurationService(provider.Object);

        var penalty = await service.GetNoShowPenaltyAsync();

        Assert.Equal(-7, penalty);
    }

    [Fact]
    public async Task GetLateCancelPenaltyAsync_DelegatesToProvider()
    {
        var provider = new Mock<ISystemConfigurationProvider>();
        provider.Setup(p => p.GetIntAsync(SystemConfigKeys.KarmaPenaltyCancel, -3, It.IsAny<CancellationToken>())).ReturnsAsync(-4);

        var service = new KarmaConfigurationService(provider.Object);

        var penalty = await service.GetLateCancelPenaltyAsync();

        Assert.Equal(-4, penalty);
    }
}
