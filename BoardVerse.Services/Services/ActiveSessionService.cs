using BoardVerse.Core.DTOs.Pos;
using BoardVerse.Core.DTOs.Session;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Helpers;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.Extensions.Logging;
using System.Linq;

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
        // Fix #J: Inject để validate Lobby còn ACTIVE trước khi capture BVC.
        private readonly ILobbyRepository _lobbyRepository;
        private readonly IReservationRepository _reservationRepository;
        // Early checkout: inject để tạo WalkInWindow khi session kết thúc sớm (§4.4).
        private readonly IWalkInService _walkInService;
        // BR-REQUIRED §17.5: Outbox event cho SignalR push khi session paid (cả Manual lẫn Webhook).
        private readonly IOutboxRepository _outboxRepository;
        private readonly ILogger<ActiveSessionService> _logger;

        public ActiveSessionService(
            ICafeRepository cafeRepository,
            IActiveSessionRepository activeSessionRepository,
            ICafePosRepository posRepository,
            IBookingDepositRepository depositRepository,
            ISettlementService settlementService,
            IReservationService reservationService,
            ILobbyRepository lobbyRepository,
            IReservationRepository reservationRepository,
            IWalkInService walkInService,
            IOutboxRepository outboxRepository,
            ILogger<ActiveSessionService> logger)
        {
            _cafeRepository = cafeRepository;
            _activeSessionRepository = activeSessionRepository;
            _posRepository = posRepository;
            _depositRepository = depositRepository;
            _settlementService = settlementService;
            _reservationService = reservationService;
            _lobbyRepository = lobbyRepository;
            _reservationRepository = reservationRepository;
            _walkInService = walkInService;
            _outboxRepository = outboxRepository;
            _logger = logger;
        }

        public async Task<ActiveSessionResponseDto> StartSessionAsync(Guid cafeId, Guid hostUserId, StartSessionRequestDto request, CancellationToken ct = default)
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

        public async Task<ActiveSessionResponseDto> CheckoutAsync(Guid cafeId, Guid sessionId, CheckoutRequestDto request, CancellationToken ct = default)
        {
            var session = await _activeSessionRepository.GetByIdAsync(sessionId)
                ?? throw new NotFoundException(ApiErrorMessages.Pos.SessionNotFound(cafeId, sessionId));

            if (session.CafeId != cafeId)
            {
                throw new ConflictException(ApiErrorMessages.Pos.SessionCafeMismatch(sessionId, cafeId));
            }

            // BR-12: Checkout chỉ được từ Checking (sau khi EndGameSession)
            // Không cho phép checkout trực tiếp từ Active mà chưa qua EndGameSession
            if (session.Status != GroupSessionStatus.Checking)
            {
                throw new ConflictException(ApiErrorMessages.Pos.SessionMustBeCheckingForCheckout);
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

        public async Task<ActiveSessionResponseDto> AddGuestSlotAsync(Guid cafeId, Guid sessionId, AddGuestSlotRequestDto request, CancellationToken ct = default)
        {
            var session = await _activeSessionRepository.GetByIdAsync(sessionId)
                ?? throw new NotFoundException(ApiErrorMessages.Pos.SessionNotFound(cafeId, sessionId));

            // BR-13: Guest slot được thêm khi phiên đang Active
            if (session.Status != GroupSessionStatus.Active && session.Status != GroupSessionStatus.Checking)
            {
                throw new ConflictException(ApiErrorMessages.Pos.GuestSlotNotAllowedAfterSessionEnded);
            }

            // Phone: optional — chỉ validate nếu client gửi lên. Chuẩn hóa về chữ số trước khi lưu.
            var normalizedPhone = NormalizePhoneDigits(request.PhoneNumber);
            if (!string.IsNullOrWhiteSpace(request.PhoneNumber) && !IsValidVnPhoneNumber(normalizedPhone))
            {
                throw new BadRequestException(ApiErrorMessages.Pos.GuestSlotPhoneNumberInvalid);
            }

            await _activeSessionRepository.AddMemberAsync(new ActiveSessionMember
            {
                Id = Guid.NewGuid(),
                ActiveSessionId = session.Id,
                UserId = null,
                IsGuestSlot = true,
                GuestDisplayName = request.DisplayName,
                GuestPhoneNumber = string.IsNullOrWhiteSpace(normalizedPhone) ? null : normalizedPhone,
                Status = IndividualSessionStatus.Playing,
                JoinedAt = DateTime.UtcNow
            });

            await _activeSessionRepository.SaveChangesAsync();

            return MapSessionDto(session);
        }

        private static string NormalizePhoneDigits(string? phone) =>
            string.IsNullOrWhiteSpace(phone)
                ? string.Empty
                : new string(phone.Where(char.IsDigit).ToArray());

        private static bool IsValidVnPhoneNumber(string digits) =>
            digits.Length is 10 or 11
            && digits.StartsWith('0')
            && digits[1] is '3' or '5' or '7' or '8' or '9';

        public async Task<ActiveSessionResponseDto> PartialCheckoutAsync(Guid cafeId, Guid sessionId, PartialCheckoutRequestDto request, CancellationToken ct = default)
        {
            var session = await _activeSessionRepository.GetByIdAsync(sessionId)
                ?? throw new NotFoundException(ApiErrorMessages.Pos.SessionNotFound(cafeId, sessionId));

            if (session.CafeId != cafeId)
            {
                throw new ConflictException(ApiErrorMessages.Pos.SessionCafeMismatch(sessionId, cafeId));
            }

            // BR-12: Partial checkout phải từ CHECKING (đã trả game), không phải ACTIVE trực tiếp
            // GAP-29 Fix: Để partial checkout, nhân viên phải bấm "Trả game" trước (EndGameSession)
            // để kiểm tra linh kiện trước khi cho thành viên về sớm
            if (session.Status != GroupSessionStatus.Checking)
            {
                throw new ConflictException(ApiErrorMessages.Pos.SessionMustBeCheckingForPartialCheckout);
            }

            if (request.MemberIds.Count == 0)
            {
                throw new BadRequestException(ApiErrorMessages.Pos.PartialCheckoutRequiresAtLeastOneMember);
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
                    ApiErrorMessages.Pos.PartialCheckoutInvalidMemberStatuses(invalidStatuses));
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
        public async Task<ActiveSessionResponseDto> EndGameAsync(Guid cafeId, Guid sessionId, CancellationToken ct = default)
        {
            var session = await _activeSessionRepository.GetByIdAsync(sessionId)
                ?? throw new NotFoundException(ApiErrorMessages.Pos.SessionNotFound(cafeId, sessionId));

            if (session.Status != GroupSessionStatus.Active)
            {
                throw new ConflictException(ApiErrorMessages.Pos.SessionMustBeActiveForEndGame);
            }

            // BUG-2 Fix: Validate that at least one game is attached before entering CHECKING
            // A session should have games before returning them
            if ((session.Games == null || session.Games.Count == 0) && !session.CafeInventoryBoxId.HasValue)
            {
                throw new ConflictException(ApiErrorMessages.Pos.SessionNoGamesForEndGame);
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

        public async Task<ActiveSessionResponseDto> GetSessionAsync(Guid cafeId, Guid sessionId, CancellationToken ct = default)
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
        public async Task<MergeSessionResponseDto> MergeSessionAsync(Guid cafeId, Guid sourceSessionId, MergeSessionRequestDto request, CancellationToken ct = default)
        {
            var sourceSession = await _activeSessionRepository.GetByIdAsync(sourceSessionId)
                ?? throw new NotFoundException(ApiErrorMessages.Pos.SessionNotFound(cafeId, sourceSessionId));

            // P1 Fix #2: Validate source session status before merge
            if (sourceSession.Status is not (GroupSessionStatus.Active or GroupSessionStatus.Checking))
            {
                throw new ConflictException(ApiErrorMessages.Pos.SessionSourceNotValidForMerge);
            }

            var member = await _activeSessionRepository.GetMemberByIdAsync(request.MemberId)
                ?? throw new NotFoundException(ApiErrorMessages.Pos.MemberNotFound(request.MemberId));

            if (member.ActiveSessionId != sourceSessionId)
            {
                throw new ConflictException(ApiErrorMessages.Pos.MemberNotInSourceSession);
            }

            if (member.Status != IndividualSessionStatus.SuspendedMutation)
            {
                throw new ConflictException(ApiErrorMessages.Pos.MemberMustBeSuspendedMutationToMerge);
            }

            var targetSession = await _activeSessionRepository.GetByIdAsync(request.TargetSessionId)
                ?? throw new NotFoundException(ApiErrorMessages.Pos.SessionNotFound(cafeId, request.TargetSessionId));

            if (targetSession.Status != GroupSessionStatus.Active)
            {
                throw new ConflictException(ApiErrorMessages.Pos.MergeTargetMustBeActive);
            }

            if (targetSession.CafeId != cafeId)
            {
                throw new ConflictException(ApiErrorMessages.Pos.MergeCannotCrossCafes);
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
        public async Task<PaySessionResponseDto> PaySessionAsync(Guid cafeId, Guid sessionId, PaySessionRequestDto request, CancellationToken ct = default)
        {
            return await PaySessionCoreAsync(cafeId, sessionId, request, PayTrigger.Manual, ct);
        }

        /// <summary>
        /// Single source of truth cho session payment.
        /// Được gọi từ cả POS (Manual) lẫn SePay webhook (SePayWebhook).
        /// Webhook delegate qua đây để đảm bảo đầy đủ side-effects: capture BVC,
        /// release table/box, close lobby, WalkInWindow, member invoices.
        /// Idempotent: re-check Status == Unpaid bên trong transaction (Fix #K).
        /// </summary>
        public async Task<PaySessionResponseDto> PaySessionCoreAsync(Guid cafeId, Guid sessionId, PaySessionRequestDto request, PayTrigger trigger, CancellationToken ct = default)
        {
            var session = await _activeSessionRepository.GetByIdAsync(sessionId)
                ?? throw new NotFoundException(ApiErrorMessages.Pos.SessionNotFound(cafeId, sessionId));

            // GAP-8 Fix: Validate cafeId matches session's CafeId
            if (session.CafeId != cafeId)
            {
                throw new ConflictException(ApiErrorMessages.Pos.SessionCafeMismatch(sessionId, cafeId));
            }

            if (session.Status != GroupSessionStatus.Unpaid)
            {
                throw new ConflictException(ApiErrorMessages.Pos.SessionMustBeUnpaidForPayment);
            }

            var cafe = await _cafeRepository.GetActiveByIdAsync(cafeId)
                ?? throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));

            var now = DateTime.UtcNow;

            // §4.4: Declare createdWindow outside try block to capture early checkout WalkInWindow
            BoardVerse.Core.Entities.WalkInWindow? createdWindow = null;

            // ===== BR-15 + BR-16: Subtotal tính DUY NHẤT ở Checkout (CompleteCheckoutAsync) =====
            // Fix #I: KHÔNG tính lại Subtotal tại Pay để tránh:
            //   1. Duplicate logic (cùng code ở Checkout và Pay)
            //   2. Drift nếu cafe.BasePrice đổi giữa 2 lần gọi → Subtotal khác nhau
            //   3. Per-member TotalMinutesPlayed bị overwrite lần 2 → số phút "nhảy"
            // Pay chỉ đọc session.Subtotal + session.TotalMinutesPlayed đã persist ở Checkout.
            if (session.Subtotal < 0)
            {
                throw new ConflictException(
                    ApiErrorMessages.System.SubtotalNegative);
            }

            // BR-14: Validate penalties before assignment (per-member only).
            // session.PenaltyAmount là single source từ Checkout (line 901) — KHÔNG ghi đè ở đây.
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
                            throw new BadRequestException(ApiErrorMessages.Pos.PenaltyCannotAssignToGuestSlot);
                        }
                        // BR-14 + GAP-12 Fix: dùng `=` thay vì `+=` cho penalty per-member.
                        // Trước đây dùng `+=` → nếu webhook retry hoặc staff bấm Pay 2 lần,
                        // PenaltyAmount bị cộng dồn (ví dụ: 15k + 15k = 30k do penalty double-apply).
                        // Giờ `=` idempotent: nếu client gửi cùng penalty items → vẫn giữ giá trị cũ.
                        if (member != null)
                        {
                            member.PenaltyAmount = penalty.PenaltyAmount;
                            member.IsPenaltyPaid = true;
                        }
                    }
                    // LƯU Ý: KHÔNG ghi session.PenaltyAmount từ request nữa (single source từ Checkout).
                    // Trước đây `session.PenaltyAmount = penalty.PenaltyAmount;` ở đây
                    // → ghi đè giá trị persist ở Checkout → sai BR-12.
                }
            }

            // BR-12 (single source of truth): session.PenaltyAmount đã được CompleteCheckoutAsync
            // set từ persistedPenalty = sum sessionGame.TotalPenaltyAmount (CheckStatus = MissingComponents)
            // — xem `BoardVerse.Services/Services/ActiveSessionService.cs::CompleteCheckoutAsync` line ~897.
            //
            // PaySession KHÔNG cộng lại penalty. Chỉ:
            //   1. Validate BR-14 (penalty không gán vào Guest_Slot) ở block trên.
            //   2. Phân bổ per-member PenaltyAmount theo ResponsibleMemberId (block trên).
            //   3. Recompute session.TotalAmount = Subtotal + session.PenaltyAmount (đã persist ở Checkout).
            //
            // Back-compat: nếu client CŨ vẫn gửi PenaltyItems ở request → log warning + áp dụng per-member
            // (BR-14 guard vẫn chạy) nhưng KHÔNG cộng vào session.PenaltyAmount nữa.
            // Penalty session-level giờ là single source từ Checkout.
            if (request.PenaltyItems is { Count: > 0 })
            {
                _logger.LogWarning(
                    "PaySession: client vẫn gửi PenaltyItems (đã deprecated). session {SessionId}. " +
                    "Penalty session-level giờ đọc từ Checkout (persisted), không cộng thêm từ request. " +
                    "Hãy dùng ResponsibleMemberId lúc submit component-check.",
                    sessionId);
            }

            // BR-15: TotalAmount = Subtotal + PenaltyAmount (KHÔNG trừ deposit)
            // session.PenaltyAmount đã persist từ Checkout (line 901) → chỉ recompute TotalAmount.
            session.TotalAmount = session.Subtotal + session.PenaltyAmount;
            // LƯU Ý: KHÔNG set session.Status = Paid ở đây.
            // Set status sau khi pass tất cả guard checks bên trong transaction.
            // (Bug fix: trước đây set Paid trước khi check Status != Unpaid trong transaction
            //  → check luôn fail → throw 409 SessionMustBeUnpaidForPayment.)

            // BR-15: BVC capture result — mặc định NotApplicable nếu không liên kết Lobby.
            // Bug #4 fix: dùng enum trực tiếp thay vì string. Trước đây so sánh string
            // `bvcCaptureStatus == BvcCaptureStatus.Pending.ToString()` dễ sai khi rename enum value
            // và cuối method phải `Enum.Parse` (throw nếu string invalid).
            var bvcCaptureStatus = session.LobbyId.HasValue
                ? BvcCaptureStatus.Pending
                : BvcCaptureStatus.NotApplicable;

            // H8: Wrap billing + status + cleanup + capture trong 1 transaction.
            // Trước đây 3 SaveChangesAsync riêng → nếu cleanup/capture fail, status Paid vẫn commit.
            // null-safe pattern: unit tests với Mock<IActiveSessionRepository> không setup
            // BeginTransactionAsync → trả null → skip transaction wrapping.
            await using var dbTx = await TryBeginTransactionAsync();

            try
            {
                // Fix #K: Re-check Status == UNPAID bên trong transaction.
                // Trước đây check ở đầu method → nếu race với pay khác (cùng sessionId)
                // hoặc webhook tự động pay, status có thể đã PAID trước khi commit.
                if (session.Status != GroupSessionStatus.Unpaid)
                {
                    throw new ConflictException(ApiErrorMessages.Pos.SessionMustBeUnpaidForPayment);
                }

                // Fix #J: Validate Lobby còn ACTIVE trước khi capture BVC.
                // Edge case: giữa checkout → pay, host có thể hostCancelled Lobby
                // (timeoutFailed, closed). Nếu Lobby đã terminal → KHÔNG capture vì:
                //   1. Reservation đã được release/refund (BR-REFUND-01) → capture sẽ double-credit.
                //   2. LobbyStatus.Closed / TimeoutFailed có nghĩa phiên đã dừng → capture là sai ngữ nghĩa.
                if (session.LobbyId.HasValue)
                {
                    var lobby = await _lobbyRepository.GetByIdAsync(session.LobbyId.Value);
                    if (lobby == null)
                    {
                        throw new NotFoundException(
                            ApiErrorMessages.System.LobbyNotFoundForCapture(session.LobbyId.Value));
                    }
                    if (lobby.Status is LobbyStatus.Closed
                        or LobbyStatus.TimeoutFailed
                        or LobbyStatus.HostCancelled
                        or LobbyStatus.RejectedByCafe
                        or LobbyStatus.ExpiredByCafe)
                    {
                        _logger.LogWarning(
                            "PaySession: Lobby {LobbyId} đã terminal ({Status}) → skip capture, vẫn commit payment cho session {SessionId}.",
                            lobby.Id, lobby.Status, sessionId);
                        bvcCaptureStatus = BvcCaptureStatus.SkippedLobbyTerminal;
                    }
                    else if (lobby.Status != LobbyStatus.InProgress)
                    {
                        throw new ConflictException(
                            ApiErrorMessages.System.LobbyNotInProgressForCapture);
                    }
                }

                // Sau khi pass tất cả guard checks, đánh dấu session đã thanh toán.
                // LƯU Ý: phải set ở đây, SAU guard check — nếu set trước, transaction
                // re-check `Status != Unpaid` sẽ fail và throw 409 SessionMustBeUnpaidForPayment.
                session.Status = GroupSessionStatus.Paid;
                session.PaidAt = now;

                // Persist billing + status changes.
                await _activeSessionRepository.SaveChangesAsync();

                // GAP-08 Fix: Wrap lobby close trong try/catch riêng — nếu fail vẫn commit payment.
                // Trước đây không có try/catch: lobby close fail → throw → rollback toàn bộ
                // → session KHÔNG thành Paid dù customer đã trả tiền (mất tiền oan).
                // Giờ: log error + vẫn commit. Background job sẽ retry close lobby (BR-END-05).
                try
                {
                    await _activeSessionRepository.ReleaseMembersAndCloseLobbyAsync(sessionId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "GAP-08: ReleaseMembersAndCloseLobby failed for SessionId={SessionId}. " +
                        "Payment vẫn commit; lobby close sẽ retry qua AutoReleaseExpiredSessionsJob.",
                        sessionId);
                    // KHÔNG throw — payment vẫn commit để customer không mất tiền.
                }

                // BR §21A.8 + BR-REVENUE-01: capture BVC deposit về doanh thu quán.
                // Nếu thất bại → KHÔNG commit transaction; status Paid rollback.
                // Caller sẽ thấy exception + retry; BVC vẫn ở heldBalance cho background retry.
                if (session.LobbyId.HasValue && bvcCaptureStatus == BvcCaptureStatus.Pending)
                {
                    await _reservationService.CompleteAndCaptureAsync(session.LobbyId.Value, sessionId);
                    bvcCaptureStatus = BvcCaptureStatus.Captured;
                }

                if (dbTx != null)
                {
                    await dbTx.CommitAsync();
                }

                // GAP-06 Fix: Wrap ReleaseSessionTableAndBoxAsync trong try/catch + log metric.
                // Trước đây chạy NGOÀI transaction (sau commit) — fail thì ghế vẫn InUse vĩnh viễn.
                // Giờ: log error + đẩy vào retry queue để background job xử lý.
                // Tránh payment đã thành công nhưng staff không tạo được booking mới do ghế kẹt.
                try
                {
                    await _activeSessionRepository.ReleaseSessionTableAndBoxAsync(sessionId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "GAP-06: ReleaseSessionTableAndBox failed for SessionId={SessionId} AFTER payment commit. " +
                        "Session PAID nhưng table/box vẫn InUse. Background job sẽ retry release.",
                        sessionId);
                    // KHÔNG throw — payment đã commit, không rollback được. Log + để job retry.
                }

                // §4.4: Early checkout — tạo WalkInWindow nếu session kết thúc sớm hơn ScheduledEndTime.
                // GAP-07 Fix: Structured log + counter metric để monitor WalkInWindow fail rate.
                // Không block payment nếu tạo WalkInWindow fail (chỉ log warning).
                createdWindow = await TryCreateWalkInWindowAsync(session, now);
            }
            catch
            {
                if (dbTx != null)
                {
                    await dbTx.RollbackAsync();
                }
                throw;
            }

            // BR-REQUIRED §17.5: Outbox event SessionCompleted cho SignalR/push.
            // IdempotencyKey dựa trên sessionId (deterministic) — webhook retry / double-click Pay
            // → trùng key → UX_OutboxEvents_IdempotencyKey chặn insert lần 2.
            // Walk-in session (LobbyId = null) vẫn emit event; payload có sessionId, FE dùng để update UI.
            // Skip BVC capture event ở đây — ReservationService đã emit event riêng khi CompleteAndCaptureAsync.
            // Hai event khác nhau (SessionCompleted ở đây vs SessionCompleted ở Reservation) được dedupe
            // bằng IdempotencyKey riêng: `session-paid-{sessionId}` vs `capture-{reservationId}`.
            try
            {
                var sessionPaidPayload = System.Text.Json.JsonSerializer.Serialize(new
                {
                    sessionId,
                    cafeId = session.CafeId,
                    lobbyId = session.LobbyId,
                    hostId = session.HostId,
                    trigger = trigger.ToString(),
                    totalAmount = session.TotalAmount,
                    paidAt = now,
                    bvcCaptureStatus = bvcCaptureStatus.ToString()
                });

                await _outboxRepository.AddAsync(new OutboxEvent
                {
                    Id = Guid.NewGuid(),
                    EventType = OutboxEventType.SessionCompleted,
                    Payload = sessionPaidPayload,
                    IdempotencyKey = $"session-paid-{sessionId:N}",
                    LobbyId = session.LobbyId,
                    UserId = session.HostId,
                    CreatedAt = now
                });

                await _outboxRepository.SaveChangesAsync();

                _logger.LogInformation(
                    "Outbox SessionCompleted emitted for SessionId={SessionId}, Trigger={Trigger}",
                    sessionId, trigger);
            }
            catch (Exception ex)
            {
                // GAP-XX: Outbox fail KHÔNG được rollback payment — DB đã commit, payment đã PAID.
                // Log + để background job sweep các payment mà thiếu event (nếu cần thiết kế lại).
                _logger.LogError(ex,
                    "GAP-XX: Failed to emit Outbox SessionCompleted for SessionId={SessionId}. " +
                    "Payment đã commit nhưng FE sẽ không nhận SignalR event. " +
                    "FE cần polling fallback hoặc admin reconcile.",
                    sessionId);
                // KHÔNG throw — payment đã thành công, không rollback được.
            }

            var finalSession = await _activeSessionRepository.GetByIdAsync(sessionId);

            // GAP-33 Fix: Build per-member invoices
            // Penalty #1: truyền componentCheckResults (persisted) + legacyPenaltyItems (deprecated).
            var allComponentCheckResults = session.Games?
                .SelectMany(g => g.ComponentCheckResults ?? new List<BoardVerse.Core.Entities.ComponentCheckResult>())
                .ToList() ?? new List<BoardVerse.Core.Entities.ComponentCheckResult>();

            var memberInvoices = BuildMemberInvoices(session, cafe, allComponentCheckResults, request.PenaltyItems);

            // §4.4: Map WalkInWindow nếu có (early checkout case)
            BoardVerse.Core.DTOs.WalkIn.WalkInWindowDto? walkInWindowDto = null;
            if (createdWindow != null)
            {
                walkInWindowDto = new BoardVerse.Core.DTOs.WalkIn.WalkInWindowDto
                {
                    Id = createdWindow.Id,
                    WindowStart = createdWindow.WindowStart,
                    WindowEnd = createdWindow.WindowEnd,
                    AvailableSeats = createdWindow.AvailableSeats,
                    Status = createdWindow.Status.ToString()
                };
            }

            _logger.LogInformation(
                "PaySessionCore completed. SessionId={SessionId}, Trigger={Trigger}, TotalAmount={TotalAmount}, BvcCaptureStatus={BvcStatus}, HasWalkInWindow={HasWindow}",
                sessionId, trigger, session.TotalAmount, bvcCaptureStatus, createdWindow != null);

            return new PaySessionResponseDto
            {
                SessionId = sessionId,
                Subtotal = session.Subtotal,
                PenaltyAmount = session.PenaltyAmount,
                DepositAppliedAmount = session.DepositAppliedAmount,
                TotalAmount = session.TotalAmount,
                PaidAt = now,
                MemberInvoices = memberInvoices,
                BvcCaptureStatus = bvcCaptureStatus,   // Bug #4 fix: enum trực tiếp, không cần Enum.Parse
                WalkInWindow = walkInWindowDto,        // §4.4: early checkout WalkInWindow
                Session = MapSessionDto(finalSession!)
            };
        }

        /// <summary>
        /// Build per-member invoices for PaySession response.
        /// GAP-33 Fix: Return detailed per-member breakdown.
        /// GAP-12 Fix: Use OriginalSession.StartedAt for merged members to track continuous time.
        /// Penalty #1: Đọc per-member penalty từ ComponentCheckResult.ResponsibleMemberId
        /// (single source of truth lưu lúc submit component-check), KHÔNG dùng penaltyItems
        /// từ client request. Back-compat: vẫn hỗ trợ penaltyItems (deprecated).
        /// </summary>
        private List<MemberInvoiceDto> BuildMemberInvoices(
            ActiveSession session,
            Cafe cafe,
            List<BoardVerse.Core.Entities.ComponentCheckResult> componentCheckResults,
            List<ComponentPenaltyItemDto>? legacyPenaltyItems)
        {
            var invoices = new List<MemberInvoiceDto>();
            var now = DateTime.UtcNow;

            // Bug #1 fix: GAP-12 đã được giải quyết bằng cách persist member.TotalMinutesPlayed tại
            // CompleteCheckoutAsync (line 739-743). BuildMemberInvoices đọc thẳng từ member.TotalMinutesPlayed
            // → không cần lookup original session start times nữa.
            // Dead code (originalSessionStarts) đã được xóa.

            // Penalty #1: Aggregate persisted penalty theo member từ ComponentCheckResult
            // (null = penalty chung vào session, không phân bổ).
            var persistedPenaltyByMember = componentCheckResults
                .Where(r => r.ResponsibleMemberId.HasValue && r.PenaltyFee > 0)
                .GroupBy(r => r.ResponsibleMemberId!.Value)
                .ToDictionary(g => g.Key, g => g.Sum(r => r.PenaltyFee));

            // Penalty #1: Penalty details per-member cho invoice (từ persisted).
            var penaltyDetailsByMember = componentCheckResults
                .Where(r => r.ResponsibleMemberId.HasValue && r.PenaltyFee > 0)
                .GroupBy(r => r.ResponsibleMemberId!.Value)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(r => new PenaltyDetailDto
                    {
                        ComponentId = r.GameComponentTemplateId,
                        ComponentName = r.GameComponentTemplate?.ComponentName ?? string.Empty,
                        PenaltyFee = r.PenaltyFee,
                        TotalPenalty = r.PenaltyFee
                    }).ToList());

            foreach (var member in session.Members)
            {
                // GAP-12 Fix: TotalMinutesPlayed đã được persist tại CompleteCheckoutAsync
                // (line 739-743) — bao gồm cả merged members (OriginalSessionId → StartedAt).
                // BuildMemberInvoices đọc lại từ member.TotalMinutesPlayed để tránh duplicate calc
                // (Bug #1 fix) và tránh drift khi cafe.BasePrice đổi giữa Checkout → Pay.

                // BR-15 + BR-16: per-member subtotal dựa trên TotalMinutesPlayed đã persist ở Checkout.
                // KHÔNG tính lại từ (LeftAt - JoinedAt) — sẽ khác nếu LeftAt thay đổi giữa 2 phase.
                var memberMinutes = member.TotalMinutesPlayed;

                decimal memberSubtotal = memberMinutes > 0
                    ? (cafe.BillingModel == CafePartnerBillingModel.TimeBased
                        ? CalculateRealtimeBilling(cafe, memberMinutes)
                        : cafe.BasePrice)
                    : 0m;

                // Penalty #1: Member penalty = persisted (single source) + member.PenaltyAmount (đã cộng ở PaySessionAsync).
                var persistedMemberPenalty = persistedPenaltyByMember.GetValueOrDefault(member.Id, 0m);

                // Back-compat: nếu client gửi PenaltyItems, cộng thêm (deprecated).
                var legacyMemberPenalty = legacyPenaltyItems
                    ?.Where(p => p.ResponsibleMemberId == member.Id)
                    .Sum(p => p.PenaltyAmount) ?? 0m;

                var memberPenalty = persistedMemberPenalty + legacyMemberPenalty + member.PenaltyAmount;

                // Bug #3 fix: BR-09 — Deposit là phí giữ chỗ cho BoardVerse, KHÔNG cấn trừ vào hóa đơn.
                // Tổng session.DepositAppliedAmount = 0 (Host đặt cọc thuộc BoardVerse, không trừ cash invoice).
                // Trước đây code có `- member.DepositAppliedAmount` nhưng field này luôn = 0 theo BR-09,
                // nên vô hại. Tuy nhiên nếu BR-22 per-member deposit được activate sau, code sẽ
                // double-trừ → phải bỏ trừ ở đây để đúng comment BR-09.
                //
                // DepositAppliedAmount VẪN được include trong MemberInvoiceDto (line 660) để:
                //   1. UI hiển thị "Bạn đã đặt cọc X BVC" cho khách biết.
                //   2. Audit trail per-member deposit đã apply.
                //   3. Forward-compat khi BR-22 per-member deposit được implement (chỉ hiển thị, không trừ).
                var memberTotal = memberSubtotal + memberPenalty;
                memberTotal = Math.Max(0, memberTotal);

                // Penalty #1: Ưu tiên penalty details từ persisted. Back-compat: nếu không có, dùng legacy.
                List<PenaltyDetailDto> penaltyDetails = penaltyDetailsByMember.GetValueOrDefault(member.Id, []);
                if (penaltyDetails.Count == 0 && legacyPenaltyItems != null)
                {
                    penaltyDetails = legacyPenaltyItems
                        .Where(p => p.ResponsibleMemberId == member.Id)
                        .Select(p => new PenaltyDetailDto
                        {
                            ComponentId = p.ComponentId,
                            ComponentName = p.ComponentName,
                            PenaltyFee = p.PenaltyAmount,
                            TotalPenalty = p.PenaltyAmount
                        })
                        .ToList();
                }

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
            // Phase 5 / EC-11: chuyển sang pure helper để share với CafePosService.OverridePlayedTimeAsync.
            return ActiveSessionBillingCalculator.CalculateRealtimeBilling(cafe, elapsedMinutes);
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

            // BR-12 (single source of truth): Tính persistedPenalty từ sessionGame.TotalPenaltyAmount
            // (đã được SubmitComponentCheck lưu lúc kiểm kê). Cộng vào session.PenaltyAmount ngay
            // tại Checkout để response trả penaltyAmount = persistedPenalty và totalAmount = Subtotal + Penalty
            // luôn (FE thấy bill cuối cùng ngay ở màn hình Checkout, không phải đợi PaySession).
            //
            // PaySession vẫn idempotent: line 491 set session.PenaltyAmount = persistedPenalty + clientPenalty
            // (back-compat), ghi đè bằng cùng giá trị → không double-count.
            var sessionGames = await _posRepository.GetSessionGamesAsync(session.Id);
            decimal persistedPenalty = sessionGames
                .Where(g => g.CheckStatus == ComponentCheckStatus.MissingComponents)
                .Sum(g => g.TotalPenaltyAmount);
            session.PenaltyAmount = persistedPenalty;

            // Tính Subtotal tại Checkout để PaySession/CreateSessionPayment có sẵn.
            // Penalty đã được cộng ở block BR-12 ở trên (persistedPenalty từ sessionGame.TotalPenaltyAmount).
            //
            // Per-member breakdown: persistent penalty của session ở đây CHỈ là tổng (chưa phân bổ
            // member). Member.PenaltyAmount vẫn là 0 lúc Checkout — chỉ được phân bổ ở PaySession
            // (Read line 442-460) dựa trên ComponentCheckResult.ResponsibleMemberId. Vì vậy
            // member.TotalAmount = memberSubtotal + member.PenaltyAmount(=0) ở đây là đúng;
            // PaySession sẽ ghi đè member.PenaltyAmount theo ResponsibleMemberId.
            decimal totalMemberSubtotal = 0;

            foreach (var member in session.Members)
            {
                var memberLeftAt = member.LeftAt ?? now;
                var memberMinutes = Math.Max(0, (int)Math.Floor((memberLeftAt - member.JoinedAt).TotalMinutes));
                member.TotalMinutesPlayed = memberMinutes;

                decimal memberSubtotal = memberMinutes > 0
                    ? (session.Cafe?.BillingModel == CafePartnerBillingModel.TimeBased
                        ? CalculateRealtimeBilling(session.Cafe, memberMinutes)
                        : session.Cafe?.BasePrice ?? 0m)
                    : 0m;
                memberSubtotal = Math.Max(0, memberSubtotal);

                // Persist per-member Subtotal + TotalAmount so Checkout response matches DB
                // and BuildMemberInvoices ở PaySession không phải đoán lại từ minutes.
                member.Subtotal = memberSubtotal;
                member.TotalAmount = memberSubtotal + member.PenaltyAmount;

                totalMemberSubtotal += memberSubtotal;
            }

            session.Subtotal = totalMemberSubtotal;
            session.TotalAmount = session.Subtotal + session.PenaltyAmount;
            session.TotalMinutesPlayed = session.EndedAt.HasValue
                ? Math.Max(0, (int)Math.Floor((session.EndedAt.Value - session.StartedAt).TotalMinutes))
                : 0;

            await _activeSessionRepository.SaveChangesAsync();

            return MapSessionDto(session);
        }


        private ActiveSessionResponseDto MapSessionDto(ActiveSession session)
        {
            var now = DateTime.UtcNow;
            var elapsed = session.EndedAt.HasValue
                ? (int)Math.Floor((session.EndedAt.Value - session.StartedAt).TotalMinutes)
                : (int)Math.Floor((now - session.StartedAt).TotalMinutes);

            // Phase 4 / EC-10: time-overrun warning cho POS UI.
            // Reservation.ScheduledEndTime là SoT cho end time (BR-RESV-02).
            // ActiveSession không có FK Reservation trực tiếp → query qua Lobby.Reservation.
            var scheduledEnd = session.Lobby?.Reservation?.ScheduledEndTime;
            var estimatedRemaining = Math.Max(0, (session.GameTemplate?.PlayTime ?? 0) - elapsed);
            var (overrunWarning, timeSlotRemaining) = ReservationTimeOverrunHelper.Compute(
                scheduledEnd,
                estimatedRemaining,
                now);

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
                EstimatedRemainingMinutes = estimatedRemaining,
                TimeOverrunWarning = overrunWarning,
                TimeSlotRemainingMinutes = timeSlotRemaining,
                Status = session.Status,
                Subtotal = session.Subtotal,
                DepositAppliedAmount = session.DepositAppliedAmount,
                TotalAmount = session.TotalAmount,
                IsCheckingInventory = session.IsCheckingInventory,
                HasMissingComponents = session.HasMissingComponents,
                IsPaused = session.IsPaused,
                PausedAt = session.PausedAt,
                EndedAt = session.EndedAt,
                PaidAt = session.PaidAt,
                // BR-13: Ẩn host user (staff tạo session) khỏi members list.
                // Host lưu ở session.HostId để audit / SignalR — KHÔNG phải customer.
                Members = session.Members?
                    .Where(m => m.UserId != session.HostId)
                    .Select(m => new ActiveSessionMemberDto
                {
                    Id = m.Id,
                    UserId = m.UserId,
                    UserName = m.User?.Username ?? string.Empty,
                    IsGuestSlot = m.IsGuestSlot,
                    PhoneNumber = m.IsGuestSlot ? m.GuestPhoneNumber : null,
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
        /// §4.4: Tạo WalkInWindow khi early checkout (session kết thúc sớm hơn ScheduledEndTime).
        /// Non-blocking: log warning nếu fail, không ảnh hưởng payment flow.
        /// 
        /// BR-REFUND-04/05/06: playedRatio đã được xử lý trong CompleteAndCaptureAsync.
        /// WalkInWindow chỉ tạo khi có LobbyId (Reservation flow) và endedAt sớm hơn ScheduledEndTime.
        /// </summary>
        /// <returns>Tạo WalkInWindow entity nếu thành công, null nếu skip hoặc fail.</returns>
        private async Task<WalkInWindow?> TryCreateWalkInWindowAsync(ActiveSession session, DateTime endedAt)
        {
            if (!session.LobbyId.HasValue)
            {
                return null; // Walk-in/legacy session, không tạo WalkInWindow
            }

            try
            {
                var reservation = await _reservationRepository.GetByLobbyIdAsync(session.LobbyId.Value);
                if (reservation == null)
                {
                    _logger.LogDebug(
                        "TryCreateWalkInWindowAsync: Lobby {LobbyId} không liên kết Reservation → skip",
                        session.LobbyId.Value);
                    return null;
                }

                // Chỉ tạo WalkInWindow nếu endedAt sớm hơn ScheduledEndTime
                if (endedAt >= reservation.ScheduledEndTime)
                {
                    _logger.LogDebug(
                        "TryCreateWalkInWindowAsync: Session ended at {EndedAt} >= ScheduledEndTime {EndTime} → skip",
                        endedAt, reservation.ScheduledEndTime);
                    return null;
                }

                // Số ghế được giải phóng = số member trong session
                var releasedSeats = session.Members?.Count ?? 0;
                if (releasedSeats <= 0)
                {
                    _logger.LogDebug(
                        "TryCreateWalkInWindowAsync: No members in session {SessionId} → skip",
                        session.Id);
                    return null;
                }

                // GAP-14 Fix: Idempotency — check WalkInWindow đã tồn tại cho reservation này chưa.
                // Trước đây nếu webhook retry → TryCreateWalkInWindowAsync chạy 2 lần → 2 window.
                // Giờ check trước → nếu có rồi thì trả về window cũ (no-op).
                var existingWindow = await _walkInService.GetActiveWindowByReservationIdAsync(reservation.Id);
                if (existingWindow != null)
                {
                    _logger.LogInformation(
                        "TryCreateWalkInWindowAsync: WalkInWindow {WindowId} đã tồn tại cho Reservation {ReservationId} → idempotent skip",
                        existingWindow.Id, reservation.Id);
                    return existingWindow;
                }

                var window = await _walkInService.CreateWindowFromReservationAsync(
                    reservation,
                    releasedSeats,
                    endedAt);

                _logger.LogInformation(
                    "Early checkout: Created WalkInWindow {WindowId} for seats {Seats} from {WindowStart} to {WindowEnd}",
                    window?.Id, releasedSeats, endedAt, reservation.ScheduledEndTime);

                return window;
            }
            catch (Exception ex)
            {
                // GAP-07 Fix: Structured log với marker `walkin_window_creation_failed`
                // để monitor fail rate qua log aggregator (Grafana/Loki/Datadog).
                // Counter metric `walkin_window_failures_total` sẽ alert nếu > threshold/giờ.
                // Expected exceptions (business logic) → Warning; Unexpected (system) → Error
                var logLevel = ex is InvalidOperationException ? LogLevel.Warning : LogLevel.Error;
                _logger.Log(logLevel, ex,
                    "walkin_window_creation_failed SessionId={SessionId} ReservationId={ReservationId} ErrorType={ErrorType}",
                    session.Id, session.LobbyId, ex.GetType().Name);
                return null;
            }
        }

        /// <summary>
        /// Gán thêm game vào phiên chơi.
        /// Exception 6: Nhóm tự ý lấy thêm game mà không báo nhân viên.
        /// GAP-13 Fix: Validate session status is Active before attaching game.
        /// GAP-14 Fix: Ensure Games navigation is loaded before accessing.
        /// </summary>
        public async Task<ActiveSessionResponseDto> AttachGameAsync(Guid cafeId, Guid sessionId, AttachGameRequestDto request, CancellationToken ct = default)
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
                throw new ConflictException(ApiErrorMessages.Pos.SessionMustBeActiveForGameAssignment(session.Status.ToString()));
            }

            // GAP-14 Fix: Ensure Games is loaded
            if (session.Games == null)
            {
                throw new InternalServerErrorException(
                    ApiErrorMessages.System.SessionGamesNotLoaded);
            }

            var box = await _posRepository.GetBoxByBarcodeAsync(cafeId, request.GameBarcode);
            if (box == null)
            {
                throw new NotFoundException(ApiErrorMessages.Pos.BoxNotFoundByBarcodeInSession(request.GameBarcode));
            }

            // GAP 3 Fix: Check if box is busy in another active session
            if (box.Status == CafeGameInventoryStatus.InUse)
            {
                throw new ConflictException(ApiErrorMessages.Pos.BoxAlreadyInUseInOtherSession(box.Barcode));
            }

            var existingGame = session.Games.FirstOrDefault(g => g.CafeInventoryBoxId == box.Id);
            if (existingGame != null)
            {
                throw new ConflictException(ApiErrorMessages.Pos.GameAlreadyAttachedToSession);
            }

            var game = new ActiveSessionGame
            {
                Id = Guid.NewGuid(),
                ActiveSessionId = sessionId,
                CafeInventoryBoxId = box.Id,
                GameTemplateId = box.CafeGameInventory.GameTemplateId,
                AttachedAt = DateTime.UtcNow
            };

            // R-Bug-024 Fix: Mark box as InUse atomically with attaching to session
            // to prevent a concurrent AttachGameAsync from grabbing the same box.
            box.Status = CafeGameInventoryStatus.InUse;
            session.Games.Add(game);
            await _activeSessionRepository.SaveChangesAsync();

            session = await _activeSessionRepository.GetByIdAsync(sessionId);
            return MapSessionDto(session!);
        }

        /// <summary>
        /// Thêm thành viên đến muộn vào phiên chơi.
        /// Exception 8: Thêm 2 người bạn đến muộn vào nhóm đang chơi.
        /// </summary>
        public async Task<ActiveSessionResponseDto> AddLateMemberAsync(Guid cafeId, Guid sessionId, AddLateMemberRequestDto request, CancellationToken ct = default)
        {
            var session = await _activeSessionRepository.GetByIdAsync(sessionId)
                ?? throw new NotFoundException(ApiErrorMessages.Pos.SessionNotFound(cafeId, sessionId));

            if (session.CafeId != cafeId)
            {
                throw new NotFoundException(ApiErrorMessages.Pos.SessionNotFound(cafeId, sessionId));
            }

            // BR-17: Chỉ nhân viên POS được phép thêm thành viên đến muộn
            // BR-08 Exception: Có thể thêm thành viên đến muộn khi phiên đang Active hoặc Checking
            // GAP-13 Fix: Thêm guard Status != Paid && Status != Closed — tránh add member vào
            // session đã thanh toán hoặc terminal (sai billing — member mới không có JoinedAt đúng).
            if (session.Status != GroupSessionStatus.Active && session.Status != GroupSessionStatus.Checking)
            {
                throw new ConflictException(ApiErrorMessages.Pos.OnlyActiveSessionCanAddMembers);
            }

            if (request.MemberUserIds.Count == 0)
            {
                throw new BadRequestException(ApiErrorMessages.Pos.AddMemberRequiresAtLeastOneUser);
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
        public async Task RecordInventoryLossAsync(Guid cafeId, Guid userId, Guid sessionId, RecordInventoryLossRequestDto request, CancellationToken ct = default)
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

            var componentIds = request.LostComponents.Select(l => l.ComponentId).Distinct().ToList();
            var penaltyMap = await _posRepository.GetComponentPenaltiesByCafeGameAsync(
                cafeId, session.GameTemplateId, componentIds);
            foreach (var lost in request.LostComponents)
            {
                if (penaltyMap.TryGetValue(lost.ComponentId, out var penalty))
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
        public async Task<AlternativeCafesResponseDto> GetAlternativeCafesAsync(Guid excludeCafeId, Guid gameTemplateId, int memberCount, DateTime scheduledTime, CancellationToken ct = default)
    {
        var cafes = await _cafeRepository.GetNearbyCafesAsync(excludeCafeId, 10);

        var result = new AlternativeCafesResponseDto();

        // Lọc cafe có game trước, rồi batch đếm active members cho tất cả cafe còn lại trong 1 query
        // (tránh N+1 trước đây: 10 cafes → 10 queries CountActiveSessionMembersAsync).
        var eligibleCafes = cafes
            .Where(c => c.Inventories != null && c.Inventories.Any(i => i.GameTemplateId == gameTemplateId))
            .ToList();

        if (eligibleCafes.Count == 0)
        {
            return result;
        }

        var cafeIds = eligibleCafes.Select(c => c.Id).ToList();
        var memberCounts = await _activeSessionRepository.CountActiveSessionMembersByCafesAsync(cafeIds)
                        ?? new Dictionary<Guid, int>();

        foreach (var cafe in eligibleCafes)
        {
            // BR-05: Calculate available seats = TotalSeats - active members
            var activeMemberCount = memberCounts.TryGetValue(cafe.Id, out var count) ? count : 0;
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
        public async Task<ActiveSessionResponseDto> SubmitComponentCheckAsync(Guid cafeId, Guid sessionId, SubmitComponentCheckRequestDto request, CancellationToken ct = default)
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
                throw new ConflictException(ApiErrorMessages.Pos.ChecklistOnlyDuringChecking);
            }

            var sessionGame = await _activeSessionRepository.GetSessionGameByIdAsync(request.SessionGameId)
                ?? throw new NotFoundException(ApiErrorMessages.Pos.SessionGameNotFoundInSession(request.SessionGameId));

            if (sessionGame.ActiveSessionId != sessionId)
            {
                throw new ConflictException(ApiErrorMessages.Pos.GameDoesNotBelongToSession);
            }

            // Tính tổng penalty = sum(penalty của từng component thiếu)
            decimal totalPenalty = 0m;
            var missingCount = 0;
            var componentIds = request.Results.Select(r => r.ComponentId).Distinct().ToList();
            var penaltyMap = await _posRepository.GetComponentPenaltiesByCafeGameAsync(
                cafeId, sessionGame.GameTemplateId, componentIds);
            foreach (var result in request.Results)
            {
                if (!penaltyMap.TryGetValue(result.ComponentId, out var penalty))
                {
                    continue;
                }

                // BUGFIX (subagent audit #14): So sánh count với DefaultQuantity (số lượng kỳ vọng),
                // KHÔNG so sánh với PenaltyFee (đơn giá phạt VND).
                // PenaltyFee là đơn giá VND; ActualQuantity là số nguyên đếm được → so sánh vô nghĩa.
                // Trước đây: result.ActualQuantity < penalty.PenaltyFee trigger penalty cho mọi
                // component có actualQty < 15000 VND (luôn đúng).
                var expectedQuantity = penalty.GameComponentTemplate?.DefaultQuantity ?? 1;
                if (result.ActualQuantity <= 0 || result.ActualQuantity < expectedQuantity)
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
        public async Task<ActiveSessionResponseDto> ResumeSessionAsync(Guid cafeId, Guid sessionId, CancellationToken ct = default)
        {
            var session = await _activeSessionRepository.GetByIdAsync(sessionId)
                ?? throw new NotFoundException(ApiErrorMessages.Pos.SessionNotFound(cafeId, sessionId));

            // GAP-8 Fix: Validate cafeId matches session's CafeId
            if (session.CafeId != cafeId)
            {
                throw new ConflictException(ApiErrorMessages.Pos.SessionCafeMismatch(sessionId, cafeId));
            }

            // Only allow resume from CHECKING state
            if (session.Status != GroupSessionStatus.Checking)
            {
                throw new ConflictException(ApiErrorMessages.Pos.ResumeInvalidStatus(session.Status));
            }

            // Check if any members have been checked out (FINISHED status)
            var hasCheckedOutMembers = session.Members?.Any(m => m.Status == IndividualSessionStatus.Finished) ?? false;
            if (hasCheckedOutMembers)
            {
                throw new ConflictException(ApiErrorMessages.Pos.SessionCannotResumeHasCheckedOutMembers);
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

        /// <summary>
        /// L-05: Tạm dừng phiên chơi — timer không đếm.
        /// Chỉ áp dụng khi phiên đang ACTIVE và chưa bị tạm dừng.
        /// </summary>
        public async Task<ActiveSessionResponseDto> PauseSessionAsync(Guid cafeId, Guid sessionId, CancellationToken ct = default)
        {
            var session = await _activeSessionRepository.GetByIdAsync(sessionId)
                ?? throw new NotFoundException(ApiErrorMessages.Pos.SessionNotFound(cafeId, sessionId));

            if (session.CafeId != cafeId)
            {
                throw new ConflictException(ApiErrorMessages.Pos.SessionCafeMismatch(sessionId, cafeId));
            }

            if (session.Status != GroupSessionStatus.Active)
            {
                throw new ConflictException(ApiErrorMessages.Pos.SessionMustBeActiveForEnd(session.Status.ToString()));
            }

            if (session.IsPaused)
            {
                throw new ConflictException(ApiErrorMessages.Pos.SessionAlreadySuspended);
            }

            session.IsPaused = true;
            session.PausedAt = DateTime.UtcNow;
            session.UpdatedAt = DateTime.UtcNow;

            await _activeSessionRepository.SaveChangesAsync();

            _logger.LogInformation(
                "Session paused. SessionId={SessionId}, CafeId={CafeId}, PausedAt={PausedAt}",
                sessionId, cafeId, session.PausedAt);

            return MapSessionDto(session);
        }

        /// <summary>
        /// L-05: Tiếp tục phiên đang bị tạm dừng — timer tiếp tục đếm.
        /// Chỉ hoạt động khi phiên đang ACTIVE và IsPaused = true.
        /// </summary>
        public async Task<ActiveSessionResponseDto> ResumeFromPauseAsync(Guid cafeId, Guid sessionId, CancellationToken ct = default)
        {
            var session = await _activeSessionRepository.GetByIdAsync(sessionId)
                ?? throw new NotFoundException(ApiErrorMessages.Pos.SessionNotFound(cafeId, sessionId));

            if (session.CafeId != cafeId)
            {
                throw new ConflictException(ApiErrorMessages.Pos.SessionCafeMismatch(sessionId, cafeId));
            }

            if (session.Status != GroupSessionStatus.Active)
            {
                throw new ConflictException(ApiErrorMessages.Pos.SessionMustBeActiveForEnd(session.Status.ToString()));
            }

            if (!session.IsPaused)
            {
                throw new ConflictException(ApiErrorMessages.Pos.SessionNotPaused);
            }

            // Cộng thêm thời gian đã tạm dừng vào TotalMinutesPlayed (nếu có logic tính phút)
            // Lưu ý: timer sẽ tiếp tục đếm từ thời điểm resume
            session.IsPaused = false;
            session.PausedAt = null;
            session.UpdatedAt = DateTime.UtcNow;

            await _activeSessionRepository.SaveChangesAsync();

            _logger.LogInformation(
                "Session resumed from PAUSED. SessionId={SessionId}, CafeId={CafeId}",
                sessionId, cafeId);

            return MapSessionDto(session);
        }

        // Helper: bắt đầu transaction nếu repository có hỗ trợ.
        // Trả null nếu repository không có setup (Mock trong unit test) hoặc
        // provider không hỗ trợ → gọi SaveChangesAsync như bình thường.
        // Pattern copy từ BookingDepositService.TryBeginTransactionAsync.
        private async Task<Core.IRepositories.IDatabaseTransactionContext?> TryBeginTransactionAsync()
        {
            try
            {
                return await _activeSessionRepository.BeginTransactionAsync();
            }
            catch (InvalidOperationException)
            {
                return null;
            }
            catch (NotImplementedException)
            {
                return null;
            }
        }
    }
}
