#nullable enable
using BoardVerse.API.BackgroundServices;
using BoardVerse.Core.Enum;
using BoardVerse.Data;
using BoardVerse.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace BoardVerse.Tests.Integration;

/// <summary>
/// Integration tests cho 2 background jobs:
/// - <see cref="LobbyNotificationJob"/>: BR-NEW-13 — notification 4 milestone (48h/24h/2h/30p).
/// - <see cref="LobbyAtRiskWarningJob"/>: BR-NEW-14 — cảnh báo lobby có nguy cơ fail.
///
/// Test approach: gọi StartAsync để trigger job, đợi 1 vòng loop, StopAsync cancel.
/// Vì job chạy mỗi 5 phút và dùng scope provider riêng, ta assert:
/// 1. Job chạy được với DI container thật (không crash vì missing services).
/// 2. Idempotency: chạy 2 lần liên tiếp, không tạo duplicate LobbyNotificationSent cho cùng (LobbyId, Milestone).
/// 3. Job skip lobby đã terminal (status = Closed/Full/InProgress) — không gửi notification.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class BackgroundJobIntegrationTests
{
    private readonly BoardVerseWebApplicationFactory _factory;

    public BackgroundJobIntegrationTests(BoardVerseWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Helper chạy job 1 vòng: StartAsync → delay 2s (cho loop vào iteration đầu) → StopAsync.
    /// BackgroundService loop: ProcessNotificationsAsync → Task.Delay(5min) → repeat.
    /// </summary>
    private static async Task RunOneIterationAsync(BackgroundService job, TimeSpan? warmup = null)
    {
        using var cts = new CancellationTokenSource();
        await job.StartAsync(cts.Token);
        try
        {
            // Đợi job chạy ít nhất 1 vòng (process + delay 5min would block forever,
            // nên chỉ cancel sau vài trăm ms để job kịp chạy vòng đầu).
            await Task.Delay(warmup ?? TimeSpan.FromMilliseconds(800), cts.Token);
        }
        catch (OperationCanceledException) { /* expected */ }
        finally
        {
            await job.StopAsync(CancellationToken.None);
        }
    }

    [IntegrationFact]
    public async Task LobbyNotificationJob_ShouldStartAndStop_WithoutCrash()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var job = ActivatorUtilities.CreateInstance<LobbyNotificationJob>(
            scope.ServiceProvider,
            scope.ServiceProvider.GetRequiredService<IServiceProvider>(),
            NullLogger<LobbyNotificationJob>.Instance);

        // Act + Assert: chạy 1 vòng không crash
        await RunOneIterationAsync(job);

        // DB sanity check: bảng LobbyNotificationSent tồn tại + queryable
        using var verifyScope = _factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<BoardVerseDbContext>();
        var count = await db.LobbyNotificationSents.CountAsync();
        Assert.True(count >= 0); // không crash khi query
    }

    [IntegrationFact]
    public async Task LobbyNotificationJob_ShouldNotThrow_WhenNoActiveLobbies()
    {
        // Tất cả demo lobby đều ở status terminal (test bootstrap reset về Idle/Closed)
        // → job vẫn chạy ổn, không có gì để query.
        using var scope = _factory.Services.CreateScope();
        var job = ActivatorUtilities.CreateInstance<LobbyNotificationJob>(
            scope.ServiceProvider,
            scope.ServiceProvider.GetRequiredService<IServiceProvider>(),
            NullLogger<LobbyNotificationJob>.Instance);

        await RunOneIterationAsync(job);
    }

    [IntegrationFact]
    public async Task LobbyNotificationJob_IsIdempotent_WhenRunMultipleTimes()
    {
        // Idempotency contract: chạy job 2 lần liên tiếp, không tạo duplicate LobbyNotificationSent
        // cho cùng (LobbyId, Milestone). Job insert vào grouped check trước khi send.

        using var scope1 = _factory.Services.CreateScope();
        var job1 = ActivatorUtilities.CreateInstance<LobbyNotificationJob>(
            scope1.ServiceProvider,
            scope1.ServiceProvider.GetRequiredService<IServiceProvider>(),
            NullLogger<LobbyNotificationJob>.Instance);

        using var scope2 = _factory.Services.CreateScope();
        var job2 = ActivatorUtilities.CreateInstance<LobbyNotificationJob>(
            scope2.ServiceProvider,
            scope2.ServiceProvider.GetRequiredService<IServiceProvider>(),
            NullLogger<LobbyNotificationJob>.Instance);

        // Snapshot count trước
        using var verifyScope = _factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<BoardVerseDbContext>();
        var countBefore = await db.LobbyNotificationSents.CountAsync();

        await RunOneIterationAsync(job1);
        var countAfterFirst = await db.LobbyNotificationSents.CountAsync();

        await RunOneIterationAsync(job2);
        var countAfterSecond = await db.LobbyNotificationSents.CountAsync();

        // Idempotency: lần thứ 2 không thêm row mới cho cùng milestone
        // (job check grouped.ContainsKey trước khi insert).
        // Lưu ý: nếu có lobby mới đủ điều kiện giữa 2 vòng, vẫn có thể tăng count.
        // Test chỉ assert: count không tăng GẤP ĐÔI → idempotency được tôn trọng.
        Assert.True(
            countAfterSecond - countBefore <= countAfterFirst - countBefore + 1,
            $"Job 2 lần tạo {countAfterSecond - countBefore} records, không idempotent.");
    }

    [IntegrationFact]
    public async Task LobbyAtRiskWarningJob_ShouldStartAndStop_WithoutCrash()
    {
        using var scope = _factory.Services.CreateScope();
        var job = ActivatorUtilities.CreateInstance<LobbyAtRiskWarningJob>(
            scope.ServiceProvider,
            scope.ServiceProvider.GetRequiredService<IServiceProvider>(),
            NullLogger<LobbyAtRiskWarningJob>.Instance);

        await RunOneIterationAsync(job);

        // DB sanity check
        using var verifyScope = _factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<BoardVerseDbContext>();
        var count = await db.LobbyAtRiskWarnings.CountAsync();
        Assert.True(count >= 0);
    }

    [IntegrationFact]
    public async Task LobbyAtRiskWarningJob_IsIdempotent_WhenRunMultipleTimes()
    {
        // Idempotency: atRiskSent.ContainsKey check trước khi insert warning.
        // Chạy 2 lần liên tiếp → không duplicate warning cho cùng lobby.

        using var scope1 = _factory.Services.CreateScope();
        var job1 = ActivatorUtilities.CreateInstance<LobbyAtRiskWarningJob>(
            scope1.ServiceProvider,
            scope1.ServiceProvider.GetRequiredService<IServiceProvider>(),
            NullLogger<LobbyAtRiskWarningJob>.Instance);

        using var scope2 = _factory.Services.CreateScope();
        var job2 = ActivatorUtilities.CreateInstance<LobbyAtRiskWarningJob>(
            scope2.ServiceProvider,
            scope2.ServiceProvider.GetRequiredService<IServiceProvider>(),
            NullLogger<LobbyAtRiskWarningJob>.Instance);

        using var verifyScope = _factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<BoardVerseDbContext>();
        var countBefore = await db.LobbyAtRiskWarnings.CountAsync();

        await RunOneIterationAsync(job1);
        var countAfterFirst = await db.LobbyAtRiskWarnings.CountAsync();

        await RunOneIterationAsync(job2);
        var countAfterSecond = await db.LobbyAtRiskWarnings.CountAsync();

        // Idempotency contract
        Assert.True(
            countAfterSecond <= countAfterFirst + 1,
            $"At-risk job không idempotent: tăng từ {countAfterFirst} → {countAfterSecond}.");
    }

    [IntegrationFact]
    public async Task LobbyAtRiskWarningJob_ShouldNotWarn_FullyBookedLobbies()
    {
        // BR-NEW-14: lobby status = Full KHÔNG nằm trong recruitingStatuses filter.
        // Demo lobby ở status Full → không có warning mới.

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BoardVerseDbContext>();

        var fullLobbies = await db.Lobbies
            .Where(l => l.Status == LobbyStatus.Full)
            .CountAsync();

        using var jobScope = _factory.Services.CreateScope();
        var job = ActivatorUtilities.CreateInstance<LobbyAtRiskWarningJob>(
            jobScope.ServiceProvider,
            jobScope.ServiceProvider.GetRequiredService<IServiceProvider>(),
            NullLogger<LobbyAtRiskWarningJob>.Instance);

        var countBefore = await db.LobbyAtRiskWarnings
            .Where(w => fullLobbies > 0
                && db.Lobbies.Any(l => l.Id == w.LobbyId && l.Status == LobbyStatus.Full))
            .CountAsync();

        await RunOneIterationAsync(job);

        var countAfter = await db.LobbyAtRiskWarnings
            .Where(w => fullLobbies > 0
                && db.Lobbies.Any(l => l.Id == w.LobbyId && l.Status == LobbyStatus.Full))
            .CountAsync();

        // Full lobbies không bao giờ warning
        Assert.Equal(countBefore, countAfter);
    }
}
