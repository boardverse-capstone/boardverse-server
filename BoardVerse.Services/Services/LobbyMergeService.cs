using BoardVerse.Core.DTOs.Lobby;
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
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

// LobbyMerge là nested class trong Lobby (cùng cấp với các member khác của Lobby)
// ApiErrorMessages.Lobby chứa LobbyMerge như nested class
using LobbyMergeErrors = BoardVerse.Core.Messages.ApiErrorMessages.Lobby.LobbyMerge;

// BR-DEMO-02: DemoGuard cho phép bypass BR-USER-LIMIT-02/03 khi demo mode bật.

namespace BoardVerse.Services.Services;

/// <summary>
/// Service xử lý ghép nhóm lobby (Lobby Merge).
/// Theo Exception Path §4 — boardverse-business-context.mdc.
///
/// Kịch bản: Nhóm A gồm A1, A2, A3, A4 đang chơi. A1, A2 về sớm.
/// A3 muốn chuyển sang Nhóm B (đang active tại quán).
/// Staff quét mã A3 → tạo LobbyMergeRequest → duyệt → A3 được ghép vào Nhóm B.
///
/// Ambient transaction pattern (BR-REQUIRED §17.5):
/// Kiểm tra CurrentTransaction trước khi BeginTransactionAsync để tránh
/// InvalidOperationException khi được gọi từ method đã có transaction.
///
/// BR-REQUIRED §17.4: Concurrency control — FOR UPDATE lock trên ActiveSession.
/// BR-REQUIRED §17.1: Idempotency key cho CreateMergeRequestAsync.
/// BR-REQUIRED §17.6: Append-only audit log (LobbyMergeAuditLog).
/// G8: Seat availability check — không cho ghép nếu quán không đủ chỗ.
/// BR-USER-LIMIT-02: Schedule overlap check khi ghép member.
/// BR-USER-LIMIT-03: Cap heldBalance validation.
/// Deposit handling: Captured deposit không cho ghép.
/// Staff permission: chỉ staff/manager mới duyệt/từ chối.
/// </summary>
public class LobbyMergeService : ILobbyMergeService
{
    private const int MergeRequestExpiryMinutes = 15;

    // BR-USER-LIMIT-03 caps (BVC)
    private const long CapRegularUser = 500_000;
    private const long CapVipUser = 1_000_000;
    private const long CapHighRiskUser = 200_000;

    private readonly BoardVerseDbContext _db;
    private readonly ILobbyRepository _lobbyRepository;
    private readonly ILobbyMemberRepository _lobbyMemberRepository;
    private readonly ICafeRepository _cafeRepository;
    private readonly IUserManagementRepository _userRepository;
    private readonly IWalletRepository _walletRepository;
    private readonly IBookingDepositRepository _depositRepository;
    private readonly ISeatInventoryRepository _seatInventoryRepository;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ISystemConfigurationProvider _configProvider;
    private readonly ILogger<LobbyMergeService> _logger;
    private readonly ILobbyHubService _lobbyHubService;
    private readonly ILobbyInviteRepository _lobbyInviteRepository;

    public LobbyMergeService(
        BoardVerseDbContext db,
        ILobbyRepository lobbyRepository,
        ILobbyMemberRepository lobbyMemberRepository,
        ICafeRepository cafeRepository,
        IUserManagementRepository userRepository,
        IWalletRepository walletRepository,
        IBookingDepositRepository depositRepository,
        ISeatInventoryRepository seatInventoryRepository,
        IHttpContextAccessor httpContextAccessor,
        ISystemConfigurationProvider configProvider,
        ILogger<LobbyMergeService> logger,
        ILobbyHubService lobbyHubService,
        ILobbyInviteRepository lobbyInviteRepository)
    {
        _db = db;
        _lobbyRepository = lobbyRepository;
        _lobbyMemberRepository = lobbyMemberRepository;
        _cafeRepository = cafeRepository;
        _userRepository = userRepository;
        _walletRepository = walletRepository;
        _depositRepository = depositRepository;
        _seatInventoryRepository = seatInventoryRepository;
        _httpContextAccessor = httpContextAccessor;
        _configProvider = configProvider;
        _logger = logger;
        _lobbyHubService = lobbyHubService;
        _lobbyInviteRepository = lobbyInviteRepository;
    }

