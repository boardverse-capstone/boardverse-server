using BoardVerse.Core.DTOs.Pos;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using System.Transactions;

namespace BoardVerse.Services.Services
{
    public class CafePosService : ICafePosService
    {
        private readonly ICafePosRepository _posRepository;
        private readonly ICafeRepository _cafeRepository;
        private readonly IBookingDepositRepository _depositRepository;
        private readonly IBookingRepository _bookingRepository;
        private readonly IActiveSessionRepository _activeSessionRepository;
        private readonly IPosHubService _posHubService;
        private readonly ILobbyRepository _lobbyRepository;
        private readonly IUserProfileRepository _userProfileRepository;
        private readonly BoardVerseDbContext _db;

        public CafePosService(
            ICafePosRepository posRepository,
            ICafeRepository cafeRepository,
            IBookingDepositRepository depositRepository,
            IBookingRepository bookingRepository,
            IActiveSessionRepository activeSessionRepository,
            IPosHubService posHubService,
            ILobbyRepository lobbyRepository,
            IUserProfileRepository userProfileRepository,
            BoardVerseDbContext db)
        {
            _posRepository = posRepository;
            _cafeRepository = cafeRepository;
            _depositRepository = depositRepository;
            _bookingRepository = bookingRepository;
            _activeSessionRepository = activeSessionRepository;
            _posHubService = posHubService;
            _lobbyRepository = lobbyRepository;
            _userProfileRepository = userProfileRepository;
            _db = db;
        }

        public async Task<IReadOnlyList<CafeTableStatusDto>> GetTablesAsync(
            Guid cafeId,
            Guid userId,
            string userRole)
        {
            await EnsurePosAccessAsync(cafeId, userId, userRole);

            var tables = await _posRepository.GetActiveTablesAsync(cafeId);
            // Fix Bug #3: Only show Available tables in the POS list
            return tables
                .Where(t => t.Status == CafeTableStatus.Available)
                .OrderBy(t => t.SortOrder)
                .ThenBy(t => t.Name)
                .Select(t => new CafeTableStatusDto
                {
                    Id = t.Id,
                    Name = t.Name,
                    SortOrder = t.SortOrder,
                    Status = t.Status
                })
                .ToList();
        }

        public async Task SyncTablesAsync(Guid cafeId, Guid managerId, IReadOnlyList<string> tableNames)
        {
            var cafe = await _cafeRepository.GetByIdAsync(cafeId);
            if (cafe == null)
            {
                throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));
            }

            if (cafe.ManagerId != managerId)
            {
                throw new ForbiddenException(ApiErrorMessages.Pos.AccessForbidden(cafeId));
            }

