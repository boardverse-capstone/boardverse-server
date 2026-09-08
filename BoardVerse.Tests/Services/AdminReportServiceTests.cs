using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho AdminReportService — dashboard overview + lobby/deposit reports.
/// BR-ADMIN-REPORT: aggregates từ BookingDeposit + ActiveSession + Lobby repositories.
/// </summary>
public class AdminReportServiceTests
{
    private static BoardVerseDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<BoardVerseDbContext>()
            .UseInMemoryDatabase($"AdminReportServiceTests-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new BoardVerseDbContext(options);
    }

    private static (Mock<ICafeRepository> cafeRepo,
                    Mock<IBookingRepository> bookingRepo,
                    Mock<ILobbyRepository> lobbyRepo,
                    Mock<ITournamentRepository> tournamentRepo,
                    Mock<IUserProfileRepository> userProfileRepo,
                    Mock<IBookingDepositRepository> depositRepo)
        CreateMockRepos()
    {
        return (
            new Mock<ICafeRepository>(),
            new Mock<IBookingRepository>(),
            new Mock<ILobbyRepository>(),
            new Mock<ITournamentRepository>(),
            new Mock<IUserProfileRepository>(),
            new Mock<IBookingDepositRepository>()
        );
    }

    private static AdminReportService CreateService(
        Mock<ICafeRepository> cafeRepo,
        Mock<IBookingRepository> bookingRepo,
        Mock<ILobbyRepository> lobbyRepo,
        Mock<ITournamentRepository> tournamentRepo,
        Mock<IUserProfileRepository> userProfileRepo,
        Mock<IBookingDepositRepository> depositRepo,
        BoardVerseDbContext db) =>
        new(cafeRepo.Object, bookingRepo.Object, lobbyRepo.Object, tournamentRepo.Object,
            userProfileRepo.Object, depositRepo.Object, db);

