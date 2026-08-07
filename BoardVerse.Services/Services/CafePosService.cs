using BoardVerse.Core.DTOs.Pos;
using BoardVerse.Core.DTOs.Reservation;
using BoardVerse.Core.DTOs.Session;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Helpers;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Transactions;

namespace BoardVerse.Services.Services
{
    public class CafePosService : ICafePosService
    {
        private const int DefaultTokenTtlMinutes = 30;

        private readonly ICafePosRepository _posRepository;
        private readonly ICafeRepository _cafeRepository;
        private readonly IBookingDepositRepository _depositRepository;
        private readonly IBookingRepository _bookingRepository;
        private readonly IActiveSessionRepository _activeSessionRepository;
        private readonly IActiveSessionService _activeSessionService;
        private readonly IPosHubService _posHubService;
        private readonly ILobbyRepository _lobbyRepository;
        private readonly IUserProfileRepository _userProfileRepository;
        private readonly IReservationService _reservationService;
        private readonly IReservationRepository _reservationRepository;
        private readonly IPosCheckInTokenRepository _tokenRepository;
        private readonly ILogger<CafePosService> _logger;
        private readonly BoardVerseDbContext _db;

        public CafePosService(
            ICafePosRepository posRepository,
            ICafeRepository cafeRepository,
            IBookingDepositRepository depositRepository,
            IBookingRepository bookingRepository,
            IActiveSessionRepository activeSessionRepository,
            IActiveSessionService activeSessionService,
            IPosHubService posHubService,
            ILobbyRepository lobbyRepository,
            IUserProfileRepository userProfileRepository,
            IReservationService reservationService,
            IReservationRepository reservationRepository,
            IPosCheckInTokenRepository tokenRepository,
            ILogger<CafePosService> logger,
            BoardVerseDbContext db)
        {
            _posRepository = posRepository;
            _cafeRepository = cafeRepository;
            _depositRepository = depositRepository;
            _bookingRepository = bookingRepository;
            _activeSessionRepository = activeSessionRepository;
            _activeSessionService = activeSessionService;
            _posHubService = posHubService;
            _lobbyRepository = lobbyRepository;
            _userProfileRepository = userProfileRepository;
            _reservationService = reservationService;
            _reservationRepository = reservationRepository;
            _tokenRepository = tokenRepository;
            _logger = logger;
            _db = db;
        }

        /// <summary>
        /// GAP-21 Fix: GetTables với filter includeOnlyAvailable.
        /// </summary>
        public async Task<IReadOnlyList<CafeTableStatusDto>> GetTablesAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            bool includeOnlyAvailable = true)
        {
            await EnsurePosAccessAsync(cafeId, userId, userRole);

            var allTables = await _posRepository.GetActiveTablesAsync(cafeId);

            // GAP-21 Fix: Nếu includeOnlyAvailable=false, trả tất cả trạng thái để POS monitor
            var tables = includeOnlyAvailable
                ? allTables.Where(t => t.Status == CafeTableStatus.Available).ToList()
                : allTables.ToList();

            return tables
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
            var items = tableNames
                .Select((name, index) => new CafeTableSyncItem
                {
                    Name = name,
                    SortOrder = index,
                    SeatCount = null
                })
                .ToList();

            await SyncTablesAsync(cafeId, managerId, items);
        }

        public async Task SyncTablesAsync(Guid cafeId, Guid managerId, IReadOnlyList<CafeTableSyncItem> tables)
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

