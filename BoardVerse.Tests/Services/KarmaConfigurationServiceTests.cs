using BoardVerse.Core.Data;
using BoardVerse.Core.Exceptions;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using Moq;

namespace BoardVerse.Tests.Services;

public class KarmaPenaltyConfigurationServiceTests
{
    private readonly Mock<ISystemConfigurationProvider> _provider = new();
    private KarmaConfigurationService CreateService() => new(_provider.Object);

    [Fact]
    public async Task GetLateCancelPenaltyAsync_ReturnsConfiguredValue()
    {
        _provider.Setup(p => p.GetIntAsync(SystemConfigKeys.KarmaPenaltyCancel, -3)).ReturnsAsync(-7);

        var svc = CreateService();

        var value = await svc.GetLateCancelPenaltyAsync();

        Assert.Equal(-7, value);
        _provider.Verify(p => p.GetIntAsync(SystemConfigKeys.KarmaPenaltyCancel, -3), Times.Once);
    }

    [Fact]
    public async Task GetLateCancelPenaltyAsync_FallsBackToDefaultWhenMissing()
    {
        _provider.Setup(p => p.GetIntAsync(SystemConfigKeys.KarmaPenaltyCancel, -3)).ReturnsAsync(-3);

        var svc = CreateService();

        var value = await svc.GetLateCancelPenaltyAsync();

        Assert.Equal(-3, value);
    }

    [Fact]
    public async Task GetNoShowPenaltyAsync_ReturnsConfiguredValue()
    {
        _provider.Setup(p => p.GetIntAsync(SystemConfigKeys.KarmaPenaltyNoshow, -5)).ReturnsAsync(-10);

        var svc = CreateService();

        var value = await svc.GetNoShowPenaltyAsync();

        Assert.Equal(-10, value);
    }

    [Fact]
    public async Task GetNoShowPenaltyAsync_FallsBackToDefault()
    {
        _provider.Setup(p => p.GetIntAsync(SystemConfigKeys.KarmaPenaltyNoshow, -5)).ReturnsAsync(-5);

        var svc = CreateService();

        var value = await svc.GetNoShowPenaltyAsync();

        Assert.Equal(-5, value);
    }
}