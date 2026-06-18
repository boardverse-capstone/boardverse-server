using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.Data;
using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using Microsoft.Extensions.Caching.Distributed;
using Moq;

namespace BoardVerse.Tests.Services;

public class SystemConfigurationServiceTests
{
    [Fact]
    public async Task GetIntAsync_ReturnsParsedValueFromRepository()
    {
        var repo = new Mock<ISystemConfigurationRepository>();
        repo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<SystemConfiguration>
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
        repo.Setup(r => r.GetAllAsync()).ReturnsAsync([]);

        var service = BuildService(repo);

        var value = await service.GetDoubleAsync(SystemConfigKeys.MatchmakingRadiusKm, 15.0);

        Assert.Equal(15.0, value);
    }

    [Fact]
    public async Task BulkUpdateConfigsAsync_UpsertsAndInvalidatesCache()
    {
        var repo = new Mock<ISystemConfigurationRepository>();
        repo.Setup(r => r.GetAllAsync()).ReturnsAsync([]);
        repo.Setup(r => r.UpsertAsync(It.IsAny<IEnumerable<SystemConfiguration>>()))
            .Returns(Task.CompletedTask);

        var cache = new Mock<IDistributedCache>();
        var service = new SystemConfigurationService(repo.Object, cache.Object);

        await service.BulkUpdateConfigsAsync(new SystemConfigBulkUpdateRequestDto
        {
            Configs =
            [
                new SystemConfigUpdateItemDto
                {
                    ConfigKey = SystemConfigKeys.MatchmakingRadiusKm,
                    ConfigValue = "20"
                }
            ]
        });

        repo.Verify(r => r.UpsertAsync(It.IsAny<IEnumerable<SystemConfiguration>>()), Times.Once);
        repo.Verify(r => r.SaveChangesAsync(), Times.Once);
        cache.Verify(c => c.RemoveAsync(It.IsAny<string>(), default), Times.Once);
    }

    private static SystemConfigurationService BuildService(Mock<ISystemConfigurationRepository> repo)
    {
        var cache = new Mock<IDistributedCache>();
        cache.Setup(c => c.GetAsync(It.IsAny<string>(), default)).ReturnsAsync((byte[]?)null);
        return new SystemConfigurationService(repo.Object, cache.Object);
    }
}

public class KarmaConfigurationServiceTests
{
    [Fact]
    public async Task GetNoShowPenaltyAsync_DelegatesToProvider()
    {
        var provider = new Mock<ISystemConfigurationProvider>();
        provider.Setup(p => p.GetIntAsync(SystemConfigKeys.KarmaPenaltyNoshow, -5)).ReturnsAsync(-7);

        var service = new KarmaConfigurationService(provider.Object);

        var penalty = await service.GetNoShowPenaltyAsync();

        Assert.Equal(-7, penalty);
    }

    [Fact]
    public async Task GetLateCancelPenaltyAsync_DelegatesToProvider()
    {
        var provider = new Mock<ISystemConfigurationProvider>();
        provider.Setup(p => p.GetIntAsync(SystemConfigKeys.KarmaPenaltyCancel, -3)).ReturnsAsync(-4);

        var service = new KarmaConfigurationService(provider.Object);

        var penalty = await service.GetLateCancelPenaltyAsync();

        Assert.Equal(-4, penalty);
    }
}
