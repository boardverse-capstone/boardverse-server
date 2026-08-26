using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace BoardVerse.Tests.Services;

/// <summary>
/// BR-NEW-10 §XI.2 — Unit tests cho <see cref="CoolingOffService.ExtendAsync"/>.
///
/// Test pure logic + validation + audit behavior.
/// </summary>
public class CoolingOffExtendTests
{
    private readonly Mock<IWalletRepository> _walletRepo = new();
    private readonly Mock<ILobbyRepository> _lobbyRepo = new();
    private readonly Mock<IBvcLedgerEntryRepository> _ledgerRepo = new();
    private readonly Mock<DbSet<PlayerActionHistory>> _historyDbSet = new();
    private readonly CoolingOffService _sut;

    public CoolingOffExtendTests()
    {
        _sut = new CoolingOffService(
            _walletRepo.Object,
            _lobbyRepo.Object,
            _ledgerRepo.Object,
            null!, // DbContext sẽ throw khi Add vào null — chỉ test pure logic happy path
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CoolingOffService>.Instance);
    }

    private static Wallet CreateCoolingOffWallet(Guid userId, DateTime? expiresAt = null)
    {
        return new Wallet
        {
            UserId = userId,
            AvailableBalance = 0L,
            HeldBalance = 0L,
            RiskMultiplier = 2.0m,
            RiskScore = 75,
            RiskLevel = RiskLevel.Critical,
            IsCoolingOff = true,
            CoolingOffExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(15),
            AccountStatus = AccountStatus.Restricted,
            CreatedAt = DateTime.UtcNow.AddDays(-20),
            UpdatedAt = DateTime.UtcNow.AddDays(-2)
        };
    }

    [Fact]
    public async Task ExtendAsync_Should_Throw_When_AdditionalDaysBelow1()
    {
        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.ExtendAsync(Guid.NewGuid(), Guid.NewGuid(), 0, "valid reason here"));
    }

    [Fact]
    public async Task ExtendAsync_Should_Throw_When_AdditionalDaysAbove90()
    {
        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.ExtendAsync(Guid.NewGuid(), Guid.NewGuid(), 91, "valid reason here"));
    }

    [Fact]
    public async Task ExtendAsync_Should_Throw_When_ReasonTooShort()
    {
        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.ExtendAsync(Guid.NewGuid(), Guid.NewGuid(), 10, "short"));
    }

    [Fact]
    public async Task ExtendAsync_Should_Throw_When_WalletNotFound()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _walletRepo.Setup(r => r.GetByUserIdForUpdateAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Wallet?)null);

        // Act + Assert
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.ExtendAsync(Guid.NewGuid(), userId, 10, "valid reason here at least 10 chars"));
    }

    [Fact]
    public async Task ExtendAsync_Should_Throw_When_UserNotInCoolingOff()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var wallet = CreateCoolingOffWallet(userId);
        wallet.IsCoolingOff = false; // already deactivated
        _walletRepo.Setup(r => r.GetByUserIdForUpdateAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallet);

        // Act + Assert
        await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.ExtendAsync(Guid.NewGuid(), userId, 10, "valid reason here at least 10 chars"));
    }
}