            await _cafeRepository.SyncCafeTablesAsync(cafeId, tableNames);
        }

        public async Task<IReadOnlyList<CafeInventoryBoxDto>> GetBoxesAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid? gameTemplateId)
        {
            await EnsurePosAccessAsync(cafeId, userId, userRole);

            var boxes = await _posRepository.GetBoxesAsync(cafeId, gameTemplateId);
            return boxes.Select(MapBox).ToList();
        }

        public async Task<CafeInventoryBoxDto> GetBoxByBarcodeAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            string barcode)
        {
            await EnsurePosAccessAsync(cafeId, userId, userRole);

            var normalized = barcode.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new BadRequestException(ApiErrorMessages.Pos.BarcodeRequired);
            }

            var box = await _posRepository.GetBoxByBarcodeAsync(cafeId, normalized);
            if (box == null)
            {
                throw new NotFoundException(ApiErrorMessages.Pos.BoxNotFound(cafeId, normalized));
            }

            return MapBox(box);
        }

        public async Task<IReadOnlyList<ActiveSessionDto>> GetActiveSessionsAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid? gameTemplateId)
        {
            await EnsurePosAccessAsync(cafeId, userId, userRole);

            var sessions = await _posRepository.GetActiveSessionsAsync(cafeId, gameTemplateId);
            var utcNow = DateTime.UtcNow;

            return sessions
                .OrderBy(s => s.StartedAt)
                .Select(s => MapSession(s, utcNow))
                .ToList();
        }

        /// <summary>
        /// Preview booking info trước khi check-in.
        /// AC 1.1: Hiển thị danh sách thành viên + game info TRƯỚC khi check-in.
        /// </summary>
        public async Task<BookingPreviewDto> GetBookingPreviewAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            string bookingCode)
        {
            await EnsurePosAccessAsync(cafeId, userId, userRole);

            var deposit = await _depositRepository.GetByBookingCodeAsync(bookingCode.Trim());
            if (deposit == null)
            {
                throw new NotFoundException($"Không tìm thấy đơn đặt chỗ với mã '{bookingCode}'.");
            }

            if (deposit.CafeId != cafeId)
            {
                throw new ConflictException("Đơn đặt chỗ này không thuộc quán này.");
            }

            // Get host profile using available method
            var hostProfile = await _userProfileRepository.GetByIdWithProfileAsync(deposit.UserId);

            // Get lobby info if available - check via ActiveSessionId link
            BookingLobbyInfoDto? lobbyInfo = null;
            if (deposit.ActiveSessionId.HasValue)
            {
                var lobby = await _lobbyRepository.GetByActiveSessionIdAsync(deposit.ActiveSessionId.Value);
                if (lobby != null)
                {
                    lobbyInfo = new BookingLobbyInfoDto
                    {
                        LobbyId = lobby.Id,
                        GameName = lobby.GameTemplate?.Name ?? "Unknown",
                        MinPlayers = lobby.MinPlayers,
                        MaxPlayers = lobby.MaxMembers,
                        CurrentMemberCount = 1 // Host only for now
                    };
                }
            }

            // Determine if can check-in
            bool canCheckIn = deposit.Status == BookingDepositStatus.Paid;
            string? cannotCheckInReason = null;
            
            if (deposit.Status == BookingDepositStatus.Pending)
            {
                cannotCheckInReason = "Đơn cọc chưa thanh toán.";
            }
            else if (deposit.Status == BookingDepositStatus.Released)
            {
                cannotCheckInReason = "Đơn cọc đã được giải ngân.";
            }
            else if (deposit.Status == BookingDepositStatus.Refunded)
            {
                cannotCheckInReason = "Đơn cọc đã được hoàn tiền.";
            }
            else if (deposit.Status == BookingDepositStatus.Forfeited)
            {
                cannotCheckInReason = "Đơn cọc đã bị tịch thu.";
            }

            return new BookingPreviewDto
            {
                BookingCode = deposit.OrderId,
                DepositStatus = deposit.Status.ToString(),
                DepositAmount = deposit.Amount,
                ScheduledStartTime = deposit.ScheduledAt,
                RegisteredMemberCount = 1, // Host only for now
                CanCheckIn = canCheckIn,
                CannotCheckInReason = cannotCheckInReason,
                Host = new BookingMemberInfoDto
                {
                    UserId = deposit.UserId,
                    DisplayName = hostProfile?.Profile?.FirstName ?? hostProfile?.Username ?? "Unknown",
                    AvatarUrl = hostProfile?.Profile?.AvatarUrl,
                    KarmaScore = hostProfile?.Profile?.KarmaPoints ?? 0
                },
                Lobby = lobbyInfo
            };
        }

        public async Task<ActiveSessionDto> StartGameSessionAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            StartGameSessionRequestDto request)
        {
            await EnsurePosAccessAsync(cafeId, userId, userRole);

            var table = await _posRepository.GetTableAsync(cafeId, request.CafeTableId);
            if (table == null)
            {
                throw new NotFoundException(ApiErrorMessages.Pos.TableNotFound(cafeId, request.CafeTableId));
            }

            if (table.Status is CafeTableStatus.Reserved or CafeTableStatus.EventInProgress)
            {
                throw new ConflictException(ApiErrorMessages.Pos.TableNotAvailableForGame(request.CafeTableId));
            }

            if (table.Status != CafeTableStatus.Available)
            {
                throw new ConflictException(ApiErrorMessages.Pos.TableNotAvailableForGame(request.CafeTableId));
            }

            var barcode = request.Barcode.Trim();
            var box = await _posRepository.GetBoxByBarcodeAsync(cafeId, barcode);
            if (box == null)
            {
                throw new NotFoundException(ApiErrorMessages.Pos.BoxNotFound(cafeId, barcode));
            }

            if (box.Status != CafeGameInventoryStatus.Available)
            {
                throw new ConflictException(ApiErrorMessages.Pos.BoxNotAvailable(box.Barcode, box.Status.ToString()));
            }

            var existingSession = await _posRepository.GetActiveSessionByBoxIdAsync(box.Id);
            if (existingSession != null)
            {
                throw new ConflictException(ApiErrorMessages.Pos.BoxAlreadyInSession(box.Barcode));
            }

            var now = DateTime.UtcNow;
            var gameTemplateId = box.CafeGameInventory.GameTemplateId;

            var session = new ActiveSession
            {
                Id = Guid.NewGuid(),
                CafeId = cafeId,
                CafeTableId = table.Id,
                CafeInventoryBoxId = box.Id,
                GameTemplateId = gameTemplateId,
                HostId = userId,
                StartedAt = now,
                Status = GroupSessionStatus.Active,
                CreatedAt = now
            };

            var hostMember = new ActiveSessionMember
            {
                Id = Guid.NewGuid(),
                ActiveSessionId = session.Id,
                UserId = userId,
                JoinedAt = now,
                Status = IndividualSessionStatus.Playing
            };

            // BR-12: Auto-create ActiveSessionGame when starting session.
            // This ensures SubmitComponentCheck has a valid target when session enters CHECKING.
            var sessionGame = new ActiveSessionGame
            {
                Id = Guid.NewGuid(),
                ActiveSessionId = session.Id,
                CafeInventoryBoxId = box.Id,
                GameTemplateId = gameTemplateId,
                AttachedAt = now,
                CheckStatus = ComponentCheckStatus.NotChecked
            };

            box.Status = CafeGameInventoryStatus.InUse;
            box.UpdatedAt = now;

            table.Status = CafeTableStatus.InUse;
            table.UpdatedAt = now;

            await _posRepository.AddSessionAsync(session);
            await _posRepository.AddSessionMemberAsync(hostMember);
            await _posRepository.AddSessionGameAsync(sessionGame);
            await _posRepository.SaveChangesAsync();

            session.CafeTable = table;
            session.CafeInventoryBox = box;
            session.GameTemplate = box.CafeGameInventory.GameTemplate;
            session.Host = null!; // Host navigation not needed for response - use HostId
            session.Members = [hostMember];

            return MapSession(session, now);
        }

        /// <summary>
        /// Host-led check-in: Quét một lần mã đặt chỗ (BookingCode = OrderId) để kích hoạt phiên chơi.
        /// MDC Happy Path Step 9: "Quét một lần mã định danh đặt chỗ trên ứng dụng của người chơi khởi tạo để thực hiện thủ tục vào quán cho cả nhóm"
        /// BR-05: Deposit phải ở trạng thái Paid mới được check-in
        /// BR-06: Quá 30 phút không check-in → Booking EXPIRED
        /// BR-09: Deposit chỉ dùng để giữ chỗ, KHÔNG trừ vào hóa đơn session
        /// </summary>
        public async Task<ActiveSessionDto> StartSessionFromBookingAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            StartSessionFromBookingRequestDto request)
        {
            await EnsurePosAccessAsync(cafeId, userId, userRole);

            // Tìm deposit bằng BookingCode (OrderId)
            var deposit = await _depositRepository.GetByBookingCodeAsync(request.BookingCode.Trim());
            if (deposit == null)
            {
                throw new NotFoundException($"Không tìm thấy đơn đặt chỗ với mã '{request.BookingCode}'.");
            }

            // Kiểm tra deposit thuộc đúng cafe
            if (deposit.CafeId != cafeId)
            {
                throw new ConflictException("Đơn đặt chỗ này không thuộc quán này.");
            }

            // BR-05: Kiểm tra deposit đã Paid
            if (deposit.Status != BookingDepositStatus.Paid)
            {
                throw new ConflictException(ApiErrorMessages.Pos.BookingDepositNotPaid);
            }

            // Host là người đặt cọc (UserId trong deposit)
            var hostId = deposit.UserId;

            // Kiểm tra bàn
            var table = await _posRepository.GetTableAsync(cafeId, request.CafeTableId);
            if (table == null)
            {
                throw new NotFoundException(ApiErrorMessages.Pos.TableNotFound(cafeId, request.CafeTableId));
            }

            if (table.Status != CafeTableStatus.Available)
            {
                throw new ConflictException(ApiErrorMessages.Pos.TableNotAvailableForGame(request.CafeTableId));
            }

            // Kiểm tra game box
            var barcode = request.Barcode.Trim();
            var box = await _posRepository.GetBoxByBarcodeAsync(cafeId, barcode);
            if (box == null)
            {
                throw new NotFoundException(ApiErrorMessages.Pos.BoxNotFound(cafeId, barcode));
            }

            if (box.Status != CafeGameInventoryStatus.Available)
            {
                throw new ConflictException(ApiErrorMessages.Pos.BoxNotAvailable(box.Barcode, box.Status.ToString()));
            }

            var existingSession = await _posRepository.GetActiveSessionByBoxIdAsync(box.Id);
            if (existingSession != null)
            {
                throw new ConflictException(ApiErrorMessages.Pos.BoxAlreadyInSession(box.Barcode));
            }

            var now = DateTime.UtcNow;
            var gameTemplateId = box.CafeGameInventory.GameTemplateId;

            // Tạo session với Host là người đặt cọc
            var session = new ActiveSession
            {
                Id = Guid.NewGuid(),
                CafeId = cafeId,
                CafeTableId = table.Id,
                CafeInventoryBoxId = box.Id,
                GameTemplateId = gameTemplateId,
                HostId = hostId,
                LobbyId = null,
                Status = GroupSessionStatus.Active,
                StartedAt = now,
                CreatedAt = now,
                // BR-09: DepositAppliedAmount = 0 (không trừ deposit vào session)
                DepositAppliedAmount = 0,
                Subtotal = 0,
                TotalAmount = 0
            };

            // Tạo member cho Host
            var hostMember = new ActiveSessionMember
            {
                Id = Guid.NewGuid(),
                ActiveSessionId = session.Id,
                UserId = hostId,
                JoinedAt = now,
                Status = IndividualSessionStatus.Playing
            };

            // BR-12: Auto-create ActiveSessionGame when starting session
            var sessionGame = new ActiveSessionGame
            {
                Id = Guid.NewGuid(),
                ActiveSessionId = session.Id,
                CafeInventoryBoxId = box.Id,
                AttachedAt = now,
                CheckStatus = ComponentCheckStatus.NotChecked
            };

            box.Status = CafeGameInventoryStatus.InUse;
            box.UpdatedAt = now;

            table.Status = CafeTableStatus.InUse;
            table.UpdatedAt = now;

            // Link deposit vào session
            deposit.ActiveSessionId = session.Id;
            deposit.UpdatedAt = now;

            // P1 Fix #8: Wrap all database operations in a transaction for atomicity
            await using var transaction = await _db.Database.BeginTransactionAsync();

            try
            {
                // Lưu
                await _posRepository.AddSessionAsync(session);
                await _posRepository.SaveChangesAsync();

                await _posRepository.AddSessionMemberAsync(hostMember);
                await _posRepository.AddSessionGameAsync(sessionGame);
                await _posRepository.UpdateDepositAsync(deposit);
                await _posRepository.SaveChangesAsync();

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            // AC 1.4: Gửi SignalR notification cho mobile app
            var cafe = await _cafeRepository.GetByIdAsync(cafeId);
            var memberUserIds = new List<Guid> { hostId };
            
            // Get lobby members if available via ActiveSessionId link
            if (deposit.ActiveSessionId.HasValue)
            {
                var lobby = await _lobbyRepository.GetByActiveSessionIdAsync(deposit.ActiveSessionId.Value);
                if (lobby != null)
                {
                    // Get members from lobby - using GetByIdWithMembersAsync
                    var lobbyWithMembers = await _lobbyRepository.GetByIdWithMembersAsync(lobby.Id);
                    if (lobbyWithMembers?.Members != null)
                    {
                        foreach (var lobbyMember in lobbyWithMembers.Members.Where(m => m.UserId != hostId))
                        {
                            var additionalMember = new ActiveSessionMember
                            {
                                Id = Guid.NewGuid(),
                                ActiveSessionId = session.Id,
                                UserId = lobbyMember.UserId,
                                JoinedAt = now,
                                Status = IndividualSessionStatus.Playing
                            };
                            await _posRepository.AddSessionMemberAsync(additionalMember);
                            memberUserIds.Add(lobbyMember.UserId);
                        }
                        await _posRepository.SaveChangesAsync();
                    }
                }
            }
            
            await _posHubService.NotifySessionActivatedAsync(
                session.Id,
                cafeId,
                cafe?.Name ?? "Unknown Cafe",
                hostId,
                memberUserIds);

            session.CafeTable = table;
            session.CafeInventoryBox = box;
            session.GameTemplate = box.CafeGameInventory.GameTemplate;
            session.Host = null!; // Host navigation not needed for response - use HostId
            session.Members = memberUserIds.Select(uid => new ActiveSessionMember
            {
                Id = Guid.NewGuid(),
                ActiveSessionId = session.Id,
                UserId = uid,
                JoinedAt = now,
                Status = IndividualSessionStatus.Playing
            }).ToList();

            return MapSession(session, now);
        }

        public async Task<ActiveSessionDto> EndGameSessionAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid sessionId)
        {
            await EnsurePosAccessAsync(cafeId, userId, userRole);

            var session = await _posRepository.GetActiveSessionByIdAsync(cafeId, sessionId);
            if (session == null)
            {
                throw new NotFoundException(ApiErrorMessages.Pos.SessionNotFound(cafeId, sessionId));
            }

            var now = DateTime.UtcNow;
            session.EndedAt = now;
            // BR-12: Chuyển sang Checking để chờ kiểm kê linh kiện trước khi xuất hóa đơn
            session.Status = GroupSessionStatus.Checking;
            session.IsCheckingInventory = true;

            // W1 Fix: Null check for CafeInventoryBox before dereferencing
            if (session.CafeInventoryBox == null)
            {
                throw new NotFoundException("Không tìm thấy hộp game trong phiên chơi.");
            }
            session.CafeInventoryBox.Status = CafeGameInventoryStatus.Available;
            session.CafeInventoryBox.UpdatedAt = now;

            var otherSessionsOnTable = await _posRepository.GetActiveSessionsAsync(cafeId, null);
            var tableStillBusy = otherSessionsOnTable.Any(s =>
                s.Id != session.Id && s.CafeTableId == session.CafeTableId);

            // W1 Fix: Null check for CafeTable before dereferencing
            if (!tableStillBusy && session.CafeTable != null && session.CafeTable.Status == CafeTableStatus.InUse)
            {
                session.CafeTable.Status = CafeTableStatus.Available;
                session.CafeTable.UpdatedAt = now;
            }

            await _posRepository.SaveChangesAsync();

            return MapSession(session, now);
        }

        private async Task EnsurePosAccessAsync(Guid cafeId, Guid userId, string userRole)
        {
            var cafe = await _cafeRepository.GetActiveByIdAsync(cafeId);
            if (cafe == null)
            {
                throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));
            }

            if (!await _posRepository.CanOperateCafeAsync(cafeId, userId, userRole))
            {
                throw new ForbiddenException(ApiErrorMessages.Pos.AccessForbidden(cafeId));
            }
        }

        private static CafeInventoryBoxDto MapBox(CafeInventoryBox box) => new()
        {
            Id = box.Id,
            CafeGameInventoryId = box.CafeGameInventoryId,
            GameTemplateId = box.CafeGameInventory.GameTemplateId,
            GameName = box.CafeGameInventory.GameTemplate?.Name ?? string.Empty,
            Barcode = box.Barcode,
            Status = box.Status
        };

        private static ActiveSessionDto MapSession(ActiveSession session, DateTime utcNow)
        {
            var playTime = session.Games?.FirstOrDefault()?.GameTemplate?.PlayTime 
                ?? session.GameTemplate?.PlayTime 
                ?? 0;
            var elapsedMinutes = (int)Math.Floor((utcNow - session.StartedAt).TotalMinutes);
            var remaining = playTime > 0
                ? (int)Math.Max(0, Math.Ceiling((double)playTime - elapsedMinutes))
                : 0;

            return new ActiveSessionDto
            {
                Id = session.Id,
                HostId = session.HostId,
                HostName = session.Host?.Username ?? string.Empty,
                LobbyId = session.LobbyId,
                CafeTableId = session.CafeTableId,
                TableName = session.CafeTable?.Name ?? string.Empty,
                DefaultPlayTimeMinutes = playTime,
                StartedAt = session.StartedAt,
                ElapsedMinutes = Math.Max(0, elapsedMinutes),
                EstimatedRemainingMinutes = remaining,
                Members = session.Members?.Where(m => m.Status != IndividualSessionStatus.Finished).Select(m => new ActiveSessionMemberDto
                {
                    Id = m.Id,
                    UserId = m.UserId,
                    UserName = m.User?.Username ?? string.Empty,
                    JoinedAt = m.JoinedAt,
                    LeftAt = m.LeftAt,
                    Status = m.Status
                }).ToList() ?? [],
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
                }).ToList() ?? []
            };
        }

        // BR-12: Component Checklist
        public async Task<ComponentChecklistDto> GetComponentChecklistAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid sessionGameId)
        {
            await EnsurePosAccessAsync(cafeId, userId, userRole);

            var sessionGame = await _posRepository.GetActiveSessionGameByIdAsync(sessionGameId);
            if (sessionGame == null)
            {
                throw new NotFoundException(ApiErrorMessages.Pos.SessionGameNotFound(sessionGameId));
            }

            if (sessionGame.ActiveSession.CafeId != cafeId)
            {
                throw new NotFoundException(ApiErrorMessages.Pos.SessionGameNotFound(sessionGameId));
            }

            var components = sessionGame.GameTemplate.Components?.ToList() ?? [];

            var checklist = new ComponentChecklistDto
            {
                SessionGameId = sessionGame.Id,
                GameTemplateId = sessionGame.GameTemplateId,
                GameName = sessionGame.GameTemplate.Name,
                Components = []
            };

            foreach (var component in components)
            {
                var penalty = await _posRepository.GetComponentPenaltyAsync(
                    cafeId, sessionGame.GameTemplateId, component.Id);

                checklist.Components.Add(new ComponentCheckItemDto
                {
                    ComponentId = component.Id,
                    ComponentName = component.ComponentName,
                    ComponentKind = component.ComponentKind,
                    ExpectedQuantity = component.DefaultQuantity,
                    ActualQuantity = 0,
                    PenaltyFee = penalty?.PenaltyFee ?? 0
                });
            }

            return checklist;
        }

        public async Task<ComponentChecklistDto> SubmitComponentCheckAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            SubmitComponentCheckRequestDto request)
        {
            await EnsurePosAccessAsync(cafeId, userId, userRole);

            var sessionGame = await _posRepository.GetActiveSessionGameByIdAsync(request.SessionGameId);
            if (sessionGame == null)
            {
                throw new NotFoundException(ApiErrorMessages.Pos.SessionGameNotFound(request.SessionGameId));
            }

            if (sessionGame.ActiveSession.CafeId != cafeId)
            {
                throw new NotFoundException(ApiErrorMessages.Pos.SessionGameNotFound(request.SessionGameId));
            }

            if (sessionGame.CheckStatus != ComponentCheckStatus.NotChecked)
            {
                throw new ConflictException(
                    ApiErrorMessages.Pos.ComponentCheckAlreadyDone(request.SessionGameId));
            }

            // AC 3.2: "Tất cả hợp lệ" → skip kiểm tra chi tiết
            if (request.MarkAllValid)
            {
                sessionGame.CheckStatus = ComponentCheckStatus.Verified;
                sessionGame.CheckedAt = DateTime.UtcNow;
                sessionGame.TotalPenaltyAmount = 0;
                await _posRepository.SaveChangesAsync();
                return await GetComponentChecklistAsync(cafeId, userId, userRole, request.SessionGameId);
            }

            // Chi tiết từng linh kiện + tính penalty
            var gameTemplateId = sessionGame.GameTemplateId;
            var validComponentIds = sessionGame.GameTemplate.Components
                .Select(c => c.Id)
                .ToHashSet();

            foreach (var result in request.Results)
            {
                if (!validComponentIds.Contains(result.ComponentId))
                {
                    throw new BadRequestException(
                        ApiErrorMessages.Pos.ComponentNotBelongToGame(result.ComponentId, gameTemplateId));
                }
            }

            // P2 Fix #15: Check for duplicate component IDs in request
            var duplicateIds = request.Results
                .GroupBy(r => r.ComponentId)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            if (duplicateIds.Count > 0)
            {
                throw new BadRequestException(
                    $"Kết quả kiểm tra chứa component ID trùng lặp: {string.Join(", ", duplicateIds)}");
            }

            decimal totalPenalty = 0;
            var resultLookup = request.Results.ToDictionary(r => r.ComponentId, r => r.ActualQuantity);

            foreach (var component in sessionGame.GameTemplate.Components)
            {
                var actualQty = resultLookup.GetValueOrDefault(component.Id, 0);
                if (actualQty < component.DefaultQuantity)
                {
                    var penalty = await _posRepository.GetComponentPenaltyAsync(
                        cafeId, gameTemplateId, component.Id);
                    if (penalty != null)
                    {
                        var missing = component.DefaultQuantity - actualQty;
                        totalPenalty += penalty.PenaltyFee * missing;
                    }
                }
            }

            var hasMissing = request.Results.Any(r =>
            {
                var component = sessionGame.GameTemplate.Components
                    .FirstOrDefault(c => c.Id == r.ComponentId);
                return component != null && r.ActualQuantity < component.DefaultQuantity;
            });

            sessionGame.CheckStatus = hasMissing
                ? ComponentCheckStatus.MissingComponents
                : ComponentCheckStatus.Verified;
            sessionGame.CheckedAt = DateTime.UtcNow;
            sessionGame.TotalPenaltyAmount = totalPenalty;

            await _posRepository.SaveChangesAsync();

            return await GetComponentChecklistAsync(cafeId, userId, userRole, request.SessionGameId);
        }

        // POST /api/cafes/{cafeId}/pos/sessions/{sessionId}/return-game
        public async Task<ReturnGameResponseDto> ReturnGameAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid sessionId,
            ReturnGameRequestDto request)
        {
            await EnsurePosAccessAsync(cafeId, userId, userRole);

            var session = await _activeSessionRepository.GetByIdAsync(sessionId);
            if (session == null || session.CafeId != cafeId)
            {
                throw new NotFoundException($"Không tìm thấy phiên chơi '{sessionId}'.");
            }

            var box = await _posRepository.GetInventoryBoxByIdAsync(request.InventoryBoxId);
            if (box == null || box.CafeGameInventory.CafeId != cafeId)
            {
                throw new NotFoundException($"Không tìm thấy hộp game '{request.InventoryBoxId}'.");
            }

            // Tính surcharge_fine
            decimal totalFine = 0;
            bool hasDamaged = false;

            foreach (var damaged in request.DamagedComponents)
            {
                var penalty = box.CafeGameInventory?.ComponentPenalties
                    ?.FirstOrDefault(p => p.GameComponentTemplateId == damaged.ComponentId);
                if (penalty != null)
                {
                    var fineForMissing = damaged.MissingQuantity * penalty.PenaltyFee;
                    var fineForDamaged = damaged.DamagedQuantity * penalty.PenaltyFee;
                    totalFine += fineForMissing + fineForDamaged;

                    if (damaged.DamagedQuantity > 0)
                    {
                        hasDamaged = true;
                    }
                }
            }

            // Cập nhật surcharge_fine vào session
            session.SurchargeFine = totalFine;
            await _activeSessionRepository.UpdateAsync(session);
            await _activeSessionRepository.SaveChangesAsync();

            // Cập nhật trạng thái box: NeedsMaintenance nếu hỏng, Available nếu nguyên
            // Fix Bug #4: Must set box to Available when returned undamaged
            if (hasDamaged)
            {
                box.Status = CafeGameInventoryStatus.Maintenance;
                box.UpdatedAt = DateTime.UtcNow;
                await _posRepository.UpdateInventoryBoxAsync(box);
                await _posRepository.SaveChangesAsync();
            }
            else
            {
                box.Status = CafeGameInventoryStatus.Available;
                box.UpdatedAt = DateTime.UtcNow;
                await _posRepository.UpdateInventoryBoxAsync(box);
                await _posRepository.SaveChangesAsync();
            }

            return new ReturnGameResponseDto
            {
                SessionId = sessionId,
                InventoryBoxId = request.InventoryBoxId,
                SurchargeFine = totalFine,
                HasDamagedComponents = hasDamaged,
                BoxMaintenanceStatus = box.Status.ToString()
            };
        }
    }
}
