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
using Npgsql;

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
    private readonly IWalletService _walletService;
    private readonly IBookingDepositRepository _depositRepository;
    private readonly ISeatInventoryRepository _seatInventoryRepository;
    private readonly IActiveSessionRepository _activeSessionRepository;
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
        IWalletService walletService,
        IBookingDepositRepository depositRepository,
        ISeatInventoryRepository seatInventoryRepository,
        IActiveSessionRepository activeSessionRepository,
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
        _walletService = walletService;
        _depositRepository = depositRepository;
        _seatInventoryRepository = seatInventoryRepository;
        _activeSessionRepository = activeSessionRepository;
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
        //
        // Gap-fix 2026-10-01: Phân biệt 2 trường hợp block cross-game merge:
        //   (a) Source còn box InUse + CheckStatus = NotChecked → throw "MergeSourceBoxNotCheckedYet"
        //       hướng dẫn staff kiểm kê linh kiện trước khi ghép (theo BR-12).
        //   (b) Source còn box InUse + CheckStatus = Verified/MissingComponents nhưng game khác
        //       → throw "MergeDifferentGames" (lobby vẫn đang attach box khác game với target).
        // Trước đây cả 2 case dùng chung MergeDifferentGames → staff không biết phải làm gì
        // tiếp theo (đặc biệt khi 2 lobby cùng tên game trong UI nhưng khác GameTemplateId
        // do duplicate seed data).
        if (sourceLobby.GameTemplateId != targetLobby.GameTemplateId)
        {
            var sourceInUseBoxInfo = await _db.ActiveSessionGames
                .AsNoTracking()
                .Where(g =>
                    g.ActiveSession!.LobbyId == sourceLobby.Id &&
                    g.CafeInventoryBox!.Status == CafeGameInventoryStatus.InUse)
                .Select(g => new { g.CheckStatus, g.GameTemplateId })
                .FirstOrDefaultAsync(cancellationToken);

            if (sourceInUseBoxInfo != null)
            {
                if (sourceInUseBoxInfo.CheckStatus == ComponentCheckStatus.NotChecked)
                {
                    // (a) Chưa kiểm kê — staff phải làm ComponentCheck trước.
                    var sourceGameName = await _db.GameTemplates
                        .AsNoTracking()
                        .Where(g => g.Id == sourceLobby.GameTemplateId)
                        .Select(g => g.Name)
                        .FirstOrDefaultAsync(cancellationToken) ?? sourceLobby.GameTemplateId.ToString();

                    var targetGameName = await _db.GameTemplates
                        .AsNoTracking()
                        .Where(g => g.Id == targetLobby.GameTemplateId)
                        .Select(g => g.Name)
                        .FirstOrDefaultAsync(cancellationToken) ?? targetLobby.GameTemplateId.ToString();

                    throw new BadRequestException(
                        LobbyMergeErrors.MergeSourceBoxNotCheckedYet(sourceGameName, targetGameName));
                }

                // (b) Đã kiểm kê nhưng game khác — box vẫn attach vào source.
                throw new BadRequestException(LobbyMergeErrors.MergeDifferentGames);
            }

            // else: source không còn game đang chơi trên bàn → cho phép merge khác game
            _logger.LogInformation(
                "LobbyMerge: cho phép cross-game merge Source={SourceLobbyId} (game={SourceGameId}) → Target={TargetLobbyId} (game={TargetGameId}) do source không còn box InUse.",
                sourceLobby.Id, sourceLobby.GameTemplateId, targetLobby.Id, targetLobby.GameTemplateId);
        }

        // ===== Ambient Transaction Pattern — BR-REQUIRED §17.5 =====
        // Wrap từ bước 6 (idempotency + existing-pending check) cho tới INSERT trong
        // một Serializable transaction. Lý do:
        //
        //   1. Race condition DB-enforced bởi IX_LMR_SourceTarget_Pending
        //      (partial unique index trên (SourceLobbyId, TargetLobbyId) WHERE Status = 0).
        //      Index này được tạo bằng raw SQL (xem "sql/" folder) — không có EF migration
        //      nào tracking nó. Nếu thiếu transaction wrap, 2 request đồng thời có thể
        //      cùng qua existingPending check (AnyAsync không lock), cùng INSERT, request
        //      thứ 2 → PostgresException 23505 → ApiExceptionMiddleware trả 500 thay vì 409.
        //      Có transaction (Serializable hoặc default) giúp giảm race window, nhưng vẫn
        //      cần catch 23505 làm safety net cuối cùng vì unique index check happens
        //      tại INSERT time, không phải tại transaction start.
        //
        //   2. Outer caller có thể đã wrap transaction riêng → check ambient trước.
        var ambientTx = _db.Database.CurrentTransaction;
        IDbContextTransaction? ownedTx = null;
        if (ambientTx == null)
        {
            ownedTx = await _db.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable, cancellationToken);
        }

        LobbyMergeRequestDto? resultDto;
        try
        {

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

        // ===== Validate SelectedMemberIds (Option 1 fix 2026-10-01) =====
        // Staff truyền SelectedMemberIds để chỉ chuyển một số member cụ thể, không phải tất cả.
        // - null/empty → transfer TẤT CẢ active members (backward compatible, giữ behavior cũ).
        // - Có giá trị → validate từng ID phải match active member hiện tại của source lobby.
        //
        // ID semantics:
        //   - Online source  : LobbyMember.Id
        //   - Walk-in source : ActiveSessionMember.Id (bao gồm guest slot)
        //
        // Tính lại combinedCount dựa trên số selected (không phải tổng source active).
        var selectedMemberIds = NormalizeSelectedMemberIds(dto.SelectedMemberIds);
        var isSelectiveMerge = selectedMemberIds.Count > 0;
        HashSet<Guid>? activeIdLookup = null;
        if (isSelectiveMerge)
        {
            var sourceTransferSet = await LoadSourceTransferableMembersAsync(
                sourceLobby, cancellationToken);
            var onlineIds = sourceTransferSet.ActiveMembers.Select(m => m.Id).ToHashSet();
            var walkInIds = sourceTransferSet.WalkInMembers.Select(m => m.Id).ToHashSet();
            activeIdLookup = new HashSet<Guid>(onlineIds.Concat(walkInIds));

            var invalidIds = selectedMemberIds.Where(id => !activeIdLookup.Contains(id)).ToList();
            if (invalidIds.Count > 0)
            {
                var invalidIdsStr = string.Join(", ", invalidIds.Select(id => id.ToString()[..8]));
                throw new BadRequestException(
                    LobbyMergeErrors.InvalidSelectedMemberIds(invalidIdsStr));
            }

            // Recalculate combinedCount dựa trên selected (không phải tổng source active)
            combinedCount = selectedMemberIds.Count + targetCounts.ActiveCount;

            _logger.LogInformation(
                "LobbyMerge.CreateRequest: selective merge Source={SourceLobbyId} | " +
                "selectedMembers={SelectedCount} (out of {TotalActive} active)",
                sourceLobby.Id, selectedMemberIds.Count, sourceCounts.ActiveCount);
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
            FitsCapacity = fitsCapacity,
            SelectedMemberIdsJson = isSelectiveMerge
                ? System.Text.Json.JsonSerializer.Serialize(selectedMemberIds)
                : null
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
                selectedMemberIds = isSelectiveMerge ? selectedMemberIds : null,
                isSelectiveMerge,
                reason = dto.Reason
            },
            success: true);
        _db.LobbyMergeAuditLogs.Add(mergeRequestAudit);

            try
            {
                await _db.SaveChangesAsync(cancellationToken);

                // Commit owned transaction trước khi load navigation. Nếu không có owned
                // transaction (ambient caller đã wrap) thì skip — outer caller tự quản lý.
                if (ownedTx != null)
                    await ownedTx.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException dbe) when (IsUniqueViolationOnMergePendingConstraint(dbe))
            {
                // ===== Race-condition safety net =====
                // Two concurrent requests cùng qua existingPending check trước khi INSERT.
                // DB unique index IX_LMR_SourceTarget_Pending chặn request thứ 2 → 23505.
                // Map sang 409 Conflict với message rõ ràng cho staff thay vì 500.
                _logger.LogWarning(
                    "LobbyMerge.CreateRequest: detected concurrent duplicate pending request. " +
                    "Source={SourceLobbyId} | Target={TargetLobbyId} | By={UserId}",
                    dto.SourceLobbyId, dto.TargetLobbyId, staffUserId);

                if (ownedTx != null)
                {
                    try { await ownedTx.RollbackAsync(cancellationToken); }
                    catch (InvalidOperationException)
                    {
                        // EF Core có thể tự rollback khi SaveChangesAsync throw. Bỏ qua.
                    }
                }
                throw new ConflictException(LobbyMergeErrors.MergeRequestAlreadyPending);
            }

            await _db.Entry(request).Reference(r => r.SourceLobby).LoadAsync(cancellationToken);
            await _db.Entry(request).Reference(r => r.TargetLobby).LoadAsync(cancellationToken);
            await _db.Entry(request).Reference(r => r.RequestedByUser).LoadAsync(cancellationToken);

            _logger.LogInformation(
                "LobbyMergeRequest created: {RequestId} | Source={SourceLobbyId} | Target={TargetLobbyId} | By={UserId}",
                request.Id, dto.SourceLobbyId, dto.TargetLobbyId, staffUserId);

            resultDto = MapToDto(request);
            return resultDto;
        }
        catch
        {
            // Re-throw đã được xử lý phía trên cho unique violation. Catch này chỉ để
            // đảm bảo rollback owned transaction cho các exception khác (NotFound,
            // Forbidden, BadRequest, Conflict v.v.) trước khi bubbles lên middleware.
            if (ownedTx != null)
            {
                try { await ownedTx.RollbackAsync(cancellationToken); }
                catch (InvalidOperationException)
                {
                    // Transaction đã được EF Core rollback tự động khi SaveChangesAsync
                    // throw → bỏ qua InvalidOperationException.
                }
            }
            throw;
        }
        finally
        {
            if (ownedTx != null) await ownedTx.DisposeAsync();
        }
    }

    /// <summary>
    /// Detect Postgres unique-violation trên partial index IX_LMR_SourceTarget_Pending
    /// (xem raw migration trong sql/ folder — index này được tạo tay, không tracking EF).
    /// SqlState 23505 = unique_violation.
    /// </summary>
    private static bool IsUniqueViolationOnMergePendingConstraint(DbUpdateException ex)
    {
        if (ex.InnerException is not PostgresException pg) return false;
        if (pg.SqlState != "23505") return false;
        // Npgsql có thể trả về ConstraintName kèm hoặc không kèm dấu nháy kép tùy version.
        // Match cả 2 variant để future-proof.
        var name = pg.ConstraintName;
        return name == "IX_LMR_SourceTarget_Pending"
            || name == "\"IX_LMR_SourceTarget_Pending\"";
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

            // ===== H5 fix (2026-09-30): Re-check source lobby status =====
            // Trước đây Step 3 chỉ validate target lobby. Source lobby có thể đã chuyển
            // Closed/HostCancelled/ExpiredByCafe/Dissolved giữa Create và Approve
            // (race window). Nếu không re-check → vẫn cho merge từ 1 lobby đã chết.
            //
            // Lưu ý: source lobby đã load qua Include(r => r.SourceLobby), Status là snapshot
            // tại thời điểm Approve bắt đầu (FOR UPDATE lock đã giữ row LobbyMergeRequests
            // nhưng KHÔNG giữ Lobby). Vẫn cần check thủ công.
            var validSourceStatusesForApprove = new[]
            {
                LobbyStatus.Open, LobbyStatus.Viable, LobbyStatus.Full,
                LobbyStatus.InProgress, LobbyStatus.PendingActivation
            };
            if (!validSourceStatusesForApprove.Contains(sourceLobby.Status))
                throw new ConflictException(LobbyMergeErrors.SourceLobbyClosedDuringReview);

            // ===== Deserialize SelectedMemberIds từ request (Option 1 fix 2026-10-01) =====
            // null/empty → transfer all (backward compatible).
            // Có giá trị → chỉ transfer các member trong list (LobbyMember.Id cho online,
            // ActiveSessionMember.Id cho walk-in).
            var selectedIdsAtApprove = NormalizeSelectedMemberIds(
                DeserializeSelectedMemberIds(mergeRequest.SelectedMemberIdsJson));
            var isSelectiveApprove = selectedIdsAtApprove.Count > 0;

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

            // ===== Apply SelectedMemberIds filter (Option 1 fix 2026-10-01) =====
            // Nếu request có SelectedMemberIds (set tại Create), filter cả activeMembers
            // và walkInMembers xuống chỉ những ID được chọn. Re-validate vì member có thể
            // đã rời source lobby giữa Create (validate) và Approve (15 phút sau).
            var invalidIdsAtApprove = new List<Guid>();
            if (isSelectiveApprove)
            {
                var preFilterOnlineCount = activeMembers.Count;
                var preFilterWalkInCount = walkInMembers.Count;

                activeMembers = activeMembers
                    .Where(m => selectedIdsAtApprove.Contains(m.Id))
                    .ToList();
                walkInMembers = walkInMembers
                    .Where(m => selectedIdsAtApprove.Contains(m.Id))
                    .ToList();

                // Detect IDs không match (member đã rời khỏi source lobby)
                var allCurrentlyActiveIds = sourceTransferSet.ActiveMembers
                    .Select(m => m.Id)
                    .Concat(sourceTransferSet.WalkInMembers.Select(m => m.Id))
                    .ToHashSet();
                invalidIdsAtApprove = selectedIdsAtApprove
                    .Where(id => !allCurrentlyActiveIds.Contains(id))
                    .ToList();

                _logger.LogInformation(
                    "LobbyMerge.Approve: selective merge filter applied. " +
                    "Before: online={OnlineBefore}, walkIn={WalkInBefore}. " +
                    "After: online={OnlineAfter}, walkIn={WalkInAfter}. " +
                    "InvalidIds={InvalidCount}",
                    preFilterOnlineCount, preFilterWalkInCount,
                    activeMembers.Count, walkInMembers.Count,
                    invalidIdsAtApprove.Count);
            }

            // Tổng số transferable members (online LobbyMember + walk-in ActiveSessionMember)
            var totalTransferable = activeMembers.Count + walkInMembers.Count;

            // Nếu selective merge mà không còn member nào match (tất cả đã rời) → reject
            // giống như Step 9. Lưu ý: chỉ reject khi isSelectiveApprove AND 0 match.
            // Nếu không selective (transfer all) mà 0 match → vẫn rơi vào Step 9 như cũ.
            if (isSelectiveApprove && totalTransferable == 0)
            {
                var invalidIdsStr = string.Join(", ", invalidIdsAtApprove.Select(id => id.ToString()[..8]));
                _logger.LogWarning(
                    "LobbyMerge.Approve: All selected member IDs no longer active in source lobby. " +
                    "RequestId={RequestId} | Invalid={InvalidIds}",
                    requestId, invalidIdsStr);

                mergeRequest.Status = LobbyMergeRequestStatus.Rejected;
                mergeRequest.ReviewedByUserId = staffUserId;
                mergeRequest.ReviewedAt = DateTime.UtcNow;
                mergeRequest.ReviewNote = $"All selected member IDs no longer valid at approve time: {invalidIdsStr}";
                mergeRequest.UpdatedAt = DateTime.UtcNow;

                var rejectAudit = CreateAuditLog(
                    mergeRequest.Id, sourceLobby.Id, targetLobby.Id,
                    sourceLobby.ReservationId, targetLobby.ReservationId,
                    staffUserId, "MergeRejected",
                    new
                    {
                        reason = "AllSelectedIdsInvalid",
                        invalidIds = invalidIdsAtApprove
                    },
                    success: false, errorMessage: "All selected IDs no longer valid.");
                _db.LobbyMergeAuditLogs.Add(rejectAudit);

                await _db.SaveChangesAsync(cancellationToken);
                if (ownedTx != null) await ownedTx.CommitAsync(cancellationToken);

                throw new ConflictException(LobbyMergeErrors.SelectedMemberIdsEmptyAfterFilter);
            }

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
                        // H4 fix (2026-09-30): BR-RISK-04 hard reject cho Suspended/Banned.
                        // Trước đây code chỉ giảm cap heldBalance cho Suspended/Banned về 200k BVC
                        // nhưng vẫn cho merge. Theo BR-RISK-04: Suspended/Banned chặn
                        // "tạo lobby, join lobby" → merge = "join lobby khác" → phải reject hẳn.
                        // (Restricted vẫn cho phép theo BR-RISK-04 — chỉ cảnh báo UI).
                        if (wallet.AccountStatus == AccountStatus.Suspended ||
                            wallet.AccountStatus == AccountStatus.Banned)
                        {
                            var user = await _userRepository.GetByIdAsync(member.UserId, cancellationToken);
                            var displayName = user?.Username ?? member.UserId.ToString();
                            throw new ConflictException(
                                LobbyMergeErrors.MemberAccountRestricted(
                                    displayName, wallet.AccountStatus.ToString()));
                        }

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

                        // H3 fix (2026-09-30): BR-USER-LIMIT-01 + BR-USER-LIMIT-05 check cho
                        // transferring member. Nếu user đã host 1 lobby + member 1 lobby khác
                        // (tổng = 2), merge sẽ thành host 1 + member 2 = 3 → vi phạm BR-USER-LIMIT-01.
                        //
                        // BR-USER-LIMIT-05 đã bỏ ngày 2026-09-12, nhưng cap tổng vẫn áp dụng.
                        // Lưu ý: wallet load đã lock FOR UPDATE nhưng lobby count query KHÔNG lock.
                        // Race condition nhỏ (giữa 2 staff merge song song) có thể vượt cap
                        // trong vài ms. Acceptable cho MVP, sẽ được handle bằng DB constraint
                        // sau nếu cần.
                        var memberHostCount = (await _lobbyRepository.GetActiveLobbiesByHostAsync(
                            member.UserId, cancellationToken)).Count;
                        var memberMemberCount = (await _lobbyRepository.GetActiveLobbiesByMemberAsync(
                            member.UserId, cancellationToken)).Count;

                        if (memberHostCount + memberMemberCount >= 2)
                        {
                            var user = await _userRepository.GetByIdAsync(member.UserId, cancellationToken);
                            var displayName = user?.Username ?? member.UserId.ToString();
                            throw new ConflictException(
                                LobbyMergeErrors.MemberExceedsLobbyLimitAfterMerge(
                                    displayName, memberHostCount, memberMemberCount));
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

            // ===== Step 5b (Option 2): Auto-promote new host BEFORE transferring old host =====
            // BR-USER-LIMIT-01: Mỗi lobby active phải có đúng 1 host.
            // Edge case: source's host nằm trong activeMembers (sẽ transfer sang target)
            // NHƯNG source vẫn còn member khác ở lại (stillActive > 0 sau Step 11).
            // → Sau khi host rời, source là lobby mồ côi (có member nhưng không host) →
            //   vi phạm BR-USER-LIMIT-01.
            //
            // Fix: Nếu host đang được transfer VÀ source còn ≥ 1 member sẽ ở lại →
            // promote member đó thành host mới TRƯỚC khi host cũ rời.
            // Chỉ áp dụng cho online lobby (walk-in không có khái niệm HostUserId).
            //
            // Edge case 2: Nếu KHÔNG còn member nào ở lại (stillActive == 0) → Step 11
            // sẽ đóng source → host role tự kết thúc cùng lobby → không cần promote.
            await TryAutoPromoteSourceHostAsync(
                sourceLobby, activeMembers, mergeRequest, staffUserId, cancellationToken);

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
                //
                // H6 fix (2026-09-30): Trước đây query `m.OriginalSessionId == sourceLobby.Id`
                // — chỉ tìm được ActiveSessionMember có OriginalSessionId khớp source lobby.
                // Edge case: nếu member A đã được cascade-merged từ lobby C → B trước đó,
                // OriginalSessionId = sessionC.Id (set bởi merge đầu tiên), KHÔNG bằng
                // sourceLobby.Id (B) → query miss → A vẫn ở session C, KHÔNG được
                // update sang target → orphan data.
                //
                // Fix: tìm theo BOTH OriginalSessionId và MergedFromLobbyId để cover cả
                // trường hợp cascade-merge. Logic: A đang tham gia session của source lobby,
                // dù OriginalSessionId là C (gốc) hay MergedFromLobbyId là B (source hiện tại).
                var memberSession = await _db.ActiveSessionMembers
                    .FirstOrDefaultAsync(m =>
                        m.UserId == member.UserId &&
                        (m.OriginalSessionId == sourceLobby.Id ||
                         m.MergedFromLobbyId == sourceLobby.Id) &&
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

            // ===== Gap #2 fix: Sync source Reservation.CurrentPlayers sau partial merge =====
            // Trước đây: Step 10a transfer members từ source → target nhưng KHÔNG update
            // source.Reservation.CurrentPlayers. Source reservation vẫn giữ count cũ →
            // BR-NEW-08 (1 lobby / playDate+timeSlot / cafe / user) và reporting có thể
            // thấy sai (vd: source hiển thị 4 người dù chỉ còn 2 ở lại).
            //
            // Chỉ sync khi source VẪN còn members (stillActive > 0). Trường hợp stillActive == 0
            // sẽ set Reservation.Status = AbsorbedByMerge bên dưới, CurrentPlayers không còn
            // ý nghĩa (lobby đã đóng).
            if (sourceLobby.ReservationId.HasValue && stillActive > 0)
            {
                var sourceReservation = await _db.Reservations.FirstOrDefaultAsync(
                    r => r.Id == sourceLobby.ReservationId.Value, cancellationToken);
                if (sourceReservation != null)
                {
                    var actualSourceMembers = await _lobbyMemberRepository.GetByLobbyAsync(
                        sourceLobby.Id, cancellationToken);
                    var actualActiveCount = actualSourceMembers.Count(
                        m => m.IsActive && m.Status != LobbyMemberStatus.Left
                            && m.Status != LobbyMemberStatus.Kicked
                            && m.Status != LobbyMemberStatus.LobbyTerminated);

                    // Host mới (từ Step 5b) đã được promote NHƯNG vẫn là active member của source
                    // → actualActiveCount bao gồm host mới. Đây là số CurrentPlayers đúng.
                    if (sourceReservation.CurrentPlayers != actualActiveCount)
                    {
                        _logger.LogInformation(
                            "LobbyMerge: syncing source reservation {ReservationId} CurrentPlayers " +
                            "{Old} → {New} after partial merge",
                            sourceReservation.Id, sourceReservation.CurrentPlayers, actualActiveCount);
                        sourceReservation.CurrentPlayers = actualActiveCount;
                        sourceReservation.UpdatedAt = DateTime.UtcNow;
                    }
                }
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

                    // M2 / Gap #33 fix (2026-10-01): After orphan session closed → release the
                    // attached table + box back to Available. Trước đây chỉ set Status = Closed
                    // nhưng KHÔNG đụng vào CafeTables.Status / CafeInventoryBoxes.Status →
                    // CafeTables vẫn InUse → walk-in mới StartGameSessionAsync bị reject
                    // với "đang được giữ hoặc trong sự kiện" (ApiErrorMessages.Pos.TableNotAvailableForGame).
                    //
                    // Best-effort: lỗi release KHÔNG làm fail cả merge (audit log đầy đủ + warning).
                    // Background job (AutoReleaseExpiredSessionsJob) sẽ retry release ở session
                    // status Closed sau đó.
                    await ReleaseSourceSessionResourcesAsync(
                        orphanSession, mergeRequest, sourceLobby.Id, targetLobby.Id, cancellationToken);
                }

                // M1 / Exception 4 (CẬP NHẬT 2026-10-01):
                // Source lobby dissolve → transfer deposit sang target reservation.
                // KHÔNG refund về Host A's wallet — deposit FOLLOWS theo members qua merge.
                // docs/design/host-deposit-discount-and-bvc-payment-design.md §A6 + §B3.1
                if (sourceLobby.ReservationId.HasValue)
                {
                    var reservation = await _db.Reservations
                        .FirstOrDefaultAsync(r => r.Id == sourceLobby.ReservationId, cancellationToken);
                    if (reservation != null)
                    {
                        // BR-MERGE-01: Merge = staff-initiated absorption, KHÔNG phải host cancel.
                        // Dùng status AbsorbedByMerge riêng (thay vì CancelledByPlayer) để:
                        //   1. Tránh bị filter nhầm vào refund policy (BR-REFUND-02/03/08).
                        //   2. Staff POS vẫn thấy merged reservation trong tab terminal state
                        //      (xem CafePosService.ActiveAndTerminalReservationStatuses).
                        //   3. Audit/report phân loại "host cancel" vs "absorbed by merge" chính xác.
                        reservation.Status = ReservationStatus.AbsorbedByMerge;
                        reservation.SourceDissolved = true;
                        reservation.MergedIntoReservationId = targetLobby.ReservationId;
                        reservation.MergedAt = DateTime.UtcNow;
                        reservation.MergedByUserId = staffUserId;
                        reservation.UpdatedAt = DateTime.UtcNow;

                        // ============================================================
                        // M1 / Exception 4: TRANSFER DEPOSIT sang target reservation
                        // ============================================================
                        // Tổng deposit có thể transfer = DepositAmount + CarriedOverDepositBvc
                        // (carry-over cũ từ chain merge trước đó).
                        var carriedAmount = reservation.DepositAmount + reservation.CarriedOverDepositBvc;

                        if (carriedAmount > 0)
                        {
                            if (targetLobby.ReservationId.HasValue)
                            {
                                // === HAPPY PATH: target là online lobby có reservation ===
                                // Transfer sang target.CarriedOverDepositBvc.
                                var targetReservation = await _db.Reservations
                                    .FirstOrDefaultAsync(r => r.Id == targetLobby.ReservationId, cancellationToken);

                                if (targetReservation != null)
                                {
                                    targetReservation.CarriedOverDepositBvc += carriedAmount;
                                    targetReservation.CarriedOverFromReservationIds =
                                        AppendCsv(targetReservation.CarriedOverFromReservationIds, reservation.Id.ToString());
                                    targetReservation.CarriedOverFromUserIds =
                                        AppendCsv(targetReservation.CarriedOverFromUserIds, reservation.HostId.ToString());
                                    targetReservation.UpdatedAt = DateTime.UtcNow;

                                    _logger.LogInformation(
                                        "LobbyMerge: transferred {Amount} BVC deposit from source reservation {SourceResId} " +
                                        "(host {SourceHostId}) → target reservation {TargetResId}. " +
                                        "Source.Status = AbsorbedByMerge. Target.CarriedOverDepositBvc = {CarriedTotal}",
                                        carriedAmount, reservation.Id, reservation.HostId,
                                        targetReservation.Id, targetReservation.CarriedOverDepositBvc);

                                    // Ghi LobbyMergeAuditLog event mới: DepositFollowedOnMerge
                                    var depositFollowedAudit = CreateAuditLog(
                                        mergeRequest.Id, sourceLobby.Id, targetLobby.Id,
                                        sourceLobby.ReservationId, targetLobby.ReservationId,
                                        staffUserId, "DepositFollowedOnMerge",
                                        new
                                        {
                                            carriedAmountBvc = carriedAmount,
                                            sourceOriginalDeposit = reservation.DepositAmount,
                                            sourceCarriedOver = reservation.CarriedOverDepositBvc,
                                            sourceHostId = reservation.HostId,
                                            targetCarriedOverAfter = targetReservation.CarriedOverDepositBvc,
                                            transferredAt = DateTime.UtcNow
                                        },
                                        success: true);
                                    _db.LobbyMergeAuditLogs.Add(depositFollowedAudit);
                                }
                                else
                                {
                                    _logger.LogWarning(
                                        "LobbyMerge: target reservation {TargetResId} not found — cannot transfer deposit. " +
                                        "Source deposit {Amount} BVC will be released to wallet.",
                                        targetLobby.ReservationId, carriedAmount);
                                    await ReleaseDepositToWalletAsync(reservation, carriedAmount, cancellationToken);
                                }
                            }
                            else
                            {
                                // === EDGE CASE: target là walk-in lobby (ReservationId == null) ===
                                // Không thể carry over → release source deposit về Host A's wallet.
                                _logger.LogInformation(
                                    "LobbyMerge: target is walk-in lobby (no reservation) — releasing " +
                                    "source deposit {Amount} BVC to source host {HostId} wallet.",
                                    carriedAmount, reservation.HostId);
                                await ReleaseDepositToWalletAsync(reservation, carriedAmount, cancellationToken);
                            }

                            // Source reservation: clear deposit amount (đã transfer hoặc release).
                            reservation.DepositAmount = 0;
                            reservation.CarriedOverDepositBvc = 0;
                            reservation.UpdatedAt = DateTime.UtcNow;
                        }
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
                    isSelectiveApprove,
                    selectedMemberIds = isSelectiveApprove ? selectedIdsAtApprove : null,
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
                // Chỉ populate khi selective merge để tránh response quá to cho case 30+ người
                // (transfer all). Xem LobbyMergeApprovedDto.transferredMemberIds doc.
                TransferredMemberIds = isSelectiveApprove
                    ? activeMembers.Select(m => m.Id).Concat(walkInMembers.Select(m => m.Id)).ToList()
                    : new List<Guid>(),
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

        // H7 fix (2026-09-30): FOR UPDATE lock + cross-cafe check.
        // Trước đây: Reject không có FOR UPDATE → 2 staff bấm Reject cùng lúc → cả 2 set
        // Rejected, ghi 2 audit logs duplicate. Approve có FOR UPDATE nên Reject cũng cần.
        //
        // Dùng LoadMergeRequestWithLockAsync (protected virtual) thay vì FromSqlRaw trực tiếp
        // để TestableLobbyMergeService (unit test subclass) có thể override.
        var request = await LoadMergeRequestWithLockAsync(requestId, cancellationToken)
            ?? throw new NotFoundException(LobbyMergeErrors.MergeRequestNotFound(requestId));

        // H2 fix: Cross-cafe check (chỉ staff của cafe chứa source hoặc target lobby mới reject được)
        if (request.SourceLobby?.CafeId != cafeId && request.TargetLobby?.CafeId != cafeId)
            throw new ForbiddenException(LobbyMergeErrors.StaffPermissionDenied);

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
        // H1 fix (2026-09-30): Staff permission check (missing before, security bug — any
        // logged-in user could cancel a merge request). So sánh với Create/Approve/Reject
        // đều có check này.
        var isStaff = await _cafeRepository.IsManagerOrStaffAsync(cafeId, userId, cancellationToken);
        if (!isStaff)
            throw new ForbiddenException(LobbyMergeErrors.StaffPermissionDenied);

        // FOR UPDATE lock để chống race condition H7: 2 staff bấm Cancel cùng lúc,
        // 2 staff bấm Cancel + 1 staff bấm Approve, v.v. → cả 2 thành công, audit log duplicate.
        // Dùng LoadMergeRequestWithLockAsync (protected virtual) thay vì FromSqlRaw trực tiếp
        // để TestableLobbyMergeService có thể override.
        var request = await LoadMergeRequestWithLockAsync(requestId, cancellationToken)
            ?? throw new NotFoundException(LobbyMergeErrors.MergeRequestNotFound(requestId));

        // H2 fix (2026-09-30): Cross-cafe check. Staff cafe A có thể truyền cafeId của cafe B
        // vào URL → cancel merge request của cafe B. Phải verify request thuộc cafeId.
        if (request.SourceLobby?.CafeId != cafeId && request.TargetLobby?.CafeId != cafeId)
            throw new ForbiddenException(LobbyMergeErrors.CancelPermissionDenied);

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

    /// <summary>
    /// Step 5b (Option 2 fix): Auto-promote new host for source lobby khi host cũ bị transfer.
    ///
    /// BR-USER-LIMIT-01: Mỗi lobby active phải có đúng 1 host.
    /// Edge case: source's host nằm trong activeMembers (sẽ transfer sang target) NHƯNG
    /// source vẫn còn member khác ở lại → nếu không promote, source sẽ thành lobby mồ côi.
    ///
    /// Logic:
    /// - Chỉ áp dụng cho online lobby (walk-in không có HostUserId concept).
    /// - Nếu host KHÔNG trong activeMembers → không cần promote (host vẫn ở source).
    /// - Nếu host trong activeMembers NHƯNG source không còn member nào ở lại →
    ///   Step 11 sẽ đóng source → host role tự kết thúc → không promote.
    /// - Nếu host trong activeMembers VÀ có member ở lại → tìm candidate đầu tiên
    ///   (theo JoinedAt) pass BR-USER-LIMIT-01 validation để promote thành host mới
    ///   TRƯỚC khi host cũ rời source. Nếu hết candidate pass → throw ConflictException
    ///   để abort merge (an toàn hơn để source thành lobby mồ côi).
    ///
    /// Protected virtual để unit test có thể verify logic riêng (không cần chạy full
    /// ApproveMergeAsync với FOR UPDATE).
    /// </summary>
    /// <returns>true nếu đã promote, false nếu không promote (host ở lại hoặc không có candidate).</returns>
    /// <exception cref="ConflictException">Abort merge nếu không tìm được valid new host
    /// (BR-USER-LIMIT-01 violation cho tất cả stay candidates).</exception>
    protected virtual async Task<bool> TryAutoPromoteSourceHostAsync(
        Lobby sourceLobby,
        IReadOnlyList<LobbyMember> activeMembers,
        LobbyMergeRequest mergeRequest,
        Guid staffUserId,
        CancellationToken cancellationToken)
    {
        // Chỉ áp dụng cho online lobby
        if (!sourceLobby.ReservationId.HasValue) return false;

        var hostUserId = sourceLobby.HostUserId;
        var hostIsBeingTransferred = activeMembers.Any(m => m.UserId == hostUserId);
        if (!hostIsBeingTransferred)
        {
            // Host không trong transfer set → không cần promote
            return false;
        }

        var transferUserIds = activeMembers.Select(m => m.UserId).ToHashSet();
        var allSourceMembers = await _lobbyMemberRepository.GetByLobbyAsync(
            sourceLobby.Id, cancellationToken);

        // Member sẽ ở lại source = active, chưa transfer, chưa rời
        var stayCandidates = allSourceMembers
            .Where(m => m.IsActive
                && m.Status != LobbyMemberStatus.Left
                && m.Status != LobbyMemberStatus.Kicked
                && m.Status != LobbyMemberStatus.LobbyTerminated
                && !transferUserIds.Contains(m.UserId))
            .OrderBy(m => m.JoinedAt)
            .ToList();

        if (stayCandidates.Count == 0)
        {
            // Không có member ở lại → Step 11 sẽ đóng source → không cần promote
            _logger.LogInformation(
                "LobbyMerge: source host {HostId} transferring with no remaining members — " +
                "source will dissolve after Step 11 (stillActive == 0), no promote needed",
                sourceLobby.HostUserId, sourceLobby.Id);
            return false;
        }

        // ===== Gap #1 fix: BR-USER-LIMIT-01 validation cho new host =====
        // Loop qua stayCandidates theo JoinedAt, pick candidate đầu tiên KHÔNG vi phạm
        // BR-USER-LIMIT-01 (chưa host/member lobby active khác). Nếu hết → abort merge.
        LobbyMember? newHostMember = null;
        var skippedCandidates = new List<Guid>();

        foreach (var candidate in stayCandidates)
        {
            // BR-USER-LIMIT-01: new host KHÔNG được đang là host của lobby active khác.
            // BR-USER-LIMIT-05 (đã bỏ BR ngày 2026-09-12): host được phép transfer sang lobby
            // khác, NHƯNG BR-USER-LIMIT-01 vẫn giữ cap tổng lobby ≤ 2 active cho mỗi user.
            //
            // Lưu ý: candidate có thể đã là member của lobby active khác. Nếu promote
            // candidate đó thành host của source → tổng lobby của user đó sẽ vượt 2 (source
            // mới + lobby đang làm member). Vi phạm BR-USER-LIMIT-01.
            //
            // Skip candidate này, log lý do, thử candidate kế tiếp.
            var newHostActiveLobbies = await _lobbyRepository.GetActiveLobbiesByHostAsync(
                candidate.UserId, cancellationToken);
            var newHostMemberOfLobbies = await _lobbyRepository.GetActiveLobbiesByMemberAsync(
                candidate.UserId, cancellationToken);

            if (newHostActiveLobbies.Count + newHostMemberOfLobbies.Count >= 2)
            {
                _logger.LogWarning(
                    "LobbyMerge: skip candidate {UserId} for new host (BR-USER-LIMIT-01: " +
                    "{Active} host + {Member} member = {Total} ≥ 2 active lobbies)",
                    candidate.UserId, newHostActiveLobbies.Count, newHostMemberOfLobbies.Count,
                    newHostActiveLobbies.Count + newHostMemberOfLobbies.Count);
                skippedCandidates.Add(candidate.UserId);
                continue;
            }

            if (newHostMemberOfLobbies.Count > 0)
            {
                _logger.LogWarning(
                    "LobbyMerge: skip candidate {UserId} for new host (BR-USER-LIMIT-01: " +
                    "đang là member của {Count} lobby active khác)",
                    candidate.UserId, newHostMemberOfLobbies.Count);
                skippedCandidates.Add(candidate.UserId);
                continue;
            }

            // Candidate pass BR-USER-LIMIT-01
            newHostMember = candidate;
            break;
        }

        if (newHostMember == null)
        {
            // Không tìm được candidate valid → ABORT merge để tránh source lobby thành mồ côi
            // (không có host sau khi host cũ chuyển sang target).
            //
            // Lý do abort (thay vì cho phép merge): an toàn hơn cho data integrity. Staff
            // có thể retry sau khi một trong các skipped candidate rời lobby active khác.
            throw new ConflictException(
                $"Không thể ghép lobby: host cũ đang chuyển sang lobby đích nhưng {stayCandidates.Count} " +
                $"thành viên ở lại đều đã đạt giới hạn BR-USER-LIMIT-01 (host 1 lobby + member 1 lobby khác). " +
                $"Vui lòng yêu cầu các thành viên ở lại rời lobby khác trước, hoặc hủy phòng cũ.");
        }

        var oldHostUserId = sourceLobby.HostUserId;

        // Update source lobby HostUserId → new host
        sourceLobby.HostUserId = newHostMember.UserId;
        sourceLobby.UpdatedAt = DateTime.UtcNow;

        // Update LobbyMember.IsHost flags
        var oldHostMember = activeMembers.First(m => m.UserId == oldHostUserId);
        oldHostMember.IsHost = false;
        newHostMember.IsHost = true;

        // Audit log
        var promoteAudit = CreateAuditLog(
            mergeRequest.Id, sourceLobby.Id, mergeRequest.TargetLobbyId,
            sourceLobby.ReservationId, mergeRequest.TargetLobby?.ReservationId,
            staffUserId, "HostAutoPromotedOnMerge",
            new
            {
                previousHostUserId = oldHostUserId,
                newHostUserId = newHostMember.UserId,
                reason = "SourceHostTransferredByMerge",
                stayMembersCount = stayCandidates.Count,
                skippedCandidatesCount = skippedCandidates.Count,
                skippedCandidateIds = skippedCandidates
            },
            success: true);
        _db.LobbyMergeAuditLogs.Add(promoteAudit);

        // ===== Gap #4 fix: Expire pending invites của new host trên source lobby =====
        // BR-LOBBY-INVITE-09: Lobby terminal → expire tất cả pending invites. Tuy source chưa
        // terminal (vẫn còn members), nhưng new host vừa được promote đồng nghĩa với việc
        // họ đã "nhận trách nhiệm" host của source — invites pending cho họ trên lobby
        // này giờ không còn ý nghĩa (lobby vẫn active nhưng họ đã ở trong với tư cách host).
        await _lobbyInviteRepository.ExpirePendingForUsersAsync(
            lobbyId: sourceLobby.Id,
            inviteeIds: new List<Guid> { newHostMember.UserId },
            cancellationToken);

        // ===== Gap #5 fix: SignalR NotifyHostChanged cho new host =====
        // Client (Flutter app, POS) hiển thị HostUserId từ lobby detail. Nếu không notify,
        // UI vẫn hiển thị old host → UX broken (action gửi tin nhắn với Host role sẽ fail).
        try
        {
            await _lobbyHubService.NotifyHostChanged(sourceLobby.Id, newHostMember.UserId);
        }
        catch (Exception ex)
        {
            // Fire-and-forget: promote đã commit in-memory, notification lỗi không rollback.
            // Old host đã được transfer sang target (sẽ save ở Step 10a), new host đã được set.
            _logger.LogWarning(ex,
                "LobbyMerge: failed to NotifyHostChanged for new host {NewHostId} on source {SourceId}",
                newHostMember.UserId, sourceLobby.Id);
        }

        _logger.LogInformation(
            "LobbyMerge: auto-promoted user {NewHostId} as new host of source lobby {SourceId} " +
            "(previous host {OldHostId} transferred to target, skipped {Skipped} candidates with BR-USER-LIMIT-01 violation)",
            newHostMember.UserId, sourceLobby.Id, oldHostUserId, skippedCandidates.Count);

        return true;
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
    /// Normalize SelectedMemberIds input: trim null/empty/default Guid, dedupe.
    /// Trả về empty list nếu input null/empty/all Guid.Empty.
    /// Empty list nghĩa là "transfer all" (backward compatible).
    /// </summary>
    private static List<Guid> NormalizeSelectedMemberIds(List<Guid>? input)
    {
        if (input == null) return new List<Guid>();
        return input
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Deserialize SelectedMemberIdsJson từ LobbyMergeRequest.
    /// Return null nếu JSON rỗng/invalid (giữ backward compatible).
    /// </summary>
    private static List<Guid>? DeserializeSelectedMemberIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(json);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

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
            FitsCapacity = r.FitsCapacity,
            SelectedMemberIds = DeserializeSelectedMemberIds(r.SelectedMemberIdsJson)
        };
    }

    // ===== M1 / Exception 4: Deposit transfer helpers (CẬP NHẬT 2026-10-01) =====
    // docs/design/host-deposit-discount-and-bvc-payment-design.md §B3.1 + §B3.2

    /// <summary>
    /// Append một value vào CSV string (idempotent — không duplicate nếu đã tồn tại).
    /// Dùng cho <c>CarriedOverFromReservationIds</c> và <c>CarriedOverFromUserIds</c>.
    /// </summary>
    private static string AppendCsv(string? existing, string newValue)
    {
        if (string.IsNullOrEmpty(existing)) return newValue;
        var set = new HashSet<string>(existing.Split(',', StringSplitOptions.RemoveEmptyEntries));
        set.Add(newValue);
        return string.Join(",", set);
    }

    /// <summary>
    /// Release source deposit về Host's wallet khi KHÔNG thể carry over:
    /// - Target lobby là walk-in (ReservationId == null) → không có reservation để accumulate.
    /// - Target reservation không tìm thấy (DB inconsistency) → fallback an toàn.
    /// Idempotent qua idempotencyKey chứa reservationId + timestamp.
    /// </summary>
    private async Task ReleaseDepositToWalletAsync(
        Reservation sourceReservation,
        long amountBvc,
        CancellationToken cancellationToken)
    {
        if (amountBvc <= 0) return;

        var idempotencyKey = $"release-source-merge-{sourceReservation.Id}-{DateTime.UtcNow:o}";

        // BR-REFUND-01: hoàn deposit về Host's wallet khi lobby terminal.
        // Dùng ledger entry DepositRelease (existing value 10 — không thay đổi enum).
        // Gọi trực tiếp wallet service thay vì qua controller — đã trong transaction context.
        if (_walletService != null)
        {
            await _walletService.ReleaseDepositAsync(
                userId: sourceReservation.HostId,
                amountBvc: amountBvc,
                relatedLobbyId: null,
                relatedReservationId: sourceReservation.Id,
                idempotencyKey: idempotencyKey);
        }
        else
        {
            _logger.LogWarning(
                "LobbyMerge: _walletService is null — cannot release {Amount} BVC to wallet for host {HostId}. " +
                "Source reservation {ResId} stays in heldBalance (admin intervention required).",
                amountBvc, sourceReservation.HostId, sourceReservation.Id);
        }
    }

    /// <summary>
    /// M2 / Gap #33 fix (2026-10-01): Sau khi merge approve đóng orphan ActiveSession của source
    /// lobby, phải release <c>CafeTables.Status</c> và <c>CafeInventoryBoxes.Status</c> về Available.
    /// <para>
    /// Trước đây code chỉ set <c>ActiveSession.Status = GroupSessionStatus.Closed</c> nhưng KHÔNG
    /// đụng vào bàn/box → <c>CafeTables.Status = InUse</c> còn nguyên → walk-in mới
    /// <c>StartGameSessionAsync</c> bị reject với
    /// <c>ApiErrorMessages.Pos.TableNotAvailableForGame</c> ("đang được giữ hoặc trong sự kiện").
    /// </para>
    /// <para>
    /// <b>Best-effort:</b> lỗi release KHÔNG làm fail cả merge — chỉ log warning + ghi audit log.
    /// Background job <c>AutoReleaseExpiredSessionsJob</c> sẽ retry release cho session
    /// <c>Status = Closed</c> còn <c>CafeTableId.HasValue</c> ở lần chạy kế tiếp (xem
    /// <c>BoardVerse.API/BackgroundServices/AutoReleaseExpiredSessionsJob.cs</c>).
    /// </para>
    /// <para>
    /// Dùng <c>IActiveSessionRepository.ReleaseSessionTableAndBoxAsync</c> — cùng helper với
    /// <c>PaySessionAsync</c> + <c>ForceCloseService</c> + <c>AutoReleaseExpiredSessionsJob</c>
    /// để đảm bảo consistent side-effects (idempotent: chỉ flip status khi đang InUse).
    /// </para>
    /// </summary>
    /// <param name="orphanSession">ActiveSession vừa bị close ở Step 11 (Status = Closed).</param>
    /// <param name="mergeRequest">Merge request hiện tại — dùng cho audit log.</param>
    /// <param name="sourceLobbyId">Source lobby đã dissolve — 2FA audit trail.</param>
    /// <param name="targetLobbyId">Target lobby — audit trail.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// <c>protected virtual</c> để unit test có thể exercise trực tiếp (qua wrapper
    /// <c>InvokeReleaseSourceSessionResourcesAsync</c>) mà không cần chạy full
    /// <c>ApproveMergeAsync</c> happy path (gặp khó khăn vì FromSqlRaw FOR UPDATE
    /// không hoạt động với InMemory DB).
    /// </remarks>
    protected virtual async Task ReleaseSourceSessionResourcesAsync(
        ActiveSession orphanSession,
        LobbyMergeRequest mergeRequest,
        Guid sourceLobbyId,
        Guid targetLobbyId,
        CancellationToken cancellationToken)
    {
        var sessionId = orphanSession.Id;
        var tableId = orphanSession.CafeTableId;
        var boxId = orphanSession.CafeInventoryBoxId;

        try
        {
            await _activeSessionRepository.ReleaseSessionTableAndBoxAsync(
                sessionId, cancellationToken);

            _logger.LogInformation(
                "LobbyMerge: released source session {SessionId} resources → CafeTable {TableId}, " +
                "CafeInventoryBox {BoxId} back to Available (after source lobby {SourceLobbyId} dissolved)",
                sessionId, tableId, boxId, sourceLobbyId);

            // Ghi LobbyMergeAuditLog event: SourceSessionResourcesReleased.
            // Audit trail quan trọng cho việc truy ngược "tại sao bàn này Available
            // mà session Status = Closed" sau này.
            var releaseAudit = CreateAuditLog(
                mergeRequest.Id, sourceLobbyId, targetLobbyId,
                mergeRequest.SourceLobby?.ReservationId,
                mergeRequest.TargetLobby?.ReservationId,
                mergeRequest.ReviewedByUserId ?? Guid.Empty, "SourceSessionResourcesReleased",
                new
                {
                    orphanSessionId = sessionId,
                    releasedTableId = tableId,
                    releasedBoxId = boxId,
                    previousSessionStatus = GroupSessionStatus.Closed,
                    releasedAt = DateTime.UtcNow,
                    reason = "SourceLobbyDissolved_AfterLastMemberMerged"
                },
                success: true);
            _db.LobbyMergeAuditLogs.Add(releaseAudit);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "LobbyMerge: FAILED to release source session {SessionId} table/box " +
                "(CafeTableId={TableId}, CafeInventoryBoxId={BoxId}). " +
                "Background job AutoReleaseExpiredSessionsJob sẽ retry. " +
                "Source lobby {SourceLobbyId} still dissolved (merge commit OK).",
                sessionId, tableId, boxId, sourceLobbyId);

            // Vẫn ghi audit log dù fail — để admin dễ trace.
            var releaseFailAudit = CreateAuditLog(
                mergeRequest.Id, sourceLobbyId, targetLobbyId,
                mergeRequest.SourceLobby?.ReservationId,
                mergeRequest.TargetLobby?.ReservationId,
                mergeRequest.ReviewedByUserId ?? Guid.Empty, "SourceSessionResourcesReleasedFailed",
                new
                {
                    orphanSessionId = sessionId,
                    releasedTableId = tableId,
                    releasedBoxId = boxId,
                    errorMessage = ex.Message,
                    willBeRetriedBy = "AutoReleaseExpiredSessionsJob",
                    failedAt = DateTime.UtcNow
                },
                success: false, errorMessage: ex.Message);
            _db.LobbyMergeAuditLogs.Add(releaseFailAudit);
        }
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
