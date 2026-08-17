using BoardVerse.Core.Data;
using BoardVerse.Services.Helpers;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BoardVerse.Tests.Services;

public class TimeWindowGuardTests
{
    private readonly Mock<ISystemConfigurationProvider> _configProvider = new();

    private static DefaultHttpContext BuildContext(string? headerValue = null, string? queryValue = null)
    {
        var ctx = new DefaultHttpContext();
        if (headerValue != null)
        {
            ctx.Request.Headers[TimeWindowGuard.HeaderName] = headerValue;
        }
        if (queryValue != null)
        {
            ctx.Request.QueryString = new QueryString($"?{TimeWindowGuard.QueryName}={queryValue}");
        }
        return ctx;
    }

    [Fact]
    public async Task ShouldBypass_ReturnsFalse_WhenNoHeaderNoQueryConfigFalse()
    {
        _configProvider
            .Setup(p => p.GetStringAsync(SystemConfigKeys.BypassTimeWindowValidations, "false"))
            .ReturnsAsync("false");

        var result = await TimeWindowGuard.ShouldBypassAsync(
            BuildContext(), _configProvider.Object,
            NullLogger.Instance, "Test.Operation");

        Assert.False(result);
    }

    [Fact]
    public async Task ShouldBypass_ReturnsTrue_WhenHeaderTrue()
    {
        _configProvider
            .Setup(p => p.GetStringAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("false");

        var result = await TimeWindowGuard.ShouldBypassAsync(
            BuildContext(headerValue: "true"), _configProvider.Object,
            NullLogger.Instance, "Test.Operation");

        Assert.True(result);
    }

    [Fact]
    public async Task ShouldBypass_ReturnsTrue_WhenQueryTrue()
    {
        _configProvider
            .Setup(p => p.GetStringAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("false");

        var result = await TimeWindowGuard.ShouldBypassAsync(
            BuildContext(queryValue: "true"), _configProvider.Object,
            NullLogger.Instance, "Test.Operation");

        Assert.True(result);
    }

    [Fact]
    public async Task ShouldBypass_ReturnsFalse_WhenHeaderFalseEvenIfConfigTrue()
    {
        // Header false = explicit "don't bypass"
        _configProvider
            .Setup(p => p.GetStringAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("true");

        var result = await TimeWindowGuard.ShouldBypassAsync(
            BuildContext(headerValue: "false"), _configProvider.Object,
            NullLogger.Instance, "Test.Operation");

        Assert.False(result);
    }

    [Fact]
    public async Task ShouldBypass_HeaderOverridesConfig_AndDoesNotQueryDb()
    {
        var result = await TimeWindowGuard.ShouldBypassAsync(
            BuildContext(headerValue: "true"), _configProvider.Object,
            NullLogger.Instance, "Test.Operation");

        Assert.True(result);
        // Khi header đã override, không nên gọi DB.
        _configProvider.Verify(
            p => p.GetStringAsync(SystemConfigKeys.BypassTimeWindowValidations, It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task ShouldBypass_ReturnsTrue_WhenDbConfigTrue()
    {
        _configProvider
            .Setup(p => p.GetStringAsync(SystemConfigKeys.BypassTimeWindowValidations, "false"))
            .ReturnsAsync("true");

        var result = await TimeWindowGuard.ShouldBypassAsync(
            BuildContext(), _configProvider.Object,
            NullLogger.Instance, "Test.Operation");

        Assert.True(result);
    }

    [Fact]
    public async Task ShouldBypass_AcceptsOneAsTrue()
    {
        _configProvider
            .Setup(p => p.GetStringAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("false");

        var result = await TimeWindowGuard.ShouldBypassAsync(
            BuildContext(headerValue: "1"), _configProvider.Object,
            NullLogger.Instance, "Test.Operation");

        Assert.True(result);
    }

    [Fact]
    public async Task ShouldBypass_AcceptsZeroAsFalse()
    {
        _configProvider
            .Setup(p => p.GetStringAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("true");

        var result = await TimeWindowGuard.ShouldBypassAsync(
            BuildContext(headerValue: "0"), _configProvider.Object,
            NullLogger.Instance, "Test.Operation");

        Assert.False(result);
    }

    [Fact]
    public async Task ShouldBypass_OverloadWithoutHttpContext_UsesDbConfig()
    {
        _configProvider
            .Setup(p => p.GetStringAsync(SystemConfigKeys.BypassTimeWindowValidations, "false"))
            .ReturnsAsync("true");

        var result = await TimeWindowGuard.ShouldBypassAsync(
            _configProvider.Object,
            NullLogger.Instance, "Test.BackgroundJob");

        Assert.True(result);
    }

    [Fact]
    public async Task ShouldBypass_ReturnsFalse_WhenHeaderGarbage()
    {
        _configProvider
            .Setup(p => p.GetStringAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("false");

        // Header không parse được → fallthrough xuống DB config (false).
        var result = await TimeWindowGuard.ShouldBypassAsync(
            BuildContext(headerValue: "garbage"), _configProvider.Object,
            NullLogger.Instance, "Test.Operation");

        Assert.False(result);
    }
}