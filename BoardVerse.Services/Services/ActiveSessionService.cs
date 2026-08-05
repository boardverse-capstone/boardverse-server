using BoardVerse.Core.DTOs.Pos;
using BoardVerse.Core.DTOs.Session;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services
{
    public class ActiveSessionService : IActiveSessionService
    {
        private readonly ICafeRepository _cafeRepository;
        private readonly IActiveSessionRepository _activeSessionRepository;
        private readonly ICafePosRepository _posRepository;
        private readonly IBookingDepositRepository _depositRepository;
        private readonly ISettlementService _settlementService;
        private readonly IReservationService _reservationService;
        private readonly ILogger<ActiveSessionService> _logger;

        public ActiveSessionService(
            ICafeRepository cafeRepository,
            IActiveSessionRepository activeSessionRepository,
            ICafePosRepository posRepository,
            IBookingDepositRepository depositRepository,
            ISettlementService settlementService,
            IReservationService reservationService,
            ILogger<ActiveSessionService> logger)
        {
            _cafeRepository = cafeRepository;
            _activeSessionRepository = activeSessionRepository;
            _posRepository = posRepository;
            _depositRepository = depositRepository;
            _settlementService = settlementService;
            _reservationService = reservationService;
            _logger = logger;
        }

        public async Task<ActiveSessionResponseDto> StartSessionAsync(Guid cafeId, Guid hostUserId, StartSessionRequestDto request)
        {
            var cafe = await _cafeRepository.GetActiveByIdAsync(cafeId)
                ?? throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));

            var session = new ActiveSession
            {
                Id = Guid.NewGuid(),
                CafeId = cafeId,
                HostId = hostUserId,
                CafeTableId = request.CafeTableId == Guid.Empty ? null : request.CafeTableId,
                CafeInventoryBoxId = null, // attach game after start (BR-06-like)
                GameTemplateId = request.GameTemplateId,
                LobbyId = request.LobbyId,
                Status = GroupSessionStatus.Active,
                StartedAt = DateTime.UtcNow,
                TotalMinutesPlayed = 0,
                Subtotal = 0,
                DepositAppliedAmount = 0,
                TotalAmount = 0,
                CreatedAt = DateTime.UtcNow
            };

            await _activeSessionRepository.AddAsync(session);

            if (request.InitialMemberUserIds != null)
            {
                foreach (var memberId in request.InitialMemberUserIds)
                {
                    await _activeSessionRepository.AddMemberAsync(new ActiveSessionMember
                    {
                        Id = Guid.NewGuid(),
                        ActiveSessionId = session.Id,
                        UserId = memberId,
                        Status = IndividualSessionStatus.Playing,
                        JoinedAt = DateTime.UtcNow
                    });
                }
            }

            // P0 Fix #3: Set table to InUse when session starts
            if (session.CafeTableId.HasValue)
            {
                var table = await _posRepository.GetTableAsync(cafeId, session.CafeTableId.Value);
                if (table != null)
                {
                    table.Status = CafeTableStatus.InUse;
                    // P1 Fix #5: Explicitly update table to ensure persistence
                    await _posRepository.UpdateTableAsync(table);
                }
            }

            // P0 Fix #3: Set box to InUse when attached to session (via barcode)
            if (!string.IsNullOrEmpty(request.Barcode))
            {
                var box = await _posRepository.GetBoxByBarcodeAsync(cafeId, request.Barcode);
                if (box != null)
                {
                    box.Status = CafeGameInventoryStatus.InUse;
                    box.UpdatedAt = DateTime.UtcNow;
                    await _posRepository.UpdateInventoryBoxAsync(box);
                    session.CafeInventoryBoxId = box.Id;
                }
            }

            await _activeSessionRepository.SaveChangesAsync();

            return MapSessionDto(session);
        }

        public async Task<ActiveSessionResponseDto> CheckoutAsync(Guid cafeId, Guid sessionId, CheckoutRequestDto request)
        {
            var session = await _activeSessionRepository.GetByIdAsync(sessionId)
                ?? throw new NotFoundException(ApiErrorMessages.Pos.SessionNotFound(cafeId, sessionId));

            // GAP-8 Fix: Validate cafeId matches session's CafeId
            if (session.CafeId != cafeId)
            {
                throw new ConflictException($"Phiên chơi '{sessionId}' không thuộc quán '{cafeId}'.");
            }

            // BR-12: Checkout chỉ được từ Checking (sau khi EndGameSession)
            // Không cho phép checkout trực tiếp từ Active mà chưa qua EndGameSession
            if (session.Status != GroupSessionStatus.Checking)
            {
                throw new ConflictException("Phiên chơi phải ở trạng thái CHECKING (đã trả game) để thanh toán. Vui lòng bấm 'Trả game' trước.");
            }

            // BR-12: BẮT BUỘC kiểm tra checklist trước khi checkout
            // Tất cả game trong session phải được kiểm tra (CheckStatus != NotChecked)
            var isFullyChecked = await _posRepository.IsSessionFullyCheckedAsync(sessionId);
            if (!isFullyChecked)
            {
                var games = await _posRepository.GetSessionGamesAsync(sessionId);
                var uncheckedCount = games.Count(g => g.CheckStatus == ComponentCheckStatus.NotChecked);
                throw new BadRequestException(ApiErrorMessages.Pos.ChecklistNotCompleteForGames(uncheckedCount));
            }

            session.IsCheckingInventory = false;
            session.HasMissingComponents = false;

            return await CompleteCheckoutAsync(session, request.Components);
        }

        public async Task<ActiveSessionResponseDto> AddGuestSlotAsync(Guid cafeId, Guid sessionId, AddGuestSlotRequestDto request)
        {
            var session = await _activeSessionRepository.GetByIdAsync(sessionId)
                ?? throw new NotFoundException(ApiErrorMessages.Pos.SessionNotFound(cafeId, sessionId));

            // BR-13: Guest slot được thêm khi phiên đang Active
            if (session.Status != GroupSessionStatus.Active && session.Status != GroupSessionStatus.Checking)
            {
                throw new ConflictException("Không thể thêm khách vô danh sau khi phiên chơi đã kết thúc.");
            }

            await _activeSessionRepository.AddMemberAsync(new ActiveSessionMember
            {
                Id = Guid.NewGuid(),
                ActiveSessionId = session.Id,
                UserId = null,
                IsGuestSlot = true,
                GuestDisplayName = request.DisplayName,
                Status = IndividualSessionStatus.Playing,
                JoinedAt = DateTime.UtcNow
            });

            await _activeSessionRepository.SaveChangesAsync();

            return MapSessionDto(session);
        }

        public async Task<ActiveSessionResponseDto> PartialCheckoutAsync(Guid cafeId, Guid sessionId, PartialCheckoutRequestDto request)
        {
            var session = await _activeSessionRepository.GetByIdAsync(sessionId)
                ?? throw new NotFoundException(ApiErrorMessages.Pos.SessionNotFound(cafeId, sessionId));

            // GAP-8 Fix: Validate cafeId matches session's CafeId
            if (session.CafeId != cafeId)
            {
                throw new ConflictException($"Phiên chơi '{sessionId}' không thuộc quán '{cafeId}'.");
            }

            // BR-12: Partial checkout phải từ CHECKING (đã trả game), không phải ACTIVE trực tiếp
            // GAP-29 Fix: Để partial checkout, nhân viên phải bấm "Trả game" trước (EndGameSession)
            // để kiểm tra linh kiện trước khi cho thành viên về sớm
            if (session.Status != GroupSessionStatus.Checking)
            {
                throw new ConflictException(
                    "Phiên chơi phải ở trạng thái CHECKING (đã bấm 'Trả game') để thanh toán một phần. Vui lòng bấm 'Trả game' trước.");
            }

            if (request.MemberIds.Count == 0)
            {
                throw new BadRequestException("Cần chọn ít nhất 1 thành viên để thanh toán một phần.");
            }

            // BUG-1 Fix: Validate selected members are in Playing status
            // Only members who are currently Playing can be checked out early
            var invalidMembers = session.Members
                .Where(m => request.MemberIds.Contains(m.Id)
                    && m.Status != IndividualSessionStatus.Playing)
                .ToList();

            if (invalidMembers.Count > 0)
            {
                var invalidStatuses = string.Join(", ", invalidMembers.Select(m => m.Status));
                throw new ConflictException(
                    $"Chỉ thành viên đang chơi mới có thể thanh toán một phần. Trạng thái không hợp lệ: {invalidStatuses}.");
            }

            // Mark selected members as SUSPENDED_MUTATION (waiting for inventory check)
            // BR-12: They cannot be charged until inventory is verified
            foreach (var member in session.Members.Where(m => request.MemberIds.Contains(m.Id)))
            {
                member.Status = IndividualSessionStatus.SuspendedMutation;
                member.LeftAt = DateTime.UtcNow;
                // P0 Fix #4: Explicitly update each member to ensure persistence
                await _activeSessionRepository.UpdateMemberAsync(member);
            }

            session.IsCheckingInventory = true;
            session.Status = GroupSessionStatus.Checking;
            await _activeSessionRepository.SaveChangesAsync();

            return MapSessionDto(session);
        }

        /// <summary>
        /// Trả game toàn bộ - chuyển session sang CHECKING để kiểm kê linh kiện.
        /// Đây là bước bắt buộc trước khi checkout (BR-12).
        /// </summary>
        public async Task<ActiveSessionResponseDto> EndGameAsync(Guid cafeId, Guid sessionId)
        {
            var session = await _activeSessionRepository.GetByIdAsync(sessionId)
                ?? throw new NotFoundException(ApiErrorMessages.Pos.SessionNotFound(cafeId, sessionId));

            if (session.Status != GroupSessionStatus.Active)
            {
                throw new ConflictException("Phiên chơi phải đang ở trạng thái ACTIVE để trả game.");
            }

            // BUG-2 Fix: Validate that at least one game is attached before entering CHECKING
            // A session should have games before returning them
            if ((session.Games == null || session.Games.Count == 0) && !session.CafeInventoryBoxId.HasValue)
            {
                throw new ConflictException(
                    "Phiên chơi chưa có game nào được gán. Vui lòng gán game trước khi trả game.");
            }

            var now = DateTime.UtcNow;

            // Mark all currently playing members as SuspendedMutation for inventory check
            foreach (var member in session.Members.Where(m => m.Status == IndividualSessionStatus.Playing))
            {
                member.Status = IndividualSessionStatus.SuspendedMutation;
                member.LeftAt = now;
            }

            // Fix: Set EndedAt so minutes can be calculated correctly at checkout
            session.EndedAt = now;
            session.IsCheckingInventory = true;
            session.Status = GroupSessionStatus.Checking;
            await _activeSessionRepository.SaveChangesAsync();

            return MapSessionDto(session);
        }

        public async Task<ActiveSessionResponseDto> GetSessionAsync(Guid cafeId, Guid sessionId)
        {
            var session = await _activeSessionRepository.GetByIdAsync(sessionId)
                ?? throw new NotFoundException(ApiErrorMessages.Pos.SessionNotFound(cafeId, sessionId));

            return MapSessionDto(session);
        }

        /// <summary>
        /// Ghép thành viên vào phiên chơi của nhóm mới.
        /// Exception 4: A3 nhảy từ nhóm A sang nhóm B.
        /// - A3 đang ở trạng thái SUSPENDED_MUTATION sau khi kiểm kê ở nhóm cũ
        /// - Nhân viên quét mã A3 → ghép vào nhóm B
        /// - A3 không mất thời gian, tổng thời gian tính liên tục từ lúc ban đầu
        /// </summary>
        public async Task<MergeSessionResponseDto> MergeSessionAsync(Guid cafeId, Guid sourceSessionId, MergeSessionRequestDto request)
        {
            var sourceSession = await _activeSessionRepository.GetByIdAsync(sourceSessionId)
                ?? throw new NotFoundException(ApiErrorMessages.Pos.SessionNotFound(cafeId, sourceSessionId));

            // P1 Fix #2: Validate source session status before merge
            if (sourceSession.Status is not (GroupSessionStatus.Active or GroupSessionStatus.Checking))
            {
                throw new ConflictException(ApiErrorMessages.Pos.SessionSourceNotValidForMerge);
            }

            var member = await _activeSessionRepository.GetMemberByIdAsync(request.MemberId)
                ?? throw new NotFoundException($"Không tìm thấy thành viên '{request.MemberId}'.");

            if (member.ActiveSessionId != sourceSessionId)
            {
                throw new ConflictException("Thành viên không thuộc phiên chơi nguồn.");
            }

            if (member.Status != IndividualSessionStatus.SuspendedMutation)
            {
                throw new ConflictException("Thành viên phải ở trạng thái SUSPENDED_MUTATION để có thể ghép nhóm.");
            }

            var targetSession = await _activeSessionRepository.GetByIdAsync(request.TargetSessionId)
                ?? throw new NotFoundException(ApiErrorMessages.Pos.SessionNotFound(cafeId, request.TargetSessionId));

            if (targetSession.Status != GroupSessionStatus.Active)
            {
                throw new ConflictException("Phiên chơi đích phải đang hoạt động.");
            }

            if (targetSession.CafeId != cafeId)
            {
                throw new ConflictException("Không thể ghép thành viên sang phiên chơi của quán khác.");
            }

            member.ActiveSessionId = request.TargetSessionId;
            member.Status = IndividualSessionStatus.Playing;

            await _activeSessionRepository.UpdateMemberAsync(member);
            await _activeSessionRepository.SaveChangesAsync();

            // P1 Fix #7: Add null check after re-fetch
            var updatedSession = await _activeSessionRepository.GetByIdAsync(request.TargetSessionId);
            if (updatedSession == null)
            {
                throw new NotFoundException(ApiErrorMessages.Pos.SessionNotFound(cafeId, request.TargetSessionId));
            }

            // GAP-12 Fix: Set OriginalSessionId to track the original session start time
            // When calculating billing for merged members, use OriginalSession.StartedAt as the base
            // to ensure continuous time tracking (A3's total time = time from original session start)
            member.OriginalSessionId ??= sourceSessionId;

            await _activeSessionRepository.UpdateMemberAsync(member);
            await _activeSessionRepository.SaveChangesAsync();

            return new MergeSessionResponseDto
            {
                MemberId = request.MemberId,
                SourceSessionId = sourceSessionId,
                TargetSessionId = request.TargetSessionId,
                MergedAt = DateTime.UtcNow,
                TargetSession = MapSessionDto(updatedSession)
            };
        }

        /// <summary>
        /// Thanh toán hóa đơn tổng của phiên chơi.
        /// BR-15: TotalAmount = Subtotal + PenaltyAmount - DepositAppliedAmount
        /// BR-16: Tính phí theo mô hình quán (thời gian thực hoặc vào cổng trọn gói)
        /// Per-member billing: Mỗi thành viên chịu phí dựa trên thời gian tham gia thực tế.
        /// </summary>
        public async Task<PaySessionResponseDto> PaySessionAsync(Guid cafeId, Guid sessionId, PaySessionRequestDto request)
        {
            var session = await _activeSessionRepository.GetByIdAsync(sessionId)
                ?? throw new NotFoundException(ApiErrorMessages.Pos.SessionNotFound(cafeId, sessionId));

            // GAP-8 Fix: Validate cafeId matches session's CafeId
            if (session.CafeId != cafeId)
            {
                throw new ConflictException($"Phiên chơi '{sessionId}' không thuộc quán '{cafeId}'.");
            }

            if (session.Status != GroupSessionStatus.Unpaid)
            {
                throw new ConflictException("Phiên chơi phải ở trạng thái UNPAID để thanh toán.");
            }

            var cafe = await _cafeRepository.GetActiveByIdAsync(cafeId)
                ?? throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));

            var now = DateTime.UtcNow;

            // Calculate elapsed minutes for the session (all members play the same duration)
            var elapsedMinutes = session.EndedAt.HasValue
                ? (int)Math.Floor((session.EndedAt.Value - session.StartedAt).TotalMinutes)
                : (int)Math.Floor((now - session.StartedAt).TotalMinutes);
            elapsedMinutes = Math.Max(0, elapsedMinutes);

            // BR-16: Calculate group subtotal based on cafe billing model
            // All members at a table play together for the same duration
            decimal totalGroupSubtotal = 0;
            if (cafe.BillingModel == CafePartnerBillingModel.TimeBased)
            {
                // BR-16: Time-based billing (hourly first + progressive blocks)
                totalGroupSubtotal = CalculateRealtimeBilling(cafe, elapsedMinutes);
            }
            else
            {
                // BR-16: Flat-rate - entry fee only
                totalGroupSubtotal = cafe.BasePrice;
            }

            // BR-15: Calculate per-member minutes and individual subtotals for display
            // (Members may leave at different times, track separately for audit)
            decimal totalMemberSubtotal = 0;
            foreach (var member in session.Members)
            {
                var memberLeftAt = member.LeftAt ?? now;
                var memberMinutes = (int)Math.Floor((memberLeftAt - member.JoinedAt).TotalMinutes);
                memberMinutes = Math.Max(0, memberMinutes);
                member.TotalMinutesPlayed = memberMinutes;

                // Per-member subtotal (for display/audit, actual charge uses group subtotal)
                // BR-16: Each member pays based on session duration, not individual time
                decimal memberSubtotal = totalGroupSubtotal;
                memberSubtotal = Math.Max(0, memberSubtotal);
                totalMemberSubtotal += memberSubtotal;
            }

            session.TotalMinutesPlayed = elapsedMinutes;
            session.Subtotal = totalGroupSubtotal;

            // BR-14: Validate penalties before assignment
            if (request.PenaltyItems != null && request.PenaltyItems.Count > 0)
            {
                foreach (var penalty in request.PenaltyItems)
                {
                    if (penalty.ResponsibleMemberId.HasValue)
                    {
                        var member = session.Members.FirstOrDefault(m => m.Id == penalty.ResponsibleMemberId.Value);
                        if (member?.IsGuestSlot == true)
                        {
                            // BR-14: Cannot assign penalty to Guest_Slot
                            throw new BadRequestException("Không thể gán phí phạt cho khách vô danh. Vui lòng gán vào hóa đơn của người khởi tạo (Host) hoặc thu tiền mặt trực tiếp. BR-14.");
                        }
                        // BR-14: Track per-member penalty
                        if (member != null)
                        {
                            member.PenaltyAmount += penalty.PenaltyAmount;
                            member.IsPenaltyPaid = true;
                        }
                    }
                    session.PenaltyAmount += penalty.PenaltyAmount;
                }
            }

            // BR-12: Read persisted penalty from component checks (single source of truth)
            var sessionGames = await _posRepository.GetSessionGamesAsync(sessionId);
            var persistedPenalty = sessionGames
                .Where(g => g.CheckStatus == ComponentCheckStatus.MissingComponents)
                .Sum(g => g.TotalPenaltyAmount);
            if (persistedPenalty > 0)
            {
                session.PenaltyAmount += persistedPenalty;
            }

            // BR-15: TotalAmount = Subtotal + PenaltyAmount (KHÔNG trừ deposit)
            // Deposit chỉ dùng để giữ chỗ, không cấn trừ vào hóa đơn
            session.TotalAmount = session.Subtotal + session.PenaltyAmount;
            session.Status = GroupSessionStatus.Paid;
            session.PaidAt = now;

            // Persist billing + status changes first — cleanup will use a separate SaveChangesAsync.
            await _activeSessionRepository.SaveChangesAsync();

            // Lifecycle cleanup: mark members checked out, release box + table, close lobby.
            // Idempotent — safe even if called multiple times.
            // Also fixes the box/table "ghost" bug where box was force-overwritten to Available
            // even if it was in a non-rentable state (e.g. Lost/Maintenance).
            await _activeSessionRepository.CompleteSessionPaymentCleanupAsync(sessionId);

            // BR §21A.8 + BR-REVENUE-01: capture BVC deposit về doanh thu quán.
            // No-op cho session không liên kết Lobby (legacy BookingDeposit flow).
            if (session.LobbyId.HasValue)
            {
                try
                {
                    await _reservationService.CompleteAndCaptureAsync(session.LobbyId.Value, sessionId);
                }
                catch (Exception ex)
                {
                    // Không fail cả PaySessionAsync nếu capture lỗi — BVC vẫn ở heldBalance,
                    // có thể retry qua background job sau.
                    _logger.LogError(ex,
                        "CompleteAndCaptureAsync failed. SessionId={SessionId}, LobbyId={LobbyId}. BVC vẫn held — cần retry.",
                        sessionId, session.LobbyId.Value);
                }
            }

            var finalSession = await _activeSessionRepository.GetByIdAsync(sessionId);

            // GAP-33 Fix: Build per-member invoices
            var memberInvoices = BuildMemberInvoices(session, totalGroupSubtotal, request.PenaltyItems);

            // GAP-34 Fix: Determine BVC capture status
            var bvcCaptureStatus = DetermineBvcCaptureStatus(session);

            return new PaySessionResponseDto
            {
                SessionId = sessionId,
                Subtotal = session.Subtotal,
                PenaltyAmount = session.PenaltyAmount,
                DepositAppliedAmount = session.DepositAppliedAmount,
                TotalAmount = session.TotalAmount,
                PaidAt = now,
                MemberInvoices = memberInvoices,
                BvcCaptureStatus = bvcCaptureStatus,
                Session = MapSessionDto(finalSession!)
            };
        }

        /// <summary>
        /// Build per-member invoices for PaySession response.
        /// GAP-33 Fix: Return detailed per-member breakdown.
        /// GAP-12 Fix: Use OriginalSession.StartedAt for merged members to track continuous time.
        /// </summary>
        private List<MemberInvoiceDto> BuildMemberInvoices(
            ActiveSession session,
            decimal groupSubtotal,
            List<ComponentPenaltyItemDto>? penaltyItems)
        {
            var invoices = new List<MemberInvoiceDto>();
            var now = DateTime.UtcNow;

            // GAP-12 Fix: Cache original session start times for merged members
            var originalSessionStarts = new Dictionary<Guid, DateTime>();
            foreach (var member in session.Members.Where(m => m.OriginalSessionId.HasValue))
            {
                if (!originalSessionStarts.ContainsKey(member.OriginalSessionId.Value))
                {
                    var originalSession = session.Members
                        .FirstOrDefault(m => m.ActiveSessionId == member.OriginalSessionId)?
                        .ActiveSession;
                    if (originalSession != null)
                    {
                        originalSessionStarts[member.OriginalSessionId.Value] = originalSession.StartedAt;
                    }
                }
            }

            foreach (var member in session.Members)
            {
                // GAP-12 Fix: For merged members, use the original session start time
                // to ensure continuous time tracking (A3's total time = time from original session start)
                var baseStartTime = member.OriginalSessionId.HasValue && originalSessionStarts.TryGetValue(member.OriginalSessionId.Value, out var originalStart)
                    ? originalStart
                    : member.JoinedAt;

                var memberLeftAt = member.LeftAt ?? now;
                var memberMinutes = (int)Math.Floor((memberLeftAt - baseStartTime).TotalMinutes);
                memberMinutes = Math.Max(0, memberMinutes);

                // Calculate member's subtotal (share of group subtotal based on play duration)
                decimal memberSubtotal = memberMinutes > 0 ? groupSubtotal : 0;

                // Get member's penalty from request
                var memberPenalty = penaltyItems
                    ?.Where(p => p.ResponsibleMemberId == member.Id)
                    .Sum(p => p.PenaltyAmount) ?? 0;

                // Add persisted penalty from component checks
                if (member.PenaltyAmount > 0)
                {
                    memberPenalty += member.PenaltyAmount;
                }

                // BR-15: Member total = Subtotal + Penalty - Deposit (BR-09: Deposit không trừ)
                // BR-09: Deposit là phí giữ chỗ, KHÔNG trừ vào hóa đơn
                // GAP-10 Fix: Use member-level deposit from schema
                var memberDeposit = member.DepositAppliedAmount;
                var memberTotal = memberSubtotal + memberPenalty - memberDeposit;
                memberTotal = Math.Max(0, memberTotal);

                // Build penalty details
                var penaltyDetails = penaltyItems
                    ?.Where(p => p.ResponsibleMemberId == member.Id)
                    .Select(p => new PenaltyDetailDto
                    {
                        ComponentId = p.ComponentId,
                        ComponentName = p.ComponentName,
                        PenaltyFee = p.PenaltyAmount,
                        TotalPenalty = p.PenaltyAmount
                    })
                    .ToList() ?? [];

                invoices.Add(new MemberInvoiceDto
                {
                    MemberId = member.Id,
                    UserId = member.UserId,
                    DisplayName = member.IsGuestSlot
                        ? member.GuestDisplayName ?? "Khách vô danh"
                        : $"User_{member.UserId.ToString()[..8]}",
                    IsGuestSlot = member.IsGuestSlot,
                    PlayedMinutes = memberMinutes,
                    JoinedAt = member.JoinedAt,
                    Subtotal = memberSubtotal,
                    PenaltyAmount = memberPenalty,
                    // GAP-10 Fix: Use member-level deposit
                    DepositAppliedAmount = member.DepositAppliedAmount,
                    TotalAmount = memberTotal,
                    BvcCaptureStatus = member.IsGuestSlot ? BvcCaptureStatus.NotApplicable : BvcCaptureStatus.Pending,
                    PenaltyDetails = penaltyDetails
                });
            }

            return invoices;
        }

        /// <summary>
        /// Determine BVC capture status for the session.
        /// GAP-34 Fix: Return BVC capture status in PaySession response.
        /// </summary>
        private BvcCaptureStatus DetermineBvcCaptureStatus(ActiveSession session)
        {
            if (!session.LobbyId.HasValue)
            {
                // No lobby = no BVC to capture
                return BvcCaptureStatus.NotApplicable;
            }

            // Check if BVC was already captured (would be set during CompleteAndCaptureAsync)
            if (session.Status == GroupSessionStatus.Paid && session.PaidAt.HasValue)
            {
                // Session is paid - BVC should have been captured
                // This is a simplified check - in production, would check ledger entries
                return BvcCaptureStatus.Captured;
            }

            return BvcCaptureStatus.Pending;
        }

        private static decimal CalculateRealtimeBilling(Core.Entities.Cafe cafe, int elapsedMinutes)
        {
            if (elapsedMinutes <= 60)
            {
                return cafe.BasePrice;
            }

            var remainingMinutes = elapsedMinutes - 60;
            var blockMinutes = cafe.TieredBlockMinutes;
            var blockPrice = cafe.TieredBlockRate ?? 0;

            var additionalBlocks = (int)Math.Ceiling((double)remainingMinutes / blockMinutes);
            return cafe.BasePrice + (additionalBlocks * blockPrice);
        }

        private async Task<ActiveSessionResponseDto> CompleteCheckoutAsync(ActiveSession session, List<ComponentCheckoutItemDto>? components)
        {
            var now = DateTime.UtcNow;
            session.EndedAt = now;
            session.Status = GroupSessionStatus.Unpaid;
            session.IsCheckingInventory = false;
            session.HasMissingComponents = false;

            if (components != null && components.Count > 0)
            {
                foreach (var component in components)
                {
                    if (component.IsMissing || component.IsDamaged)
                    {
                        session.HasMissingComponents = true;
                    }
                }
            }

            await _activeSessionRepository.SaveChangesAsync();

            return MapSessionDto(session);
        }

        private static ActiveSessionResponseDto MapSessionDto(ActiveSession session)
        {
            var now = DateTime.UtcNow;
            var elapsed = session.EndedAt.HasValue
                ? (int)Math.Floor((session.EndedAt.Value - session.StartedAt).TotalMinutes)
                : (int)Math.Floor((now - session.StartedAt).TotalMinutes);

            return new ActiveSessionResponseDto
            {
                Id = session.Id,
                CafeId = session.CafeId,
                HostId = session.HostId,
                CafeTableId = session.CafeTableId,
                TableName = session.CafeTable?.Name ?? string.Empty,
                CafeInventoryBoxId = session.CafeInventoryBoxId,
                BoxBarcode = session.CafeInventoryBox?.Barcode ?? string.Empty,
                GameTemplateId = session.GameTemplateId,
                GameName = session.GameTemplate?.Name ?? string.Empty,
                DefaultPlayTimeMinutes = session.GameTemplate?.PlayTime ?? 0,
                StartedAt = session.StartedAt,
                ElapsedMinutes = Math.Max(0, elapsed),
                EstimatedRemainingMinutes = Math.Max(0, (session.GameTemplate?.PlayTime ?? 0) - elapsed),
                Status = session.Status,
                Subtotal = session.Subtotal,
                DepositAppliedAmount = session.DepositAppliedAmount,
                TotalAmount = session.TotalAmount,
                IsCheckingInventory = session.IsCheckingInventory,
                HasMissingComponents = session.HasMissingComponents,
                EndedAt = session.EndedAt,
                PaidAt = session.PaidAt,
                Members = session.Members?.Select(m => new ActiveSessionMemberDto
                {
                    Id = m.Id,
                    UserId = m.UserId,
                    UserName = m.User?.Username ?? string.Empty,
                    IsGuestSlot = m.IsGuestSlot,
                    JoinedAt = m.JoinedAt,
                    LeftAt = m.LeftAt,
                    TotalMinutesPlayed = m.Status == IndividualSessionStatus.Finished
                        ? m.TotalMinutesPlayed
                        : (int)Math.Floor((now - m.JoinedAt).TotalMinutes),
                    PenaltyAmount = m.PenaltyAmount,
                    IsCheckedOut = m.IsCheckedOut,
                    CheckedOutAt = m.CheckedOutAt,
                    Status = m.Status
                }).ToList() ?? new List<ActiveSessionMemberDto>(),
                Games = session.Games?.Select(g => new ActiveSessionGameDto
                {
                    Id = g.Id,
                    CafeInventoryBoxId = g.CafeInventoryBoxId,
                    BoxBarcode = g.CafeInventoryBox?.Barcode ?? string.Empty,
                    GameTemplateId = g.GameTemplateId,
                    GameName = g.GameTemplate?.Name ?? string.Empty,
                    AttachedAt = g.AttachedAt,
                    CheckStatus = g.CheckStatus,
                    TotalPenaltyAmount = g.TotalPenaltyAmount
                }).ToList() ?? new List<ActiveSessionGameDto>()
            };
        }

        /// <summary>
        /// Gán thêm game vào phiên chơi.
        /// Exception 6: Nhóm tự ý lấy thêm game mà không báo nhân viên.
        /// GAP-13 Fix: Validate session status is Active before attaching game.
        /// GAP-14 Fix: Ensure Games navigation is loaded before accessing.
        /// </summary>
        public async Task<ActiveSessionResponseDto> AttachGameAsync(Guid cafeId, Guid sessionId, AttachGameRequestDto request)
        {
            var session = await _activeSessionRepository.GetByIdAsync(sessionId)
                ?? throw new NotFoundException(ApiErrorMessages.Pos.SessionNotFound(cafeId, sessionId));

            if (session.CafeId != cafeId)
            {
                throw new NotFoundException(ApiErrorMessages.Pos.SessionNotFound(cafeId, sessionId));
            }

            // GAP-13 Fix: Chỉ cho phép gán game khi phiên đang Active
            if (session.Status != GroupSessionStatus.Active)
            {
                throw new ConflictException($"Chỉ có thể gán game khi phiên đang hoạt động. Trạng thái hiện tại: {session.Status}.");
            }

            // GAP-14 Fix: Ensure Games is loaded
            if (session.Games == null)
            {
                throw new InvalidOperationException("Session Games collection not loaded. Ensure navigation is included in query.");
            }

            var box = await _posRepository.GetBoxByBarcodeAsync(cafeId, request.GameBarcode);
            if (box == null)
            {
                throw new NotFoundException($"Không tìm thấy hộp game với barcode '{request.GameBarcode}'.");
            }

            // GAP 3 Fix: Check if box is busy in another active session
            if (box.Status == CafeGameInventoryStatus.InUse)
            {
                throw new ConflictException($"Hộp game '{box.Barcode}' đang được sử dụng bởi phiên chơi khác.");
            }

            var existingGame = session.Games.FirstOrDefault(g => g.CafeInventoryBoxId == box.Id);
            if (existingGame != null)
            {
                throw new ConflictException("Game này đã được gán vào phiên chơi.");
            }

            var game = new ActiveSessionGame
            {
                Id = Guid.NewGuid(),
                ActiveSessionId = sessionId,
                CafeInventoryBoxId = box.Id,
                AttachedAt = DateTime.UtcNow
            };

            session.Games.Add(game);
            await _activeSessionRepository.SaveChangesAsync();

            session = await _activeSessionRepository.GetByIdAsync(sessionId);
            return MapSessionDto(session!);
        }

        /// <summary>
        /// Thêm thành viên đến muộn vào phiên chơi.
        /// Exception 8: Thêm 2 người bạn đến muộn vào nhóm đang chơi.
        /// </summary>
        public async Task<ActiveSessionResponseDto> AddLateMemberAsync(Guid cafeId, Guid sessionId, AddLateMemberRequestDto request)
        {
            var session = await _activeSessionRepository.GetByIdAsync(sessionId)
                ?? throw new NotFoundException(ApiErrorMessages.Pos.SessionNotFound(cafeId, sessionId));

            if (session.CafeId != cafeId)
            {
                throw new NotFoundException(ApiErrorMessages.Pos.SessionNotFound(cafeId, sessionId));
            }

            // BR-17: Chỉ nhân viên POS được phép thêm thành viên đến muộn
            // BR-08 Exception: Có thể thêm thành viên đến muộn khi phiên đang Active hoặc Checking
            if (session.Status != GroupSessionStatus.Active && session.Status != GroupSessionStatus.Checking)
            {
                throw new ConflictException("Chỉ phiên đang hoạt động mới thêm được thành viên.");
            }

            if (request.MemberUserIds.Count == 0)
            {
                throw new BadRequestException("Cần ít nhất 1 thành viên để thêm.");
            }

            var now = DateTime.UtcNow;
            foreach (var userId in request.MemberUserIds)
            {
                var existing = session.Members.FirstOrDefault(m => m.UserId == userId && m.Status == IndividualSessionStatus.Playing);
                if (existing != null)
                {
                    continue;
                }

                await _activeSessionRepository.AddMemberAsync(new ActiveSessionMember
                {
                    Id = Guid.NewGuid(),
                    ActiveSessionId = sessionId,
                    UserId = userId,
                    Status = IndividualSessionStatus.Playing,
                    JoinedAt = now
                });
            }

            await _activeSessionRepository.SaveChangesAsync();

            session = await _activeSessionRepository.GetByIdAsync(sessionId);
            return MapSessionDto(session!);
        }

        /// <summary>
        /// Ghi nhận hao hụt linh kiện trước phiên chơi.
        /// Exception 7: Nhân viên ca chiều phát hiện game bị thiếu từ ca sáng.
        /// - Ghi nhận vào ComponentLossReport để hệ thống chặn không tính phí cho nhóm khách mới.
        /// - Ghi log KarmaLog để truy ngược theo mã nhân viên.
        /// </summary>
        public async Task RecordInventoryLossAsync(Guid cafeId, Guid userId, Guid sessionId, RecordInventoryLossRequestDto request)
        {
            var session = await _activeSessionRepository.GetByIdAsync(sessionId)
                ?? throw new NotFoundException(ApiErrorMessages.Pos.SessionNotFound(cafeId, sessionId));

            if (session.CafeId != cafeId)
            {
                throw new NotFoundException(ApiErrorMessages.Pos.SessionNotFound(cafeId, sessionId));
            }

            var report = new ComponentLossReport
            {
                Id = Guid.NewGuid(),
                CafeId = cafeId,
                ActiveSessionId = sessionId,
                CafeInventoryBoxId = request.CafeInventoryBoxId,
                ReportedByUserId = userId,
                LossDescription = request.LostComponents.Count > 0
                    ? $"Thiếu {request.LostComponents.Count} linh kiện"
                    : "Ghi nhận hao hụt trước phiên",
                Notes = request.Notes,
                CreatedAt = DateTime.UtcNow
            };

            foreach (var lost in request.LostComponents)
            {
                var penalty = await _posRepository.GetComponentPenaltyAsync(
                    cafeId, session.GameTemplateId, lost.ComponentId);
                if (penalty != null)
                {
                    report.TotalPenaltyAmount += penalty.PenaltyFee;
                }
            }

            await _posRepository.AddComponentLossReportAsync(report);
            await _activeSessionRepository.SaveChangesAsync();
        }

        /// <summary>
        /// Gợi ý quán thay thế khi hết chỗ.
        /// Exception 1: Phòng đầy nhưng quán hết chỗ.
        /// BR-05: AvailableSeats = TotalSeats - (active member count)
        /// </summary>
        public async Task<AlternativeCafesResponseDto> GetAlternativeCafesAsync(Guid excludeCafeId, Guid gameTemplateId, int memberCount, DateTime scheduledTime)
        {
            var cafes = await _cafeRepository.GetNearbyCafesAsync(excludeCafeId, 10);

            var result = new AlternativeCafesResponseDto();

            foreach (var cafe in cafes)
            {
                if (cafe.Inventories == null || !cafe.Inventories.Any())
                    continue;

                var hasGame = cafe.Inventories.Any(i => i.GameTemplateId == gameTemplateId);
                if (!hasGame)
                    continue;

                // BR-05: Calculate available seats = TotalSeats - active members
                var activeMemberCount = await _activeSessionRepository.CountActiveSessionMembersAsync(cafe.Id);
                var availableSeats = cafe.TotalSeats - activeMemberCount;

                if (availableSeats >= memberCount)
                {
                    result.Cafes.Add(new AlternativeCafeDto
                    {
                        Id = cafe.Id,
                        Name = cafe.Name,
                        Address = cafe.Address,
                        DistanceKm = 0, // Would need origin lat/lon to calculate
                        AvailableSeats = availableSeats,
                        HasRequestedGame = true
                    });
                }

                if (result.Cafes.Count >= 5)
                    break;
            }

            return result;
        }

        /// <summary>
        /// Submit component checklist cho một game trong phiên chơi (BR-12).
        /// Nhân viên POS scan linh kiện thực tế; nếu thiếu thì hệ thống cộng phí phạt
        /// lên <see cref="ActiveSessionGame.TotalPenaltyAmount"/> và đánh dấu
        /// <see cref="ComponentCheckStatus.MissingComponents"/>. Đủ linh kiện thì đánh dấu
        /// <see cref="ComponentCheckStatus.Verified"/>.
        /// </summary>
        public async Task<ActiveSessionResponseDto> SubmitComponentCheckAsync(Guid cafeId, Guid sessionId, SubmitComponentCheckRequestDto request)
        {
            var session = await _activeSessionRepository.GetByIdAsync(sessionId)
                ?? throw new NotFoundException(ApiErrorMessages.Pos.SessionNotFound(cafeId, sessionId));

            if (session.CafeId != cafeId)
            {
                throw new NotFoundException(ApiErrorMessages.Pos.SessionNotFound(cafeId, sessionId));
            }

            // BR-12: Chỉ cho phép checklist khi đang ở trạng thái CHECKING (sau EndGameSession/PartialCheckout)
            if (session.Status != GroupSessionStatus.Checking)
            {
                throw new ConflictException("Chỉ kiểm kê linh kiện khi phiên đang ở trạng thái CHECKING.");
            }

            var sessionGame = await _activeSessionRepository.GetSessionGameByIdAsync(request.SessionGameId)
                ?? throw new NotFoundException($"Không tìm thấy game '{request.SessionGameId}' trong phiên.");

            if (sessionGame.ActiveSessionId != sessionId)
            {
                throw new ConflictException("Game không thuộc phiên chơi này.");
            }

            // Tính tổng penalty = sum(penalty của từng component thiếu)
            decimal totalPenalty = 0m;
            var missingCount = 0;
            foreach (var result in request.Results)
            {
                var penalty = await _posRepository.GetComponentPenaltyAsync(
                    cafeId, sessionGame.GameTemplateId, result.ComponentId);
                if (penalty == null)
                {
                    continue;
                }

                if (result.ActualQuantity < penalty.PenaltyFee || result.ActualQuantity <= 0)
                {
                    // Thiếu linh kiện hoặc không có → cộng phạt
                    totalPenalty += penalty.PenaltyFee;
                    missingCount++;
                }
            }

            sessionGame.TotalPenaltyAmount = totalPenalty;
            sessionGame.CheckStatus = missingCount > 0
                ? ComponentCheckStatus.MissingComponents
                : ComponentCheckStatus.Verified;
            sessionGame.CheckedAt = DateTime.UtcNow;

            await _activeSessionRepository.UpdateSessionGameAsync(sessionGame);
            await _activeSessionRepository.SaveChangesAsync();

            // Cập nhật session.HasMissingComponents để UI/Checkout biết.
            session.HasMissingComponents = missingCount > 0;
            await _activeSessionRepository.UpdateAsync(session);
            await _activeSessionRepository.SaveChangesAsync();

            return MapSessionDto(session);
        }

        /// <summary>
        /// GAP-1 Fix: Cho phép revert từ CHECKING về ACTIVE nếu nhân viên bấm nhầm.
        /// Chỉ cho phép khi chưa có thành viên nào được checkout (chưa có member trong trạng thái FINISHED).
        /// </summary>
        public async Task<ActiveSessionResponseDto> ResumeSessionAsync(Guid cafeId, Guid sessionId)
        {
            var session = await _activeSessionRepository.GetByIdAsync(sessionId)
                ?? throw new NotFoundException(ApiErrorMessages.Pos.SessionNotFound(cafeId, sessionId));

            // GAP-8 Fix: Validate cafeId matches session's CafeId
            if (session.CafeId != cafeId)
            {
                throw new ConflictException($"Phiên chơi '{sessionId}' không thuộc quán '{cafeId}'.");
            }

            // Only allow resume from CHECKING state
            if (session.Status != GroupSessionStatus.Checking)
            {
                throw new ConflictException(
                    $"Chỉ có thể khôi phục phiên đang ở trạng thái CHECKING. Trạng thái hiện tại: {session.Status}.");
            }

            // Check if any members have been checked out (FINISHED status)
            var hasCheckedOutMembers = session.Members?.Any(m => m.Status == IndividualSessionStatus.Finished) ?? false;
            if (hasCheckedOutMembers)
            {
                throw new ConflictException(
                    "Không thể khôi phục phiên vì đã có thành viên được thanh toán. Vui lòng tiếp tục thanh toán.");
            }

            // Revert session to ACTIVE
            session.Status = GroupSessionStatus.Active;
            session.EndedAt = null; // Clear the ended time to resume billing
            session.IsCheckingInventory = false;
            session.HasMissingComponents = false;

            // Revert all members back to Playing status
            if (session.Members != null)
            {
                foreach (var member in session.Members)
                {
                    if (member.Status == IndividualSessionStatus.SuspendedMutation)
                    {
                        member.Status = IndividualSessionStatus.Playing;
                        member.LeftAt = null; // Clear left time
                    }
                }
            }

            await _activeSessionRepository.SaveChangesAsync();

            _logger.LogInformation(
                "Session resumed from CHECKING to ACTIVE. SessionId={SessionId}, CafeId={CafeId}",
                sessionId, cafeId);

            return MapSessionDto(session);
        }
    }
}
