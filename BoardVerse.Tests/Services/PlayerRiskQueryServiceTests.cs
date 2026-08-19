using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using BoardVerse.Data.Repositories;
using BoardVerse.Services.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BoardVerse.Tests.Services;

/// <summary>
/// BR-RISK-09 — Unit tests cho <see cref="PlayerRiskQueryService"/>.
/// Test với InMemory DbContext (theo pattern của SettlementServiceTests / BackgroundJobRepositoryTests).
/// </summary>
public class PlayerRiskQueryServiceTests
{
    private static BoardVerseDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<BoardVerseDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new BoardVerseDbContext(options);
    }

    private static Wallet CreateWallet(
        Guid userId,
        int riskScore = 25,
        RiskLevel riskLevel = RiskLevel.Low,
        decimal riskMultiplier = 1.0m,
        bool isCoolingOff = false,
        AccountStatus accountStatus = AccountStatus.Active)
    {
        return new Wallet
        {
            UserId = userId,
            AvailableBalance = 0L,
            HeldBalance = 0L,
            RiskScore = riskScore,
            RiskLevel = riskLevel,
            RiskMultiplier = riskMultiplier,
            IsCoolingOff = isCoolingOff,
            CoolingOffExpiresAt = isCoolingOff ? DateTime.UtcNow.AddDays(15) : null,
            AccountStatus = accountStatus,
            User = new User
            {
                Id = userId,
                Username = "testplayer",
                Email = $"player{userId:N}@test.com",
                CreatedAt = DateTime.UtcNow.AddDays(-30)
            },
            UpdatedAt = DateTime.UtcNow
        };
    }

    [Fact]
    public async Task GetPlayerRiskDetailAsync_Should_ThrowNotFound_When_WalletNotFound()
    {
        // Arrange
        using var db = CreateInMemoryContext();
        var walletRepo = new WalletRepository(db);
        var sut = new PlayerRiskQueryService(walletRepo, db);

        // Act + Assert
        await Assert.ThrowsAsync<NotFoundException>(() =>
            sut.GetPlayerRiskDetailAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetPlayerRiskDetailAsync_Should_MapAllFields_When_WalletFound()
    {
        // Arrange
        var userId = Guid.NewGuid();
        using var db = CreateInMemoryContext();
        db.Wallets.Add(CreateWallet(
            userId,
            riskScore: 78,
            riskLevel: RiskLevel.Critical,
            riskMultiplier: 2.0m,
            isCoolingOff: true,
            accountStatus: AccountStatus.Restricted));
        db.SaveChanges();

        var walletRepo = new WalletRepository(db);
        var sut = new PlayerRiskQueryService(walletRepo, db);

        // Act
        var result = await sut.GetPlayerRiskDetailAsync(userId);

        // Assert
        Assert.Equal(userId, result.UserId);
        Assert.Equal("testplayer", result.Username);
        Assert.Equal(78, result.RiskScore);
        Assert.Equal("critical", result.RiskLevel);
        Assert.Equal(2.0m, result.RiskMultiplier);
        Assert.Equal("restricted", result.AccountStatus);
        Assert.True(result.IsCoolingOff);
        Assert.NotNull(result.CoolingOffExpiresAt);
        Assert.True(result.CoolingOffExpiresAt > DateTime.UtcNow);
        Assert.Equal(0, result.ActionHistoryCount); // no history seeded
        Assert.NotNull(result.Signals);
        Assert.Empty(result.Signals); // no signals history
    }

    [Fact]
    public async Task GetPlayerRiskDetailAsync_Should_ParseSignalsJson_When_HistoryHasSignals()
    {
        // Arrange
        var userId = Guid.NewGuid();
        using var db = CreateInMemoryContext();
        db.Wallets.Add(CreateWallet(userId));
        db.PlayerActionHistories.Add(new PlayerActionHistory
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ActionType = AdminActionType.AccountStatusChange,
            ActionBy = Guid.NewGuid(),
            Reason = "Auto risk score change",
            Metadata = "{\"signals\":{\"SIG-01\":15,\"SIG-03\":40,\"SIG-08\":25}}",
            CreatedAt = DateTime.UtcNow.AddHours(-1)
        });
        db.SaveChanges();

        var walletRepo = new WalletRepository(db);
        var sut = new PlayerRiskQueryService(walletRepo, db);

        // Act
        var result = await sut.GetPlayerRiskDetailAsync(userId);

        // Assert
        Assert.NotEmpty(result.Signals);
        Assert.Equal(15, result.Signals["SIG-01"]);
        Assert.Equal(40, result.Signals["SIG-03"]);
        Assert.Equal(25, result.Signals["SIG-08"]);
        Assert.Equal(1, result.ActionHistoryCount);
    }

    [Fact]
    public async Task GetPlayerRiskDetailAsync_Should_ReturnEmptySignals_When_HistoryMetadataIsMalformed()
    {
        // Arrange
        var userId = Guid.NewGuid();
        using var db = CreateInMemoryContext();
        db.Wallets.Add(CreateWallet(userId));
        db.PlayerActionHistories.Add(new PlayerActionHistory
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ActionType = AdminActionType.AccountStatusChange,
            ActionBy = Guid.NewGuid(),
            Reason = "Broken metadata",
            Metadata = "{invalid json}",
            CreatedAt = DateTime.UtcNow
        });
        db.SaveChanges();

        var walletRepo = new WalletRepository(db);
        var sut = new PlayerRiskQueryService(walletRepo, db);

        // Act
        var result = await sut.GetPlayerRiskDetailAsync(userId);

        // Assert: malformed JSON trả empty signals (không throw).
        Assert.Empty(result.Signals);
    }

    [Fact]
    public async Task GetPlayerRiskDetailAsync_Should_ReturnEmptySignals_When_HistoryHasNoSignalsProperty()
    {
        // Arrange
        var userId = Guid.NewGuid();
        using var db = CreateInMemoryContext();
        db.Wallets.Add(CreateWallet(userId));
        db.PlayerActionHistories.Add(new PlayerActionHistory
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ActionType = AdminActionType.Warning,
            ActionBy = Guid.NewGuid(),
            Reason = "Generic",
            Metadata = "{\"someOtherField\":42}", // valid JSON, but no "signals"
            CreatedAt = DateTime.UtcNow
        });
        db.SaveChanges();

        var walletRepo = new WalletRepository(db);
        var sut = new PlayerRiskQueryService(walletRepo, db);

        // Act
        var result = await sut.GetPlayerRiskDetailAsync(userId);

        // Assert
        Assert.Empty(result.Signals);
        Assert.Equal(1, result.ActionHistoryCount);
    }
}