    public async Task<LobbyMergeRequestDto> CreateMergeRequestAsync(
        Guid cafeId,
        Guid staffUserId,
        CreateLobbyMergeRequestDto dto,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate cafe tồn tại
        var cafe = await _cafeRepository.GetActiveByIdAsync(cafeId, cancellationToken)
            ?? throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));

        // 2. Staff permission check — G8: staff chỉ thao tác tại quán của mình
        var isStaff = await _cafeRepository.IsManagerOrStaffAsync(cafeId, staffUserId, cancellationToken);
        if (!isStaff)
            throw new ForbiddenException(LobbyMergeErrors.StaffPermissionDenied);

        // 3. Validate source lobby tồn tại
        var sourceLobby = await _lobbyRepository.GetByIdAsync(dto.SourceLobbyId, cancellationToken)
            ?? throw new NotFoundException(ApiErrorMessages.Lobby.NotFound(dto.SourceLobbyId));

        // 4. Validate target lobby tồn tại và đang active (InProgress hoặc Viable)
        var targetLobby = await _lobbyRepository.GetByIdAsync(dto.TargetLobbyId, cancellationToken)
            ?? throw new NotFoundException(ApiErrorMessages.Lobby.NotFound(dto.TargetLobbyId));

        if (targetLobby.Status != LobbyStatus.InProgress && targetLobby.Status != LobbyStatus.Viable)
            throw new ConflictException(LobbyMergeErrors.TargetLobbyNotActive);

        // === Gap #1: Không cho gộp chính mình ===
        if (sourceLobby.Id == targetLobby.Id)
            throw new ConflictException(LobbyMergeErrors.SameLobbyMerge);

        // === Gap #2: Lobby nguồn phải ở trạng thái hợp lệ để merge ===
        var validSourceStatuses = new[]
        {
            LobbyStatus.Open, LobbyStatus.Viable, LobbyStatus.Full,
            LobbyStatus.InProgress, LobbyStatus.PendingActivation
        };
        if (!validSourceStatuses.Contains(sourceLobby.Status))
            throw new ConflictException(LobbyMergeErrors.SourceLobbyNotValidForMerge);

        // 5. Validate cùng cafe và cùng game
        if (sourceLobby.ReservationId.HasValue && targetLobby.ReservationId.HasValue)
        {
            var sourceReservation = await _db.Reservations
                .AsNoTracking()
                .Where(r => r.Id == sourceLobby.ReservationId)
                .Select(r => new { r.CafeId, r.GameId })
                .FirstOrDefaultAsync(cancellationToken);

            var targetReservation = await _db.Reservations
                .AsNoTracking()
                .Where(r => r.Id == targetLobby.ReservationId)
                .Select(r => new { r.CafeId, r.GameId })
                .FirstOrDefaultAsync(cancellationToken);

            if (sourceReservation == null || targetReservation == null)
            {
                // Walk-in session (không qua reservation) → kiểm tra game qua lobby entity nếu có
            }
            else
            {
                if (sourceReservation.CafeId != targetReservation.CafeId)
                    throw new BadRequestException(LobbyMergeErrors.MergeCannotCrossCafes);

                if (sourceReservation.GameId != targetReservation.GameId)
                    throw new BadRequestException(LobbyMergeErrors.MergeDifferentGames);
            }
        }

        // 6. Validate idempotency key — check FIRST so retries return the same result
        if (!string.IsNullOrWhiteSpace(dto.IdempotencyKey))
        {
            var existingByKey = await _db.LobbyMergeRequests
                .AnyAsync(r => r.IdempotencyKey == dto.IdempotencyKey, cancellationToken);

            if (existingByKey)
            {
                var existing = await _db.LobbyMergeRequests
                    .AsNoTracking()
                    .FirstAsync(r => r.IdempotencyKey == dto.IdempotencyKey, cancellationToken);

                return MapToDto(existing);
            }
        }

        // 7. Validate không có yêu cầu Pending nào giữa 2 lobby này
        var existingPending = await _db.LobbyMergeRequests
            .AnyAsync(r =>
                r.Status == LobbyMergeRequestStatus.Pending &&
                ((r.SourceLobbyId == dto.SourceLobbyId && r.TargetLobbyId == dto.TargetLobbyId) ||
                 (r.SourceLobbyId == dto.TargetLobbyId && r.TargetLobbyId == dto.SourceLobbyId)),
                cancellationToken);

        if (existingPending)
            throw new ConflictException(LobbyMergeErrors.MergeRequestAlreadyPending);

        // 9. Đếm member count của source lobby
        var sourceMembers = await _lobbyMemberRepository.GetByLobbyAsync(dto.SourceLobbyId, cancellationToken);
        var activeMembers = sourceMembers.Where(m => m.IsActive && m.Status == LobbyMemberStatus.Ready).ToList();

        // 10. Tính combinedCount, seatCapacity, fitsCapacity
        var targetMembers = await _lobbyMemberRepository.GetByLobbyAsync(dto.TargetLobbyId, cancellationToken);
        var targetActiveMembers = targetMembers.Count(m => m.IsActive && m.Status == LobbyMemberStatus.Ready);
        var combinedCount = targetActiveMembers + activeMembers.Count;

        // Lấy seat capacity từ SeatInventory của target reservation
        int seatCapacity = int.MaxValue; // fallback: không giới hạn nếu không tìm thấy
        if (targetLobby.ReservationId.HasValue)
        {
            var targetRes = await _db.Reservations
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == targetLobby.ReservationId, cancellationToken);

            if (targetRes != null)
            {
                var seatInv = await _seatInventoryRepository.GetForUpdateAsync(
                    cafeId,
                    targetRes.PlayDate,
                    targetRes.PreferredStartTime ?? new TimeOnly(0),
                    targetRes.PreferredEndTime ?? new TimeOnly(23, 59),
                    cancellationToken);

                if (seatInv != null)
                    seatCapacity = seatInv.TotalSeats;
            }
        }

        var fitsCapacity = seatCapacity >= combinedCount;

        var request = new LobbyMergeRequest
        {
            Id = Guid.NewGuid(),
            SourceLobbyId = dto.SourceLobbyId,
            TargetLobbyId = dto.TargetLobbyId,
            RequestedByUserId = staffUserId,
            Status = LobbyMergeRequestStatus.Pending,
            SourceMembersCount = sourceMembers.Count,
            SourceActiveMembersAtRequest = activeMembers.Count,
            Reason = dto.Reason,
            ExpiresAt = DateTime.UtcNow.AddMinutes(MergeRequestExpiryMinutes),
            IdempotencyKey = dto.IdempotencyKey,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CombinedCount = combinedCount,
            SeatCapacity = seatCapacity == int.MaxValue ? 0 : seatCapacity,
            FitsCapacity = fitsCapacity
        };

        _db.LobbyMergeRequests.Add(request);

        // Gap #3: Dùng helper CreateAuditLog (có reservation IDs) thay vì tạo trực tiếp
        var mergeRequestAudit = CreateAuditLog(
            request.Id,
            sourceLobby.Id,
            targetLobby.Id,
            sourceLobby.ReservationId,
            targetLobby.ReservationId,
            staffUserId,
            "MergeRequested",
            new
            {
                sourceMembersCount = sourceMembers.Count,
                activeMembersAtRequest = activeMembers.Count,
                reason = dto.Reason
            },
            success: true);
        _db.LobbyMergeAuditLogs.Add(mergeRequestAudit);

        await _db.SaveChangesAsync(cancellationToken);

        await _db.Entry(request).Reference(r => r.SourceLobby).LoadAsync(cancellationToken);
        await _db.Entry(request).Reference(r => r.TargetLobby).LoadAsync(cancellationToken);
        await _db.Entry(request).Reference(r => r.RequestedByUser).LoadAsync(cancellationToken);

        _logger.LogInformation(
            "LobbyMergeRequest created: {RequestId} | Source={SourceLobbyId} | Target={TargetLobbyId} | By={UserId}",
            request.Id, dto.SourceLobbyId, dto.TargetLobbyId, staffUserId);

        return MapToDto(request);
    }

    public async Task<LobbyMergeApprovedDto> ApproveMergeAsync(
        Guid cafeId,
        Guid staffUserId,
        Guid requestId,
        string? reviewNote = null,
        CancellationToken cancellationToken = default)
    {
        // Ambient transaction pattern — BR-REQUIRED §17.5
        var ambientTx = _db.Database.CurrentTransaction;
        IDbContextTransaction? ownedTx = null;
        if (ambientTx == null)
        {
            ownedTx = await _db.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable, cancellationToken);
        }

        try
        {
            // ===== Step 1: Staff permission =====
            var isStaff = await _cafeRepository.IsManagerOrStaffAsync(cafeId, staffUserId, cancellationToken);
            if (!isStaff)
                throw new ForbiddenException(LobbyMergeErrors.StaffPermissionDenied);

            // ===== Step 2: Load merge request with FOR UPDATE lock =====
            // Lock merge request row to prevent concurrent approval
            var mergeRequest = await LoadMergeRequestWithLockAsync(requestId, cancellationToken);

            if (mergeRequest == null)
                throw new NotFoundException(LobbyMergeErrors.MergeRequestNotFound(requestId));

            if (mergeRequest.Status != LobbyMergeRequestStatus.Pending)
                throw new ConflictException(LobbyMergeErrors.MergeRequestNotPending);

            if (mergeRequest.ExpiresAt < DateTime.UtcNow)
                throw new ConflictException(LobbyMergeErrors.MergeRequestExpired);

            var sourceLobby = mergeRequest.SourceLobby;
            var targetLobby = mergeRequest.TargetLobby;

            // ===== Step 3: Validate target lobby vẫn active =====
            if (targetLobby.Status != LobbyStatus.InProgress && targetLobby.Status != LobbyStatus.Viable)
                throw new ConflictException(LobbyMergeErrors.TargetLobbyNotActive);

            // ===== Step 4: Load target ActiveSession với FOR UPDATE — BR-REQUIRED §17.4 =====
            var targetSession = await _db.ActiveSessions
                .FromSqlRaw(
                    "SELECT * FROM \"ActiveSessions\" WHERE \"LobbyId\" = {0} AND \"Status\" = {1} FOR UPDATE",
                    targetLobby.Id, (int)GroupSessionStatus.Active)
                .Include(s => s.Members)
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new NotFoundException(LobbyMergeErrors.TargetSessionNotFound);

            // ===== Step 5: G8 — Seat availability check =====
            // Kiểm tra AvailableSeats >= số member cần ghép trước khi thực hiện
            var activeMembers = sourceLobby.Members
                .Where(m => m.IsActive && m.Status == LobbyMemberStatus.Ready)
                .ToList();

            if (activeMembers.Count > 0)
            {
                // Lấy SeatInventory với FOR UPDATE để tránh race condition
                var (sourceReservation, targetReservation) = await LoadReservationsAsync(
                    sourceLobby, targetLobby, cancellationToken);

                // Gap #2: Walk-in session (targetReservation == null)
                // → dùng thời gian session hiện tại làm proxy cho SeatInventory query
                DateOnly seatPlayDate;
                TimeOnly seatStartTime;
                TimeOnly seatEndTime;
                if (targetReservation != null)
                {
                    seatPlayDate = targetReservation.PlayDate;
                    seatStartTime = targetReservation.PreferredStartTime ?? new TimeOnly(0);
                    seatEndTime = targetReservation.PreferredEndTime ?? new TimeOnly(23, 59);
                }
                else
                {
                    seatPlayDate = DateOnly.FromDateTime(targetSession.StartedAt);
                    seatStartTime = TimeOnly.FromDateTime(targetSession.StartedAt);
                    seatEndTime = targetSession.EndedAt.HasValue
                        ? TimeOnly.FromDateTime(targetSession.EndedAt.Value)
                        : new TimeOnly(23, 59);
                }

                var seatInventory = await _seatInventoryRepository.GetForUpdateAsync(
                    cafeId, seatPlayDate, seatStartTime, seatEndTime, cancellationToken);

                if (seatInventory != null && seatInventory.AvailableSeats < activeMembers.Count)
                {
                    _logger.LogWarning(
                        "Seat not available for merge: RequestId={RequestId} | Available={Available} | Required={Required}",
                        requestId, seatInventory.AvailableSeats, activeMembers.Count);

                    throw new ConflictException(
                        LobbyMergeErrors.SeatNotAvailableForMerge);
                }
                else if (seatInventory == null)
                {
                    // Walk-in: không tìm thấy SeatInventory → không block merge
                    // nhưng log warning để staff biết seat check không áp dụng
                    _logger.LogWarning(
                        "SeatInventory not found for walk-in merge: RequestId={RequestId} | CafeId={CafeId} | Date={PlayDate}",
                        requestId, cafeId, seatPlayDate);
                }
            }

            // ===== Step 6: BR-USER-LIMIT-02 — Schedule overlap check =====
            // BR-DEMO-02: Demo mode bypasses BR-USER-LIMIT-02 (schedule overlap).
            var bypassDemo = await DemoGuard.ShouldBypassDemoLocksAsync(
                _httpContextAccessor?.HttpContext, _configProvider, _logger,
                operation: "LobbyMerge.ApproveMerge", entityId: requestId, ct: cancellationToken);

            if (!bypassDemo)
            {
                foreach (var member in activeMembers)
                {
                    var targetStart = targetSession.StartedAt;
                    var targetEnd = targetSession.EndedAt ?? DateTime.UtcNow.AddHours(4); // Estimate max 4h
                    var targetPlayDate = DateOnly.FromDateTime(targetStart);
                    var targetStartTime = TimeOnly.FromDateTime(targetStart);
                    var targetEndTime = TimeOnly.FromDateTime(targetEnd);

                    // Kiểm tra member có lobby overlap (exclude lobby hiện tại của họ = sourceLobby)
                    var overlapping = await _lobbyRepository.GetOverlappingLobbiesAsync(
                        member.UserId,
                        targetPlayDate,
                        targetStartTime,
                        targetEndTime,
                        DateTime.UtcNow,
                        cancellationToken);

                    // Loại trừ lobby nguồn (member đang rời) và lobby đích (đang ghép vào)
                    var hasConflict = overlapping
                        .Where(l => l.Id != sourceLobby.Id && l.Id != targetLobby.Id)
                        .Any();

                    if (hasConflict)
                    {
                        _logger.LogWarning(
                            "Schedule overlap for member {MemberId} in merge request {RequestId}",
                            member.UserId, requestId);

                        throw new ConflictException(
                            LobbyMergeErrors.MemberHasScheduleConflict);
                    }
                }
            }

            // ===== Step 7: BR-USER-LIMIT-03 — Cap heldBalance validation =====
            // BR-DEMO-02: Demo mode bypasses BR-USER-LIMIT-03 (deposit cap).
            if (!bypassDemo)
            {
                foreach (var member in activeMembers.Where(m => m.UserId != Guid.Empty))
                {
                    var wallet = await _walletRepository.GetByUserIdForUpdateAsync(
                        member.UserId, cancellationToken);

                    if (wallet != null)
                    {
                        var cap = wallet.AccountStatus switch
                        {
                            AccountStatus.Restricted => CapHighRiskUser,
                            AccountStatus.Suspended => CapHighRiskUser,
                            AccountStatus.Banned => CapHighRiskUser,
                            _ => CapRegularUser
                        };

                        if (wallet.TotalActiveDeposit > cap)
                        {
                            var user = await _userRepository.GetByIdAsync(member.UserId, cancellationToken);
                            var displayName = user?.Username ?? member.UserId.ToString();

                            throw new ConflictException(
                                LobbyMergeErrors.MemberDepositCapExceeded(
                                    displayName, wallet.AvailableBalance, wallet.HeldBalance));
                        }
                    }
                }
            }

            // ===== Step 8: Deposit status check =====
            // Kiểm tra deposit của source reservation chưa bị captured/forfeited
            // và capture status để ghi vào ActiveSessionLobbySource audit trail
            BookingDepositStatus? depositStatusAtMerge = null;
            if (sourceLobby.ReservationId.HasValue)
            {
                var sourceReservation = await _db.Reservations
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.Id == sourceLobby.ReservationId, cancellationToken);

                if (sourceReservation != null)
                {
                    // Check: reservation đã bị absorbed chưa
                    if (sourceReservation.SourceDissolved)
                        throw new ConflictException(
                            LobbyMergeErrors.ReservationAlreadyAbsorbed);

                    // Kiểm tra deposit đã Paid hoặc Forfeited → không cho merge
                    var deposit = await _depositRepository.GetByBookingIdAsync(
                        sourceReservation.Id, cancellationToken);

                    depositStatusAtMerge = deposit?.Status;

                    if (deposit != null &&
                        (deposit.Status == BookingDepositStatus.Paid ||
                         deposit.Status == BookingDepositStatus.Forfeited))
                    {
                        throw new ConflictException(
                            LobbyMergeErrors.SourceDepositAlreadyCaptured);
                    }
                }
            }

            // ===== Step 9: Không có member nào active → từ chối nhẹ =====
            if (activeMembers.Count == 0)
            {
                mergeRequest.Status = LobbyMergeRequestStatus.Rejected;
                mergeRequest.ReviewedByUserId = staffUserId;
                mergeRequest.ReviewedAt = DateTime.UtcNow;
                mergeRequest.ReviewNote = "No active members in source lobby to transfer.";
                mergeRequest.UpdatedAt = DateTime.UtcNow;

                var rejectAudit = CreateAuditLog(
                    mergeRequest.Id, sourceLobby.Id, targetLobby.Id,
                    sourceLobby.ReservationId, targetLobby.ReservationId,
                    staffUserId, "MergeRejected", new { reason = "NoActiveMembers", membersCount = 0 },
                    success: false, errorMessage: "No active members in source lobby.");
                _db.LobbyMergeAuditLogs.Add(rejectAudit);

                await _db.SaveChangesAsync(cancellationToken);
                if (ownedTx != null) await ownedTx.CommitAsync(cancellationToken);

                return new LobbyMergeApprovedDto
                {
                    MergeRequestId = mergeRequest.Id,
                    SourceLobbyId = sourceLobby.Id,
                    TargetLobbyId = targetLobby.Id,
                    MembersTransferred = 0,
                    TargetActiveSessionId = targetSession.Id,
                    IdempotencyKey = mergeRequest.IdempotencyKey
                };
            }

            // ===== Step 10: Transfer từng member =====
            // Gap #1/#5: track ActiveSessionLobbySource đã tạo để update SourceDissolved sau dissolve
            var createdSources = new List<ActiveSessionLobbySource>();

            foreach (var member in activeMembers)
            {
                // Audit log trước khi thay đổi
                var transferAudit = CreateAuditLog(
                    mergeRequest.Id, sourceLobby.Id, targetLobby.Id,
                    sourceLobby.ReservationId, targetLobby.ReservationId,
                    staffUserId, "MemberTransferred",
                    new
                    {
                        memberId = member.Id,
                        userId = member.UserId,
                        previousLobbyId = sourceLobby.Id,
                        newLobbyId = targetLobby.Id,
                        transferredAt = DateTime.UtcNow
                    },
                    success: true);
                _db.LobbyMergeAuditLogs.Add(transferAudit);

                // Update LobbyMember
                member.LobbyId = targetLobby.Id;
                member.LeftAt = DateTime.UtcNow;
                member.LeftReason = LeftReason.MergedIntoAnotherLobby;
                member.Status = LobbyMemberStatus.Left;
                member.PreviousLobbyId = sourceLobby.Id;
                // PreviousReservationId: link về reservation gốc của source lobby
                if (sourceLobby.ReservationId.HasValue)
                    member.PreviousReservationId = sourceLobby.ReservationId;

                // Chuyển ActiveSessionMember từ source session sang target session (nếu có)
                var memberSession = await _db.ActiveSessionMembers
                    .FirstOrDefaultAsync(m =>
                        m.UserId == member.UserId &&
                        m.OriginalSessionId == sourceLobby.Id &&
                        m.Status == IndividualSessionStatus.Playing,
                        cancellationToken);

                if (memberSession != null)
                {
                    memberSession.OriginalSessionId = targetSession.Id;
                    memberSession.MergedFromLobbyId = sourceLobby.Id;
                    memberSession.MergedAt = DateTime.UtcNow;
                }

                // Insert ActiveSessionLobbySource cho mỗi member được ghép
                var source = new ActiveSessionLobbySource
                {
                    Id = Guid.NewGuid(),
                    ActiveSessionId = targetSession.Id,
                    LobbyId = sourceLobby.Id,
                    ReservationId = sourceLobby.ReservationId,
                    MergedByUserId = staffUserId,
                    MergedAt = DateTime.UtcNow,
                    SourceDissolved = false,
                    DepositStatusAtMerge = depositStatusAtMerge ?? BookingDepositStatus.Pending,
                    CreatedAt = DateTime.UtcNow
                };
                _db.ActiveSessionLobbySources.Add(source);
                createdSources.Add(source);
            }

            // ===== Step 11: Dissolve source lobby nếu không còn member =====
            var remainingMembers = await _lobbyMemberRepository.GetByLobbyAsync(
                sourceLobby.Id, cancellationToken);
            var stillActive = remainingMembers.Count(m => m.IsActive && m.Status == LobbyMemberStatus.Ready);

            if (stillActive == 0)
            {
                sourceLobby.Status = LobbyStatus.Closed;
                sourceLobby.UpdatedAt = DateTime.UtcNow;

                if (sourceLobby.ReservationId.HasValue)
                {
                    var reservation = await _db.Reservations
                        .FirstOrDefaultAsync(r => r.Id == sourceLobby.ReservationId, cancellationToken);
                    if (reservation != null)
                    {
                        reservation.Status = ReservationStatus.CancelledByPlayer;
                        reservation.SourceDissolved = true;
                        reservation.MergedIntoReservationId = targetLobby.ReservationId;
                        reservation.MergedAt = DateTime.UtcNow;
                        reservation.MergedByUserId = staffUserId;
                        reservation.UpdatedAt = DateTime.UtcNow;
                    }
                }

                var dissolveAudit = CreateAuditLog(
                    mergeRequest.Id, sourceLobby.Id, targetLobby.Id,
                    sourceLobby.ReservationId, targetLobby.ReservationId,
                    staffUserId, "SourceLobbyDissolved",
                    new
                    {
                        previousMembersCount = activeMembers.Count,
                        dissolvedAt = DateTime.UtcNow
                    },
                    success: true);
                _db.LobbyMergeAuditLogs.Add(dissolveAudit);

                // Gap #1/#5: Update SourceDissolved = true trên tất cả ActiveSessionLobbySource
                // đã tạo cho các member vừa được chuyển từ lobby nguồn
                foreach (var src in createdSources)
                {
                    src.SourceDissolved = true;
                    src.SourceDissolvedAt = DateTime.UtcNow;
                }
            }

            // ===== Step 12: Cập nhật merge request =====
            mergeRequest.Status = LobbyMergeRequestStatus.Approved;
            mergeRequest.ReviewedByUserId = staffUserId;
            mergeRequest.ReviewedAt = DateTime.UtcNow;
            mergeRequest.ReviewNote = reviewNote;
            mergeRequest.UpdatedAt = DateTime.UtcNow;

            var approvedAudit = CreateAuditLog(
                mergeRequest.Id, sourceLobby.Id, targetLobby.Id,
                sourceLobby.ReservationId, targetLobby.ReservationId,
                staffUserId, "MergeApproved",
                new
                {
                    membersTransferred = activeMembers.Count,
                    sourceDissolved = stillActive == 0,
                    reviewNote
                },
                success: true);
            _db.LobbyMergeAuditLogs.Add(approvedAudit);

            await _db.SaveChangesAsync(cancellationToken);

            if (ownedTx != null) await ownedTx.CommitAsync(cancellationToken);

            // === Phase 4: SignalR notifications + invite expiry (G19, G20, G21) ===
            try
            {
                // G21: Expire pending invites cho source lobby và các member vừa được ghép
                // BR-LOBBY-INVITE-09: Lobby terminal → tất cả pending invite chuyển Expired ngay
                var transferredUserIds = activeMembers.Select(m => m.UserId).ToList();
                await _lobbyInviteRepository.ExpirePendingForUsersAsync(
                    lobbyId: sourceLobby.Id,
                    inviteeIds: transferredUserIds,
                    cancellationToken);

                // G19: Notify source lobby (Nhóm A) — members biết lobby đã bị absorbed
                await _lobbyHubService.NotifyLobbyMergedInto(
                    sourceLobby.Id, targetLobby.Id, mergeRequest.Id, activeMembers.Count);

                // G20: Notify target lobby (Nhóm B) — thông báo member mới từ merge
                if (activeMembers.Count > 0)
                {
                    await _lobbyHubService.NotifyMemberJoinedFromMerge(
                        targetLobby.Id, transferredUserIds, sourceLobby.Id, mergeRequest.Id);
                }
            }
            catch (Exception ex)
            {
                // Fire-and-forget: merge đã commit, notification lỗi không làm rollback
                _logger.LogError(ex,
                    "Phase 4 notification failed after merge approval: RequestId={RequestId}",
                    requestId);
            }

            _logger.LogInformation(
                "LobbyMerge approved: RequestId={RequestId} | Source={SourceLobbyId} | Target={TargetLobbyId} | MembersTransferred={Count}",
                requestId, sourceLobby.Id, targetLobby.Id, activeMembers.Count);

            return new LobbyMergeApprovedDto
            {
                MergeRequestId = mergeRequest.Id,
                SourceLobbyId = sourceLobby.Id,
                TargetLobbyId = targetLobby.Id,
                MembersTransferred = activeMembers.Count,
                TargetActiveSessionId = targetSession.Id,
                IdempotencyKey = mergeRequest.IdempotencyKey
            };
        }
        catch
        {
            if (ownedTx != null) await ownedTx.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (ownedTx != null) await ownedTx.DisposeAsync();
        }
    }

    public async Task<LobbyMergeRequestDto> RejectMergeAsync(
        Guid cafeId,
        Guid staffUserId,
        Guid requestId,
        ReviewLobbyMergeRequestDto dto,
        CancellationToken cancellationToken = default)
    {
        // Staff permission check
        var isStaff = await _cafeRepository.IsManagerOrStaffAsync(cafeId, staffUserId, cancellationToken);
        if (!isStaff)
            throw new ForbiddenException(LobbyMergeErrors.StaffPermissionDenied);

        var request = await _db.LobbyMergeRequests
            .Include(r => r.SourceLobby)
            .Include(r => r.TargetLobby)
            .Include(r => r.RequestedByUser)
            .FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken)
            ?? throw new NotFoundException(LobbyMergeErrors.MergeRequestNotFound(requestId));

        if (request.Status != LobbyMergeRequestStatus.Pending)
            throw new ConflictException(LobbyMergeErrors.MergeRequestNotPending);

        request.Status = LobbyMergeRequestStatus.Rejected;
        request.ReviewedByUserId = staffUserId;
        request.ReviewedAt = DateTime.UtcNow;
        request.ReviewNote = dto.ReviewNote;
        request.UpdatedAt = DateTime.UtcNow;

        // Gap #3: Resolve reservation IDs từ navigation properties đã loaded
        var sourceReservationId = request.SourceLobby?.ReservationId;
        var targetReservationId = request.TargetLobby?.ReservationId;

        var audit = CreateAuditLog(
            request.Id, request.SourceLobbyId, request.TargetLobbyId,
            sourceReservationId, targetReservationId,
            staffUserId, "MergeRejected",
            new { reviewNote = dto.ReviewNote },
            success: true);
        _db.LobbyMergeAuditLogs.Add(audit);

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "LobbyMerge rejected: RequestId={RequestId} | By={UserId}",
            requestId, staffUserId);

        return MapToDto(request);
    }

    public async Task<LobbyMergeRequestDto?> GetMergeRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        var request = await _db.LobbyMergeRequests
            .Include(r => r.SourceLobby)
            .Include(r => r.TargetLobby)
            .Include(r => r.RequestedByUser)
            .Include(r => r.ReviewedByUser)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);

        return request == null ? null : MapToDto(request);
    }

    public async Task<IReadOnlyList<LobbyMergeRequestDto>> GetPendingRequestsAsync(
        Guid cafeId,
        CancellationToken cancellationToken = default)
    {
        var requests = await _db.LobbyMergeRequests
            .Include(r => r.SourceLobby)
            .Include(r => r.TargetLobby)
            .Include(r => r.RequestedByUser)
            .Where(r =>
                r.Status == LobbyMergeRequestStatus.Pending &&
                (r.SourceLobby.CafeId == cafeId || r.TargetLobby.CafeId == cafeId))
            .OrderBy(r => r.CreatedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return requests.Select(MapToDto).ToList();
    }

    public async Task<IReadOnlyList<LobbyMergeRequestDto>> GetRequestsByLobbyAsync(
        Guid lobbyId,
        CancellationToken cancellationToken = default)
    {
        var requests = await _db.LobbyMergeRequests
            .Include(r => r.SourceLobby)
            .Include(r => r.TargetLobby)
            .Include(r => r.RequestedByUser)
            .Where(r => r.SourceLobbyId == lobbyId || r.TargetLobbyId == lobbyId)
            .OrderByDescending(r => r.CreatedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return requests.Select(MapToDto).ToList();
    }

    public async Task<IReadOnlyList<LobbyMergeAuditLogDto>> GetLobbyMergeHistoryAsync(
        Guid lobbyId,
        CancellationToken cancellationToken = default)
    {
        var logs = await _db.LobbyMergeAuditLogs
            .Include(l => l.PerformedByUser)
            .Where(l => l.SourceLobbyId == lobbyId || l.TargetLobbyId == lobbyId)
            .OrderByDescending(l => l.CreatedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return logs.Select(MapAuditToDto).ToList();
    }

    public async Task<LobbyMergeRequestDto> CancelMergeRequestAsync(
        Guid cafeId,
        Guid userId,
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        var request = await _db.LobbyMergeRequests
            .Include(r => r.SourceLobby)
            .Include(r => r.TargetLobby)
            .Include(r => r.RequestedByUser)
            .FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken)
            ?? throw new NotFoundException(LobbyMergeErrors.MergeRequestNotFound(requestId));

        if (request.Status != LobbyMergeRequestStatus.Pending)
            throw new ConflictException(LobbyMergeErrors.MergeRequestNotPending);

        request.Status = LobbyMergeRequestStatus.Cancelled;
        request.ReviewedByUserId = userId;
        request.ReviewedAt = DateTime.UtcNow;
        request.UpdatedAt = DateTime.UtcNow;

        // Gap #3: Resolve reservation IDs từ navigation properties đã loaded
        var sourceReservationId = request.SourceLobby?.ReservationId;
        var targetReservationId = request.TargetLobby?.ReservationId;

        var audit = CreateAuditLog(
            request.Id, request.SourceLobbyId, request.TargetLobbyId,
            sourceReservationId, targetReservationId,
            userId, "MergeCancelled",
            new { cancelledBy = userId.ToString() },
            success: true);
        _db.LobbyMergeAuditLogs.Add(audit);

        await _db.SaveChangesAsync(cancellationToken);

        return MapToDto(request);
    }

    public async Task<int> ExpireOverdueRequestsAsync(CancellationToken cancellationToken = default)
    {
        var overdue = await _db.LobbyMergeRequests
            .Include(r => r.SourceLobby)
            .Include(r => r.TargetLobby)
            .Where(r =>
                r.Status == LobbyMergeRequestStatus.Pending &&
                r.ExpiresAt < DateTime.UtcNow)
            .ToListAsync(cancellationToken);

        foreach (var request in overdue)
        {
            request.Status = LobbyMergeRequestStatus.Expired;
            request.UpdatedAt = DateTime.UtcNow;

            // Gap #3: Resolve reservation IDs từ navigation properties
            var sourceReservationId = request.SourceLobby?.ReservationId;
            var targetReservationId = request.TargetLobby?.ReservationId;

            var audit = CreateAuditLog(
                request.Id, request.SourceLobbyId, request.TargetLobbyId,
                sourceReservationId, targetReservationId,
                Guid.Empty, "MergeExpired",
                new { expiredAt = DateTime.UtcNow, expiresAt = request.ExpiresAt },
                success: true);
            _db.LobbyMergeAuditLogs.Add(audit);
        }

        if (overdue.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Expired {Count} overdue lobby merge requests.", overdue.Count);
        }

        return overdue.Count;
    }

    // ===== Protected virtual — overridable trong test subclass =====

    /// <summary>
    /// Load LobbyMergeRequest với row-level lock (FOR UPDATE).
    /// Protected virtual để unit test subclass có thể override và trả về test data
    /// mà không cần InMemory DB hỗ trợ FromSqlRaw.
    /// </summary>
    protected virtual async Task<LobbyMergeRequest?> LoadMergeRequestWithLockAsync(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        return await _db.LobbyMergeRequests
            .FromSqlRaw(
                "SELECT * FROM \"LobbyMergeRequests\" WHERE \"Id\" = {0} FOR UPDATE",
                requestId)
            .Include(r => r.SourceLobby)
                .ThenInclude(l => l.Members)
            .Include(r => r.TargetLobby)
            .FirstOrDefaultAsync(cancellationToken);
    }

    // ===== Private helpers =====

    /// <summary>
    /// Load reservations của source và target lobby (dùng cho seat availability check).
    /// </summary>
    private async Task<(Reservation? Source, Reservation? Target)> LoadReservationsAsync(
        Lobby sourceLobby,
        Lobby targetLobby,
        CancellationToken cancellationToken)
    {
        Reservation? sourceRes = null;
        Reservation? targetRes = null;

        if (sourceLobby.ReservationId.HasValue)
        {
            sourceRes = await _db.Reservations
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == sourceLobby.ReservationId, cancellationToken);
        }

        if (targetLobby.ReservationId.HasValue)
        {
            targetRes = await _db.Reservations
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == targetLobby.ReservationId, cancellationToken);
        }

        return (sourceRes, targetRes);
    }

    private static LobbyMergeAuditLog CreateAuditLog(
        Guid? mergeRequestId,
        Guid? sourceLobbyId,
        Guid? targetLobbyId,
        Guid? sourceReservationId,   // Gap #3: populate audit trail với reservation IDs
        Guid? targetReservationId,
        Guid performedByUserId,
        string action,
        object? metadata,
        bool? success,
        string? errorMessage = null)
    {
        return new LobbyMergeAuditLog
        {
            Id = Guid.NewGuid(),
            MergeRequestId = mergeRequestId,
            SourceLobbyId = sourceLobbyId,
            TargetLobbyId = targetLobbyId,
            SourceReservationId = sourceReservationId,
            TargetReservationId = targetReservationId,
            PerformedByUserId = performedByUserId,
            Action = action,
            Metadata = System.Text.Json.JsonSerializer.Serialize(metadata),
            Success = success,
            ErrorMessage = errorMessage,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static LobbyMergeRequestDto MapToDto(LobbyMergeRequest r)
    {
        return new LobbyMergeRequestDto
        {
            Id = r.Id,
            SourceLobbyId = r.SourceLobbyId,
            TargetLobbyId = r.TargetLobbyId,
            SourceLobbyName = r.SourceLobby?.HostUserId.ToString() ?? r.SourceLobbyId.ToString(),
            TargetLobbyName = r.TargetLobby?.HostUserId.ToString() ?? r.TargetLobbyId.ToString(),
            RequestedByUserId = r.RequestedByUserId,
            RequestedByUserName = r.RequestedByUser?.Username ?? r.RequestedByUserId.ToString(),
            Status = r.Status,
            StatusText = r.Status.ToString(),
            SourceMembersCount = r.SourceMembersCount,
            SourceActiveMembersAtRequest = r.SourceActiveMembersAtRequest,
            Reason = r.Reason,
            ReviewedByUserId = r.ReviewedByUserId,
            ReviewedByUserName = r.ReviewedByUser?.Username,
            ReviewedAt = r.ReviewedAt,
            ReviewNote = r.ReviewNote,
            ExpiresAt = r.ExpiresAt,
            IdempotencyKey = r.IdempotencyKey,
            CreatedAt = r.CreatedAt,
            CombinedCount = r.CombinedCount,
            SeatCapacity = r.SeatCapacity,
            FitsCapacity = r.FitsCapacity
        };
    }

    private static LobbyMergeAuditLogDto MapAuditToDto(LobbyMergeAuditLog l)
    {
        return new LobbyMergeAuditLogDto
        {
            Id = l.Id,
            MergeRequestId = l.MergeRequestId,
            SourceLobbyId = l.SourceLobbyId,
            TargetLobbyId = l.TargetLobbyId,
            PerformedByUserId = l.PerformedByUserId,
            PerformedByUserName = l.PerformedByUser?.Username ?? l.PerformedByUserId.ToString(),
            Action = l.Action,
            Metadata = l.Metadata,
            Success = l.Success,
            ErrorMessage = l.ErrorMessage,
            CreatedAt = l.CreatedAt
        };
    }
}
