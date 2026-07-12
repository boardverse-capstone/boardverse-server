using BoardVerse.Core.DTOs.Pos;
using BoardVerse.Core.DTOs.Session;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;

namespace BoardVerse.Services.Services
{
    public class ActiveSessionService : IActiveSessionService
    {
        private readonly ICafeRepository _cafeRepository;
        private readonly IActiveSessionRepository _activeSessionRepository;
        private readonly ICafePosRepository _posRepository;
        private readonly IBookingDepositRepository _depositRepository;
        private readonly ILobbyRepository _lobbyRepository;
        private readonly ISettlementService _settlementService;

        public ActiveSessionService(
            ICafeRepository cafeRepository,
            IActiveSessionRepository activeSessionRepository,
            ICafePosRepository posRepository,
            IBookingDepositRepository depositRepository,
            ILobbyRepository lobbyRepository,
            ISettlementService settlementService)
        {
            _cafeRepository = cafeRepository;
            _activeSessionRepository = activeSessionRepository;
            _posRepository = posRepository;
            _depositRepository = depositRepository;
            _lobbyRepository = lobbyRepository;
            _settlementService = settlementService;
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
                CafeTableId = request.CafeTableId,
                CafeInventoryBoxId = Guid.Empty,
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

            await _activeSessionRepository.SaveChangesAsync();

            return MapSessionDto(session);
        }

