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

        // 5. Validate cùng cafe
        if (sourceLobby.CafeId != targetLobby.CafeId)
            throw new BadRequestException(LobbyMergeErrors.MergeCannotCrossCafes);

        // 5b. BR Exception 4 (boardverse-business-context.mdc) — cross-game merge:
        // A3 rời Nhóm A (game đã trả về quán qua ComponentCheck) → đứng độc lập → merge Nhóm B (game khác).
        // Logic giống Gap 4 fix trong ActiveSessionService.MergeSessionAsync:
        // chỉ chặn khi Nguồn còn box InUse (game đang chơi trên bàn) khác game với Đích.
        // Nếu Nguồn không còn box InUse (game đã trả / lobby chưa attach box) → cho phép cross-game.
        if (sourceLobby.GameTemplateId != targetLobby.GameTemplateId)
        {
            var sourceHasActiveBox = await _db.ActiveSessionGames
                .AsNoTracking()
                .AnyAsync(g =>
                    g.ActiveSession!.LobbyId == sourceLobby.Id &&
                    g.CafeInventoryBox!.Status == CafeGameInventoryStatus.InUse,
                    cancellationToken);

            if (sourceHasActiveBox)
            {
                throw new BadRequestException(LobbyMergeErrors.MergeDifferentGames);
            }
            // else: source không còn game đang chơi trên bàn → cho phép merge khác game
            _logger.LogInformation(
                "LobbyMerge: cho phép cross-game merge Source={SourceLobbyId} (game={SourceGameId}) → Target={TargetLobbyId} (game={TargetGameId}) do source không còn box InUse.",
                sourceLobby.Id, sourceLobby.GameTemplateId, targetLobby.Id, targetLobby.GameTemplateId);
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

        // 9. Đếm member count của source lobby + target lobby.
        // Gap #5 Fix (2026-09-29): Source có thể là WALK-IN lobby (ReservationId == null).
        // Walk-in lobbies KHÔNG có LobbyMembers rows — chỉ có ActiveSessionMembers
        // (guest slots với IsGuestSlot=true, UserId=null). Nếu dùng _lobbyMemberRepository
        // cho walk-in source/target sẽ trả 0 → combinedCount = 0, ghost merge bug.
        // Helper LoadSourceMemberCountsAsync xử lý cả 2 path.
        var sourceCounts = await LoadSourceMemberCountsAsync(sourceLobby, cancellationToken);
        var targetCounts = await LoadSourceMemberCountsAsync(targetLobby, cancellationToken);
        var combinedCount = sourceCounts.ActiveCount + targetCounts.ActiveCount;

        // Bug fix (2026-09-29): Source lobby rỗng (không có member active nào) → không thể
        // transfer ai, không nên cho tạo request. Trước đây staff retry liên tục tạo request
        // rác (12 lần/24h cho cùng source lobby trong production logs) → DB bloat + UX kém.
        // Fix: early reject khi sourceActiveCount == 0 với message rõ ràng cho staff.
        if (sourceCounts.ActiveCount == 0)
        {
            throw new ConflictException(LobbyMergeErrors.NoActiveMembersToTransfer);
        }

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
            SourceMembersCount = sourceCounts.TotalCount,
            SourceActiveMembersAtRequest = sourceCounts.ActiveCount,
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
                sourceMembersCount = sourceCounts.TotalCount,
                activeMembersAtRequest = sourceCounts.ActiveCount,
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

            // ===== Step 5: G8 — Seat availability check + Gap #5: Walk-in support =====
            // Kiểm tra AvailableSeats >= số member cần ghép trước khi thực hiện.
            //
            // Gap #5 Fix (2026-09-29): Source có thể là WALK-IN lobby (ReservationId == null).
            // Walk-in lobbies KHÔNG có LobbyMembers — chỉ có ActiveSessionMembers trong
            // source's ActiveSession (guest slots). Dùng helper LoadSourceTransferableMembersAsync
            // để chọn đúng nguồn dữ liệu (LobbyMember cho online, ActiveSessionMember cho walk-in).
            var sourceTransferSet = await LoadSourceTransferableMembersAsync(
                sourceLobby, cancellationToken);
            var activeMembers = sourceTransferSet.ActiveMembers;
            var walkInMembers = sourceTransferSet.WalkInMembers;
            var sourceActiveSession = sourceTransferSet.SourceActiveSession;
            // Tổng số transferable members (online LobbyMember + walk-in ActiveSessionMember)
            var totalTransferable = activeMembers.Count + walkInMembers.Count;

            if (totalTransferable > 0)
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

                if (seatInventory != null && seatInventory.AvailableSeats < totalTransferable)
                {
                    _logger.LogWarning(
                        "Seat not available for merge: RequestId={RequestId} | Available={Available} | Required={Required}",
                        requestId, seatInventory.AvailableSeats, totalTransferable);

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

            // ===== Step 9: Không có member nào active → throw ConflictException =====
            // Trước đây code silently đánh Rejected + return success → staff thấy "thành công"
            // nhưng thực tế 0 member di chuyển. Fix: throw ConflictException với message rõ ràng.
            if (totalTransferable == 0)
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

                throw new ConflictException(ApiErrorMessages.Lobby.LobbyMerge.NoActiveMembersToTransfer);
            }

            // ===== Step 10: Transfer từng member =====
            // Gap #1/#5: track ActiveSessionLobbySource đã tạo để update SourceDissolved sau dissolve
            //
            // Bug fix (2026-09-29): trước đây code tạo MỘT ActiveSessionLobbySource row cho MỖI
            // member được ghép, tất cả cùng LobbyId + SourceDissolved=false. DB có partial unique
            // index IX_ASLS_LobbyId_Active (UNIQUE LobbyId WHERE SourceDissolved = false) → member
            // thứ 2 trong cùng merge bị reject với `23505: duplicate key value`. Triệu chứng:
            // ApproveMergeAsync HTTP 500 khi ghép ≥ 2 members, gây ghost merge / mất deposit audit.
            // Fix: chỉ tạo DUY NHẤT một ActiveSessionLobbySource row cho source lobby (không phải
            // mỗi member). Row này được mark SourceDissolved=true sau khi source lobby đóng.
            var createdSources = new List<ActiveSessionLobbySource>();
            var lobbySource = new ActiveSessionLobbySource
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
            _db.ActiveSessionLobbySources.Add(lobbySource);
            createdSources.Add(lobbySource);

            // ===== Sub-step 10a: Transfer online LobbyMembers (ReservationId != null) =====
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
                        transferredAt = DateTime.UtcNow,
                        transferType = "LobbyMember"
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
            }

            // ===== Sub-step 10b: Transfer walk-in ActiveSessionMembers (ReservationId == null) =====
            // Walk-in source lobby không có LobbyMember rows — chỉ có ActiveSessionMember
            // guest slots. Cần move trực tiếp các ActiveSessionMember rows sang target session.
            foreach (var walkInMember in walkInMembers)
            {
                var transferAudit = CreateAuditLog(
                    mergeRequest.Id, sourceLobby.Id, targetLobby.Id,
                    sourceLobby.ReservationId, targetLobby.ReservationId,
                    staffUserId, "MemberTransferred",
                    new
                    {
                        memberId = walkInMember.Id,
                        userId = walkInMember.UserId,
                        guestDisplayName = walkInMember.GuestDisplayName,
                        previousLobbyId = sourceLobby.Id,
                        newLobbyId = targetLobby.Id,
                        previousSessionId = walkInMember.ActiveSessionId,
                        newSessionId = targetSession.Id,
                        transferredAt = DateTime.UtcNow,
                        transferType = "ActiveSessionMember"
                    },
                    success: true);
                _db.LobbyMergeAuditLogs.Add(transferAudit);

                // Chuyển ActiveSessionMember từ source session sang target session
                walkInMember.ActiveSessionId = targetSession.Id;
                walkInMember.MergedFromLobbyId = sourceLobby.Id;
                walkInMember.MergedAt = DateTime.UtcNow;
                if (walkInMember.OriginalSessionId == null && sourceActiveSession != null)
                {
                    walkInMember.OriginalSessionId = sourceActiveSession.Id;
                }
                if (walkInMember.OriginalLobbyId == null)
                {
                    walkInMember.OriginalLobbyId = sourceLobby.Id;
                }
            }

            // ===== Step 11 prep: persist transfer modifications BEFORE counting remaining =====
            // Bug fix (2026-09-29): Step 10a/10b đã sửa LobbyId / ActiveSessionId của members trong
            // tracker nhưng CHƯA SaveChanges. Step 11 query DB để đếm remainingPlaying members
            // → DB vẫn trỏ về source → count > 0 → source lobby KHÔNG BAO GIỜ đóng dù merge
            // thực sự rỗng. Fix: SaveChanges ở đây để DB phản ánh tracker state trước khi count.
            await _db.SaveChangesAsync(cancellationToken);

            // ===== Step 11: Dissolve source lobby nếu không còn member =====
            // Tính cả LobbyMember (online) và ActiveSessionMember Playing (walk-in) để xác định còn active hay không.
            int stillActive;
            if (sourceLobby.ReservationId.HasValue)
            {
                var remainingMembers = await _lobbyMemberRepository.GetByLobbyAsync(
                    sourceLobby.Id, cancellationToken);
                stillActive = remainingMembers.Count(m => m.IsActive && m.Status == LobbyMemberStatus.Ready);
            }
            else
            {
                // Walk-in source: đếm ActiveSessionMember còn Playing trong ActiveSession của source
                var remainingWalkIn = sourceActiveSession == null
                    ? 0
                    : await _db.ActiveSessionMembers
                        .CountAsync(m =>
                            m.ActiveSessionId == sourceActiveSession.Id &&
                            m.Status == IndividualSessionStatus.Playing,
                            cancellationToken);
                stillActive = remainingWalkIn;
            }

            if (stillActive == 0)
            {
                sourceLobby.Status = LobbyStatus.Closed;
                sourceLobby.UpdatedAt = DateTime.UtcNow;

                // Gap #3 Fix: Walk-in session (được tạo bởi CafePosService.StartWalkInSessionAsync)
                // có ActiveSession.LobbyId = walkInLobby.Id. Sau khi lobby dissolve, session này
                // bị orphan (không ai trong đó, không bị đóng). Tìm và đóng nó ngay.
                var orphanSession = await _db.ActiveSessions
                    .FirstOrDefaultAsync(s =>
                        s.LobbyId == sourceLobby.Id &&
                        s.Status == GroupSessionStatus.Active,
                        cancellationToken);
                if (orphanSession != null)
                {
                    orphanSession.EndedAt = DateTime.UtcNow;
                    orphanSession.Status = GroupSessionStatus.Closed;
                    orphanSession.UpdatedAt = DateTime.UtcNow;
                    _logger.LogInformation(
                        "LobbyMerge: closed orphan walk-in session {SessionId} after source lobby {LobbyId} dissolved",
                        orphanSession.Id, sourceLobby.Id);
                }

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
                        previousMembersCount = totalTransferable,
                        lobbyMembersCount = activeMembers.Count,
                        walkInMembersCount = walkInMembers.Count,
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
                    membersTransferred = totalTransferable,
                    lobbyMembersTransferred = activeMembers.Count,
                    walkInMembersTransferred = walkInMembers.Count,
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
                // Lấy cả LobbyMember.UserIds (online) và walk-in guest UserIds (walk-in)
                var lobbyMemberUserIds = activeMembers.Select(m => m.UserId).ToList();
                var walkInUserIds = walkInMembers
                    .Where(m => m.UserId.HasValue)
                    .Select(m => m.UserId!.Value)
                    .ToList();
                var transferredUserIds = lobbyMemberUserIds.Concat(walkInUserIds).Distinct().ToList();

                await _lobbyInviteRepository.ExpirePendingForUsersAsync(
                    lobbyId: sourceLobby.Id,
                    inviteeIds: transferredUserIds,
                    cancellationToken);

                // G19: Notify source lobby (Nhóm A) — members biết lobby đã bị absorbed
                await _lobbyHubService.NotifyLobbyMergedInto(
                    sourceLobby.Id, targetLobby.Id, mergeRequest.Id, totalTransferable);

                // G20: Notify target lobby (Nhóm B) — thông báo member mới từ merge
                if (totalTransferable > 0)
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
                "LobbyMerge approved: RequestId={RequestId} | Source={SourceLobbyId} | Target={TargetLobbyId} | LobbyMembersTransferred={LobbyCount} | WalkInMembersTransferred={WalkInCount} | Total={Count}",
                requestId, sourceLobby.Id, targetLobby.Id, activeMembers.Count, walkInMembers.Count, totalTransferable);

            return new LobbyMergeApprovedDto
            {
                MergeRequestId = mergeRequest.Id,
                SourceLobbyId = sourceLobby.Id,
                TargetLobbyId = targetLobby.Id,
                MembersTransferred = totalTransferable,
                TargetActiveSessionId = targetSession.Id,
                IdempotencyKey = mergeRequest.IdempotencyKey
            };
        }
        catch
        {
            // Bug fix (2026-09-29): Nếu path "totalTransferable == 0" đã CommitAsync() thành công
            // rồi throw ConflictException → catch block gọi RollbackAsync trên transaction ĐÃ COMMIT.
            // EF Core throw InvalidOperationException("This transaction has completed successfully...")
            // → InvalidOperationException nuốt ConflictException → middleware trả 500 thay vì 409.
            // Fix: nuốt InvalidOperationException từ RollbackAsync (transaction đã commit, không cần rollback).
            if (ownedTx != null)
            {
                try
                {
                    await ownedTx.RollbackAsync(cancellationToken);
                }
                catch (InvalidOperationException)
                {
                    // Transaction đã được commit ở Step 9 → rollback không hợp lệ. Bỏ qua.
                }
            }
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
    /// Counts member cho cả online (LobbyMember) và walk-in (ActiveSessionMember) lobby.
    /// Online  : lobby.ReservationId.HasValue → count LobbyMembers.
    /// Walk-in : lobby.ReservationId == null → count ActiveSessionMembers trong lobby.ActiveSessionId.
    /// </summary>
    private readonly record struct LobbyMemberCounts(int TotalCount, int ActiveCount);

    /// <summary>
    /// Result set cho LoadSourceTransferableMembersAsync.
    /// - ActiveMembers : LobbyMember rows cho online lobby (IsActive + Status = Ready).
    /// - WalkInMembers : ActiveSessionMember rows cho walk-in lobby (Status = Playing).
    /// - SourceActiveSession : walk-in session của source lobby (null với online lobby).
    /// </summary>
    private readonly record struct LobbyMergeSourceTransferSet(
        IReadOnlyList<LobbyMember> ActiveMembers,
        IReadOnlyList<ActiveSessionMember> WalkInMembers,
        ActiveSession? SourceActiveSession);

    /// <summary>
    /// Đếm member count của 1 lobby (online hoặc walk-in).
    /// Online  : LobbyMember.Status = Ready là "active", các status khác (Joined/Kicked/Left/LobbyTerminated) là "inactive".
    /// Walk-in : ActiveSessionMember.Status = Playing là "active", các status khác (SuspendedMutation/Finished) là "inactive".
    ///
    /// Bug fix (2026-09-29): Trước đây walk-in chỉ query qua `lobby.ActiveSessionId`. Field này
    /// KHÔNG được set bởi ReservationService.CheckInAsync (online flow qua POS) → loadCount = 0.
    /// Fix: query ngược qua `ActiveSession.LobbyId == lobby.Id` (FK set khi session tạo, đáng tin cậy),
    /// fallback sang `lobby.ActiveSessionId` cho dữ liệu legacy.
    /// </summary>
    private async Task<LobbyMemberCounts> LoadSourceMemberCountsAsync(
        Lobby lobby,
        CancellationToken cancellationToken)
    {
        // Online lobby — dùng LobbyMember
        if (lobby.ReservationId.HasValue)
        {
            var members = await _lobbyMemberRepository.GetByLobbyAsync(lobby.Id, cancellationToken);
            var activeCount = members.Count(m => m.IsActive && m.Status == LobbyMemberStatus.Ready);
            return new LobbyMemberCounts(members.Count, activeCount);
        }

        // Walk-in lobby — resolve session qua LobbyId reverse-FK (đáng tin cậy), fallback ActiveSessionId
        var sourceSession = await ResolveSourceActiveSessionAsync(lobby, cancellationToken);
        if (sourceSession == null)
        {
            // Lobby không có session nào liên kết → lobby trống hoàn toàn
            return new LobbyMemberCounts(0, 0);
        }

        var sessionMembers = await _db.ActiveSessionMembers
            .AsNoTracking()
            .Where(m => m.ActiveSessionId == sourceSession.Id)
            .ToListAsync(cancellationToken);

        // Defensive: ToListAsync() should return empty list, but in some InMemory DB
        // edge cases có thể trả null → fallback để tránh NRE.
        var sessionMembersList = sessionMembers ?? new List<ActiveSessionMember>();
        var activePlaying = sessionMembersList.Count(m => m.Status == IndividualSessionStatus.Playing);
        return new LobbyMemberCounts(sessionMembersList.Count, activePlaying);
    }

    /// <summary>
    /// Load transferable members cho cả online (LobbyMember) và walk-in (ActiveSessionMember).
    /// Online  : trả LobbyMember có IsActive && Status == Ready.
    /// Walk-in : trả ActiveSessionMember có Status == Playing (kèm source ActiveSession để update sau).
    ///
    /// Bug fix (2026-09-29): Walk-in giờ resolve session qua reverse-FK `ActiveSession.LobbyId == lobby.Id`
    /// (set khi session tạo bởi CafePosService.StartSessionFromReservationAsync cho online lobby
    /// check-in qua POS, hoặc CafePosService.StartWalkInSessionAsync cho walk-in).
    /// Fallback `lobby.ActiveSessionId` cho legacy data chỉ set bởi walk-in Phase 2.
    /// </summary>
    private async Task<LobbyMergeSourceTransferSet> LoadSourceTransferableMembersAsync(
        Lobby sourceLobby,
        CancellationToken cancellationToken)
    {
        // Online lobby — dùng LobbyMember
        if (sourceLobby.ReservationId.HasValue)
        {
            var members = await _lobbyMemberRepository.GetByLobbyAsync(sourceLobby.Id, cancellationToken);
            var active = members
                .Where(m => m.IsActive && m.Status == LobbyMemberStatus.Ready)
                .ToList();
            return new LobbyMergeSourceTransferSet(active, Array.Empty<ActiveSessionMember>(), null);
        }

        // Walk-in lobby — resolve session qua reverse-FK LobbyId (đáng tin cậy)
        var sourceSession = await ResolveSourceActiveSessionAsync(sourceLobby, cancellationToken);
        if (sourceSession == null)
        {
            return new LobbyMergeSourceTransferSet(
                Array.Empty<LobbyMember>(),
                Array.Empty<ActiveSessionMember>(),
                null);
        }

        var walkInMembers = await _db.ActiveSessionMembers
            .Where(m =>
                m.ActiveSessionId == sourceSession.Id &&
                m.Status == IndividualSessionStatus.Playing)
            .ToListAsync(cancellationToken);

        return new LobbyMergeSourceTransferSet(
            Array.Empty<LobbyMember>(),
            walkInMembers,
            sourceSession);
    }

    /// <summary>
    /// Resolve ActiveSession liên kết với source lobby một cách đáng tin cậy.
    ///
    /// Bug fix (2026-09-29): Trước đây chỉ dùng `lobby.ActiveSessionId`. Field này không được set
    /// bởi ReservationService.CheckInAsync (online flow qua POS) — chỉ set bởi:
    ///   - CafePosService.StartWalkInSessionAsync (walk-in Phase 2)
    ///   - LobbyService.TransitionToInProgressAsync (chỉ khi lobby.Status == Full|WaitingCheckIn)
    /// → Online lobby checked-in qua POS có `lobby.ActiveSessionId == null` mặc dù có
    /// `ActiveSession.LobbyId == lobby.Id`. Query ngược qua LobbyId là đáng tin cậy hơn vì
    /// FK được set khi session tạo (PrepareSessionSkeletonAsync hoặc walk-in Phase 1).
    ///
    /// Lookup order:
    ///   1. ActiveSession.LobbyId == sourceLobby.Id AND Status = Active (Active/Checking)
    ///   2. Fallback: ActiveSession.Id == sourceLobby.ActiveSessionId
    ///
    /// Trả về session đầu tiên match theo thứ tự ưu tiên, null nếu không tìm thấy.
    /// </summary>
    private async Task<ActiveSession?> ResolveSourceActiveSessionAsync(
        Lobby sourceLobby,
        CancellationToken cancellationToken)
    {
        // Primary: query reverse-FK ActiveSession.LobbyId == sourceLobby.Id
        // Lấy session Active trước (priority), fallback Checking (game đã trả về quán nhưng
        // member vẫn có thể merge sang session khác).
        var sessionViaLobbyId = await _db.ActiveSessions
            .Where(s => s.LobbyId == sourceLobby.Id &&
                        (s.Status == GroupSessionStatus.Active ||
                         s.Status == GroupSessionStatus.Checking))
            .OrderByDescending(s => s.Status == GroupSessionStatus.Active ? 1 : 0) // Active trước
            .ThenByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (sessionViaLobbyId != null)
        {
            return sessionViaLobbyId;
        }

        // Fallback: legacy walk-in data — sessionId được set bởi Phase 2 walk-in flow
        if (sourceLobby.ActiveSessionId.HasValue)
        {
            return await _db.ActiveSessions
                .FirstOrDefaultAsync(s => s.Id == sourceLobby.ActiveSessionId.Value, cancellationToken);
        }

        return null;
    }

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
