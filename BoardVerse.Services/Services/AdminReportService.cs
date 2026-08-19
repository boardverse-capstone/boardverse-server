using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Services.Services;

public class AdminReportService : IAdminReportService
{
    private readonly ICafeRepository _cafeRepository;
    private readonly IBookingRepository _bookingRepository;
    private readonly ILobbyRepository _lobbyRepository;
    private readonly ITournamentRepository _tournamentRepository;
    private readonly IUserProfileRepository _userProfileRepository;
    private readonly IBookingDepositRepository _bookingDepositRepository;
    private readonly BoardVerseDbContext _dbContext;

    public AdminReportService(
        ICafeRepository cafeRepository,
        IBookingRepository bookingRepository,
        ILobbyRepository lobbyRepository,
        ITournamentRepository tournamentRepository,
        IUserProfileRepository userProfileRepository,
        IBookingDepositRepository bookingDepositRepository,
        BoardVerseDbContext dbContext)
    {
        _cafeRepository = cafeRepository;
        _bookingRepository = bookingRepository;
        _lobbyRepository = lobbyRepository;
        _tournamentRepository = tournamentRepository;
        _userProfileRepository = userProfileRepository;
        _bookingDepositRepository = bookingDepositRepository;
        _dbContext = dbContext;
    }

    public async Task<AdminDashboardOverviewDto> GetDashboardOverviewAsync()
    {
        // --- Bookings & Lobbies (counters) ---
        var totalBookings = await _bookingRepository.CountAllAsync(null, null);
        var pendingBookings = await _bookingRepository.CountByStatusAsync(BookingStatus.PendingDeposit, null, null);
        var confirmedBookings = await _bookingRepository.CountByStatusAsync(BookingStatus.Confirmed, null, null);
        var checkedInBookings = await _bookingRepository.CountByStatusAsync(BookingStatus.CheckedIn, null, null);
        var completedBookings = await _bookingRepository.CountByStatusAsync(BookingStatus.CheckedIn, null, null);
        // Note: BookingStatus enum does not have Completed — reuse CheckedIn as proxy for "finished sessions".
        // In production, completedBookings should map to ActiveSessionStatus.Paid instead.

        var timeoutFailures = await _lobbyRepository.CountFailuresByTypeAsync(null, null, LobbyStatus.TimeoutFailed);
        var hostCancelledFailures = await _lobbyRepository.CountFailuresByTypeAsync(null, null, LobbyStatus.HostCancelled);
        var rejectedByCafeFailures = await _lobbyRepository.CountFailuresByTypeAsync(null, null, LobbyStatus.RejectedByCafe);
        var expiredByCafeFailures = await _lobbyRepository.CountFailuresByTypeAsync(null, null, LobbyStatus.ExpiredByCafe);

        var totalLobbies = await _dbContext.Lobbies.CountAsync();
        var failedLobbies = timeoutFailures + hostCancelledFailures + rejectedByCafeFailures + expiredByCafeFailures;

        // --- Users & Cafes ---
        var totalUsers = await _userProfileRepository.CountUsersAsync();
        var totalCafes = await _cafeRepository.CountAllAsync();
        var activeCafes = await _cafeRepository.CountActiveAsync();

        // --- Tournaments ---
        var totalTournaments = await _tournamentRepository.CountAllAsync();
        var activeTournaments = await _tournamentRepository.CountByStatusAsync(TournamentStatus.OnGoing);
        var draftTournaments = await _tournamentRepository.CountByStatusAsync(TournamentStatus.Draft);
        var registrationOpenTournaments = await _tournamentRepository.CountByStatusAsync(TournamentStatus.RegistrationOpen);

        // --- Deposits (BookingDeposit) ---
        var pendingDeposit = await _bookingDepositRepository.SumByStatusAsync(BookingDepositStatus.Pending, null, null);
        var paidDeposit = await _bookingDepositRepository.SumByStatusAsync(BookingDepositStatus.Paid, null, null);
        var refundedDeposit = await _bookingDepositRepository.SumByStatusAsync(BookingDepositStatus.Refunded, null, null);
        var forfeitedDeposit = await _bookingDepositRepository.SumByStatusAsync(BookingDepositStatus.Forfeited, null, null);

        var totalDepositsCount = pendingDeposit.Count + paidDeposit.Count + refundedDeposit.Count + forfeitedDeposit.Count;
        var totalDepositsAmount = pendingDeposit.TotalAmount + paidDeposit.TotalAmount
                                + refundedDeposit.TotalAmount + forfeitedDeposit.TotalAmount;

        // --- Revenue (from ActiveSession.TotalAmount where Status = Paid) ---
        var revenueAgg = await _dbContext.ActiveSessions
            .Where(s => s.Status == GroupSessionStatus.Paid)
            .GroupBy(s => 1)
            .Select(g => new { Total = g.Sum(s => (decimal?)s.TotalAmount) ?? 0m })
            .FirstOrDefaultAsync();
        var totalRevenueVnd = revenueAgg?.Total ?? 0m;

        return new AdminDashboardOverviewDto
        {
            // Users / Cafes
            TotalUsers = totalUsers,
            TotalCafes = totalCafes,
            ActiveCafes = activeCafes,
            // Tournaments
            TotalTournaments = totalTournaments,
            ActiveTournaments = activeTournaments,
            DraftTournaments = draftTournaments,
            RegistrationOpenTournaments = registrationOpenTournaments,
            // Lobbies
            TotalLobbies = totalLobbies,
            FailedLobbies = failedLobbies,
            // Bookings
            TotalBookings = totalBookings,
            PendingBookings = pendingBookings,
            ConfirmedBookings = confirmedBookings,
            CheckedInBookings = checkedInBookings,
            CompletedBookings = completedBookings,
            // Revenue
            TotalRevenueVnd = totalRevenueVnd,
            // Deposits
            TotalDeposits = totalDepositsCount,
            TotalDepositsAmountVnd = totalDepositsAmount,
            PendingDeposits = pendingDeposit.Count,
            CancelledDeposits = refundedDeposit.Count + forfeitedDeposit.Count,
            // Lobby Failures breakdown
            TotalLobbyFailures = failedLobbies,
            TimeoutFailures = timeoutFailures,
            HostCancelledFailures = hostCancelledFailures,
            RejectedByCafeFailures = rejectedByCafeFailures,
            ExpiredByCafeFailures = expiredByCafeFailures,
            // Audit
            GeneratedAt = DateTime.UtcNow
        };
    }