    [Fact]
    public async Task GetDashboardOverviewAsync_AllZeros_ReturnsEmptyDto()
    {
        var (cafeRepo, bookingRepo, lobbyRepo, tournamentRepo, userProfileRepo, depositRepo) = CreateMockRepos();
        var db = CreateInMemoryDb();

        bookingRepo.Setup(r => r.CountAllAsync(null, null, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        bookingRepo.Setup(r => r.CountByStatusAsync(It.IsAny<BookingStatus>(), null, null, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        lobbyRepo.Setup(r => r.CountFailuresByTypeAsync(null, null, LobbyStatus.TimeoutFailed, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        lobbyRepo.Setup(r => r.CountFailuresByTypeAsync(null, null, LobbyStatus.HostCancelled, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        lobbyRepo.Setup(r => r.CountFailuresByTypeAsync(null, null, LobbyStatus.RejectedByCafe, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        lobbyRepo.Setup(r => r.CountFailuresByTypeAsync(null, null, LobbyStatus.ExpiredByCafe, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        lobbyRepo.Setup(r => r.CountFailuresByTypeAsync(null, null, LobbyStatus.Dissolved, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        userProfileRepo.Setup(r => r.CountUsersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
        cafeRepo.Setup(r => r.CountAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
        cafeRepo.Setup(r => r.CountActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
        tournamentRepo.Setup(r => r.CountAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
        tournamentRepo.Setup(r => r.CountByStatusAsync(It.IsAny<TournamentStatus>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        depositRepo.Setup(r => r.SumByStatusAsync(It.IsAny<BookingDepositStatus>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((0, 0m));

        var sut = CreateService(cafeRepo, bookingRepo, lobbyRepo, tournamentRepo, userProfileRepo, depositRepo, db);

        var result = await sut.GetDashboardOverviewAsync();

        Assert.Equal(0, result.TotalBookings);
        Assert.Equal(0, result.TotalUsers);
        Assert.Equal(0, result.TotalCafes);
        Assert.Equal(0, result.ActiveCafes);
        Assert.Equal(0, result.TotalLobbyFailures);
        Assert.Equal(0m, result.TotalRevenueVnd);
    }

    [Fact]
    public async Task GetDashboardOverviewAsync_WithRevenue_AggregatesCorrectly()
    {
        var (cafeRepo, bookingRepo, lobbyRepo, tournamentRepo, userProfileRepo, depositRepo) = CreateMockRepos();
        var db = CreateInMemoryDb();

        // Add one paid session to db to aggregate revenue
        var cafe = new Cafe { Id = Guid.NewGuid(), Name = "Test", Address = "addr", IsActive = true };
        db.Cafes.Add(cafe);
        db.ActiveSessions.Add(new ActiveSession
        {
            Id = Guid.NewGuid(),
            CafeId = cafe.Id,
            HostId = Guid.NewGuid(),
            GameTemplateId = Guid.NewGuid(),
            Status = GroupSessionStatus.Paid,
            StartedAt = DateTime.UtcNow.AddHours(-2),
            PaidAt = DateTime.UtcNow,
            TotalAmount = 100_000m
        });
        await db.SaveChangesAsync();

        bookingRepo.Setup(r => r.CountAllAsync(null, null, It.IsAny<CancellationToken>())).ReturnsAsync(5);
        bookingRepo.Setup(r => r.CountByStatusAsync(It.IsAny<BookingStatus>(), null, null, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        // Setup returns 2 for ALL status queries
        lobbyRepo.Setup(r => r.CountFailuresByTypeAsync(null, null, LobbyStatus.TimeoutFailed, It.IsAny<CancellationToken>())).ReturnsAsync(2);
        lobbyRepo.Setup(r => r.CountFailuresByTypeAsync(null, null, LobbyStatus.HostCancelled, It.IsAny<CancellationToken>())).ReturnsAsync(2);
        lobbyRepo.Setup(r => r.CountFailuresByTypeAsync(null, null, LobbyStatus.RejectedByCafe, It.IsAny<CancellationToken>())).ReturnsAsync(2);
        lobbyRepo.Setup(r => r.CountFailuresByTypeAsync(null, null, LobbyStatus.ExpiredByCafe, It.IsAny<CancellationToken>())).ReturnsAsync(2);
        lobbyRepo.Setup(r => r.CountFailuresByTypeAsync(null, null, LobbyStatus.Dissolved, It.IsAny<CancellationToken>())).ReturnsAsync(2);
        userProfileRepo.Setup(r => r.CountUsersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(50);
        cafeRepo.Setup(r => r.CountAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(10);
        cafeRepo.Setup(r => r.CountActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(7);
        tournamentRepo.Setup(r => r.CountAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(3);
        tournamentRepo.Setup(r => r.CountByStatusAsync(It.IsAny<TournamentStatus>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        depositRepo.Setup(r => r.SumByStatusAsync(It.IsAny<BookingDepositStatus>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((2, 50_000m));

        var sut = CreateService(cafeRepo, bookingRepo, lobbyRepo, tournamentRepo, userProfileRepo, depositRepo, db);

        var result = await sut.GetDashboardOverviewAsync();

        Assert.Equal(5, result.TotalBookings);
        Assert.Equal(50, result.TotalUsers);
        Assert.Equal(10, result.TotalCafes);
        Assert.Equal(7, result.ActiveCafes);
        // 5 failure statuses × 2 each = 10 (TimeoutFailed + HostCancelled + RejectedByCafe + ExpiredByCafe + Dissolved)
        Assert.Equal(10, result.TotalLobbyFailures);
        Assert.Equal(2, result.TimeoutFailures);
        Assert.Equal(2, result.HostCancelledFailures);
        Assert.Equal(2, result.RejectedByCafeFailures);
        Assert.Equal(2, result.ExpiredByCafeFailures);
        Assert.Equal(2, result.DissolvedFailures);
        Assert.Equal(100_000m, result.TotalRevenueVnd);
    }

    [Fact]
    public async Task GetLobbyFailuresReportAsync_InvalidFailureType_ReturnsAllStatuses()
    {
        var (cafeRepo, bookingRepo, lobbyRepo, tournamentRepo, userProfileRepo, depositRepo) = CreateMockRepos();
        var db = CreateInMemoryDb();

        lobbyRepo.Setup(r => r.GetAdminLobbyFailuresAsync(1, 20, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Lobby>(), 0));
        lobbyRepo.Setup(r => r.CountFailuresByTypeAsync(null, null, LobbyStatus.TimeoutFailed, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        lobbyRepo.Setup(r => r.CountFailuresByTypeAsync(null, null, LobbyStatus.HostCancelled, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        lobbyRepo.Setup(r => r.CountFailuresByTypeAsync(null, null, LobbyStatus.RejectedByCafe, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        lobbyRepo.Setup(r => r.CountFailuresByTypeAsync(null, null, LobbyStatus.ExpiredByCafe, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        lobbyRepo.Setup(r => r.CountFailuresByTypeAsync(null, null, LobbyStatus.Dissolved, It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var sut = CreateService(cafeRepo, bookingRepo, lobbyRepo, tournamentRepo, userProfileRepo, depositRepo, db);

        var result = await sut.GetLobbyFailuresReportAsync(1, 20, null, null, "INVALID_TYPE");

        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetLobbyFailuresReportAsync_ValidFailureType_ParsesAndFilters()
    {
        var (cafeRepo, bookingRepo, lobbyRepo, tournamentRepo, userProfileRepo, depositRepo) = CreateMockRepos();
        var db = CreateInMemoryDb();

        lobbyRepo.Setup(r => r.GetAdminLobbyFailuresAsync(1, 20, null, null, LobbyStatus.TimeoutFailed, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Lobby>(), 0));
        lobbyRepo.Setup(r => r.CountFailuresByTypeAsync(null, null, LobbyStatus.TimeoutFailed, It.IsAny<CancellationToken>())).ReturnsAsync(5);
        // When failureType is set, only the matching status is counted (others = 0)
        lobbyRepo.Setup(r => r.CountFailuresByTypeAsync(null, null, LobbyStatus.HostCancelled, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        lobbyRepo.Setup(r => r.CountFailuresByTypeAsync(null, null, LobbyStatus.RejectedByCafe, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        lobbyRepo.Setup(r => r.CountFailuresByTypeAsync(null, null, LobbyStatus.ExpiredByCafe, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        lobbyRepo.Setup(r => r.CountFailuresByTypeAsync(null, null, LobbyStatus.Dissolved, It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var sut = CreateService(cafeRepo, bookingRepo, lobbyRepo, tournamentRepo, userProfileRepo, depositRepo, db);

        var result = await sut.GetLobbyFailuresReportAsync(1, 20, null, null, "TimeoutFailed");

        Assert.Equal(0, result.TotalCount);
        Assert.Equal(5, result.TimeoutCount);
        Assert.Equal(0, result.HostCancelledCount);
        Assert.Equal(0, result.RejectedByCafeCount);
        Assert.Equal(0, result.ExpiredByCafeCount);
        Assert.Equal(0, result.DissolvedCount);
    }

    [Fact]
    public async Task GetDepositsReportAsync_EmptyDb_ReturnsEmptyReport()
    {
        var (cafeRepo, bookingRepo, lobbyRepo, tournamentRepo, userProfileRepo, depositRepo) = CreateMockRepos();
        var db = CreateInMemoryDb();

        depositRepo.Setup(r => r.SumByStatusAsync(It.IsAny<BookingDepositStatus>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((0, 0m));

        var sut = CreateService(cafeRepo, bookingRepo, lobbyRepo, tournamentRepo, userProfileRepo, depositRepo, db);

        var result = await sut.GetDepositsReportAsync(1, 20, null, null);

        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0m, result.TotalAmountVnd);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetDepositsReportAsync_WithDeposits_AggregatesByStatus()
    {
        var (cafeRepo, bookingRepo, lobbyRepo, tournamentRepo, userProfileRepo, depositRepo) = CreateMockRepos();
        var db = CreateInMemoryDb();

        depositRepo.Setup(r => r.SumByStatusAsync(BookingDepositStatus.Pending, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((3, 30_000m));
        depositRepo.Setup(r => r.SumByStatusAsync(BookingDepositStatus.Paid, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((10, 100_000m));
        depositRepo.Setup(r => r.SumByStatusAsync(BookingDepositStatus.Refunded, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((2, 20_000m));
        depositRepo.Setup(r => r.SumByStatusAsync(BookingDepositStatus.Forfeited, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((1, 10_000m));

        var sut = CreateService(cafeRepo, bookingRepo, lobbyRepo, tournamentRepo, userProfileRepo, depositRepo, db);

        var result = await sut.GetDepositsReportAsync(1, 20, null, null);

        Assert.Equal(3, result.TotalPending);
        Assert.Equal(10, result.TotalPaid);
        Assert.Equal(2, result.TotalRefunded);
        Assert.Equal(1, result.TotalForfeited);
        Assert.Equal(160_000m, result.TotalAmountVnd);
    }

    [Fact]
    public async Task GetDepositsReportAsync_WithDateFilter_AppliesFilter()
    {
        var (cafeRepo, bookingRepo, lobbyRepo, tournamentRepo, userProfileRepo, depositRepo) = CreateMockRepos();
        var db = CreateInMemoryDb();

        var from = DateTime.UtcNow.AddDays(-7);
        var to = DateTime.UtcNow;
        depositRepo.Setup(r => r.SumByStatusAsync(It.IsAny<BookingDepositStatus>(), from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync((0, 0m));

        var sut = CreateService(cafeRepo, bookingRepo, lobbyRepo, tournamentRepo, userProfileRepo, depositRepo, db);

        var result = await sut.GetDepositsReportAsync(1, 20, from, to);

        // Verify all status sums called with the date filter
        depositRepo.Verify(r => r.SumByStatusAsync(BookingDepositStatus.Pending, from, to, It.IsAny<CancellationToken>()), Times.Once);
        depositRepo.Verify(r => r.SumByStatusAsync(BookingDepositStatus.Paid, from, to, It.IsAny<CancellationToken>()), Times.Once);
        depositRepo.Verify(r => r.SumByStatusAsync(BookingDepositStatus.Refunded, from, to, It.IsAny<CancellationToken>()), Times.Once);
        depositRepo.Verify(r => r.SumByStatusAsync(BookingDepositStatus.Forfeited, from, to, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetDashboardOverviewAsync_FailedLobbiesSummedAcrossTypes()
    {
        var (cafeRepo, bookingRepo, lobbyRepo, tournamentRepo, userProfileRepo, depositRepo) = CreateMockRepos();
        var db = CreateInMemoryDb();

        bookingRepo.Setup(r => r.CountAllAsync(null, null, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        bookingRepo.Setup(r => r.CountByStatusAsync(It.IsAny<BookingStatus>(), null, null, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        lobbyRepo.Setup(r => r.CountFailuresByTypeAsync(null, null, LobbyStatus.TimeoutFailed, It.IsAny<CancellationToken>())).ReturnsAsync(3);
        lobbyRepo.Setup(r => r.CountFailuresByTypeAsync(null, null, LobbyStatus.HostCancelled, It.IsAny<CancellationToken>())).ReturnsAsync(2);
        lobbyRepo.Setup(r => r.CountFailuresByTypeAsync(null, null, LobbyStatus.RejectedByCafe, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        lobbyRepo.Setup(r => r.CountFailuresByTypeAsync(null, null, LobbyStatus.ExpiredByCafe, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        lobbyRepo.Setup(r => r.CountFailuresByTypeAsync(null, null, LobbyStatus.Dissolved, It.IsAny<CancellationToken>())).ReturnsAsync(4);
        userProfileRepo.Setup(r => r.CountUsersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
        cafeRepo.Setup(r => r.CountAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
        cafeRepo.Setup(r => r.CountActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
        tournamentRepo.Setup(r => r.CountAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
        tournamentRepo.Setup(r => r.CountByStatusAsync(It.IsAny<TournamentStatus>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        depositRepo.Setup(r => r.SumByStatusAsync(It.IsAny<BookingDepositStatus>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((0, 0m));

        var sut = CreateService(cafeRepo, bookingRepo, lobbyRepo, tournamentRepo, userProfileRepo, depositRepo, db);

        var result = await sut.GetDashboardOverviewAsync();

        Assert.Equal(10, result.TotalLobbyFailures);
        Assert.Equal(3, result.TimeoutFailures);
        Assert.Equal(2, result.HostCancelledFailures);
        Assert.Equal(1, result.RejectedByCafeFailures);
        Assert.Equal(0, result.ExpiredByCafeFailures);
        Assert.Equal(4, result.DissolvedFailures);
    }
}