            await _cafeRepository.SyncCafeTablesAsync(cafeId, tables);
        }

        /// <summary>
        /// Cập nhật một phần thông tin bàn (Name/SeatCount/SortOrder).
        /// Validation: tất cả field optional, ít nhất một phải có giá trị; chặn update khi bàn đang có session hoạt động.
        /// </summary>
        public async Task<CafeTableStatusDto> UpdateCafeTableAsync(
            Guid cafeId,
            Guid managerId,
            Guid tableId,
            UpdateCafeTableRequestDto request)
        {
            await EnsurePosAccessAsync(cafeId, managerId, "Manager");

            var table = await _posRepository.GetTableAsync(cafeId, tableId)
                ?? throw new NotFoundException(ApiErrorMessages.Pos.TableNotFound(cafeId, tableId));

            if (await _posRepository.HasActiveSessionForTableAsync(cafeId, tableId))
            {
                throw new ConflictException(ApiErrorMessages.Pos.TableInUse(tableId));
            }

            var allTables = await _posRepository.GetActiveTablesAsync(cafeId);
            CafeTableUpdateHelper.ApplyUpdate(table, request, allTables);

            await _posRepository.UpdateTableAsync(table);
            await _posRepository.SaveChangesAsync();

            // Keep TableLayoutJson in sync with the (possibly renamed/reordered) table.
            await _cafeRepository.RefreshTableLayoutJsonAsync(cafeId);
            await _cafeRepository.SaveChangesAsync();

            return new CafeTableStatusDto
            {
                Id = table.Id,
                Name = table.Name,
                SortOrder = table.SortOrder,
                SeatCount = table.SeatCount,
                Status = table.Status
            };
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

        /// <summary>
        /// GAP 1 Fix: Get session by ID for frontend to view session details.
        /// </summary>
        public async Task<ActiveSessionDto> GetSessionByIdAsync(
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

            return MapSession(session, DateTime.UtcNow);
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
                throw new NotFoundException(ApiErrorMessages.Pos.BookingNotFoundByCode(bookingCode));
            }

            if (deposit.CafeId != cafeId)
            {
                throw new ConflictException(ApiErrorMessages.Pos.BookingNotInThisCafe);
            }

            // Get host profile using available method
            var hostUser = await _userProfileRepository.GetByIdWithProfileAsync(deposit.UserId);

            // Get lobby info if available - check via ActiveSessionId link
            BookingLobbyInfoDto? lobbyInfo = null;
            if (deposit.ActiveSessionId.HasValue)
            {
                var lobby = await _lobbyRepository.GetByActiveSessionIdAsync(deposit.ActiveSessionId.Value);
                if (lobby != null)
                {
                    // GAP-18 Fix: Populate members list from lobby
                    var members = new List<BookingMemberInfoDto>();

                    // Add host
                    if (hostUser != null)
                    {
                        var profile = hostUser.Profile;
                        var displayName = profile != null
                            ? $"{profile.FirstName} {profile.LastName}".Trim()
                            : hostUser.Username ?? "Host";

                        members.Add(new BookingMemberInfoDto
                        {
                            UserId = hostUser.Id,
                            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Host" : displayName,
                            AvatarUrl = profile?.AvatarUrl,
                            KarmaScore = profile?.KarmaPoints ?? 0
                        });
                    }

                    // Add other members if available (from lobby member list)
                    // Note: LobbyMembers would be loaded separately if needed
                    lobbyInfo = new BookingLobbyInfoDto
                    {
                        LobbyId = lobby.Id,
                        GameName = lobby.GameTemplate?.Name ?? "Unknown",
                        MinPlayers = lobby.MinPlayers,
                        MaxPlayers = lobby.MaxMembers,
                        CurrentMemberCount = members.Count,
                        Members = members
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
                RegisteredMemberCount = lobbyInfo?.Members?.Count ?? 1,
                CanCheckIn = canCheckIn,
                CannotCheckInReason = cannotCheckInReason,
                Host = new BookingMemberInfoDto
                {
                    UserId = deposit.UserId,
                    DisplayName = hostUser?.Profile?.FirstName ?? hostUser?.Username ?? "Unknown",
                    AvatarUrl = hostUser?.Profile?.AvatarUrl,
                    KarmaScore = hostUser?.Profile?.KarmaPoints ?? 0
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
            // L3: Không detach Host — MapSession đọc session.Host?.Username.
            // Nếu sau này cần HostName, load Host qua repository trước khi map.
            session.Members = [hostMember];

            return MapSession(session, now);
        }

        /// <summary>
        /// POS tạo QR token cho player scan check-in (BR §21A.7 — 2 chiều).
        /// Staff bấm "Tạo QR mời khách scan" → lưu token vào DB → hiển thị QR.
        /// Player scan token → check-in vào cùng reservation.
        ///
        /// Flow:
        /// 1. Validate staff có quyền POS.
        /// 2. Nếu có ReservationId: validate thuộc cafe, status sẵn sàng check-in.
        /// 3. Sinh token unique (collision check tối đa 5 lần).
        /// 4. Set TTL mặc định 30 phút.
        /// </summary>
        public async Task<PosCheckInTokenDto> CreateCheckInTokenAsync(
            Guid cafeId,
            Guid staffUserId,
            string staffRole,
            CreatePosCheckInTokenRequestDto request)
        {
            await EnsurePosAccessAsync(cafeId, staffUserId, staffRole);

            // Validate cafe đang active
            var cafe = await _cafeRepository.GetByIdAsync(cafeId);
            if (cafe == null)
            {
                throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));
            }

            // Nếu gắn reservation: validate thuộc cafe + status cho phép check-in
            if (request.ReservationId.HasValue)
            {
                var reservation = await _reservationRepository.GetByIdAsync(request.ReservationId.Value);
                if (reservation == null)
                {
                    throw new NotFoundException(
                        ApiErrorMessages.Reservation.ReservationNotFound(request.ReservationId.Value));
                }
                if (reservation.CafeId != cafeId)
                {
                    throw new ConflictException(
                        ApiErrorMessages.Reservation.CafeMismatchOnCheckIn(reservation.CafeId, cafeId));
                }
            }

            // Sinh token unique
            var ttl = TimeSpan.FromMinutes(request.TtlMinutes ?? DefaultTokenTtlMinutes);
            var expiresAt = DateTime.UtcNow.Add(ttl);

            string token;
            var attempts = 0;
            do
            {
                token = PosTokenGenerator.Generate();
                attempts++;
                if (attempts > 5)
                {
                    throw new InvalidOperationException("Không thể sinh PosCheckInToken unique sau 5 lần thử.");
                }
            } while (await _tokenRepository.TokenExistsAsync(token));

            var entity = new PosCheckInToken
            {
                Id = Guid.NewGuid(),
                CafeId = cafeId,
                ReservationId = request.ReservationId,
                Token = token,
                CreatedByStaffId = staffUserId,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = expiresAt,
                IsRevoked = false
            };

            await _tokenRepository.AddAsync(entity);

            _logger.LogInformation(
                "PosCheckInToken created. Token={Token}, CafeId={CafeId}, ReservationId={ReservationId}, ExpiresAt={ExpiresAt}",
                token, cafeId, request.ReservationId, expiresAt);

            return new PosCheckInTokenDto
            {
                Id = entity.Id,
                CafeId = entity.CafeId,
                ReservationId = entity.ReservationId,
                Token = entity.Token,
                QrPayload = BuildQrPayload(entity.Token),
                CreatedAt = entity.CreatedAt,
                ExpiresAt = entity.ExpiresAt
            };
        }

        private static string BuildQrPayload(string token) =>
            $"boardverse://check-in?token={Uri.EscapeDataString(token)}";

        /// <summary>
        /// POS check-in (BR §21A.7): Staff quét QR (ReservationCode hoặc BookingCode legacy) để kích hoạt phiên chơi.
        /// BR mới (BVC/Reservation): mã là ReservationCode 8-char alphanumeric uppercase (exclude 0/1/I/O).
        /// BR cũ (VND/BookingDeposit): mã là BookingCode "BV{N}" — giữ backward compat.
        /// MDC Happy Path Step 9: "Quét một lần mã định danh đặt chỗ trên ứng dụng của người chơi khởi tạo để thực hiện thủ tục vào quán cho cả nhóm"
        /// </summary>
        public async Task<ActiveSessionDto> CheckInByCodeAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            CheckInRequestDto request)
        {
            await EnsurePosAccessAsync(cafeId, userId, userRole);

            // GAP-1/GAP-37 Fix: IdempotencyKey chống double-tap
            if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                var idempotentSession = await _posRepository.GetSessionByIdempotencyKeyAsync(request.IdempotencyKey);
                if (idempotentSession != null)
                {
                    _logger.LogInformation(
                        "CheckIn idempotent replay. IdempotencyKey={Key}, ExistingSessionId={SessionId}",
                        request.IdempotencyKey, idempotentSession.Id);
                    // Map session to DTO - need to call a method that exists
                    return await GetSessionByIdAsync(cafeId, userId, userRole, idempotentSession.Id);
                }
            }

            // GAP-1/GAP-37 Fix: Nonce chống replay attack
            if (!string.IsNullOrWhiteSpace(request.Nonce))
            {
                var nonceUsed = await _posRepository.IsNonceUsedAsync(request.Nonce);
                if (nonceUsed)
                {
                    throw new ConflictException(
                        $"Mã QR này đã được sử dụng. Vui lòng yêu cầu khách quét lại mã mới.");
                }
            }

            var code = request.Code.Trim();

            // BR mới §21A.7 + Detection: phân biệt ReservationCode vs BookingCode cũ.
            var codeType = ReservationCodeDetector.Detect(code);

            ActiveSessionDto result;
            if (codeType == ReservationCodeDetector.CodeType.Reservation)
            {
                // ====== BR-MỚI: BVC Reservation flow ======
                result = await StartSessionFromReservationAsync(cafeId, userId, code, request);
            }
            else
            {
                // ====== BR-CŨ: VND BookingDeposit flow (backward compat) ======
                result = await StartSessionFromLegacyBookingAsync(cafeId, userId, code, request);
            }

            // GAP-1/GAP-37 Fix: Lưu IdempotencyKey + Nonce sau khi thành công
            if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                await _posRepository.SaveIdempotencyKeyAsync(result.Id, request.IdempotencyKey);
            }
            if (!string.IsNullOrWhiteSpace(request.Nonce))
            {
                await _posRepository.MarkNonceUsedAsync(request.Nonce);
            }

            return result;
        }

        /// <summary>
        /// BR §21A.7: ReservationCode → ReservationService.CheckInAsync (atomic + outbox).
        /// Sau check-in: tạo ActiveSession + ActiveSessionMember ở flow POS (giống legacy).
        /// </summary>
        private async Task<ActiveSessionDto> StartSessionFromReservationAsync(
            Guid cafeId,
            Guid userId,
            string reservationCode,
            CheckInRequestDto request)
        {
            // 1) Build ActiveSession skeleton trước để có Id cho ReservationService.CheckInAsync.
            //    ReservationService.CheckInAsync chỉ làm: Reservation→CheckedIn + Lobby→InProgress
            //    + atomic seat/game abstract inventory move + outbox. KHÔNG tạo ActiveSession.
            var (table, box, session, hostMember, sessionGame) = await PrepareSessionSkeletonAsync(
                cafeId, userId, reservationCode, request, hostFromReservation: true);

            // 2) Gọi ReservationService.CheckInAsync — atomic DB transaction + outbox.
            //    GAP #6 fix: idempotency key dùng reservationCode (stable) thay vì session.Id
            //    (mỗi POS attempt session.Id mới → key khác → idempotency replay không bắt được).
            var checkInRequest = new ReservationCheckInRequestDto
            {
                CafeId = cafeId,
                ReservationCode = reservationCode,
                ActiveSessionId = session.Id,
                IdempotencyKey = $"pos-checkin:{reservationCode}"
            };

            ReservationCheckInResponseDto checkInResult;
            try
            {
                checkInResult = await _reservationService.CheckInAsync(userId, checkInRequest);
            }
            catch (NotFoundException)
            {
                throw new NotFoundException(ApiErrorMessages.Pos.ReservationNotFoundByCode(reservationCode));
            }
            catch (ConflictException ex)
            {
                // Reservation không thuộc cafe này, hoặc chưa Confirmed.
                throw new ConflictException(
                    $"Không thể check-in reservation '{reservationCode}': {ex.Message}");
            }

            _logger.LogInformation(
                "POS check-in (BR mới): reservation {ReservationId} → ActiveSession {ActiveSessionId}",
                checkInResult.ReservationId, session.Id);

            // 3) Persist physical box/table + session (ReservationService đã lo atomic Reservation flip).
            await using var tx = await _db.Database.BeginTransactionAsync();

            try
            {
                await _posRepository.AddSessionAsync(session);
                await _posRepository.SaveChangesAsync();

                await _posRepository.AddSessionMemberAsync(hostMember);
                await _posRepository.AddSessionGameAsync(sessionGame);
                await _posRepository.SaveChangesAsync();

                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }

            // 4) SignalR notify (giống legacy).
            var cafe = await _cafeRepository.GetByIdAsync(cafeId);
            var memberUserIds = new List<Guid> { session.HostId };
            await _posHubService.NotifySessionActivatedAsync(
                session.Id,
                cafeId,
                cafe?.Name ?? "Unknown Cafe",
                session.HostId,
                memberUserIds);

            session.CafeTable = table;
            session.CafeInventoryBox = box;
            session.GameTemplate = box.CafeGameInventory.GameTemplate;
            session.Members = [hostMember];

            return MapSession(session, DateTime.UtcNow);
        }

        /// <summary>
        /// BR cũ: BookingCode "BV{N}" → BookingDeposit lookup + create ActiveSession.
        /// Giữ nguyên để không vỡ POS đang dùng flow VND.
        /// </summary>
        private async Task<ActiveSessionDto> StartSessionFromLegacyBookingAsync(
            Guid cafeId,
            Guid userId,
            string bookingCode,
            CheckInRequestDto request)
        {
            // (logic cũ của StartSessionFromBookingAsync — không đổi gì)
            await using var transaction = await _db.Database.BeginTransactionAsync();

            // ...existing legacy code below...
            var deposit = await _depositRepository.GetByBookingCodeAsync(bookingCode);
            if (deposit == null)
            {
                throw new NotFoundException(ApiErrorMessages.Pos.BookingNotFoundByCode(bookingCode));
            }
            if (deposit.CafeId != cafeId)
            {
                throw new ConflictException(ApiErrorMessages.Pos.BookingNotInThisCafe);
            }
            if (deposit.Status != BookingDepositStatus.Paid)
            {
                throw new ConflictException(ApiErrorMessages.Pos.BookingDepositNotPaid);
            }

            var hostId = deposit.UserId;

            // Tái sử dụng PrepareSessionSkeleton — hostId comes from deposit not from QR.
            var (table, box, session, hostMember, sessionGame) = await PrepareSessionSkeletonAsync(
                cafeId, userId, bookingCode, request, hostFromReservation: false, overrideHostId: hostId);

            var now = DateTime.UtcNow;
            var gameTemplateId = box.CafeGameInventory.GameTemplateId;

            // Cập nhật deposit
            deposit.ActiveSessionId = session.Id;
            deposit.UpdatedAt = now;

            try
            {
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

            var cafe = await _cafeRepository.GetByIdAsync(cafeId);
            var memberUserIds = new List<Guid> { hostId };

            await _posHubService.NotifySessionActivatedAsync(
                session.Id,
                cafeId,
                cafe?.Name ?? "Unknown Cafe",
                hostId,
                memberUserIds);

            session.CafeTable = table;
            session.CafeInventoryBox = box;
            session.GameTemplate = box.CafeGameInventory.GameTemplate;
            // L3: Không detach Host navigation.
            session.Members = [hostMember];

            return MapSession(session, now);
        }

        /// <summary>
        /// Helper: validate table + box + tạo ActiveSession skeleton.
        /// Dùng chung cho cả 2 flow (Reservation mới + Booking cũ).
        /// </summary>
        private async Task<(CafeTable table, CafeInventoryBox box, ActiveSession session,
            ActiveSessionMember hostMember, ActiveSessionGame sessionGame)>
            PrepareSessionSkeletonAsync(
                Guid cafeId,
                Guid userId,
                string code,
                CheckInRequestDto request,
                bool hostFromReservation,
                Guid? overrideHostId = null)
        {
            var table = await _posRepository.GetTableAsync(cafeId, request.CafeTableId)
                ?? throw new NotFoundException(ApiErrorMessages.Pos.TableNotFound(cafeId, request.CafeTableId));

            if (table.Status != CafeTableStatus.Available)
            {
                throw new ConflictException(ApiErrorMessages.Pos.TableNotAvailableForGame(request.CafeTableId));
            }

            var barcode = request.Barcode.Trim();
            var box = await _posRepository.GetBoxByBarcodeAsync(cafeId, barcode)
                ?? throw new NotFoundException(ApiErrorMessages.Pos.BoxNotFound(cafeId, barcode));

            if (box.Status != CafeGameInventoryStatus.Available)
            {
                throw new ConflictException(ApiErrorMessages.Pos.BoxNotAvailable(box.Barcode, box.Status.ToString()));
            }

            var existingSession = await _posRepository.GetActiveSessionByBoxIdAsync(box.Id);
            if (existingSession != null)
            {
                throw new ConflictException(ApiErrorMessages.Pos.BoxAlreadyInSession(box.Barcode));
            }

            // Hoist gameTemplateId declaration lên đầu để dùng cho GAP #7 validate.
            var gameTemplateId = box.CafeGameInventory.GameTemplateId;

            // Host for ActiveSession = reservation host (BR mới) hoặc deposit.UserId (cũ).
            // Nếu POS userId không phải host vẫn cho phép (staff có quyền quét QR của khách).
            var sessionHostId = hostFromReservation
                ? userId // sẽ được ReservationService.CheckInAsync ghi đè bằng reservation.HostId
                : overrideHostId ?? userId;

            // Nếu là Reservation flow: lookup reservation qua repository (không dùng raw _db.Set).
            // Lấy HostId + LobbyId để:
            //  - session.HostId đúng ngay từ đầu.
            //  - session.LobbyId set để PaySessionAsync → CompleteAndCaptureAsync chạy được
            //    (GAP #3 fix — không set LobbyId = null làm BVC không capture về quán).
            //  - validate reservation.GameId == box.GameTemplateId (GAP #7 fix).
            Guid? sessionLobbyId = null;
            if (hostFromReservation)
            {
                var preReservation = await _reservationRepository.GetByReservationCodeAsync(code.Trim());
                if (preReservation != null)
                {
                    sessionHostId = preReservation.HostId;
                    sessionLobbyId = preReservation.LobbyId;

                    // GAP #7 fix: validate game match — staff scan box sai game.
                    if (preReservation.GameId != gameTemplateId)
                    {
                        throw new ConflictException(
                            $"Reservation '{preReservation.Id}' đặt game '{preReservation.GameId}' " +
                            $"nhưng staff đang scan box game '{gameTemplateId}'. Không thể check-in.");
                    }
                }
            }

            var now = DateTime.UtcNow;
            var sessionId = Guid.NewGuid();

            var session = new ActiveSession
            {
                Id = sessionId,
                CafeId = cafeId,
                CafeTableId = table.Id,
                CafeInventoryBoxId = box.Id,
                GameTemplateId = gameTemplateId,
                HostId = sessionHostId,
                LobbyId = sessionLobbyId, // GAP #3 fix: set từ reservation để capture BVC chạy.
                Status = GroupSessionStatus.Active,
                StartedAt = now,
                CreatedAt = now,
                DepositAppliedAmount = 0,
                Subtotal = 0,
                TotalAmount = 0
            };

            var hostMember = new ActiveSessionMember
            {
                Id = Guid.NewGuid(),
                ActiveSessionId = sessionId,
                UserId = sessionHostId,
                JoinedAt = now,
                Status = IndividualSessionStatus.Playing
            };

            var sessionGame = new ActiveSessionGame
            {
                Id = Guid.NewGuid(),
                ActiveSessionId = sessionId,
                CafeInventoryBoxId = box.Id,
                GameTemplateId = gameTemplateId,
                AttachedAt = now,
                CheckStatus = ComponentCheckStatus.NotChecked
            };

            box.Status = CafeGameInventoryStatus.InUse;
            box.UpdatedAt = now;
            table.Status = CafeTableStatus.InUse;
            table.UpdatedAt = now;

            return (table, box, session, hostMember, sessionGame);
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

            // BUG 2 Fix: Validate session is Active before ending
            if (session.Status != GroupSessionStatus.Active)
            {
                throw new ConflictException(ApiErrorMessages.Pos.SessionMustBeActiveForEnd(session.Status.ToString()));
            }

            var now = DateTime.UtcNow;
            session.EndedAt = now;
            // BR-12: Chuyển sang Checking để chờ kiểm kê linh kiện trước khi xuất hóa đơn
            session.Status = GroupSessionStatus.Checking;
            session.IsCheckingInventory = true;

            // W1 Fix: Null check for CafeInventoryBox before dereferencing
            if (session.CafeInventoryBox == null)
            {
                throw new NotFoundException(ApiErrorMessages.Pos.NoGameBoxInSession);
            }
            session.CafeInventoryBox.Status = CafeGameInventoryStatus.Available;
            session.CafeInventoryBox.UpdatedAt = now;

            // BUG 1 Fix: Release ALL boxes including extra games attached to session
            var extraGames = await _posRepository.GetSessionGamesAsync(sessionId);
            foreach (var game in extraGames)
            {
                if (game.CafeInventoryBox != null)
                {
                    game.CafeInventoryBox.Status = CafeGameInventoryStatus.Available;
                    game.CafeInventoryBox.UpdatedAt = now;
                }
            }

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
            // GAP-7 Fix: Reject Guid.Empty as a valid user (security)
            if (userId == Guid.Empty)
            {
                throw new UnauthorizedAccessException("Invalid user context.");
            }

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
        // GET: trả danh sách linh kiện cần kiểm, chưa có số liệu thực tế.
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

            return new ComponentChecklistDto
            {
                SessionGameId = sessionGame.Id,
                GameTemplateId = sessionGame.GameTemplateId,
                GameName = sessionGame.GameTemplate.Name,
                Components = components.Select(c => new ComponentCheckItemDto
                {
                    ComponentId = c.Id,
                    ComponentName = c.ComponentName,
                    ComponentKind = c.ComponentKind,
                    ExpectedQuantity = c.DefaultQuantity
                }).ToList()
            };
        }

        public async Task<ComponentCheckResultDto> SubmitComponentCheckAsync(
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

            // GAP-24 Fix: Validate session is in CHECKING status before allowing component check
            var session = sessionGame.ActiveSession;
            if (session.Status != GroupSessionStatus.Checking)
            {
                throw new ConflictException(
                    $"Chỉ có thể kiểm tra linh kiện khi phiên đang ở trạng thái CHECKING (đã trả game). Trạng thái hiện tại: {session.Status}.");
            }

            var components = sessionGame.GameTemplate.Components?.ToList() ?? [];

            // AC 3.2: "Tất cả hợp lệ" → mark Verified ngay. Vẫn insert 1 dòng result cho mỗi component
            // với ActualQuantity = ExpectedQuantity để admin audit "staff bấm AllValid lúc Y, không đếm chi tiết".
            if (request.MarkAllValid)
            {
                var now = DateTime.UtcNow;
                sessionGame.CheckStatus = ComponentCheckStatus.Verified;
                sessionGame.CheckedAt = now;
                sessionGame.CheckedByStaffId = userId;
                sessionGame.TotalPenaltyAmount = 0;

                var allValidResults = components.Select(c => new ComponentCheckResult
                {
                    Id = Guid.NewGuid(),
                    ActiveSessionGameId = sessionGame.Id,
                    GameComponentTemplateId = c.Id,
                    ExpectedQuantity = c.DefaultQuantity,
                    ActualQuantity = c.DefaultQuantity,
                    PenaltyFee = 0,
                    StaffId = userId,
                    CheckedAt = now
                }).ToList();
                await _posRepository.AddComponentCheckResultsAsync(allValidResults);
                await _posRepository.SaveChangesAsync();

                return new ComponentCheckResultDto
                {
                    SessionGameId = sessionGame.Id,
                    GameTemplateId = sessionGame.GameTemplateId,
                    GameName = sessionGame.GameTemplate.Name,
                    CheckStatus = sessionGame.CheckStatus,
                    CheckedAt = sessionGame.CheckedAt ?? now,
                    TotalPenaltyAmount = 0,
                    Components = components.Select(c => new ComponentCheckResultItemDto
                    {
                        ComponentId = c.Id,
                        ComponentName = c.ComponentName,
                        ComponentKind = c.ComponentKind,
                        ExpectedQuantity = c.DefaultQuantity,
                        ActualQuantity = c.DefaultQuantity,
                        PenaltyFee = 0
                    }).ToList()
                };
            }

            // Chi tiết từng linh kiện + tính penalty
            var gameTemplateId = sessionGame.GameTemplateId;
            var validComponentIds = components.Select(c => c.Id).ToHashSet();

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

            // GAP-16 Fix: Track components with missing penalty config for warning
            var missingPenaltyComponents = new List<string>();

            var componentIds = components.Select(c => c.Id).ToList();
            var penaltyMap = await _posRepository.GetComponentPenaltiesByCafeGameAsync(
                cafeId, gameTemplateId, componentIds);

            var resultComponents = new List<ComponentCheckResultItemDto>();
            var nowDetailed = DateTime.UtcNow;
            var hasMissing = false;

            foreach (var component in components)
            {
                var actualQty = resultLookup.GetValueOrDefault(component.Id, 0);
                var expectedQty = component.DefaultQuantity;
                var missing = expectedQty - actualQty;
                decimal penaltyFee = 0;

                if (actualQty < expectedQty)
                {
                    hasMissing = true;
                    if (penaltyMap.TryGetValue(component.Id, out var penalty))
                    {
                        penaltyFee = penalty.PenaltyFee * missing;
                        totalPenalty += penaltyFee;
                    }
                    else
                    {
                        // GAP-16 Fix: Log warning when penalty config is missing
                        missingPenaltyComponents.Add($"{component.ComponentName} (thiếu cấu hình phí đền bù)");
                        _logger.LogWarning(
                            "Component penalty config missing. CafeId={CafeId}, GameTemplateId={GameTemplateId}, ComponentId={ComponentId}, ComponentName={ComponentName}",
                            cafeId, gameTemplateId, component.Id, component.ComponentName);
                    }
                }

                resultComponents.Add(new ComponentCheckResultItemDto
                {
                    ComponentId = component.Id,
                    ComponentName = component.ComponentName,
                    ComponentKind = component.ComponentKind,
                    ExpectedQuantity = expectedQty,
                    ActualQuantity = actualQty,
                    PenaltyFee = penaltyFee
                });
            }

            // GAP-16 Fix: Log warning with all missing penalty components
            if (missingPenaltyComponents.Count > 0)
            {
                _logger.LogWarning(
                    "Missing penalty config for {Count} components in session {SessionGameId}: {Components}",
                    missingPenaltyComponents.Count, request.SessionGameId, string.Join("; ", missingPenaltyComponents));
            }

            sessionGame.CheckStatus = hasMissing
                ? ComponentCheckStatus.MissingComponents
                : ComponentCheckStatus.Verified;
            sessionGame.CheckedAt = nowDetailed;
            sessionGame.CheckedByStaffId = userId;
            sessionGame.TotalPenaltyAmount = totalPenalty;

            // BR-12: Lưu audit trail cho từng component (kể cả đủ, ActualQuantity = ExpectedQuantity).
            // Admin có thể truy vết staff có thật sự kiểm tra hay bấm AllValid.
            var detailedResults = resultComponents.Select(r => new ComponentCheckResult
            {
                Id = Guid.NewGuid(),
                ActiveSessionGameId = sessionGame.Id,
                GameComponentTemplateId = r.ComponentId,
                ExpectedQuantity = r.ExpectedQuantity,
                ActualQuantity = r.ActualQuantity,
                PenaltyFee = r.PenaltyFee,
                StaffId = userId,
                CheckedAt = nowDetailed
            }).ToList();
            await _posRepository.AddComponentCheckResultsAsync(detailedResults);
            await _posRepository.SaveChangesAsync();

            return new ComponentCheckResultDto
            {
                SessionGameId = sessionGame.Id,
                GameTemplateId = sessionGame.GameTemplateId,
                GameName = sessionGame.GameTemplate.Name,
                CheckStatus = sessionGame.CheckStatus,
                CheckedAt = sessionGame.CheckedAt ?? nowDetailed,
                TotalPenaltyAmount = totalPenalty,
                Components = resultComponents
            };
        }

        // GAP-25 Fix: Reset checklist — cho phép staff reset lại checklist nếu đã kiểm tra sai
        public async Task<ComponentChecklistDto> ResetComponentCheckAsync(
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

            // GAP-24 Fix: Validate session is in CHECKING status before allowing reset
            var session = sessionGame.ActiveSession;
            if (session.Status != GroupSessionStatus.Checking)
            {
                throw new ConflictException(
                    $"Chỉ có thể reset checklist khi phiên đang ở trạng thái CHECKING. Trạng thái hiện tại: {session.Status}.");
            }

            // Reset checklist
            sessionGame.CheckStatus = ComponentCheckStatus.NotChecked;
            sessionGame.CheckedAt = null;
            sessionGame.CheckedByStaffId = null;
            sessionGame.TotalPenaltyAmount = 0;

            // BR-12: Xóa audit trail cũ để staff có thể kiểm tra lại từ đầu.
            // Lưu ý: chỉ xóa các dòng thuộc session game hiện tại (không cascade sang session khác).
            await _posRepository.DeleteComponentCheckResultsAsync(sessionGameId);

            await _posRepository.SaveChangesAsync();

            _logger.LogInformation(
                "Component checklist reset. SessionGameId={SessionGameId}, CafeId={CafeId}, StaffId={StaffId}",
                sessionGameId, cafeId, userId);

            return await GetComponentChecklistAsync(cafeId, userId, userRole, sessionGameId);
        }

        // POST /api/cafes/{cafeId}/pos/sessions/{sessionId}/return-game
        // GAP-26 Fix: Validate box belongs to the session.
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
                throw new NotFoundException(ApiErrorMessages.Pos.SessionNotFoundById(sessionId));
            }

            var box = await _posRepository.GetInventoryBoxByIdAsync(request.InventoryBoxId);
            if (box == null || box.CafeGameInventory.CafeId != cafeId)
            {
                throw new NotFoundException(ApiErrorMessages.Pos.BoxNotFoundById(request.InventoryBoxId));
            }

            // GAP-26 Fix: Validate box belongs to this session's Games list
            var sessionGame = session.Games?.FirstOrDefault(g => g.CafeInventoryBoxId == request.InventoryBoxId);
            if (sessionGame == null)
            {
                throw new ConflictException(
                    $"Hộp game '{box.Barcode}' không thuộc phiên chơi này. Vui lòng kiểm tra lại.");
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

        // ====== Billing Operations - delegates to ActiveSessionService ======

        /// <summary>
        /// Gán thêm game vào phiên chơi.
        /// Exception 6: Nhóm tự ý lấy thêm game mà không báo nhân viên.
        /// GAP-13 Fix: Validate session status is Active before attaching game.
        /// GAP-14 Fix: Ensure Games navigation is loaded before accessing.
        /// </summary>
        public async Task<ActiveSessionDto> AttachGameAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid sessionId,
            AttachGameRequestDto request)
        {
            await EnsurePosAccessAsync(cafeId, userId, userRole);

            // Delegate to ActiveSessionService (which now has GAP-13 fix)
            await _activeSessionService.AttachGameAsync(cafeId, sessionId, request);

            // Fetch session entity with Games included to map response
            var session = await _posRepository.GetActiveSessionByIdAsync(cafeId, sessionId);
            return MapSession(session!, DateTime.UtcNow);
        }

        /// <summary>
        /// Thêm khách vô danh vào phiên chơi.
        /// Exception 10: Khách không có ứng dụng hoặc điện thoại hết pin.
        /// </summary>
        public async Task<ActiveSessionDto> AddGuestSlotAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid sessionId,
            AddGuestSlotRequestDto request)
        {
            await EnsurePosAccessAsync(cafeId, userId, userRole);

            var result = await _activeSessionService.AddGuestSlotAsync(cafeId, sessionId, request);

            // Fetch session entity to map response
            var session = await _posRepository.GetActiveSessionByIdAsync(cafeId, sessionId);
            return MapSession(session!, DateTime.UtcNow);
        }

        /// <summary>
        /// Thêm thành viên đến muộn vào phiên chơi.
        /// Exception 8: Thêm 2 người bạn đến muộn vào nhóm đang chơi.
        /// </summary>
        public async Task<ActiveSessionDto> AddLateMemberAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid sessionId,
            AddLateMemberRequestDto request)
        {
            await EnsurePosAccessAsync(cafeId, userId, userRole);

            var result = await _activeSessionService.AddLateMemberAsync(cafeId, sessionId, request);

            // Fetch session entity to map response
            var session = await _posRepository.GetActiveSessionByIdAsync(cafeId, sessionId);
            return MapSession(session!, DateTime.UtcNow);
        }

        /// <summary>
        /// Ghi nhận hao hụt linh kiện trước phiên chơi.
        /// Exception 7: Nhân viên ca chiều phát hiện game thiếu từ ca sáng.
        /// </summary>
        public async Task RecordInventoryLossAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid sessionId,
            RecordInventoryLossRequestDto request)
        {
            await EnsurePosAccessAsync(cafeId, userId, userRole);

            await _activeSessionService.RecordInventoryLossAsync(cafeId, userId, sessionId, request);
        }

        /// <summary>
        /// P-04: Ghi nhận hao hụt linh kiện TRƯỚC KHI có phiên chơi — dùng cho shift handoff.
        /// Không cần sessionId, chỉ cần cafeId + game box info.
        /// Tạo ComponentLossReport với ActiveSessionId = null.
        /// </summary>
        public async Task RecordPreSessionInventoryLossAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            RecordPreSessionInventoryLossRequestDto request)
        {
            await EnsurePosAccessAsync(cafeId, userId, userRole);

            var report = new ComponentLossReport
            {
                Id = Guid.NewGuid(),
                CafeId = cafeId,
                ActiveSessionId = null, // P-04: không có phiên chơi
                CafeInventoryBoxId = request.CafeInventoryBoxId,
                ReportedByUserId = userId,
                LossDescription = request.LostComponents.Count > 0
                    ? $"Hao hụt trước ca: thiếu {request.LostComponents.Count} linh kiện"
                    : "Ghi nhận hao hụt trước ca làm việc",
                Notes = request.Notes,
                CreatedAt = DateTime.UtcNow
            };

            await _posRepository.AddComponentLossReportAsync(report);

            _logger.LogInformation(
                "Pre-session inventory loss recorded. CafeId={CafeId}, BoxId={BoxId}, ReportedBy={UserId}",
                cafeId, request.CafeInventoryBoxId, userId);
        }

        // ====== Checkout & Payment Operations ======

        /// <summary>
        /// Thanh toán toàn bộ phiên chơi sau kiểm kê linh kiện.
        /// BR-12: Chỉ gọi được khi session ở trạng thái CHECKING và đã kiểm kê đủ.
        /// GAP-7 Fix: Nhận userId/role để EnsurePosAccessAsync đúng cách.
        /// </summary>
        public async Task<ActiveSessionResponseDto> CheckoutAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid sessionId,
            CheckoutRequestDto request)
        {
            await EnsurePosAccessAsync(cafeId, userId, userRole);

            return await _activeSessionService.CheckoutAsync(cafeId, sessionId, request);
        }

        /// <summary>
        /// Thanh toán hóa đơn tổng của phiên chơi.
        /// BR-15: TotalAmount = Subtotal + PenaltyAmount - DepositAppliedAmount
        /// BR-09: Deposit chỉ cấn trừ DUY NHẤT 1 LẦN vào hóa đơn tổng
        /// GAP-7 Fix: Nhận userId/role để EnsurePosAccessAsync đúng cách.
        /// </summary>
        public async Task<PaySessionResponseDto> PaySessionAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid sessionId,
            PaySessionRequestDto request)
        {
            await EnsurePosAccessAsync(cafeId, userId, userRole);

            return await _activeSessionService.PaySessionAsync(cafeId, sessionId, request);
        }

        /// <summary>
        /// Thanh toán một phần cho nhóm về sớm.
        /// BR-12: Khóa in hóa đơn đến khi kiểm kê xong.
        /// GAP-7 Fix: Nhận userId/role để EnsurePosAccessAsync đúng cách.
        /// </summary>
        public async Task<ActiveSessionResponseDto> PartialCheckoutAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid sessionId,
            PartialCheckoutRequestDto request)
        {
            await EnsurePosAccessAsync(cafeId, userId, userRole);

            return await _activeSessionService.PartialCheckoutAsync(cafeId, sessionId, request);
        }

        /// <summary>
        /// Ghép thành viên vào phiên chơi của nhóm mới.
        /// Exception 4: A3 nhảy từ nhóm A sang nhóm B.
        /// GAP-7 Fix: Nhận userId/role để EnsurePosAccessAsync đúng cách.
        /// </summary>
        public async Task<MergeSessionResponseDto> MergeSessionAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid sourceSessionId,
            MergeSessionRequestDto request)
        {
            await EnsurePosAccessAsync(cafeId, userId, userRole);

            return await _activeSessionService.MergeSessionAsync(cafeId, sourceSessionId, request);
        }
    }
}
