using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.Services;
using BoardVerse.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BoardVerse.Tests.Services;

/// <summary>
/// R-01 (BR-RISK-02): Unit tests cho PlayerAlertService — admin acknowledge/resolve/dismiss + auto-trigger từ signals.
/// </summary>
public class PlayerAlertServiceTests
{
    private static readonly Guid AlertId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid UserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbb01");
    private static readonly Guid AdminId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    /// <summary>
    /// InMemory DbContext cho audit-log tests — tránh FK constraint + JSON column conflict với Postgres test DB.
    /// </summary>
    private static BoardVerse.Data.BoardVerseDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<BoardVerse.Data.BoardVerseDbContext>()
            .UseInMemoryDatabase($"PlayerAlertTests-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new BoardVerse.Data.BoardVerseDbContext(options);
    }

    private static PlayerAlert BuildAlert(
        PlayerAlertStatus status = PlayerAlertStatus.Open,
        PlayerAlertSeverity severity = PlayerAlertSeverity.Critical)
    {
        return new PlayerAlert
        {
            Id = AlertId,
            UserId = UserId,
            AlertType = PlayerAlertType.AutoThresholdCrossed,
            Severity = severity,
            Status = status,
            RiskScoreSnapshot = 85,
            Signals = "{\"SIG-01\":3,\"SIG-03\":750}",
            CreatedAt = DateTime.UtcNow.AddHours(-1)
        };
    }

    #region AcknowledgeAsync

    [Fact]
    public async Task AcknowledgeAsync_OpenAlert_SetsAcknowledged()
    {
        // BR-RISK-02: admin xem alert → status = Acknowledged, ghi AcknowledgedBy/At.
        var repo = new Mock<IPlayerAlertRepository>();
        var alert = BuildAlert(PlayerAlertStatus.Open);
        repo.Setup(r => r.GetByIdAsync(AlertId)).ReturnsAsync(alert);

        var service = new PlayerAlertService(repo.Object, new FakeDbContext(), NullLogger<PlayerAlertService>.Instance);
        var result = await service.AcknowledgeAsync(AlertId, AdminId);

        Assert.Equal(PlayerAlertStatus.Acknowledged, alert.Status);
        Assert.Equal(AdminId, alert.AcknowledgedBy);
        Assert.NotNull(alert.AcknowledgedAt);
        Assert.Equal(PlayerAlertStatus.Acknowledged, result.Status);
    }

    [Fact]
    public async Task AcknowledgeAsync_AlertNotFound_ThrowsNotFound()
    {
        var repo = new Mock<IPlayerAlertRepository>();
        repo.Setup(r => r.GetByIdAsync(AlertId)).ReturnsAsync((PlayerAlert?)null);

        var service = new PlayerAlertService(repo.Object, new FakeDbContext(), NullLogger<PlayerAlertService>.Instance);

        await Assert.ThrowsAsync<NotFoundException>(() => service.AcknowledgeAsync(AlertId, AdminId));
    }

    [Fact]
    public async Task AcknowledgeAsync_AlreadyAcknowledged_ThrowsConflict()
    {
        // BR-RISK-02: chỉ alert Open mới acknowledge được.
        var repo = new Mock<IPlayerAlertRepository>();
        var alert = BuildAlert(PlayerAlertStatus.Acknowledged);
        repo.Setup(r => r.GetByIdAsync(AlertId)).ReturnsAsync(alert);

        var service = new PlayerAlertService(repo.Object, new FakeDbContext(), NullLogger<PlayerAlertService>.Instance);

        await Assert.ThrowsAsync<ConflictException>(() => service.AcknowledgeAsync(AlertId, AdminId));
    }

    [Fact]
    public async Task AcknowledgeAsync_AlreadyResolved_ThrowsConflict()
    {
        // Regression: alert đã Resolved cũng không được acknowledge.
        var repo = new Mock<IPlayerAlertRepository>();
        var alert = BuildAlert(PlayerAlertStatus.Resolved);
        repo.Setup(r => r.GetByIdAsync(AlertId)).ReturnsAsync(alert);

        var service = new PlayerAlertService(repo.Object, new FakeDbContext(), NullLogger<PlayerAlertService>.Instance);

        await Assert.ThrowsAsync<ConflictException>(() => service.AcknowledgeAsync(AlertId, AdminId));
    }

