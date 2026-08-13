using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace BoardVerse.Tests.Services;

/// <summary>
/// BR-NEW-10 §XI — Unit tests cho <see cref="CoolingOffService"/>.
///
/// Test pure logic của <c>EvaluateTrigger</c> qua <c>DetectSignalsAsync</c>:
/// - Threshold 3 lobby TimeoutFailed trong 7d → activate.
/// - Threshold 3 lobby HostCancelled trong 7d → activate.
/// - Threshold forfeit &gt; 500 BVC (= 500.000 VND) trong 30d → activate.
/// - Below threshold → no activation.
/// - Skip nếu wallet đã IsCoolingOff.
/// </summary>
public class CoolingOffServiceTests
{
    private readonly Mock<IWalletRepository> _walletRepo = new();
    private readonly Mock<ILobbyRepository> _lobbyRepo = new();
    private readonly Mock<IBvcLedgerEntryRepository> _ledgerRepo = new();
    private readonly CoolingOffService _sut;

    public CoolingOffServiceTests()
    {
        _sut = new CoolingOffService(
            _walletRepo.Object,
            _lobbyRepo.Object,
            _ledgerRepo.Object,
            null!, // DbContext không cần cho các pure logic tests (detect/expire/escalate).
            NullLogger<CoolingOffService>.Instance);
    }

    private static Wallet CreateWallet(Guid userId, bool isCoolingOff = false)
    {
        return new Wallet
        {
            UserId = userId,
            AvailableBalance = 100_000L,
            HeldBalance = 0L,
            RiskMultiplier = 1.0m,
            RiskScore = 0,
            RiskLevel = RiskLevel.Low,
            IsCoolingOff = isCoolingOff,
            CoolingOffExpiresAt = isCoolingOff ? DateTime.UtcNow.AddDays(30) : null,
            AccountStatus = AccountStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    // ===== DetectSignalsAsync =====

    [Fact]
    public async Task DetectSignalsAsync_Should_ReturnZeroCounts_When_NoFailures()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _lobbyRepo.Setup(r => r.CountFailuresByTypeForHostAsync(
                userId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), LobbyStatus.TimeoutFailed))
            .ReturnsAsync(0);
        _lobbyRepo.Setup(r => r.CountFailuresByTypeForHostAsync(
                userId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), LobbyStatus.HostCancelled))
            .ReturnsAsync(0);
        _ledgerRepo.Setup(r => r.SumForfeitAsync(userId, It.IsAny<DateTime>()))
            .ReturnsAsync(0m);

        // Act
        var signals = await _sut.DetectSignalsAsync(userId, DateTime.UtcNow);