    public async Task<AdminLobbyFailuresReportDto> GetLobbyFailuresReportAsync(
        int page,
        int pageSize,
        DateTime? fromUtc,
        DateTime? toUtc,
        string? failureType)
    {
        LobbyStatus? status = null;
        if (!string.IsNullOrWhiteSpace(failureType) && Enum.TryParse<LobbyStatus>(failureType, true, out var parsed))
        {
            status = parsed;
        }

        var (lobbyItems, totalCount) = await _lobbyRepository.GetAdminLobbyFailuresAsync(
            page, pageSize, fromUtc, toUtc, status);
        var items = lobbyItems;

        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        // Get breakdown by type
        var timeoutCount = status == null || status == LobbyStatus.TimeoutFailed
            ? await _lobbyRepository.CountFailuresByTypeAsync(fromUtc, toUtc, LobbyStatus.TimeoutFailed)
            : 0;
        var hostCancelledCount = status == null || status == LobbyStatus.HostCancelled
            ? await _lobbyRepository.CountFailuresByTypeAsync(fromUtc, toUtc, LobbyStatus.HostCancelled)
            : 0;
        var rejectedByCafeCount = status == null || status == LobbyStatus.RejectedByCafe
            ? await _lobbyRepository.CountFailuresByTypeAsync(fromUtc, toUtc, LobbyStatus.RejectedByCafe)
            : 0;
        var expiredByCafeCount = status == null || status == LobbyStatus.ExpiredByCafe
            ? await _lobbyRepository.CountFailuresByTypeAsync(fromUtc, toUtc, LobbyStatus.ExpiredByCafe)
            : 0;

        return new AdminLobbyFailuresReportDto
        {
            Items = items.Select(l => new AdminLobbyFailureItemDto
            {
                Id = l.Id,
                Title = l.Description ?? "Lobby " + l.Id.ToString()[..8],
                GameTemplateName = l.GameTemplate?.Name ?? "N/A",
                HostUsername = l.HostUser?.Username ?? "N/A",
                Status = l.Status.ToString(),
                FailureType = GetFailureType(l.Status),
                MemberCount = l.Members?.Count ?? 0,
                CreatedAt = l.CreatedAt,
                ClosedAt = l.UpdatedAt
            }).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalPages,
            TimeoutCount = timeoutCount,
            HostCancelledCount = hostCancelledCount,
            RejectedByCafeCount = rejectedByCafeCount,
            ExpiredByCafeCount = expiredByCafeCount
        };
    }