    [Fact]
    public async Task AcknowledgeAsync_AlreadyDismissed_ThrowsConflict()
    {
        var repo = new Mock<IPlayerAlertRepository>();
        var alert = BuildAlert(PlayerAlertStatus.Dismissed);
        repo.Setup(r => r.GetByIdAsync(AlertId)).ReturnsAsync(alert);

        var service = new PlayerAlertService(repo.Object, new FakeDbContext(), NullLogger<PlayerAlertService>.Instance);

        await Assert.ThrowsAsync<ConflictException>(() => service.AcknowledgeAsync(AlertId, AdminId));
    }

    #endregion

    #region ResolveAsync

    [Fact]
    public async Task ResolveAsync_OpenAlert_SetsResolvedAndWritesAudit()
    {
        // BR-RISK-02 + BR-RISK-05: resolve phải ghi PlayerActionHistory audit log.
        var repo = new Mock<IPlayerAlertRepository>();
        var alert = BuildAlert(PlayerAlertStatus.Open);
        repo.Setup(r => r.GetByIdAsync(AlertId)).ReturnsAsync(alert);

        var fakeDb = CreateInMemoryDbContext();
        var service = new PlayerAlertService(repo.Object, fakeDb, NullLogger<PlayerAlertService>.Instance);
        var result = await service.ResolveAsync(AlertId, AdminId, "Confirmed multi-account.");

        Assert.Equal(PlayerAlertStatus.Resolved, alert.Status);
        Assert.Equal(AdminId, alert.AcknowledgedBy);
        Assert.NotNull(alert.AcknowledgedAt);
        Assert.Equal("Confirmed multi-account.", alert.ResolutionNote);
        // Audit log
        Assert.Contains(fakeDb.PlayerActionHistories.Local,
            h => h.UserId == UserId && h.ActionBy == AdminId && h.Reason.Contains("resolved"));
    }