        // Assert
        Assert.Equal(0, signals.TimeoutFailedCount7d);
        Assert.Equal(0, signals.HostCancelledCount7d);
        Assert.Equal(0L, signals.ForfeitAmount30d);
    }

    [Fact]
    public async Task DetectSignalsAsync_Should_ReturnTimeoutCount_When_LobbiesFailed()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _lobbyRepo.Setup(r => r.CountFailuresByTypeForHostAsync(
                userId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), LobbyStatus.TimeoutFailed))
            .ReturnsAsync(3);
        _lobbyRepo.Setup(r => r.CountFailuresByTypeForHostAsync(
                userId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), LobbyStatus.HostCancelled))
            .ReturnsAsync(0);
        _ledgerRepo.Setup(r => r.SumForfeitAsync(userId, It.IsAny<DateTime>()))
            .ReturnsAsync(0m);

        // Act
        var signals = await _sut.DetectSignalsAsync(userId, DateTime.UtcNow);

        // Assert
        Assert.Equal(3, signals.TimeoutFailedCount7d);
    }

    [Fact]
    public async Task DetectSignalsAsync_Should_ConvertDecimalForfeit_ToLong()
    {
        // Arrange: SumForfeitAsync returns decimal (theo interface).
        var userId = Guid.NewGuid();
        _lobbyRepo.Setup(r => r.CountFailuresByTypeForHostAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<LobbyStatus?>()))
            .ReturnsAsync(0);
        _ledgerRepo.Setup(r => r.SumForfeitAsync(userId, It.IsAny<DateTime>()))
            .ReturnsAsync(750.75m);

        // Act
        var signals = await _sut.DetectSignalsAsync(userId, DateTime.UtcNow);

        // Assert: cast (long)750.75m = 750.
        Assert.Equal(750L, signals.ForfeitAmount30d);
    }

    // ===== DetectAndActivateAsync =====

    [Fact]
    public async Task DetectAndActivateAsync_Should_Activate_When_TimeoutThresholdMet()
    {
        // Arrange: 1 wallet, 3 timeout failures (≥ threshold).
        var userId = Guid.NewGuid();
        var wallet = CreateWallet(userId);
        _walletRepo.Setup(r => r.GetActiveWalletsPagedAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<Wallet> { wallet });

        _lobbyRepo.Setup(r => r.CountFailuresByTypeForHostAsync(
                userId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), LobbyStatus.TimeoutFailed))
            .ReturnsAsync(3);
        _lobbyRepo.Setup(r => r.CountFailuresByTypeForHostAsync(
                userId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), LobbyStatus.HostCancelled))
            .ReturnsAsync(0);
        _ledgerRepo.Setup(r => r.SumForfeitAsync(userId, It.IsAny<DateTime>()))
            .ReturnsAsync(0m);

        // Act
        var activated = await _sut.DetectAndActivateAsync(DateTime.UtcNow, 100);

        // Assert
        Assert.Equal(1, activated);
        Assert.True(wallet.IsCoolingOff);
        Assert.NotNull(wallet.CoolingOffExpiresAt);
        Assert.True(wallet.CoolingOffExpiresAt > DateTime.UtcNow.AddDays(29));
        Assert.True(wallet.CoolingOffExpiresAt < DateTime.UtcNow.AddDays(31));
        Assert.Equal(2.0m, wallet.RiskMultiplier);
    }

    [Fact]
    public async Task DetectAndActivateAsync_Should_Activate_When_CancelThresholdMet()
    {
        // Arrange: 3 host-cancelled → activate.
        var userId = Guid.NewGuid();
        var wallet = CreateWallet(userId);
        _walletRepo.Setup(r => r.GetActiveWalletsPagedAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<Wallet> { wallet });

        _lobbyRepo.Setup(r => r.CountFailuresByTypeForHostAsync(
                userId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), LobbyStatus.TimeoutFailed))
            .ReturnsAsync(0);
        _lobbyRepo.Setup(r => r.CountFailuresByTypeForHostAsync(
                userId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), LobbyStatus.HostCancelled))
            .ReturnsAsync(3);
        _ledgerRepo.Setup(r => r.SumForfeitAsync(userId, It.IsAny<DateTime>()))
            .ReturnsAsync(0m);

        // Act
        var activated = await _sut.DetectAndActivateAsync(DateTime.UtcNow, 100);

        // Assert
        Assert.Equal(1, activated);
        Assert.True(wallet.IsCoolingOff);
    }

    [Fact]
    public async Task DetectAndActivateAsync_Should_Activate_When_ForfeitExceedsThreshold()
    {
        // Arrange: forfeit = 600 BVC (> 500 threshold = 500.000 VND).
        var userId = Guid.NewGuid();
        var wallet = CreateWallet(userId);
        _walletRepo.Setup(r => r.GetActiveWalletsPagedAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<Wallet> { wallet });

        _lobbyRepo.Setup(r => r.CountFailuresByTypeForHostAsync(
                userId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), LobbyStatus.TimeoutFailed))
            .ReturnsAsync(0);
        _lobbyRepo.Setup(r => r.CountFailuresByTypeForHostAsync(
                userId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), LobbyStatus.HostCancelled))
            .ReturnsAsync(0);
        _ledgerRepo.Setup(r => r.SumForfeitAsync(userId, It.IsAny<DateTime>()))
            .ReturnsAsync(600m);

        // Act
        var activated = await _sut.DetectAndActivateAsync(DateTime.UtcNow, 100);

        // Assert
        Assert.Equal(1, activated);
        Assert.True(wallet.IsCoolingOff);
    }

    [Fact]
    public async Task DetectAndActivateAsync_Should_NotActivate_When_BelowThreshold()
    {
        // Arrange: 2 timeout (below 3) + 100 forfeit (below 500 BVC).
        var userId = Guid.NewGuid();
        var wallet = CreateWallet(userId);
        _walletRepo.Setup(r => r.GetActiveWalletsPagedAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<Wallet> { wallet });

        _lobbyRepo.Setup(r => r.CountFailuresByTypeForHostAsync(
                userId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), LobbyStatus.TimeoutFailed))
            .ReturnsAsync(2);
        _lobbyRepo.Setup(r => r.CountFailuresByTypeForHostAsync(
                userId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), LobbyStatus.HostCancelled))
            .ReturnsAsync(1);
        _ledgerRepo.Setup(r => r.SumForfeitAsync(userId, It.IsAny<DateTime>()))
            .ReturnsAsync(100m);

        // Act
        var activated = await _sut.DetectAndActivateAsync(DateTime.UtcNow, 100);

        // Assert
        Assert.Equal(0, activated);
        Assert.False(wallet.IsCoolingOff);
        _walletRepo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task DetectAndActivateAsync_Should_Skip_When_AlreadyCoolingOff()
    {
        // Arrange: wallet đã cooling-off.
        var userId = Guid.NewGuid();
        var wallet = CreateWallet(userId, isCoolingOff: true);
        _walletRepo.Setup(r => r.GetActiveWalletsPagedAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<Wallet> { wallet });

        // Act
        var activated = await _sut.DetectAndActivateAsync(DateTime.UtcNow, 100);

        // Assert
        Assert.Equal(0, activated);
        // Không query signals (vì đã skip).
        _lobbyRepo.Verify(r => r.CountFailuresByTypeForHostAsync(
            It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<LobbyStatus?>()),
            Times.Never);
    }

    [Fact]
    public async Task DetectAndActivateAsync_Should_BumpRiskMultiplier_To2x_When_AlreadyHigher()
    {
        // Arrange: wallet có RiskMultiplier = 1.5 (cảnh báo). Activate → max(1.5, 2.0) = 2.0.
        var userId = Guid.NewGuid();
        var wallet = CreateWallet(userId);
        wallet.RiskMultiplier = 1.5m;
        _walletRepo.Setup(r => r.GetActiveWalletsPagedAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<Wallet> { wallet });

        _lobbyRepo.Setup(r => r.CountFailuresByTypeForHostAsync(
                userId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), LobbyStatus.TimeoutFailed))
            .ReturnsAsync(3);
        _lobbyRepo.Setup(r => r.CountFailuresByTypeForHostAsync(
                userId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), LobbyStatus.HostCancelled))
            .ReturnsAsync(0);
        _ledgerRepo.Setup(r => r.SumForfeitAsync(userId, It.IsAny<DateTime>()))
            .ReturnsAsync(0m);

        // Act
        await _sut.DetectAndActivateAsync(DateTime.UtcNow, 100);

        // Assert: RiskMultiplier bumped to 2.0 (max of existing 1.5 and threshold 2.0).
        Assert.Equal(2.0m, wallet.RiskMultiplier);
    }

    // ===== ExpireOverdueAsync =====

    [Fact]
    public async Task ExpireOverdueAsync_Should_Deactivate_When_ExpiresAtInPast()
    {
        // Arrange: 2 wallets đã quá hạn.
        var wallet1 = CreateWallet(Guid.NewGuid(), isCoolingOff: true);
        wallet1.CoolingOffExpiresAt = DateTime.UtcNow.AddDays(-1);

        var wallet2 = CreateWallet(Guid.NewGuid(), isCoolingOff: true);
        wallet2.CoolingOffExpiresAt = DateTime.UtcNow.AddHours(-5);

        _walletRepo.Setup(r => r.GetActiveCoolingOffWalletsPagedAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<Wallet> { wallet1, wallet2 });

        // Act
        var deactivated = await _sut.ExpireOverdueAsync(DateTime.UtcNow, 100);

        // Assert
        Assert.Equal(2, deactivated);
        Assert.False(wallet1.IsCoolingOff);
        Assert.Null(wallet1.CoolingOffExpiresAt);
        Assert.False(wallet2.IsCoolingOff);
        Assert.Null(wallet2.CoolingOffExpiresAt);
        _walletRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task ExpireOverdueAsync_Should_ReturnZero_When_NoOverdueWallets()
    {
        // Arrange
        _walletRepo.Setup(r => r.GetActiveCoolingOffWalletsPagedAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<Wallet>());

        // Act
        var deactivated = await _sut.ExpireOverdueAsync(DateTime.UtcNow, 100);

        // Assert
        Assert.Equal(0, deactivated);
        _walletRepo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    // ===== EscalateAsync =====

    [Fact]
    public async Task EscalateAsync_Should_ExtendTo30Days_And_MultiplyTo3x_When_CurrentlyCoolingOff()
    {
        // Arrange: user đang cooling-off với multiplier 2.0 (đã trigger).
        var userId = Guid.NewGuid();
        var wallet = CreateWallet(userId, isCoolingOff: true);
        wallet.RiskMultiplier = 2.0m;
        wallet.CoolingOffExpiresAt = DateTime.UtcNow.AddDays(15);

        _walletRepo.Setup(r => r.GetByUserIdForUpdateAsync(userId))
            .ReturnsAsync(wallet);

        // Act
        var beforeExpiresAt = wallet.CoolingOffExpiresAt;
        await _sut.EscalateAsync(userId, "Continued failing during cooling-off");

        // Assert
        Assert.True(wallet.IsCoolingOff);
        Assert.NotNull(wallet.CoolingOffExpiresAt);
        Assert.True(wallet.CoolingOffExpiresAt > beforeExpiresAt);
        // Gia hạn +30 ngày từ now (≈ 30 ngày từ hiện tại, tolerance ±1 phút).
        var daysUntilExpiry = (wallet.CoolingOffExpiresAt!.Value - DateTime.UtcNow).TotalDays;
        Assert.True(daysUntilExpiry > 29.9 && daysUntilExpiry < 30.1, $"Expected ~30 days, got {daysUntilExpiry}");
        Assert.Equal(3.0m, wallet.RiskMultiplier);
    }

    [Fact]
    public async Task EscalateAsync_Should_KeepHigherMultiplier_When_AlreadyAt3x()
    {
        // Arrange: đã escalate trước đó, multiplier = 3.0.
        var userId = Guid.NewGuid();
        var wallet = CreateWallet(userId, isCoolingOff: true);
        wallet.RiskMultiplier = 3.0m;

        _walletRepo.Setup(r => r.GetByUserIdForUpdateAsync(userId))
            .ReturnsAsync(wallet);

        // Act
        await _sut.EscalateAsync(userId, "Second escalation");

        // Assert: max(3.0, 3.0) = 3.0 (không giảm).
        Assert.Equal(3.0m, wallet.RiskMultiplier);
    }

    [Fact]
    public async Task EscalateAsync_Should_Return_When_WalletNotFound()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _walletRepo.Setup(r => r.GetByUserIdForUpdateAsync(userId))
            .ReturnsAsync((Wallet?)null);

        // Act + Assert: không throw, không save.
        await _sut.EscalateAsync(userId, "test");
        _walletRepo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task EscalateAsync_Should_Return_When_NotInCoolingOff()
    {
        // Arrange: wallet không trong cooling-off (false positive).
        var userId = Guid.NewGuid();
        var wallet = CreateWallet(userId, isCoolingOff: false);
        _walletRepo.Setup(r => r.GetByUserIdForUpdateAsync(userId))
            .ReturnsAsync(wallet);

        // Act + Assert: không thay đổi gì.
        await _sut.EscalateAsync(userId, "test");
        Assert.False(wallet.IsCoolingOff);
        _walletRepo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }
}