    public async Task<AdminDepositsReportDto> GetDepositsReportAsync(
        int page,
        int pageSize,
        DateTime? fromUtc,
        DateTime? toUtc)
    {
        var baseQuery = _dbContext.BookingDeposits.AsNoTracking().AsQueryable();
        if (fromUtc.HasValue)
        {
            baseQuery = baseQuery.Where(d => d.CreatedAt >= fromUtc.Value);
        }
        if (toUtc.HasValue)
        {
            baseQuery = baseQuery.Where(d => d.CreatedAt <= toUtc.Value);
        }

        var totalCount = await baseQuery.CountAsync();

        var items = await baseQuery
            .OrderByDescending(d => d.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new AdminDepositItemDto
            {
                Id = d.Id,
                OrderId = d.OrderId,
                UserId = d.UserId,
                Username = d.User != null ? d.User.Username : null,
                CafeId = d.CafeId,
                CafeName = d.Cafe != null ? d.Cafe.Name : null,
                Amount = d.Amount,
                Status = d.Status.ToString(),
                CreatedAt = d.CreatedAt
            })
            .ToListAsync();

        // Breakdown by status
        var pendingAgg = await _bookingDepositRepository.SumByStatusAsync(BookingDepositStatus.Pending, fromUtc, toUtc);
        var paidAgg = await _bookingDepositRepository.SumByStatusAsync(BookingDepositStatus.Paid, fromUtc, toUtc);
        var refundedAgg = await _bookingDepositRepository.SumByStatusAsync(BookingDepositStatus.Refunded, fromUtc, toUtc);
        var forfeitedAgg = await _bookingDepositRepository.SumByStatusAsync(BookingDepositStatus.Forfeited, fromUtc, toUtc);

        var totalPendingAmount = pendingAgg.TotalAmount;
        var totalPaidAmount = paidAgg.TotalAmount;
        var totalRefundedAmount = refundedAgg.TotalAmount;
        var totalForfeitedAmount = forfeitedAgg.TotalAmount;
        var totalAmountVnd = totalPendingAmount + totalPaidAmount + totalRefundedAmount + totalForfeitedAmount;

        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
        var averageDeposit = totalCount > 0 ? totalAmountVnd / totalCount : 0m;

        return new AdminDepositsReportDto
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalPages,

            // Backward-compat fields (Total* / Total*Amount)
            TotalPending = pendingAgg.Count,
            TotalPaid = paidAgg.Count,
            TotalRefunded = refundedAgg.Count,
            TotalForfeited = forfeitedAgg.Count,
            TotalPendingAmount = totalPendingAmount,
            TotalPaidAmount = totalPaidAmount,
            TotalRefundedAmount = totalRefundedAmount,
            TotalForfeitedAmount = totalForfeitedAmount,

            // Full DTO fields
            TotalDeposits = totalCount,
            TotalAmountVnd = totalAmountVnd,
            PendingDeposits = pendingAgg.Count,
            PendingAmountVnd = totalPendingAmount,
            PaidDeposits = paidAgg.Count,
            PaidAmountVnd = totalPaidAmount,
            RefundedDeposits = refundedAgg.Count,
            RefundedAmountVnd = totalRefundedAmount,
            ForfeitedDeposits = forfeitedAgg.Count,
            ForfeitedAmountVnd = totalForfeitedAmount,
            AverageDepositVnd = averageDeposit,
            Trend = new List<AdminDepositTrendItemDto>()
        };
    }

    public async Task<AdminCafePerformanceReportDto> GetCafePerformanceReportAsync(
        int page,
        int pageSize,
        string sortBy,
        bool sortDescending)
    {
        var (cafeItems, totalCount) = await _cafeRepository.GetAdminListAsync(
            page, pageSize, null, true, null);
        var items = cafeItems;

        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        // Pre-compute counts in parallel per cafe
        var cafePerformances = new List<AdminCafePerformanceDto>();
        foreach (var c in items)
        {
            var bookings = await _bookingRepository.GetByCafeIdAsync(c.Id, null, null);
            var lobbyFailures = await _lobbyRepository.CountFailuresByTypeAsync(null, null, null);
            var totalTournaments = await _dbContext.Tournaments.CountAsync(t => t.CafeId == c.Id);

            var totalB = bookings.Count;
            var completedB = bookings.Count(b => b.Status == BookingStatus.CheckedIn);
            var cancelledB = bookings.Count(b => b.Status == BookingStatus.Cancelled);
            var noShowB = bookings.Count(b => b.Status == BookingStatus.NoShow);
            var completionRate = totalB > 0 ? (decimal)completedB / totalB * 100m : 0m;

            var failedLobbies = lobbyFailures;
            var totalLobbies = await _dbContext.Lobbies.CountAsync(l => l.CafeId == c.Id);
            var failureRate = totalLobbies > 0 ? (decimal)failedLobbies / totalLobbies * 100m : 0m;

            var revenueAgg = await _dbContext.ActiveSessions
                .Where(s => s.CafeId == c.Id && s.Status == GroupSessionStatus.Paid)
                .GroupBy(s => 1)
                .Select(g => new { Total = g.Sum(s => (decimal?)s.TotalAmount) ?? 0m })
                .FirstOrDefaultAsync();
            var totalRevenue = revenueAgg?.Total ?? 0m;

            var depositAgg = await _dbContext.BookingDeposits
                .Where(d => d.CafeId == c.Id && d.Status == BookingDepositStatus.Paid)
                .GroupBy(d => 1)
                .Select(g => new { Total = g.Sum(d => (decimal?)d.Amount) ?? 0m })
                .FirstOrDefaultAsync();
            var totalDeposits = depositAgg?.Total ?? 0m;

            cafePerformances.Add(new AdminCafePerformanceDto
            {
                CafeId = c.Id,
                CafeName = c.Name,
                Address = c.Address,
                Status = c.IsActive ? "Active" : "Inactive",
                TotalBookings = totalB,
                CompletedBookings = completedB,
                CancelledBookings = cancelledB,
                NoShowBookings = noShowB,
                CompletionRate = Math.Round(completionRate, 2),
                TotalLobbies = totalLobbies,
                FailedLobbies = failedLobbies,
                FailureRate = Math.Round(failureRate, 2),
                TotalTournaments = totalTournaments,
                TotalRevenueVnd = totalRevenue,
                TotalDepositsVnd = totalDeposits,
                GeneratedAt = DateTime.UtcNow,
                ActiveCafe = c.IsActive,
                CreatedAt = c.CreatedAt
            });
        }

        // Apply sort
        cafePerformances = sortBy?.ToLowerInvariant() switch
        {
            "totalbookings" => sortDescending
                ? cafePerformances.OrderByDescending(x => x.TotalBookings).ToList()
                : cafePerformances.OrderBy(x => x.TotalBookings).ToList(),
            "completionrate" => sortDescending
                ? cafePerformances.OrderByDescending(x => x.CompletionRate).ToList()
                : cafePerformances.OrderBy(x => x.CompletionRate).ToList(),
            "failurerate" => sortDescending
                ? cafePerformances.OrderByDescending(x => x.FailureRate).ToList()
                : cafePerformances.OrderBy(x => x.FailureRate).ToList(),
            _ => sortDescending
                ? cafePerformances.OrderByDescending(x => x.TotalRevenueVnd).ToList()
                : cafePerformances.OrderBy(x => x.TotalRevenueVnd).ToList(),
        };

        return new AdminCafePerformanceReportDto
        {
            Cafes = cafePerformances,
            TotalCafes = totalCount,
            GeneratedAt = DateTime.UtcNow
        };
    }

    public async Task<AdminCafePerformanceDto?> GetCafePerformanceDetailAsync(
        Guid cafeId,
        DateTime? fromUtc,
        DateTime? toUtc)
    {
        var cafe = await _cafeRepository.GetAdminDetailAsync(cafeId);
        if (cafe == null)
        {
            return null;
        }

        var bookings = await _bookingRepository.GetByCafeIdAsync(cafeId, fromUtc, toUtc);
        var lobbyFailuresResult = await _lobbyRepository.GetAdminLobbyFailuresAsync(1, 1000, fromUtc, toUtc, null);
        var lobbyFailures = lobbyFailuresResult.TotalCount;
        var totalLobbies = await _dbContext.Lobbies.CountAsync(l => l.CafeId == cafeId);
        var totalTournaments = await _dbContext.Tournaments.CountAsync(t => t.CafeId == cafeId);

        var totalB = bookings.Count;
        var completedB = bookings.Count(b => b.Status == BookingStatus.CheckedIn);
        var cancelledB = bookings.Count(b => b.Status == BookingStatus.Cancelled);
        var noShowB = bookings.Count(b => b.Status == BookingStatus.NoShow);
        var completionRate = totalB > 0 ? (decimal)completedB / totalB * 100m : 0m;

        var failureRate = totalLobbies > 0 ? (decimal)lobbyFailures / totalLobbies * 100m : 0m;

        var revenueAgg = await _dbContext.ActiveSessions
            .Where(s => s.CafeId == cafeId && s.Status == GroupSessionStatus.Paid)
            .GroupBy(s => 1)
            .Select(g => new { Total = g.Sum(s => (decimal?)s.TotalAmount) ?? 0m })
            .FirstOrDefaultAsync();
        var totalRevenue = revenueAgg?.Total ?? 0m;

        var depositAgg = await _dbContext.BookingDeposits
            .Where(d => d.CafeId == cafeId && d.Status == BookingDepositStatus.Paid)
            .GroupBy(d => 1)
            .Select(g => new { Total = g.Sum(d => (decimal?)d.Amount) ?? 0m })
            .FirstOrDefaultAsync();
        var totalDeposits = depositAgg?.Total ?? 0m;

        return new AdminCafePerformanceDto
        {
            CafeId = cafe.Id,
            CafeName = cafe.Name,
            Address = cafe.Address,
            Status = cafe.IsActive ? "Active" : "Inactive",
            TotalBookings = totalB,
            CompletedBookings = completedB,
            CancelledBookings = cancelledB,
            NoShowBookings = noShowB,
            CompletionRate = Math.Round(completionRate, 2),
            TotalLobbies = totalLobbies,
            FailedLobbies = lobbyFailures,
            FailureRate = Math.Round(failureRate, 2),
            TotalTournaments = totalTournaments,
            TotalRevenueVnd = totalRevenue,
            TotalDepositsVnd = totalDeposits,
            GeneratedAt = DateTime.UtcNow,
            ActiveCafe = cafe.IsActive,
            CreatedAt = cafe.CreatedAt
        };
    }

    private static string GetFailureType(LobbyStatus status)
    {
        return status switch
        {
            LobbyStatus.TimeoutFailed => "Timeout",
            LobbyStatus.HostCancelled => "Host Cancelled",
            LobbyStatus.RejectedByCafe => "Rejected by Cafe",
            LobbyStatus.ExpiredByCafe => "Expired by Cafe",
            _ => status.ToString()
        };
    }
}
