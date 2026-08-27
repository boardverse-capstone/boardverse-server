using BoardVerse.Core.DTOs.Pos;
using BoardVerse.Core.DTOs.Reservation;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Data;
using BoardVerse.Services.Helpers;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services;

/// <summary>
/// Player scan QR POS để check-in (BR §21A.7 — 2 chiều).
/// QR POS lưu trong PosCheckInToken. Player gửi token → server lookup → gọi
/// ICafePosService.CheckInByCodeAsync với ReservationCode đã có sẵn (tái sử dụng flow).
///
/// Quick demo path: server tự động resolve bàn available + box available cho
/// reservation.GameId, không bắt player chọn bàn. Sau khi check-in thành công → mark token consumed.
/// </summary>
public class PlayerCheckInService : IPlayerCheckInService
{
    private readonly IPosCheckInTokenRepository _tokenRepository;
    private readonly IReservationRepository _reservationRepository;
    private readonly ILobbyRepository _lobbyRepository;
    private readonly ICafePosService _posService;
    private readonly BoardVerseDbContext _db;
    private readonly ILogger<PlayerCheckInService> _logger;
    private readonly ISystemConfigurationProvider _configProvider;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PlayerCheckInService(
        IPosCheckInTokenRepository tokenRepository,
        IReservationRepository reservationRepository,
        ILobbyRepository lobbyRepository,
        ICafePosService posService,
        BoardVerseDbContext db,
        ILogger<PlayerCheckInService> logger,
        ISystemConfigurationProvider configProvider = null!,
        IHttpContextAccessor httpContextAccessor = null!)
    {
        _tokenRepository = tokenRepository;
        _reservationRepository = reservationRepository;
        _lobbyRepository = lobbyRepository;
        _posService = posService;
        _db = db;
        _logger = logger;
        _configProvider = configProvider;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<PlayerScanTokenResponseDto> CheckInByTokenAsync(
        Guid playerUserId,
        PlayerScanTokenRequestDto request, CancellationToken cancellationToken = default)
    {
        var token = request.Token.Trim().ToUpperInvariant();

        var tokenEntity = await _tokenRepository.GetByTokenAsync(token);
        if (tokenEntity == null)
        {
            throw new NotFoundException(ApiErrorMessages.Reservation.PosTokenNotFound(token));
        }

        if (tokenEntity.IsRevoked)
        {
            throw new ConflictException(ApiErrorMessages.Reservation.PosTokenRevoked);
        }
        if (tokenEntity.ExpiresAt < DateTime.UtcNow)
        {
            throw new ConflictException(ApiErrorMessages.Reservation.PosTokenExpired);
        }
        if (tokenEntity.ConsumedAt.HasValue)
        {
            // Idempotent replay — nếu cùng user replay, trả về kết quả cũ.
            if (tokenEntity.ConsumedByUserId == playerUserId && tokenEntity.ResultActiveSessionId.HasValue)
            {
                _logger.LogInformation(
                    "PlayerCheckIn idempotent replay. Token={Token}, Player={PlayerUserId}, Session={SessionId}",
                    token, playerUserId, tokenEntity.ResultActiveSessionId);
                return new PlayerScanTokenResponseDto
                {
                    ActiveSessionId = tokenEntity.ResultActiveSessionId.Value,
                    ReservationId = tokenEntity.ReservationId ?? Guid.Empty,
                    CafeId = tokenEntity.CafeId,
                    CheckedInAt = tokenEntity.ConsumedAt.Value
                };
            }
            throw new ConflictException(ApiErrorMessages.Reservation.PosTokenAlreadyUsed);
        }

        if (!tokenEntity.ReservationId.HasValue)
        {
            throw new ConflictException(ApiErrorMessages.Reservation.PosTokenReservationMissing);
        }

        var reservation = tokenEntity.Reservation;
        if (reservation == null)
        {
            // Reservation đã bị xóa (FK set null) — token orphan.
            throw new NotFoundException(
                ApiErrorMessages.Reservation.ReservationNotFound(tokenEntity.ReservationId!.Value));
        }

        // CRITICAL Fix: Validate Reservation.Status before allowing check-in.
        // Token linked to cancelled/expired reservation must not trigger check-in.
        if (reservation.Status != ReservationStatus.Confirmed)
        {
            throw new ConflictException(
                ApiErrorMessages.Reservation.InvalidStatusForCheckIn(
                    reservation.Id, reservation.Status.ToString(), "Confirmed"));
        }

        // Validate player là Host hoặc member của lobby
        var isHost = reservation.HostId == playerUserId;
        var isMember = false;
        if (!isHost && reservation.LobbyId.HasValue)
        {
            var members = await _lobbyRepository.GetMembersAsync(reservation.LobbyId.Value);
            isMember = members.Any(m => m.UserId == playerUserId && m.IsActive);
        }
        if (!isHost && !isMember)
        {
            throw new ForbiddenException(
                ApiErrorMessages.Reservation.NotReservationMember(reservation.Id, playerUserId));
        }

        // Validate trong check-in window (BR-06: grace 30 phút sau scheduledTime).
        var now = DateTime.UtcNow;
        // windowStart = 1 giờ trước để player có thể scan sớm (linh hoạt).
        // windowEnd = 30 phút sau scheduledEndTime (grace period BR-06).
        var scheduledStart = reservation.ScheduledStartTime;
        if (scheduledStart == default)
            throw new InternalServerErrorException(
                ApiErrorMessages.ReservationExtension.CheckInMissingScheduledTime(reservation.Id));
        var scheduledEnd = reservation.ScheduledEndTime;
        if (scheduledEnd == default)
            throw new InternalServerErrorException(
                ApiErrorMessages.ReservationExtension.CheckInMissingScheduledEndTime(reservation.Id));
        var windowStart = scheduledStart.AddHours(-1);
        var windowEnd = scheduledEnd.AddMinutes(30);
        var bypassCheckInWindow = await TimeWindowGuard.ShouldBypassAsync(
            _httpContextAccessor?.HttpContext, _configProvider, _logger,
            operation: "PlayerCheckIn.Window", entityId: reservation.Id);
        if (!bypassCheckInWindow && (now < windowStart || now > windowEnd))
        {
            throw new ConflictException(
                ApiErrorMessages.Reservation.CheckInTimeWindowInvalid(
                    reservation.Id, scheduledStart, windowStart, windowEnd));
        }

        // Auto-resolve bàn available + box available cho reservation.GameId
        var table = await PickAvailableTableAsync(tokenEntity.CafeId);
        if (table == null)
        {
            var cafeName = tokenEntity.Cafe?.Name ?? tokenEntity.CafeId.ToString();
            throw new ConflictException(
                ApiErrorMessages.System.NoAvailableTablesForAutoCheckIn(cafeName));
        }

        var box = await PickAvailableBoxAsync(tokenEntity.CafeId, reservation.GameId);
        if (box == null)
        {
            throw new ConflictException(
                ApiErrorMessages.Pos.BoxNotAvailable(
                    "(tự động chọn)", CafeGameInventoryStatus.Available.ToString()));
        }

        // Build CheckInRequestDto rồi gọi POS service.
        // Dùng ReservationCode (8-char) thay vì token để tái sử dụng flow check-in có sẵn.
        var idempotencyKey = $"player-scan:{tokenEntity.Id}";
        var nonce = Guid.NewGuid().ToString("N");

        var checkInRequest = new CheckInRequestDto
        {
            Code = reservation.ReservationCode,
            CafeTableId = table.Id,
            Barcode = box.Barcode,
            IdempotencyKey = idempotencyKey,
            Nonce = nonce
        };

        var activeSession = await _posService.CheckInByCodeAsync(
            tokenEntity.CafeId,
            playerUserId,
            "Player",
            checkInRequest);

        // CRITICAL Fix: Atomic mark token consumed using ExecuteUpdateAsync with WHERE clause.
        // This prevents race condition where two concurrent requests both pass the
        // ConsumedAt == null check and both try to mark the token as consumed.
        var consumedAt = DateTime.UtcNow;
        var rowsAffected = await _db.PosCheckInTokens
            .Where(t => t.Id == tokenEntity.Id && t.ConsumedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(t => t.ConsumedAt, consumedAt)
                .SetProperty(t => t.ConsumedByUserId, playerUserId)
                .SetProperty(t => t.ResultActiveSessionId, activeSession.Id));

        if (rowsAffected == 0)
        {
            // Token was already consumed by another request (race condition prevented).
            // Re-fetch to return consistent data.
            var refreshedToken = await _tokenRepository.GetByIdAsync(tokenEntity.Id);
            if (refreshedToken?.ConsumedByUserId == playerUserId && refreshedToken.ResultActiveSessionId.HasValue)
            {
                // Same user replay — return idempotent response.
                return new PlayerScanTokenResponseDto
                {
                    ActiveSessionId = refreshedToken.ResultActiveSessionId.Value,
                    ReservationId = refreshedToken.ReservationId ?? Guid.Empty,
                    CafeId = refreshedToken.CafeId,
                    CheckedInAt = refreshedToken.ConsumedAt!.Value
                };
            }
            throw new ConflictException(ApiErrorMessages.Reservation.PosTokenAlreadyUsed);
        }

        _logger.LogInformation(
            "PlayerCheckIn consumed. Token={Token}, Player={PlayerUserId}, Session={SessionId}",
            token, playerUserId, activeSession.Id);

        return new PlayerScanTokenResponseDto
        {
            ActiveSessionId = activeSession.Id,
            ReservationId = reservation.Id,
            CafeId = tokenEntity.CafeId,
            CheckedInAt = consumedAt
        };
    }

    private async Task<CafeTable?> PickAvailableTableAsync(Guid cafeId)
    {
        return await _db.CafeTables
            .Where(t => t.CafeId == cafeId && t.Status == CafeTableStatus.Available)
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Name)
            .FirstOrDefaultAsync();
    }

    private async Task<CafeInventoryBox?> PickAvailableBoxAsync(Guid cafeId, Guid gameTemplateId)
    {
        return await _db.CafeInventoryBoxes
            .Include(b => b.CafeGameInventory)
            .Where(b => b.CafeGameInventory.CafeId == cafeId
                && b.CafeGameInventory.GameTemplateId == gameTemplateId
                && b.CafeGameInventory.IsActive
                && b.CafeGameInventory.Status == CafeGameInventoryStatus.Available
                && b.Status == CafeGameInventoryStatus.Available
                && b.IsActive)
            .OrderBy(b => b.CreatedAt)
            .FirstOrDefaultAsync();
    }
}