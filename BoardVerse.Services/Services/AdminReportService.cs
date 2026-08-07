using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.IServices;

namespace BoardVerse.Services.Services;

public class AdminReportService : IAdminReportService
{
    private readonly ICafeRepository _cafeRepository;
    private readonly IBookingRepository _bookingRepository;
    private readonly ILobbyRepository _lobbyRepository;
    private readonly ITournamentRepository _tournamentRepository;
    private readonly IUserProfileRepository _userProfileRepository;

    public AdminReportService(
        ICafeRepository cafeRepository,
        IBookingRepository bookingRepository,
        ILobbyRepository lobbyRepository,
        ITournamentRepository tournamentRepository,
        IUserProfileRepository userProfileRepository)
    {
        _cafeRepository = cafeRepository;
        _bookingRepository = bookingRepository;
        _lobbyRepository = lobbyRepository;
        _tournamentRepository = tournamentRepository;
        _userProfileRepository = userProfileRepository;
    }

    public async Task<AdminDashboardOverviewDto> GetDashboardOverviewAsync()
    {
        var totalUsers = await _userProfileRepository.CountUsersAsync();
        var totalCafes = await _cafeRepository.CountAllAsync();
        var activeCafes = await _cafeRepository.CountActiveAsync();
        var totalTournaments = await _tournamentRepository.CountAllAsync();

        var activeTournaments = await _tournamentRepository.CountByStatusAsync(TournamentStatus.OnGoing);
        var draftTournaments = await _tournamentRepository.CountByStatusAsync(TournamentStatus.Draft);
        var registrationOpenTournaments = await _tournamentRepository.CountByStatusAsync(TournamentStatus.RegistrationOpen);

        var totalBookings = await _bookingRepository.CountAllAsync(null, null);
        var pendingBookings = await _bookingRepository.CountByStatusAsync(BookingStatus.PendingDeposit, null, null);
        var confirmedBookings = await _bookingRepository.CountByStatusAsync(BookingStatus.Confirmed, null, null);
        var checkedInBookings = await _bookingRepository.CountByStatusAsync(BookingStatus.CheckedIn, null, null);

        var timeoutFailures = await _lobbyRepository.CountFailuresByTypeAsync(null, null, LobbyStatus.TimeoutFailed);
        var hostCancelledFailures = await _lobbyRepository.CountFailuresByTypeAsync(null, null, LobbyStatus.HostCancelled);
        var rejectedByCafeFailures = await _lobbyRepository.CountFailuresByTypeAsync(null, null, LobbyStatus.RejectedByCafe);
        var expiredByCafeFailures = await _lobbyRepository.CountFailuresByTypeAsync(null, null, LobbyStatus.ExpiredByCafe);

        return new AdminDashboardOverviewDto
        {
            TotalUsers = totalUsers,
            TotalCafes = totalCafes,
            ActiveCafes = activeCafes,
            TotalTournaments = totalTournaments,
            ActiveTournaments = activeTournaments,
            DraftTournaments = draftTournaments,
            RegistrationOpenTournaments = registrationOpenTournaments,
            TotalBookings = totalBookings,
            PendingBookings = pendingBookings,
            ConfirmedBookings = confirmedBookings,
            CheckedInBookings = checkedInBookings,
            TotalLobbyFailures = timeoutFailures + hostCancelledFailures + rejectedByCafeFailures + expiredByCafeFailures,
            TimeoutFailures = timeoutFailures,
            HostCancelledFailures = hostCancelledFailures,
            RejectedByCafeFailures = rejectedByCafeFailures,
            ExpiredByCafeFailures = expiredByCafeFailures
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

        var (items, totalCount) = await _lobbyRepository.GetAdminLobbyFailuresAsync(
            page, pageSize, fromUtc, toUtc, status);

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
        // Simplified implementation - in production, this would query BookingDeposits
        var totalCount = 0;
        var pendingCount = 0;
        var paidCount = 0;
        var refundedCount = 0;
        var forfeitedCount = 0;

        return new AdminDepositsReportDto
        {
            Items = new List<AdminDepositItemDto>(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = 0,
            TotalPending = pendingCount,
            TotalPaid = paidCount,
            TotalRefunded = refundedCount,
            TotalForfeited = forfeitedCount,
            TotalPendingAmount = 0m,
            TotalPaidAmount = 0m,
            TotalRefundedAmount = 0m,
            TotalForfeitedAmount = 0m
        };
    }

    public async Task<AdminCafePerformanceReportDto> GetCafePerformanceReportAsync(
        int page,
        int pageSize,
        string sortBy,
        bool sortDescending)
    {
        var (items, totalCount) = await _cafeRepository.GetAdminListAsync(
            page, pageSize, null, true, null);

        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        var cafePerformances = items.Select(c => new AdminCafePerformanceDto
        {
            CafeId = c.Id,
            CafeName = c.Name,
            Address = c.Address,
            TotalBookings = 0, // Will be populated if needed
            TotalLobbies = 0,
            TotalTournaments = 0,
            ActiveCafe = c.IsActive,
            CreatedAt = c.CreatedAt
        }).ToList();

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
        var lobbies = await _lobbyRepository.GetAdminLobbyFailuresAsync(1, 1000, fromUtc, toUtc, null);

        return new AdminCafePerformanceDto
        {
            CafeId = cafe.Id,
            CafeName = cafe.Name,
            Address = cafe.Address,
            TotalBookings = bookings.Count,
            TotalLobbies = lobbies.TotalCount,
            TotalTournaments = 0,
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