        public async Task<ActiveSessionResponseDto> CheckoutAsync(Guid cafeId, Guid sessionId, CheckoutRequestDto request)
        {
            var session = await _activeSessionRepository.GetByIdAsync(sessionId)
                ?? throw new NotFoundException(ApiErrorMessages.Pos.SessionNotFound(cafeId, sessionId));

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

            // BR-12: Partial checkout có thể từ Active hoặc Checking
            if (session.Status != GroupSessionStatus.Active && session.Status != GroupSessionStatus.Checking)
            {
                throw new ConflictException("Phiên chơi này không thể thanh toán một phần.");
            }

            if (request.MemberIds.Count == 0)
            {
                throw new BadRequestException("Cần chọn ít nhất 1 thành viên để thanh toán một phần.");
            }

            var invalidMembers = session.Members
                .Where(m => request.MemberIds.Contains(m.Id) && m.Status == IndividualSessionStatus.Finished)
                .ToList();

            if (invalidMembers.Count > 0)
            {
                throw new ConflictException("Một số thành viên đã kết thúc phiên chơi.");
            }

            // Mark selected members as SUSPENDED_MUTATION (waiting for inventory check)
            // BR-12: They cannot be charged until inventory is verified
            foreach (var member in session.Members.Where(m => request.MemberIds.Contains(m.Id)))
            {
                member.Status = IndividualSessionStatus.SuspendedMutation;
                member.LeftAt = DateTime.UtcNow;
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

            // Mark all currently playing members as SuspendedMutation for inventory check
            foreach (var member in session.Members.Where(m => m.Status == IndividualSessionStatus.Playing))
            {
                member.Status = IndividualSessionStatus.SuspendedMutation;
                member.LeftAt = DateTime.UtcNow;
            }

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

            targetSession = await _activeSessionRepository.GetByIdAsync(request.TargetSessionId);

            return new MergeSessionResponseDto
            {
                MemberId = request.MemberId,
                SourceSessionId = sourceSessionId,
                TargetSessionId = request.TargetSessionId,
                MergedAt = DateTime.UtcNow,
                TargetSession = MapSessionDto(targetSession!)
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

            if (session.Status != GroupSessionStatus.Unpaid)
            {
                throw new ConflictException("Phiên chơi phải ở trạng thái UNPAID để thanh toán.");
            }

            var cafe = await _cafeRepository.GetActiveByIdAsync(cafeId)
                ?? throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));

            var now = DateTime.UtcNow;

            // BR-16: Per-member billing - tính thời gian cho từng thành viên
            // 1. Compute per-member minutes and subtotal
            decimal totalGroupSubtotal = 0;
            foreach (var member in session.Members)
            {
                var memberLeftAt = member.LeftAt ?? now;
                var memberMinutes = (int)Math.Floor((memberLeftAt - member.JoinedAt).TotalMinutes);
                memberMinutes = Math.Max(0, memberMinutes);
                member.TotalMinutesPlayed = memberMinutes;

                // BR-16: Compute member's individual subtotal based on cafe billing model
                decimal memberSubtotal = 0;
                if (cafe.BillingModel == CafePartnerBillingModel.TimeBased)
                {
                    memberSubtotal = CalculateRealtimeBilling(cafe, memberMinutes);
                }
                else
                {
                    // BR-16 Flat-rate: first hour = entry fee, subsequent = 0
                    memberSubtotal = cafe.BasePrice; // Giá vé vào cổng
                }

                memberSubtotal = Math.Max(0, memberSubtotal);
                totalGroupSubtotal += memberSubtotal;
            }

            // BR-16: Group total = sum of all members' individual subtotals
            // Or if time-based: single calculation based on overall group active period
            var elapsedMinutes = session.EndedAt.HasValue
                ? (int)Math.Floor((session.EndedAt.Value - session.StartedAt).TotalMinutes)
                : (int)Math.Floor((now - session.StartedAt).TotalMinutes);
            elapsedMinutes = Math.Max(0, elapsedMinutes);

            if (cafe.BillingModel == CafePartnerBillingModel.TimeBased)
            {
                // BR-16: Group time-based billing (using overall group elapsed time)
                totalGroupSubtotal = CalculateRealtimeBilling(cafe, elapsedMinutes);
            }
            else
            {
                // BR-16: Flat-rate - only charge entry fee once per session
                totalGroupSubtotal = cafe.BasePrice;
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

            // BR-09: Apply deposit exactly once to the total bill.
            var deposit = await _depositRepository.GetByActiveSessionIdAsync(session.Id);
            if (deposit != null && deposit.Status == BookingDepositStatus.Paid)
            {
                session.DepositAppliedAmount = deposit.Amount;
            }

            session.TotalAmount = session.Subtotal + session.PenaltyAmount - session.DepositAppliedAmount;
            session.Status = GroupSessionStatus.Paid;
            session.PaidAt = now;

            await _activeSessionRepository.SaveChangesAsync();

            // BR-09: After payment, trigger settlement to transfer deposit from
            // master account to the cafe manager's bank account.
            CafeSettlement? settlement = null;
            if (session.DepositAppliedAmount > 0)
            {
                settlement = await _settlementService.ReleaseSessionDepositAsync(cafeId, sessionId, session.Id);
            }

            // P8 / S8: After payment, close the lobby
            if (session.LobbyId.HasValue)
            {
                var lobby = await _lobbyRepository.GetByActiveSessionIdAsync(session.Id);
                if (lobby != null)
                {
                    lobby.Status = LobbyStatus.Closed;
                    lobby.UpdatedAt = now;
                    await _lobbyRepository.SaveChangesAsync();
                }
            }

            var finalSession = await _activeSessionRepository.GetByIdAsync(sessionId);

            return new PaySessionResponseDto
            {
                SessionId = sessionId,
                Subtotal = session.Subtotal,
                PenaltyAmount = session.PenaltyAmount,
                DepositAppliedAmount = session.DepositAppliedAmount,
                TotalAmount = session.TotalAmount,
                PaidAt = now,
                SettlementStatus = settlement?.Status.ToString(),
                Session = MapSessionDto(finalSession!)
            };
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
        /// </summary>
        public async Task<ActiveSessionResponseDto> AttachGameAsync(Guid cafeId, Guid sessionId, AttachGameRequestDto request)
        {
            var session = await _activeSessionRepository.GetByIdAsync(sessionId)
                ?? throw new NotFoundException(ApiErrorMessages.Pos.SessionNotFound(cafeId, sessionId));

            if (session.CafeId != cafeId)
            {
                throw new NotFoundException(ApiErrorMessages.Pos.SessionNotFound(cafeId, sessionId));
            }

            var box = await _posRepository.GetBoxByBarcodeAsync(cafeId, request.GameBarcode);
            if (box == null)
            {
                throw new NotFoundException($"Không tìm thấy hộp game với barcode '{request.GameBarcode}'.");
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
    }
}
