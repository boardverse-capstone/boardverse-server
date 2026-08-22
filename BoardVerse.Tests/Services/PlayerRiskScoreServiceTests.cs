using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using BoardVerse.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BoardVerse.Tests.Services;

/// <summary>
/// BR-RISK-01 + BR-RISK-02 + BR-RISK-11: Unit tests cho PlayerRiskScoreService — pure functions + recompute flow.
/// </summary>
public class PlayerRiskScoreServiceTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    /// <summary>InMemory DbContext cho risk score history test (JSON column).</summary>
    private static BoardVerse.Data.BoardVerseDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<BoardVerse.Data.BoardVerseDbContext>()
            .UseInMemoryDatabase($"PlayerRiskScoreTests-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new BoardVerse.Data.BoardVerseDbContext(options);
    }

    private PlayerRiskScoreService CreateService(
        IPlayerRiskScoreRepository? riskRepo = null,
        ILobbyRepository? lobbyRepo = null,
        IBvcLedgerEntryRepository? ledgerRepo = null,
        IPlayerAlertService? alertService = null,
        BoardVerse.Data.BoardVerseDbContext? db = null)
    {
        return new PlayerRiskScoreService(
            riskRepo ?? new Mock<IPlayerRiskScoreRepository>().Object,
            lobbyRepo ?? new Mock<ILobbyRepository>().Object,
            ledgerRepo ?? new Mock<IBvcLedgerEntryRepository>().Object,
            db ?? CreateInMemoryDbContext(),
            alertService ?? new Mock<IPlayerAlertService>().Object,
            NullLogger<PlayerRiskScoreService>.Instance);
    }

    #region ComputeRiskScore (BR-RISK-01)

    [Fact]
    public void ComputeRiskScore_EmptySignals_ReturnsZero()
    {
        var svc = CreateService();
        var score = svc.ComputeRiskScore(new Dictionary<string, int>());
        Assert.Equal(0, score);
    }

    [Fact]
    public void ComputeRiskScore_OnlyTimeoutFailed_ReturnsCorrectWeight()
    {
        // BR-RISK-01: SIG-01 (timeout failed 7d) × W=15.
        var svc = CreateService();
        var score = svc.ComputeRiskScore(new Dictionary<string, int> { ["SIG-01"] = 3 });
        Assert.Equal(45, score); // 3 × 15
    }

    [Fact]
    public void ComputeRiskScore_AllSignalsCombined()
    {
        // All 5 signals: SIG-01=2 (×15=30), SIG-02=1 (×15=15), SIG-03=500 (×20/1000=10),
        // SIG-04=3 (×10=30), SIG-08=0 (×25=0) → total=85.
        var svc = CreateService();
        var score = svc.ComputeRiskScore(new Dictionary<string, int>
        {
            ["SIG-01"] = 2,
            ["SIG-02"] = 1,
            ["SIG-03"] = 500,
            ["SIG-04"] = 3,
            ["SIG-08"] = 0
        });
        Assert.Equal(85, score);
    }

    [Fact]
    public void ComputeRiskScore_ClampedAt100()
    {
        // SIG-01=10 → 150, phải clamp về 100.
        var svc = CreateService();
        var score = svc.ComputeRiskScore(new Dictionary<string, int> { ["SIG-01"] = 10 });
        Assert.Equal(100, score);
    }

    [Fact]
    public void ComputeRiskScore_NegativeValuesClampedAtZero()
    {
        // SIG-03 âm → /1000 = âm → clamp về 0.
        var svc = CreateService();
        var score = svc.ComputeRiskScore(new Dictionary<string, int> { ["SIG-03"] = -5000 });
        Assert.Equal(0, score);
    }

    [Fact]
    public void ComputeRiskScore_UnknownSignalIgnored()
    {
        // Signal không trong BR-RISK-01 (vd SIG-99) phải bị ignore.
        var svc = CreateService();
        var score = svc.ComputeRiskScore(new Dictionary<string, int>
        {
            ["SIG-01"] = 1,
            ["SIG-99"] = 100 // không tồn tại trong formula
        });
        Assert.Equal(15, score);
    }

    #endregion

    #region ResolveRiskLevel

    [Theory]
    [InlineData(0, RiskLevel.Low)]
    [InlineData(15, RiskLevel.Low)]
    [InlineData(29, RiskLevel.Low)]
    [InlineData(30, RiskLevel.Medium)]
    [InlineData(45, RiskLevel.Medium)]
    [InlineData(49, RiskLevel.Medium)]
    [InlineData(50, RiskLevel.High)]
    [InlineData(70, RiskLevel.High)]
    [InlineData(74, RiskLevel.High)]
    [InlineData(75, RiskLevel.Critical)]
    [InlineData(85, RiskLevel.Critical)]
    [InlineData(100, RiskLevel.Critical)]
    public void ResolveRiskScore_MapsCorrectlyToLevel(int score, RiskLevel expectedLevel)
    {
        var svc = CreateService();
        Assert.Equal(expectedLevel, svc.ResolveRiskLevel(score));
    }

    #endregion

    #region RecomputeForUserAsync (integration of pure functions)

    [Fact]
    public async Task RecomputeForUserAsync_NewUser_CreatesSnapshotAndHistory()
    {
        // BR-RISK-11: append RiskScoreHistory cho mỗi recompute.
        var riskRepo = new Mock<IPlayerRiskScoreRepository>();
        riskRepo.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync((PlayerRiskScore?)null);
        riskRepo.Setup(r => r.UpsertAsync(It.IsAny<PlayerRiskScore>())).Returns(Task.CompletedTask);
        riskRepo.Setup(r => r.AppendHistoryAsync(It.IsAny<RiskScoreHistory>())).Returns(Task.CompletedTask);

        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.CountFailuresByTypeForHostAsync(UserId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), LobbyStatus.TimeoutFailed, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2); // SIG-01 = 2

        var ledgerRepo = new Mock<IBvcLedgerEntryRepository>();
        ledgerRepo.Setup(r => r.SumForfeitAsync(UserId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var alertService = new Mock<IPlayerAlertService>();
        var db = CreateInMemoryDbContext();

        var svc = CreateService(riskRepo.Object, lobbyRepo.Object, ledgerRepo.Object, alertService.Object, db);
        var result = await svc.RecomputeForUserAsync(UserId, DateTime.UtcNow);

        Assert.NotNull(result);
        Assert.Equal(30, result!.RiskScore); // SIG-01=2 × 15
        Assert.Equal(RiskLevel.Medium, result.RiskLevel);
        riskRepo.Verify(r => r.UpsertAsync(It.IsAny<PlayerRiskScore>()), Times.Once);
        riskRepo.Verify(r => r.AppendHistoryAsync(It.IsAny<RiskScoreHistory>()), Times.Once);
    }

    [Fact]
    public async Task RecomputeForUserAsync_TransitionToCritical_TriggersAlert()
    {
        // BR-RISK-02: chỉ trigger alert khi level tăng lên Critical.
        var riskRepo = new Mock<IPlayerRiskScoreRepository>();
        var existing = new PlayerRiskScore
        {
            UserId = UserId,
            RiskScore = 40,
            RiskLevel = RiskLevel.Medium,
            Signals = "{}"
        };
        riskRepo.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        riskRepo.Setup(r => r.UpsertAsync(It.IsAny<PlayerRiskScore>())).Returns(Task.CompletedTask);
        riskRepo.Setup(r => r.AppendHistoryAsync(It.IsAny<RiskScoreHistory>())).Returns(Task.CompletedTask);

        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.CountFailuresByTypeForHostAsync(UserId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), LobbyStatus.TimeoutFailed, It.IsAny<CancellationToken>()))
            .ReturnsAsync(6); // SIG-01=6 → 90 điểm → Critical

        var ledgerRepo = new Mock<IBvcLedgerEntryRepository>();
        ledgerRepo.Setup(r => r.SumForfeitAsync(UserId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var alertService = new Mock<IPlayerAlertService>();
        var db = CreateInMemoryDbContext();

        var svc = CreateService(riskRepo.Object, lobbyRepo.Object, ledgerRepo.Object, alertService.Object, db);
        await svc.RecomputeForUserAsync(UserId, DateTime.UtcNow);

        // Verify alert được trigger
        alertService.Verify(a => a.EnsureAlertForSignalsAsync(
            UserId, It.IsAny<int>(), RiskLevel.Critical, RiskLevel.Medium, It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task RecomputeForUserAsync_AlreadyCritical_DoesNotTriggerAlertAgain()
    {
        // BR-RISK-02: chỉ trigger khi transition INTO Critical, không phải mỗi lần recompute.
        var riskRepo = new Mock<IPlayerRiskScoreRepository>();
        var existing = new PlayerRiskScore
        {
            UserId = UserId,
            RiskScore = 85,
            RiskLevel = RiskLevel.Critical,
            Signals = "{}"
        };
        riskRepo.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        riskRepo.Setup(r => r.UpsertAsync(It.IsAny<PlayerRiskScore>())).Returns(Task.CompletedTask);
        riskRepo.Setup(r => r.AppendHistoryAsync(It.IsAny<RiskScoreHistory>())).Returns(Task.CompletedTask);

        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.CountFailuresByTypeForHostAsync(UserId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<LobbyStatus?>(), It.IsAny<CancellationToken>())).ReturnsAsync(6);

        var ledgerRepo = new Mock<IBvcLedgerEntryRepository>();
        ledgerRepo.Setup(r => r.SumForfeitAsync(UserId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var alertService = new Mock<IPlayerAlertService>();
        var db = CreateInMemoryDbContext();

        var svc = CreateService(riskRepo.Object, lobbyRepo.Object, ledgerRepo.Object, alertService.Object, db);
        await svc.RecomputeForUserAsync(UserId, DateTime.UtcNow);

        alertService.Verify(a => a.EnsureAlertForSignalsAsync(
            It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<RiskLevel>(), It.IsAny<RiskLevel>(), It.IsAny<string?>()), Times.Never);
    }

    #endregion
}