using BoardVerse.Core.Data;
using BoardVerse.Services.Helpers;
using BoardVerse.Services.IServices;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho DemoGuard + bypass logic demo mode.
/// BR-DEMO-01..04: skip BR-USER-LIMIT-01/04/05, BR-LOBBY-01a/b (buffer),
/// BR-NEW-05 (max 5 tạo/hủy), BR-CHECKIN-01 (early grace 15 phút).
/// </summary>
public class DemoGuardTests
{
    [Fact]
    public async Task ShouldBypassDemoLocksAsync_Should_ReturnTrue_When_DbConfigTrue()
    {
        // Arrange: DB toggle ON
        var configProvider = new Mock<ISystemConfigurationProvider>();
        configProvider
            .Setup(p => p.GetStringAsync(SystemConfigKeys.DemoLoosenLobbyConstraints, "false", It.IsAny<CancellationToken>()))
            .ReturnsAsync("true");

        // Act
        var bypass = await DemoGuard.ShouldBypassDemoLocksAsync(
            httpContext: null,
            configProvider: configProvider.Object,
            logger: NullLogger.Instance,
            operation: "test.operation");

        // Assert
        Assert.True(bypass);
    }

    [Fact]
    public async Task ShouldBypassDemoLocksAsync_Should_ReturnFalse_When_DbConfigFalse()
    {
        // Arrange: DB toggle OFF (default)
        var configProvider = new Mock<ISystemConfigurationProvider>();
        configProvider
            .Setup(p => p.GetStringAsync(SystemConfigKeys.DemoLoosenLobbyConstraints, "false", It.IsAny<CancellationToken>()))
            .ReturnsAsync("false");

        // Act
        var bypass = await DemoGuard.ShouldBypassDemoLocksAsync(
            httpContext: null,
            configProvider: configProvider.Object,
            logger: NullLogger.Instance,
            operation: "test.operation");

        // Assert
        Assert.False(bypass);
    }

    [Fact]
    public async Task ShouldBypassDemoLocksAsync_Should_ReturnFalse_When_ConfigProviderNull()
    {
        // Arrange: không có config provider → fallback false
        // Act
        var bypass = await DemoGuard.ShouldBypassDemoLocksAsync(
            httpContext: null,
            configProvider: null,
            logger: NullLogger.Instance,
            operation: "test.operation");

        // Assert
        Assert.False(bypass);
    }

    [Fact]
    public async Task ShouldBypassDemoLocksAsync_Should_ReturnTrue_When_HeaderOverrideTrue()
    {
        // Arrange: HTTP header override ưu tiên cao nhất
        var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        ctx.Request.Headers["X-Bypass-Demo-Locks"] = "true";

        var configProvider = new Mock<ISystemConfigurationProvider>();
        // DB config FALSE để chứng minh header được ưu tiên
        configProvider
            .Setup(p => p.GetStringAsync(SystemConfigKeys.DemoLoosenLobbyConstraints, "false", It.IsAny<CancellationToken>()))
            .ReturnsAsync("false");

        // Act
        var bypass = await DemoGuard.ShouldBypassDemoLocksAsync(
            httpContext: ctx,
            configProvider: configProvider.Object,
            logger: NullLogger.Instance,
            operation: "test.operation");

        // Assert
        Assert.True(bypass);
    }

    [Fact]
    public async Task ShouldBypassDemoLocksAsync_Should_ReturnTrue_When_QueryOverrideTrue()
    {
        // Arrange: query param override (ưu tiên sau header)
        var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        ctx.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString("?bypassDemoLocks=1");

        var configProvider = new Mock<ISystemConfigurationProvider>();
        configProvider
            .Setup(p => p.GetStringAsync(SystemConfigKeys.DemoLoosenLobbyConstraints, "false", It.IsAny<CancellationToken>()))
            .ReturnsAsync("false");

        // Act
        var bypass = await DemoGuard.ShouldBypassDemoLocksAsync(
            httpContext: ctx,
            configProvider: configProvider.Object,
            logger: NullLogger.Instance,
            operation: "test.operation");

        // Assert
        Assert.True(bypass);
    }
}