    [Fact]
    public async Task ResolveAsync_AlertNotFound_ThrowsNotFound()
    {
        var repo = new Mock<IPlayerAlertRepository>();
        repo.Setup(r => r.GetByIdAsync(AlertId)).ReturnsAsync((PlayerAlert?)null);

        var service = new PlayerAlertService(repo.Object, new FakeDbContext(), NullLogger<PlayerAlertService>.Instance);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.ResolveAsync(AlertId, AdminId, "note"));
    }

    [Fact]
    public async Task ResolveAsync_AlreadyResolved_ThrowsConflict()
    {
        // Regression: không cho resolve 2 lần.
        var repo = new Mock<IPlayerAlertRepository>();
        var alert = BuildAlert(PlayerAlertStatus.Resolved);
        repo.Setup(r => r.GetByIdAsync(AlertId)).ReturnsAsync(alert);

        var service = new PlayerAlertService(repo.Object, new FakeDbContext(), NullLogger<PlayerAlertService>.Instance);

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.ResolveAsync(AlertId, AdminId, "double"));
    }

    [Fact]
    public async Task ResolveAsync_NoteIsTrimmed()
    {
        // UX: whitespace phải được trim.
        var repo = new Mock<IPlayerAlertRepository>();
        var alert = BuildAlert(PlayerAlertStatus.Open);
        repo.Setup(r => r.GetByIdAsync(AlertId)).ReturnsAsync(alert);

        var service = new PlayerAlertService(repo.Object, CreateInMemoryDbContext(), NullLogger<PlayerAlertService>.Instance);
        await service.ResolveAsync(AlertId, AdminId, "  some note  ");

        Assert.Equal("some note", alert.ResolutionNote);
    }

    #endregion

    #region DismissAsync

    [Fact]
    public async Task DismissAsync_OpenAlert_SetsDismissedAndWritesAudit()
    {
        // BR-RISK-02: dismiss = false positive → ghi audit "dismissed: <note>".
        var repo = new Mock<IPlayerAlertRepository>();
        var alert = BuildAlert(PlayerAlertStatus.Open);
        repo.Setup(r => r.GetByIdAsync(AlertId)).ReturnsAsync(alert);

        var fakeDb = CreateInMemoryDbContext();
        var service = new PlayerAlertService(repo.Object, fakeDb, NullLogger<PlayerAlertService>.Instance);
        var result = await service.DismissAsync(AlertId, AdminId, "False positive");

        Assert.Equal(PlayerAlertStatus.Dismissed, alert.Status);
        Assert.Equal(AdminId, alert.AcknowledgedBy);
        Assert.NotNull(alert.AcknowledgedAt);
        Assert.Equal("False positive", alert.ResolutionNote);
        Assert.Contains(fakeDb.PlayerActionHistories.Local,
            h => h.UserId == UserId && h.Reason.Contains("dismissed"));
    }

    [Fact]
    public async Task DismissAsync_AlreadyDismissed_SucceedsAndOverwritesNote()
    {
        // Regression: dismiss 2 lần vẫn được (idempotent về status, nhưng note overwrite).
        var repo = new Mock<IPlayerAlertRepository>();
        var alert = BuildAlert(PlayerAlertStatus.Dismissed);
        alert.ResolutionNote = "old reason";
        repo.Setup(r => r.GetByIdAsync(AlertId)).ReturnsAsync(alert);

        var service = new PlayerAlertService(repo.Object, CreateInMemoryDbContext(), NullLogger<PlayerAlertService>.Instance);
        await service.DismissAsync(AlertId, AdminId, "new reason");

        Assert.Equal("new reason", alert.ResolutionNote);
    }

    [Fact]
    public async Task DismissAsync_AlertNotFound_ThrowsNotFound()
    {
        var repo = new Mock<IPlayerAlertRepository>();
        repo.Setup(r => r.GetByIdAsync(AlertId)).ReturnsAsync((PlayerAlert?)null);

        var service = new PlayerAlertService(repo.Object, new FakeDbContext(), NullLogger<PlayerAlertService>.Instance);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.DismissAsync(AlertId, AdminId, "note"));
    }

    #endregion

    #region EnsureAlertForSignalsAsync

    [Fact]
    public async Task EnsureAlertForSignalsAsync_NonCriticalLevel_DoesNotCreate()
    {
        // BR-RISK-02: MVP chỉ trigger cho Critical. Low/Medium/High → no-op.
        var repo = new Mock<IPlayerAlertRepository>();
        repo.Setup(r => r.ShouldCreateAutoAlertAsync(It.IsAny<Guid>(), It.IsAny<PlayerAlertType>(),
                It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(true);

        var service = new PlayerAlertService(repo.Object, new FakeDbContext(), NullLogger<PlayerAlertService>.Instance);
        await service.EnsureAlertForSignalsAsync(UserId, 35, RiskLevel.Medium, RiskLevel.Low, "{\"SIG-01\":2}");

        repo.Verify(r => r.AddAsync(It.IsAny<PlayerAlert>()), Times.Never);
    }

    [Fact]
    public async Task EnsureAlertForSignalsAsync_CriticalLevel_CreatesAlertWithCriticalSeverity()
    {
        // BR-RISK-02: critical riskScore (>=75) → tạo PlayerAlert severity=Critical.
        var repo = new Mock<IPlayerAlertRepository>();
        repo.Setup(r => r.ShouldCreateAutoAlertAsync(UserId, PlayerAlertType.AutoThresholdCrossed,
                "{\"SIG-01\":3}", It.IsAny<int>()))
            .ReturnsAsync(true);

        PlayerAlert? captured = null;
        repo.Setup(r => r.AddAsync(It.IsAny<PlayerAlert>()))
            .Callback<PlayerAlert>(a => captured = a)
            .Returns(Task.CompletedTask);

        var service = new PlayerAlertService(repo.Object, new FakeDbContext(), NullLogger<PlayerAlertService>.Instance);
        await service.EnsureAlertForSignalsAsync(UserId, 85, RiskLevel.Critical, RiskLevel.High, "{\"SIG-01\":3}");

        Assert.NotNull(captured);
        Assert.Equal(UserId, captured!.UserId);
        Assert.Equal(PlayerAlertSeverity.Critical, captured.Severity);
        Assert.Equal(PlayerAlertType.AutoThresholdCrossed, captured.AlertType);
        Assert.Equal(PlayerAlertStatus.Open, captured.Status);
        Assert.Equal(85, captured.RiskScoreSnapshot);
    }

    [Fact]
    public async Task EnsureAlertForSignalsAsync_CooldownActive_DoesNotCreate()
    {
        // BR-RISK-02: nếu ShouldCreateAutoAlertAsync return false (đã có alert trùng signals trong 24h) → no-op.
        var repo = new Mock<IPlayerAlertRepository>();
        repo.Setup(r => r.ShouldCreateAutoAlertAsync(It.IsAny<Guid>(), It.IsAny<PlayerAlertType>(),
                It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(false);

        var service = new PlayerAlertService(repo.Object, new FakeDbContext(), NullLogger<PlayerAlertService>.Instance);
        await service.EnsureAlertForSignalsAsync(UserId, 85, RiskLevel.Critical, RiskLevel.High, "{\"SIG-01\":3}");

        repo.Verify(r => r.AddAsync(It.IsAny<PlayerAlert>()), Times.Never);
    }

    [Fact]
    public async Task EnsureAlertForSignalsAsync_NullSignals_UsesEmptyString()
    {
        // Edge case: signalsJson null → signalsKey phải là string.Empty (không phải null) cho query.
        var repo = new Mock<IPlayerAlertRepository>();
        repo.Setup(r => r.ShouldCreateAutoAlertAsync(UserId, PlayerAlertType.AutoThresholdCrossed,
                string.Empty, It.IsAny<int>()))
            .ReturnsAsync(true);

        var service = new PlayerAlertService(repo.Object, new FakeDbContext(), NullLogger<PlayerAlertService>.Instance);
        await service.EnsureAlertForSignalsAsync(UserId, 80, RiskLevel.Critical, RiskLevel.Medium, signalsJson: null);

        repo.Verify(r => r.AddAsync(It.IsAny<PlayerAlert>()), Times.Once);
    }

    #endregion

    #region GetMetricsAsync + GetPagedAsync

    [Fact]
    public async Task GetMetricsAsync_AggregatesByStatusAndSeverity()
    {
        // BR-RISK-02 dashboard: Total / OpenCritical / Open / Acknowledged / Resolved / Dismissed.
        var fakeDb = CreateInMemoryDbContext();
        fakeDb.PlayerAlerts.AddRange(
            new PlayerAlert { Id = Guid.NewGuid(), UserId = UserId, AlertType = PlayerAlertType.AutoThresholdCrossed, Severity = PlayerAlertSeverity.Critical, Status = PlayerAlertStatus.Open, RiskScoreSnapshot = 80, CreatedAt = DateTime.UtcNow },
            new PlayerAlert { Id = Guid.NewGuid(), UserId = UserId, AlertType = PlayerAlertType.AutoThresholdCrossed, Severity = PlayerAlertSeverity.Warning, Status = PlayerAlertStatus.Open, RiskScoreSnapshot = 60, CreatedAt = DateTime.UtcNow },
            new PlayerAlert { Id = Guid.NewGuid(), UserId = UserId, AlertType = PlayerAlertType.AutoThresholdCrossed, Severity = PlayerAlertSeverity.Critical, Status = PlayerAlertStatus.Acknowledged, RiskScoreSnapshot = 80, CreatedAt = DateTime.UtcNow },
            new PlayerAlert { Id = Guid.NewGuid(), UserId = UserId, AlertType = PlayerAlertType.AutoThresholdCrossed, Severity = PlayerAlertSeverity.Critical, Status = PlayerAlertStatus.Resolved, RiskScoreSnapshot = 80, CreatedAt = DateTime.UtcNow },
            new PlayerAlert { Id = Guid.NewGuid(), UserId = UserId, AlertType = PlayerAlertType.AutoThresholdCrossed, Severity = PlayerAlertSeverity.Info, Status = PlayerAlertStatus.Dismissed, RiskScoreSnapshot = 30, CreatedAt = DateTime.UtcNow }
        );
        await fakeDb.SaveChangesAsync();

        var service = new PlayerAlertService(new Mock<IPlayerAlertRepository>().Object, fakeDb, NullLogger<PlayerAlertService>.Instance);
        var metrics = await service.GetMetricsAsync();

        Assert.Equal(5, metrics.Total);
        Assert.Equal(1, metrics.OpenCritical);
        Assert.Equal(2, metrics.Open);
        Assert.Equal(1, metrics.Acknowledged);
        Assert.Equal(1, metrics.Resolved);
        Assert.Equal(1, metrics.Dismissed);
    }

    [Fact]
    public async Task GetPagedAsync_PassesThroughToRepository()
    {
        var repo = new Mock<IPlayerAlertRepository>();
        repo.Setup(r => r.GetPagedAsync(It.IsAny<PlayerAlertQuery>()))
            .ReturnsAsync(new BoardVerse.Core.Common.PaginatedResponse<BoardVerse.Core.DTOs.Admin.PlayerAlertDto>
            {
                Data = new List<BoardVerse.Core.DTOs.Admin.PlayerAlertDto>(),
                Meta = new BoardVerse.Core.Common.PaginationMeta { CurrentPage = 1, PageSize = 20, TotalItems = 0 }
            });

        var service = new PlayerAlertService(repo.Object, new FakeDbContext(), NullLogger<PlayerAlertService>.Instance);
        var query = new PlayerAlertQuery { PageNumber = 1, PageSize = 20 };
        var result = await service.GetPagedAsync(query);

        Assert.Equal(1, result.Meta.CurrentPage);
        Assert.Equal(20, result.Meta.PageSize);
        repo.Verify(r => r.GetPagedAsync(It.IsAny<PlayerAlertQuery>()), Times.Once);
    }

    #endregion

    #region DismissStaleAlertsAsync

    [Fact]
    public async Task DismissStaleAlertsAsync_MarksStaleAsDismissedAndWritesAudit()
    {
        // AlertExpiryCleanupJob: auto-dismiss alerts cũ > maxAgeDays.
        var repo = new Mock<IPlayerAlertRepository>();
        var staleAlerts = new List<PlayerAlert>
        {
            new PlayerAlert { Id = Guid.NewGuid(), UserId = UserId, AlertType = PlayerAlertType.AutoThresholdCrossed, Severity = PlayerAlertSeverity.Warning, Status = PlayerAlertStatus.Open, RiskScoreSnapshot = 60, CreatedAt = DateTime.UtcNow.AddDays(-45) },
            new PlayerAlert { Id = Guid.NewGuid(), UserId = UserId, AlertType = PlayerAlertType.AutoThresholdCrossed, Severity = PlayerAlertSeverity.Warning, Status = PlayerAlertStatus.Acknowledged, RiskScoreSnapshot = 60, CreatedAt = DateTime.UtcNow.AddDays(-31) }
        };
        repo.Setup(r => r.GetStaleAlertsForDismissalAsync(30, 100)).ReturnsAsync(staleAlerts);

        var fakeDb = CreateInMemoryDbContext();
        var service = new PlayerAlertService(repo.Object, fakeDb, NullLogger<PlayerAlertService>.Instance);
        var dismissedCount = await service.DismissStaleAlertsAsync(maxAgeDays: 30, batchSize: 100);

        Assert.Equal(2, dismissedCount);
        Assert.All(staleAlerts, a => Assert.Equal(PlayerAlertStatus.Dismissed, a.Status));
        Assert.All(staleAlerts, a => Assert.Contains("Auto-dismissed", a.ResolutionNote ?? ""));
        // Audit log phải ghi system (ActionBy = Guid.Empty)
        Assert.Equal(2, fakeDb.PlayerActionHistories.Local.Count);
        Assert.All(fakeDb.PlayerActionHistories.Local, h => Assert.Equal(Guid.Empty, h.ActionBy));
    }

    [Fact]
    public async Task DismissStaleAlertsAsync_NoStale_ReturnsZero()
    {
        var repo = new Mock<IPlayerAlertRepository>();
        repo.Setup(r => r.GetStaleAlertsForDismissalAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new List<PlayerAlert>());

        var fakeDb = CreateInMemoryDbContext();
        var service = new PlayerAlertService(repo.Object, fakeDb, NullLogger<PlayerAlertService>.Instance);
        var count = await service.DismissStaleAlertsAsync(30, 100);

        Assert.Equal(0, count);
        Assert.Empty(fakeDb.PlayerActionHistories.Local);
    }

    #endregion
}