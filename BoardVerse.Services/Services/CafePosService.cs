using BoardVerse.Core.DTOs.Common;
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
        /// includeInactive: lấy cả bàn soft-deleted (IsActive=false). Mặc định false để khớp hành vi cũ.
        /// statuses: nếu khác null, chỉ trả bàn có Status thuộc collection này (ghi đè includeOnlyAvailable).
        ///
        /// Gap-Fix "Sơ đồ bàn hiển thị sai trạng thái khi có session hoạt động":
        /// Trước đây status trả về chỉ dựa trên cột <c>CafeTables.Status</c> trong DB, dẫn đến hiện tượng
        /// bàn có session Active/Checking/Unpaid vẫn hiển thị "Available" nếu cột Status bị stale
        /// (do manual SQL fixup, migration dở, hoặc bug path trước đó chưa update).
        /// Cách fix: derive status từ <c>ActiveSessions</c> — đây là source-of-truth duy nhất.
        ///
        /// Thứ tự ưu tiên status của bàn:
        /// 1. Nếu <c>ActiveSessions</c> có session chưa thanh toán (Active/Checking/Unpaid) → <c>InUse</c>.
        /// 2. Ngược lại dùng cached <c>CafeTables.Status</c> (Reserved / EventInProgress / Available).
        /// </summary>
        public async Task<IReadOnlyList<CafeTableStatusDto>> GetTablesAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            bool includeOnlyAvailable = true,
            bool includeInactive = false,
            IReadOnlyCollection<CafeTableStatus>? statuses = null)
        {
            await EnsurePosAccessAsync(cafeId, userId, userRole);

            var allTables = await _posRepository.GetActiveTablesAsync(cafeId, includeInactive);

            // Gap-Fix: Build map bàn đang bận từ ActiveSessions (source-of-truth).
            // Self-healing: nếu CafeTables.Status bị stale do bug cũ, vẫn trả đúng InUse.
            var busyTableStatuses = await _posRepository.GetBusyTableIdsByCafeAsync(cafeId);

            // Derive status: overlay busyTableStatuses lên cached t.Status.
            // Nếu table có session chưa thanh toán → InUse, kể cả khi t.Status cached = Available.
            // Nếu table KHÔNG có session → giữ nguyên t.Status cached (Reserved/EventInProgress vẫn respected).
            var tablesWithDerivedStatus = allTables
                .Select(t => new
                {
                    Table = t,
                    DerivedStatus = busyTableStatuses.ContainsKey(t.Id)
                        ? CafeTableStatus.InUse
                        : t.Status
                })
                .ToList();

            IEnumerable<(CafeTable Table, CafeTableStatus DerivedStatus)> filtered = tablesWithDerivedStatus
                .Select(x => (x.Table, x.DerivedStatus));

            if (statuses is { Count: > 0 })
            {
                var statusSet = new HashSet<CafeTableStatus>(statuses);
                filtered = filtered.Where(x => statusSet.Contains(x.DerivedStatus));
            }
            else if (includeOnlyAvailable)
            {
                filtered = filtered.Where(x => x.DerivedStatus == CafeTableStatus.Available);
            }

            return filtered
                .OrderBy(x => x.Table.SortOrder)
                .ThenBy(x => x.Table.Name)
                .Select(x => new CafeTableStatusDto
                {
                    Id = x.Table.Id,
                    Name = x.Table.Name,
                    SortOrder = x.Table.SortOrder,
                    SeatCount = x.Table.SeatCount,
                    Status = x.DerivedStatus,
                    IsActive = x.Table.IsActive
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

            try
            {
                await _cafeRepository.SyncCafeTablesAsync(cafeId, tables);
            }
            catch (ArgumentException ex) when (ex.Message.Contains("SortOrder không được trùng lặp"))
            {
                // GAP-XX Fix: CafeTableSyncHelper throws ArgumentException khi payload có
                // 2 bàn cùng SortOrder. Convert thành BadRequestException (400) thay vì
                // để bubble lên 500.
                // ex.Message format: "SortOrder không được trùng lặp: 0, 2. Vui lòng đánh số..."
                var duplicates = ExtractDuplicates(ex.Message);
                throw new BadRequestException(
                    ApiErrorMessages.Pos.DuplicateSortOrderInPayload(duplicates));
            }
        }

        /// <summary>
        /// Parse số SortOrder trùng từ message của helper.
        /// Input: "SortOrder không được trùng lặp: 0, 2. Vui lòng ..."
        /// Output: "0, 2"
        /// </summary>
        private static string ExtractDuplicates(string message)
        {
            const string marker = "SortOrder không được trùng lặp: ";
            var startIdx = message.IndexOf(marker, StringComparison.Ordinal);
            if (startIdx < 0)
            {
                return message;
            }

            var afterMarker = message[(startIdx + marker.Length)..];
            var endIdx = afterMarker.IndexOf('.', StringComparison.Ordinal);
            return endIdx < 0 ? afterMarker : afterMarker[..endIdx];
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
        /// Lấy danh sách phiên chơi đang UNPAID (chờ thanh toán).
        /// POS staff scan để tìm phiên đã end-game nhưng quên thanh toán.
        /// - Nếu sessionId != null → trả về session cụ thể (nếu đang UNPAID).
        /// - Nếu sessionId == null → trả về tất cả UNPAID.
        /// </summary>
        public async Task<IReadOnlyList<ActiveSessionDto>> GetUnpaidSessionsAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid? sessionId = null)
        {
            await EnsurePosAccessAsync(cafeId, userId, userRole);

            var sessions = await _posRepository.GetUnpaidSessionsAsync(cafeId, sessionId);
            var utcNow = DateTime.UtcNow;

            return sessions
                .OrderBy(s => s.EndedAt ?? s.StartedAt)  // lâu nhất lên đầu
                .Select(s => MapSession(s, utcNow))
                .ToList();
        }

        /// <summary>
        /// Lấy danh sách phiên chơi đã thanh toán (PAID) theo khoảng ngày + phân trang.
        /// POS manager dùng cho end-of-day report / đối soát SePay / cash reconciliation.
        /// </summary>
        public async Task<PaginatedResult<PaidSessionDto>> GetPaidSessionsPagedAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            GetPaidSessionsQuery query)
        {
            await EnsurePosAccessAsync(cafeId, userId, userRole);

            if (query.FromDate > query.ToDate)
            {
                throw new BadRequestException(
                    ApiErrorMessages.System.DateRangeInvalid(query.FromDate, query.ToDate));
            }

            // Giới hạn range tối đa 90 ngày để tránh query nặng (audit pagination sau).
            if (query.ToDate.DayNumber - query.FromDate.DayNumber > 90)
            {
                throw new BadRequestException(
                    ApiErrorMessages.System.DateRangeExceeded(query.FromDate, query.ToDate));
            }

            var paged = await _posRepository.GetPaidSessionsPagedAsync(
                cafeId,
                query.FromDate,
                query.ToDate,
                query.GameTemplateId,
                query.StaffId,
                query.PageNumber,
                query.PageSize);

            var memberCounts = await _posRepository.GetActiveSessionMemberCountsAsync(
                cafeId,
                paged.Items.Select(s => s.Id).ToList());

            var items = paged.Items.Select(s => MapPaidSession(s, memberCounts.GetValueOrDefault(s.Id, 0))).ToList();

            return new PaginatedResult<PaidSessionDto>
            {
                Items = items,
                TotalCount = paged.TotalCount,
                PageNumber = query.PageNumber,
                PageSize = query.PageSize
            };
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

            // BR-13: Walk-in session KHÔNG tạo hostMember cho staff.
            // Staff chỉ là người khởi tạo (lưu ở HostId) — KHÔNG phải customer, không tính tiền giờ,
            // không hiển thị trong members list. Members của walk-in chỉ chứa guest slots / late members.

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
            await _posRepository.AddSessionGameAsync(sessionGame);
            await _posRepository.SaveChangesAsync();

            session.CafeTable = table;
            session.CafeInventoryBox = box;
            session.GameTemplate = box.CafeGameInventory.GameTemplate;
            // L3: Không detach Host — MapSession đọc session.Host?.Username.
            // Nếu sau này cần HostName, load Host qua repository trước khi map.
            session.Members = []; // Walk-in: không có member nào, chờ AddGuestSlot / AddLateMember.

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
                    throw new InternalServerErrorException(
                        ApiErrorMessages.System.PosCheckInTokenGenerationFailed);
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
                        ApiErrorMessages.System.QrAlreadyUsed);
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
                    ApiErrorMessages.System.ReservationCheckInFailed(reservationCode, ex.Message));
            }

            _logger.LogInformation(
                "POS check-in (BR mới): reservation {ReservationId} → ActiveSession {ActiveSessionId}",
                checkInResult.ReservationId, session.Id);

            // 3) Lấy tất cả thành viên lobby để thêm vào session.
            // BR §21A.7: "Quét một lần mã định danh đặt chỗ → kích hoạt phiên cho CẢ NHÓM".
            var lobbyMembers = new List<LobbyMember>();
            if (checkInResult.LobbyId != Guid.Empty)
            {
                var lobby = await _lobbyRepository.GetByIdWithMembersAsync(checkInResult.LobbyId);
                if (lobby != null)
                {
                    lobbyMembers = lobby.Members.Where(m => m.IsActive).ToList();
                    _logger.LogInformation(
                        "POS check-in: lobby {LobbyId} has {TotalMembers} total members, {ActiveMembers} active (after IsActive filter)",
                        lobby.Id, lobby.Members.Count, lobbyMembers.Count);
                    foreach (var lm in lobby.Members)
                    {
                        _logger.LogInformation(
                            "  member: UserId={UserId}, IsHost={IsHost}, IsActive={IsActive}, Status={Status}",
                            lm.UserId, lm.IsHost, lm.IsActive, lm.Status);
                    }
                }
                else
                {
                    _logger.LogWarning("POS check-in: lobby {LobbyId} not found when fetching members", checkInResult.LobbyId);
                }
            }

            // 4) Persist physical box/table + session (ReservationService đã lo atomic Reservation flip).
            await using var tx = await _db.Database.BeginTransactionAsync();
            var persistNow = DateTime.UtcNow;

            try
            {
                await _posRepository.AddSessionAsync(session);
                await _posRepository.SaveChangesAsync();

                // Thêm host (đã tạo trong PrepareSessionSkeletonAsync).
                await _posRepository.AddSessionMemberAsync(hostMember);

                // Thêm các thành viên khác từ lobby.
                // Skip host vì đã được thêm ở trên.
                foreach (var lobbyMember in lobbyMembers.Where(m => !m.IsHost))
                {
                    var sessionMember = new ActiveSessionMember
                    {
                        Id = Guid.NewGuid(),
                        ActiveSessionId = session.Id,
                        UserId = lobbyMember.UserId,
                        IsHost = false,
                        IsGuestSlot = false,
                        JoinedAt = persistNow,
                        Status = IndividualSessionStatus.Playing
                    };
                    await _posRepository.AddSessionMemberAsync(sessionMember);
                }

                await _posRepository.AddSessionGameAsync(sessionGame);
                await _posRepository.SaveChangesAsync();

                await tx.CommitAsync();

                // Update session.Members để trả về response đúng.
                session.Members = [hostMember, .. lobbyMembers
                    .Where(m => !m.IsHost)
                    .Select(m => new ActiveSessionMember
                    {
                        Id = Guid.NewGuid(),
                        ActiveSessionId = session.Id,
                        UserId = m.UserId,
                        IsHost = false,
                        IsGuestSlot = false,
                        JoinedAt = persistNow,
                        Status = IndividualSessionStatus.Playing
                    })];
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }

            // 5) SignalR notify — gửi tất cả user IDs trong nhóm.
            var cafe = await _cafeRepository.GetByIdAsync(cafeId);
            var memberUserIds = session.Members
                .Where(m => m.UserId.HasValue)
                .Select(m => m.UserId!.Value)
                .ToList();
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
                            ApiErrorMessages.System.ReservationGameMismatch(
                                preReservation.Id, gameTemplateId));
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
                IsHost = true,
                IsGuestSlot = false,
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

            // Gap-Fix 2026-08-15: KHÔNG set table.Status = Available ở đây.
            // EndGameSessionAsync chỉ chuyển session sang Checking (BR-12, chờ kiểm kê linh kiện).
            // Bàn vẫn phải là InUse cho đến khi thanh toán xong (Paid) — self-healing fix ở
            // GetTablesAsync derive status từ ActiveSessions (Status ∈ {Active,Checking,Unpaid})
            // vẫn trả InUse, nên UI POS sẽ thấy bàn "đang bận" cho đến khi session sang Paid.
            // Set Available duy nhất tại ActiveSessionService.PaySessionAsync (xem UpdateTableAfterPaymentAsync).

            await _posRepository.SaveChangesAsync();

            return MapSession(session, now);
        }

        private async Task EnsurePosAccessAsync(Guid cafeId, Guid userId, string userRole)
        {
            // GAP-7 Fix: Reject Guid.Empty as a valid user (security)
            if (userId == Guid.Empty)
            {
                throw new UnauthorizedException(ApiErrorMessages.System.InvalidUserContext);
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

            // Phase 4 / EC-10: cảnh báo TimeSlot sắp hết trước khi game xong.
            // Reservation.ScheduledEndTime là SoT (BR-RESV-02). Lobby.Reservation navigation
            // được load qua include ở GetActiveSessionsAsync.
            var (overrunWarning, timeSlotRemaining) = ReservationTimeOverrunHelper.Compute(
                session.Lobby?.Reservation?.ScheduledEndTime,
                remaining,
                utcNow);

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
                // Host lưu ở session.HostId để audit / SignalR — KHÔNG phải customer,
                // nên không hiển thị và không tính tiền giờ (Checkout/Pay đã lặp Members).
                Members = session.Members?
                    .Where(m => m.Status != IndividualSessionStatus.Finished
                                && m.UserId != session.HostId)
                    .Select(m => new ActiveSessionMemberDto
                {
                    Id = m.Id,
                    UserId = m.UserId,
                    UserName = m.IsGuestSlot
                        ? (string.IsNullOrWhiteSpace(m.GuestDisplayName) ? "Khách vô danh" : m.GuestDisplayName)
                        : (m.User?.Username ?? string.Empty),
                    IsGuestSlot = m.IsGuestSlot,
                    PhoneNumber = m.IsGuestSlot ? m.GuestPhoneNumber : null,
                    JoinedAt = m.JoinedAt,
                    LeftAt = m.LeftAt,
                    TotalMinutesPlayed = m.Status == IndividualSessionStatus.Finished
                        ? m.TotalMinutesPlayed
                        : (int)Math.Floor((utcNow - m.JoinedAt).TotalMinutes),
                    Subtotal = 0, // Per-member subtotal is recomputed at checkout (MapInvoices/PaySession).
                    PenaltyAmount = m.PenaltyAmount,
                    IsCheckedOut = m.IsCheckedOut,
                    CheckedOutAt = m.CheckedOutAt,
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

        /// <summary>
        /// Map ActiveSession (PAID) → PaidSessionDto cho end-of-day report.
        /// BR-REVENUE-01: PaidSession là "doanh thu đã ghi nhận".
        /// </summary>
        private static PaidSessionDto MapPaidSession(ActiveSession session, int memberCount)
        {
            return new PaidSessionDto
            {
                Id = session.Id,
                CafeId = session.CafeId,
                HostId = session.HostId,
                HostName = session.Host?.Username ?? string.Empty,
                LobbyId = session.LobbyId,
                CafeTableId = session.CafeTableId,
                TableName = session.CafeTable?.Name ?? string.Empty,
                GameTemplateId = session.GameTemplateId,
                GameName = session.GameTemplate?.Name ?? string.Empty,
                StartedAt = session.StartedAt,
                EndedAt = session.EndedAt,
                PaidAt = session.PaidAt ?? DateTime.UtcNow,
                TotalMinutesPlayed = session.TotalMinutesPlayed,
                Subtotal = session.Subtotal,
                PenaltyAmount = session.PenaltyAmount,
                DepositAppliedAmount = session.DepositAppliedAmount,
                TotalAmount = session.TotalAmount,
                MemberCount = memberCount
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

            // GAP-D: BR-12 chỉ cho phép kiểm kê khi session đang CHECKING (đã trả game).
            // GET checklist cho phép FE đồng bộ UI ngay từ khi end-game → cùng status
            // với Submit/Reset để staff không truy cập nhầm khi session ACTIVE.
            var session = sessionGame.ActiveSession;
            if (session.Status != GroupSessionStatus.Checking)
            {
                throw new ConflictException(
                    ApiErrorMessages.System.ChecklistViewRequiresChecking);
            }

            var components = sessionGame.GameTemplate.Components?.ToList() ?? [];

            // FIX Bug "ComponentChecklist luôn trả đầy đủ dù hộp đã bị mất":
            // Lấy ActualQuantity từ lần kiểm kê GẦN NHẤT của cùng box làm ExpectedQuantity mới.
            // Ví dụ: Phiên trước box thiếu 1 quân cờ → Actual=14/15.
            // Lần này staff kiểm kê → ExpectedQuantity=14 (không phải 15),
            // nếu khách trả còn 14/14 thì đủ, nếu còn 13/14 thì phạt tiếp 1 quân.
            // W-4: defensive null. GetLatestComponentCheckByBoxAsync có thể trả null
            // nếu repo setup Moq chưa trả default, hoặc DB chưa có audit trail nào.
            // null.TryGetValue → NRE 500. Empty dict là fallback an toàn.
            var latestByComponent = await _posRepository
                .GetLatestComponentCheckByBoxAsync(sessionGame.CafeInventoryBoxId)
                ?? new Dictionary<Guid, ComponentCheckResult>();

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
                    // Nếu box từng được check → dùng ActualQuantity gần nhất làm baseline mới.
                    // Nếu chưa có lần check nào → dùng DefaultQuantity từ template.
                    ExpectedQuantity = latestByComponent.TryGetValue(c.Id, out var last) && last.ActualQuantity > 0
                        ? last.ActualQuantity
                        : c.DefaultQuantity
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
                    ApiErrorMessages.System.ChecklistSubmitRequiresChecking);
            }

            // GAP-A: Wrap mutation trong 1 transaction. Nếu 2 staff cùng bấm Submit trên cùng
            // session game (2 POS tablet / 2 ca), cả hai sẽ qua check NotChecked ngay sau đó
            // cùng insert ComponentCheckResults. Insert thứ 2 vi phạm unique constraint
            // (ActiveSessionGameId, GameComponentTemplateId) → DB ném PostgresException 23505
            // → mặc định 500. Catch riêng unique violation ở cuối hàm → throw ConflictException
            // (409) thay vì 500.
            //
            // Lưu ý: việc thêm ExecuteSqlRaw với row lock cũng là 1 lựa chọn, nhưng transaction
            // đơn giản hơn và đủ tốt cho 1 staff/ca trong MVP.
            await using var tx = await _db.Database.BeginTransactionAsync();

            try
            {
                var resultDto = await SubmitComponentCheckCoreAsync(
                    sessionGame, session, cafeId, userId, request);

                await tx.CommitAsync();
                return resultDto;
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // GAP-A: duplicate (ActiveSessionGameId, GameComponentTemplateId) → 409.
                await tx.RollbackAsync();
                _logger.LogWarning(
                    "Concurrent component-check submit detected cho sessionGameId={SessionGameId} bởi staffId={StaffId}.",
                    request.SessionGameId, userId);
                throw new ConflictException(
                    ApiErrorMessages.Pos.ComponentCheckConcurrentSubmit);
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        // Core mutation logic tách ra để SubmitComponentCheckAsync có thể wrap transaction.
        private async Task<ComponentCheckResultDto> SubmitComponentCheckCoreAsync(
            ActiveSessionGame sessionGame,
            ActiveSession session,
            Guid cafeId,
            Guid userId,
            SubmitComponentCheckRequestDto request)
        {

            var components = sessionGame.GameTemplate.Components?.ToList() ?? [];

            // FIX Bug "MarkAllValid dùng DefaultQuantity thay vì baseline":
            // Lấy ActualQuantity từ lần kiểm kê GẦN NHẤT của cùng box làm baseline mới.
            // Nếu box đã thiếu linh kiện ở phiên trước → MarkAllValid phải ghi Actual = baseline,
            // không phải DefaultQuantity từ template (sẽ khôi phục sai).
            // W-4: defensive null. GetLatestComponentCheckByBoxAsync có thể trả null
            // nếu repo setup Moq chưa trả default, hoặc DB chưa có audit trail nào.
            // null.TryGetValue → NRE 500. Empty dict là fallback an toàn.
            var latestByComponent = await _posRepository
                .GetLatestComponentCheckByBoxAsync(sessionGame.CafeInventoryBoxId)
                ?? new Dictionary<Guid, ComponentCheckResult>();

            // AC 3.2: "Tất cả hợp lệ" → mark Verified ngay. Vẫn insert 1 dòng result cho mỗi component
            // với ActualQuantity = ExpectedQuantity (baseline) để admin audit "staff bấm AllValid lúc Y, không đếm chi tiết".
            if (request.MarkAllValid)
            {
                var now = DateTime.UtcNow;
                sessionGame.CheckStatus = ComponentCheckStatus.Verified;
                sessionGame.CheckedAt = now;
                sessionGame.CheckedByStaffId = userId;
                sessionGame.TotalPenaltyAmount = 0;

                var allValidResults = components.Select(c =>
                {
                    var baseline = latestByComponent.TryGetValue(c.Id, out var last) && last.ActualQuantity > 0
                        ? last.ActualQuantity
                        : c.DefaultQuantity;
                    return new ComponentCheckResult
                    {
                        Id = Guid.NewGuid(),
                        ActiveSessionGameId = sessionGame.Id,
                        GameComponentTemplateId = c.Id,
                        ExpectedQuantity = baseline,
                        ActualQuantity = baseline,
                        PenaltyFee = 0,
                        StaffId = userId,
                        CheckedAt = now
                    };
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
                    ApiErrorMessages.System.ChecklistDuplicateComponentIds(duplicateIds.First()));
            }

            // H8 Fix: Bắt buộc staff nhập ActualQuantity cho TẤT CẢ components của game.
            // Trước đây: component thiếu entry trong request.Results → mặc định actualQty=0
            // → trigger penalty mất (0 < expectedQty) → phạt nhầm khi staff lỡ quên nhập.
            var missingComponentIds = components
                .Select(c => c.Id)
                .Where(id => !request.Results.Any(r => r.ComponentId == id))
                .ToList();
            if (missingComponentIds.Count > 0)
            {
                var missingNames = components
                    .Where(c => missingComponentIds.Contains(c.Id))
                    .Select(c => c.ComponentName)
                    .ToList();
                throw new BadRequestException(
                    ApiErrorMessages.System.ChecklistMissingComponents);
            }

            // H8 Fix: cấm ActualQuantity âm (sai validation từ client).
            var negativeQtyIds = request.Results
                .Where(r => r.ActualQuantity < 0)
                .Select(r => r.ComponentId)
                .ToList();
            if (negativeQtyIds.Count > 0)
            {
                throw new BadRequestException(
                    ApiErrorMessages.System.ChecklistNegativeQuantity);
            }

            decimal totalPenalty = 0;
            var resultLookup = request.Results.ToDictionary(r => r.ComponentId, r => r.ActualQuantity);

            // GAP-16 Fix: Track components with missing penalty config for warning
            var missingPenaltyComponents = new List<string>();

            var componentIds = components.Select(c => c.Id).ToList();
            var penaltyMap = await _posRepository.GetComponentPenaltiesByCafeGameAsync(
                cafeId, gameTemplateId, componentIds);

            // Penalty #1: Build map ResponsibleMemberId per component từ request.
            // BR-14: nếu member là Guest_Slot thì reject lúc validate (xem dưới).
            // null = phạt chung vào session.PenaltyAmount, không phân bổ cho member cụ thể.
            var responsibleMemberMap = request.Results
                .Where(r => r.ResponsibleMemberId.HasValue)
                .ToDictionary(r => r.ComponentId, r => r.ResponsibleMemberId!.Value);

            if (responsibleMemberMap.Count > 0)
            {
                var memberIds = responsibleMemberMap.Values.Distinct().ToList();
                var sessionMemberIds = session.Members.Select(m => m.Id).ToHashSet();
                var guestMemberIds = session.Members
                    .Where(m => m.IsGuestSlot)
                    .Select(m => m.Id)
                    .ToHashSet();

                // GAP-C: build map ActualQuantity theo componentId để validate ngay
                // nếu staff set ResponsibleMemberId cho dòng ĐỦ (actualQty >= expectedQty).
                // Trước đây server silently set ResponsibleMemberId = null ở cuối method
                // → admin audit không thấy dấu vết → dễ che giấu nhân viên set nhầm.
                var actualQtyMap = request.Results
                    .ToDictionary(r => r.ComponentId, r => r.ActualQuantity);

                foreach (var (componentId, memberId) in responsibleMemberMap)
                {
                    if (!sessionMemberIds.Contains(memberId))
                    {
                        // Member không thuộc session → BadRequest, không phải Conflict.
                        throw new BadRequestException(
                            ApiErrorMessages.Pos.ComponentPenaltyMemberNotInSession(componentId, memberId));
                    }
                    if (guestMemberIds.Contains(memberId))
                    {
                        // BR-14: cấm gán penalty cho Guest_Slot
                        throw new BadRequestException(ApiErrorMessages.Pos.PenaltyCannotAssignToGuestSlot);
                    }

                    // GAP-C: chỉ cho phép assign ResponsibleMemberId khi linh kiện THIẾU
                    // (actualQty < expectedQty). Nếu staff set cho dòng đủ, reject ngay
                    // thay vì silently drop ở assignment ở cuối method.
                    // Lưu ý: comparison dùng DefaultQuantity ở đây (chưa load baseline),
                    // vì validate sớm trước khi apply baseline.
                    var expectedQtyForValidation = components
                        .FirstOrDefault(c => c.Id == componentId)?.DefaultQuantity ?? 0;
                    var actualQtyForValidation = actualQtyMap.GetValueOrDefault(componentId, 0);

                    if (expectedQtyForValidation > 0 && actualQtyForValidation >= expectedQtyForValidation)
                    {
                        throw new BadRequestException(
                            ApiErrorMessages.Pos.ComponentPenaltyMemberInvalidForFullComponent(memberId));
                    }
                }
            }

            var resultComponents = new List<ComponentCheckResultItemDto>();
            var nowDetailed = DateTime.UtcNow;
            var hasMissing = false;

            foreach (var component in components)
            {
                var actualQty = resultLookup.GetValueOrDefault(component.Id, 0);
                // FIX Bug "SubmitComponentCheck dùng DefaultQuantity thay vì baseline":
                // Nếu box đã bị mất linh kiện ở phiên trước → ExpectedQuantity = baseline mới
                // (ActualQuantity của lần kiểm gần nhất), không phải DefaultQuantity từ template.
                // Nếu chưa có lần check nào → dùng DefaultQuantity.
                var expectedQty = latestByComponent.TryGetValue(component.Id, out var lastBaseline) && lastBaseline.ActualQuantity > 0
                    ? lastBaseline.ActualQuantity
                    : component.DefaultQuantity;
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

                responsibleMemberMap.TryGetValue(component.Id, out var responsibleMemberId);

                resultComponents.Add(new ComponentCheckResultItemDto
                {
                    ComponentId = component.Id,
                    ComponentName = component.ComponentName,
                    ComponentKind = component.ComponentKind,
                    ExpectedQuantity = expectedQty,
                    ActualQuantity = actualQty,
                    PenaltyFee = penaltyFee,
                    ResponsibleMemberId = actualQty < expectedQty ? responsibleMemberId : null
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
                ResponsibleMemberId = r.ResponsibleMemberId,
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
                    ApiErrorMessages.System.ChecklistResetRequiresChecking);
            }

            // Reset checklist
            sessionGame.CheckStatus = ComponentCheckStatus.NotChecked;
            sessionGame.CheckedAt = null;
            sessionGame.CheckedByStaffId = null;
            sessionGame.TotalPenaltyAmount = 0;

            // BR-12: Xóa audit trail cũ để staff có thể kiểm tra lại từ đầu.
            // Lưu ý: chỉ xóa các dòng thuộc session game hiện tại (không cascade sang session khác).
            //
            // GAP-E: Wrap reset + delete trong 1 transaction. Trước đây nếu SaveChangesAsync
            // ở giữa thất bại → entity state đã thay đổi trên tracker nhưng chưa commit. Sau
            // đó delete + save khác round-trip → rủi ro data trong trạng thái lủng lẳng nếu
            // có exception giữa chừng.
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                await _posRepository.DeleteComponentCheckResultsAsync(sessionGameId);
                await _posRepository.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }

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
            // GAP-26 / Return-Game legacy: Endpoint deprecated từ 2026-08-10.
            // Penalty giờ là single source of truth từ ComponentCheckResult.ResponsibleMemberId
            // (submit lúc POST /sessions/component-check). Endpoint vẫn trả 200 + set SurchargeFine
            // để back-compat POS client cũ; v2.0 sẽ đổi thành 410 Gone.
            _logger.LogWarning(
                "[DEPRECATED] ReturnGame endpoint bị gọi bởi userId={UserId}, cafeId={CafeId}, sessionId={SessionId}. Penalty tính ở đây KHÔNG được dùng - staff phải nhập qua POST /sessions/component-check.",
                userId, cafeId, sessionId);

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
                    ApiErrorMessages.System.BoxNotInSession(box.Barcode));
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

            // Accept both JSON keys "displayName" and "username" (backward-compat with older clients).
            // Merge BEFORE re-validating length so we don't reject legitimate aliases.
            if (string.IsNullOrWhiteSpace(request.DisplayName)
                && !string.IsNullOrWhiteSpace(request.Username))
            {
                request.DisplayName = request.Username.Trim();
            }

            // GAP-17 Fix: validate after merge — name phải có ý nghĩa (2-100 ký tự, không rỗng).
            if (string.IsNullOrWhiteSpace(request.DisplayName)
                || request.DisplayName.Length < 2
                || request.DisplayName.Length > 100)
            {
                throw new BadRequestException(ApiErrorMessages.Pos.GuestSlotDisplayNameInvalid);
            }

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

        // Box history #1: truy vấn lịch sử kiểm kê MissingComponents của 1 hộp.
        public async Task<BoxComponentHistoryDto> GetBoxComponentHistoryAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid boxId,
            Guid? sessionId = null)
        {
            await EnsurePosAccessAsync(cafeId, userId, userRole);

            var box = await _posRepository.GetInventoryBoxByIdAsync(boxId);
            if (box == null)
            {
                throw new NotFoundException(ApiErrorMessages.Pos.BoxNotFoundById(boxId));
            }

            // Cross-cafe guard: box phải thuộc cafe của caller (qua CafeGameInventory.CafeId).
            var boxCafeId = box.CafeGameInventory?.CafeId ?? Guid.Empty;
            if (boxCafeId != cafeId)
            {
                throw new ConflictException(ApiErrorMessages.Pos.BoxCafeMismatch(boxId, cafeId));
            }

            // Optional sessionId: nếu truyền, validate session tồn tại và thuộc cùng cafe.
            // Guid.Empty cũng coi như null (defensive).
            Guid? effectiveSessionId = (sessionId.HasValue && sessionId.Value != Guid.Empty)
                ? sessionId.Value
                : null;

            if (effectiveSessionId.HasValue)
            {
                var sessionExists = await _posRepository.ActiveSessionExistsInCafeAsync(
                    effectiveSessionId.Value, cafeId);
                if (!sessionExists)
                {
                    throw new NotFoundException(
                        ApiErrorMessages.System.SessionNotInCafe(effectiveSessionId.Value, cafeId));
                }
            }

            var incidents = await _posRepository.GetMissingComponentIncidentsByBoxAsync(
                boxId, effectiveSessionId);

            var dto = new BoxComponentHistoryDto
            {
                BoxId = box.Id,
                BoxLabel = box.Barcode, // CafeInventoryBox không có Label; dùng Barcode làm label hiển thị.
                Barcode = box.Barcode,
                GameTemplateId = box.CafeGameInventory?.GameTemplateId ?? Guid.Empty,
                GameName = box.CafeGameInventory?.GameTemplate?.Name ?? string.Empty,
                TotalIncidents = incidents.Count,
                Incidents = []
            };

            foreach (var incident in incidents)
            {
                var memberLookup = incident.ActiveSession?.Members?
                    .ToDictionary(m => m.Id, m => m.IsGuestSlot
                        ? (incident.CheckedByStaff != null
                            ? $"Staff_{incident.CheckedByStaff.Id.ToString()[..8]}"
                            : "Khách vô danh")
                        : $"User_{m.UserId.ToString()[..8]}");

                var incidentDto = new BoxComponentIncidentDto
                {
                    SessionGameId = incident.Id,
                    SessionId = incident.ActiveSessionId,
                    CheckedAt = incident.CheckedAt ?? DateTime.MinValue,
                    StaffId = incident.CheckedByStaffId ?? Guid.Empty,
                    StaffName = incident.CheckedByStaff != null
                        ? $"Staff_{incident.CheckedByStaff.Id.ToString()[..8]}"
                        : null,
                    TotalPenaltyAmount = incident.TotalPenaltyAmount,
                    MissingComponents = incident.ComponentCheckResults
                        .Where(r => r.PenaltyFee > 0 || r.ActualQuantity < r.ExpectedQuantity)
                        .Select(r => new BoxMissingComponentDto
                        {
                            ComponentId = r.GameComponentTemplateId,
                            ComponentName = r.GameComponentTemplate?.ComponentName ?? string.Empty,
                            ComponentKind = r.GameComponentTemplate?.ComponentKind,
                            ExpectedQuantity = r.ExpectedQuantity,
                            ActualQuantity = r.ActualQuantity,
                            PenaltyFee = r.PenaltyFee,
                            ResponsibleMemberId = r.ResponsibleMemberId,
                            ResponsibleMemberName = r.ResponsibleMemberId.HasValue && memberLookup != null
                                ? memberLookup.GetValueOrDefault(r.ResponsibleMemberId.Value)
                                : null
                        })
                        .ToList()
                };
                dto.Incidents.Add(incidentDto);
            }

            return dto;
        }

        // ============ Phase 4 / EC-11: Player dispute played time ============

        /// <summary>
        /// Ghi audit log khi player khiếu nại về giờ chơi (BR §XX §POS evidence, §7.2 doc).
        /// POS logs (StartedAt scan QR timestamp + EndedAt POS button timestamp) là evidence
        /// definitive — endpoint này CHỈ audit, KHÔNG tự ý sửa hóa đơn.
        ///
        /// Manager review sau qua <c>POST /api/admin/sessions/{id}/played-time/override</c>
        /// (sẽ implement ở sprint sau) — ghi thêm <c>ActionType=PlayedTimeOverridden</c>.
        /// </summary>
        public async Task<DisputePlayedTimeResponseDto> DisputePlayedTimeAsync(
            Guid cafeId,
            Guid staffUserId,
            string staffRole,
            DisputePlayedTimeRequestDto request)
        {
            // EnsurePosAccessAsync throw nếu user không thuộc cafe này.
            await EnsurePosAccessAsync(cafeId, staffUserId, staffRole);

            var session = await _activeSessionRepository.GetByIdAsync(request.SessionId);
            if (session == null)
            {
                throw new NotFoundException(
                    ApiErrorMessages.System.SessionNotFoundInCafe(cafeId, request.SessionId));
            }

            if (session.CafeId != cafeId)
            {
                throw new NotFoundException(
                    ApiErrorMessages.System.SessionNotInCafe(request.SessionId, cafeId));
            }

            // Phase 4 / §7.2 evidence: trả về timestamps + total minutes để staff thấy rõ
            // POS logs đã ghi. Manager review dựa trên dữ liệu này.
            var totalMinutes = session.EndedAt.HasValue
                ? Math.Max(0, (int)Math.Floor((session.EndedAt.Value - session.StartedAt).TotalMinutes))
                : session.TotalMinutesPlayed;

            // Build audit metadata — append-only (BR-RISK-05 §17.6).
            var metadata = System.Text.Json.JsonSerializer.Serialize(new
            {
                cafeId,
                sessionId = session.Id,
                hostId = session.HostId,
                reservationId = session.Lobby?.Reservation?.Id,
                lobbyId = session.LobbyId,
                startedAt = session.StartedAt,
                endedAt = session.EndedAt,
                totalMinutesPlayed = totalMinutes,
                disputeType = request.DisputeType,
                playerClaim = request.PlayerClaim,
                proposedResolution = request.ProposedResolution,
                status = "Open"
            });

            var audit = new PlayerActionHistory
            {
                Id = Guid.NewGuid(),
                // BR-RISK-05 / EC-11: target user = Host (người chịu trách nhiệm billing).
                UserId = session.HostId,
                ActionType = AdminActionType.PlayedTimeDisputed,
                ActionBy = staffUserId,
                Reason = $"Player dispute played time: {request.DisputeType}",
                Metadata = metadata,
                CreatedAt = DateTime.UtcNow
            };

            _db.PlayerActionHistories.Add(audit);
            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "[EC-11] Dispute played time opened by staff {StaffId} for session {SessionId} at cafe {CafeId}",
                staffUserId, session.Id, cafeId);

            return new DisputePlayedTimeResponseDto
            {
                AuditId = audit.Id,
                SessionId = session.Id,
                SessionStartedAt = session.StartedAt,
                SessionEndedAt = session.EndedAt,
                SessionTotalMinutes = totalMinutes,
                DisputeType = request.DisputeType,
                Status = "Open",
                CreatedAt = audit.CreatedAt
            };
        }

        // ============================================================
        // Phase 5 / EC-11 — Manager override played time (BR-REFUND-07)
        // ============================================================

        public async Task<OverridePlayedTimeResponseDto> OverridePlayedTimeAsync(
            Guid cafeId,
            Guid managerUserId,
            string managerRole,
            OverridePlayedTimeRequestDto request)
        {
            // BR-RISK-07 / §XXI.7: Manager-only action.
            if (!string.Equals(managerRole, UserRole.Manager.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                throw new ForbiddenException(ApiErrorMessages.Pos.OnlyManagerCanOverride);
            }

            // Manager vẫn phải thuộc cafe (cùng gate EnsurePosAccessAsync).
            await EnsurePosAccessAsync(cafeId, managerUserId, managerRole);

            var session = await _activeSessionRepository.GetByIdAsync(request.SessionId);
            if (session == null)
            {
                throw new NotFoundException(
                    ApiErrorMessages.System.SessionNotFoundInCafe(cafeId, request.SessionId));
            }

            if (session.CafeId != cafeId)
            {
                throw new NotFoundException(
                    ApiErrorMessages.System.SessionNotInCafe(request.SessionId, cafeId));
            }

            // EC-11 §7.2: Manager chỉ được override khi session CHƯA thanh toán.
            if (session.Status == GroupSessionStatus.Paid
                || session.Status == GroupSessionStatus.Closed)
            {
                throw new ConflictException(ApiErrorMessages.Pos.CannotOverridePaidSession(session.Id));
            }

            // Điều kiện tiên quyết: phải có ít nhất 1 dispute audit trước đó.
            // Lookup trong PlayerActionHistories với ActionType=PlayedTimeDisputed, target = session.HostId,
            // metadata chứa sessionId = session.Id, status = "Open".
            // NOTE: Metadata là jsonb — không thể dùng `.Contains` (EF generate `jsonb ~~ unknown`).
            // Fetch candidate theo các column indexed trước, sau đó filter JSON ở client.
            var candidates = await _db.PlayerActionHistories
                .Where(p => p.ActionType == AdminActionType.PlayedTimeDisputed
                            && p.UserId == session.HostId
                            && p.Metadata != null)
                .OrderByDescending(p => p.CreatedAt)
                .Take(20)
                .ToListAsync();

            var sessionIdMarker = $"\"sessionId\":\"{session.Id}\"";
            var disputeAudit = candidates.FirstOrDefault(p =>
                p.Metadata != null && p.Metadata.Contains(sessionIdMarker, StringComparison.OrdinalIgnoreCase));

            if (disputeAudit == null)
            {
                throw new ConflictException(ApiErrorMessages.Pos.NoDisputeBeforeOverride(session.Id));
            }

            var now = DateTime.UtcNow;

            // ===== Recalculate Subtotal theo NewTotalMinutesPlayed =====
            // BR-15 + BR-16: Subtotal tính DUY NHẤT ở Checkout, nhưng Manager override cần
            // recalc tại thời điểm override. Quy tắc:
            // - Per-member Subtotal = realtime billing(memberMinutes) nếu TimeBased, hoặc BasePrice nếu Flat.
            // - Override set tất cả members dùng cùng NewTotalMinutesPlayed (đơn giản hóa).
            // - Nếu session đã có member-level TotalMinutesPlayed phân bổ → giữ member.TotalMinutesPlayed
            //   theo tỉ lệ cũ, scale bằng NewTotalMinutesPlayed / PreviousTotal.
            var previousSubtotal = session.Subtotal;
            var previousTotalMinutes = session.TotalMinutesPlayed;

            var cafe = await _cafeRepository.GetActiveByIdAsync(cafeId);
            if (cafe == null)
            {
                throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));
            }

            // Đếm members có TotalMinutesPlayed > 0 (đã chơi) để tính lại.
            // Members có TotalMinutesPlayed = 0 là walk-in không tính giờ.
            var payingMembers = session.Members?
                .Where(m => (m.JoinedAt != default && (m.LeftAt ?? now) > m.JoinedAt))
                .ToList() ?? new List<ActiveSessionMember>();

            // Override total minutes chia đều cho members theo tỉ lệ thời gian join.
            // Nếu không có member nào tham gia, fallback = NewTotalMinutes x BasePrice.
            if (payingMembers.Count == 0)
            {
                // Edge case: không có member nào trong session (chỉ có Host). Vẫn recalc.
                var fallbackSubtotal = cafe.BillingModel == CafePartnerBillingModel.TimeBased
                    ? ActiveSessionBillingCalculator.CalculateRealtimeBilling(cafe, request.NewTotalMinutesPlayed)
                    : cafe.BasePrice;
                session.Subtotal = Math.Max(0, fallbackSubtotal);
            }
            else
            {
                decimal newTotalSubtotal = 0;
                if (previousTotalMinutes > 0)
                {
                    // Scale tỉ lệ: member.NewMinutes = member.PreviousMinutes * (NewTotal / PreviousTotal)
                    foreach (var member in payingMembers)
                    {
                        var memberPreviousMinutes = Math.Max(0, member.TotalMinutesPlayed);
                        var scaledMinutes = (int)Math.Floor(
                            memberPreviousMinutes * (decimal)request.NewTotalMinutesPlayed / previousTotalMinutes);

                        member.TotalMinutesPlayed = scaledMinutes;

                        decimal memberSubtotal = scaledMinutes > 0
                            ? (cafe.BillingModel == CafePartnerBillingModel.TimeBased
                                ? ActiveSessionBillingCalculator.CalculateRealtimeBilling(cafe, scaledMinutes)
                                : cafe.BasePrice)
                            : 0m;
                        newTotalSubtotal += Math.Max(0, memberSubtotal);
                    }
                }
                else
                {
                    // PreviousTotal = 0 (edge case). Chia đều NewTotalMinutes cho tất cả members.
                    var perMemberMinutes = (int)Math.Floor((decimal)request.NewTotalMinutesPlayed / payingMembers.Count);
                    foreach (var member in payingMembers)
                    {
                        member.TotalMinutesPlayed = perMemberMinutes;
                        decimal memberSubtotal = perMemberMinutes > 0
                            ? (cafe.BillingModel == CafePartnerBillingModel.TimeBased
                                ? ActiveSessionBillingCalculator.CalculateRealtimeBilling(cafe, perMemberMinutes)
                                : cafe.BasePrice)
                            : 0m;
                        newTotalSubtotal += Math.Max(0, memberSubtotal);
                    }
                }

                session.Subtotal = Math.Max(0, newTotalSubtotal);
            }

            // TotalAmount = Subtotal + Penalty. Penalty giữ nguyên (Manager override chỉ sửa minutes).
            session.TotalAmount = session.Subtotal + session.PenaltyAmount;
            session.TotalMinutesPlayed = request.NewTotalMinutesPlayed;

            // Ghi audit override — append-only (BR-RISK-05 §17.6).
            var metadata = System.Text.Json.JsonSerializer.Serialize(new
            {
                cafeId,
                sessionId = session.Id,
                hostId = session.HostId,
                reservationId = session.Lobby?.Reservation?.Id,
                lobbyId = session.LobbyId,
                startedAt = session.StartedAt,
                endedAt = session.EndedAt,
                previousTotalMinutes,
                newTotalMinutes = request.NewTotalMinutesPlayed,
                previousSubtotal,
                newSubtotal = session.Subtotal,
                subtotalDelta = session.Subtotal - previousSubtotal,
                newTotalAmount = session.TotalAmount,
                overrideReason = request.OverrideReason,
                linkedDisputeAuditId = disputeAudit.Id,
                status = "Overridden"
            });

            var overrideAudit = new PlayerActionHistory
            {
                Id = Guid.NewGuid(),
                UserId = session.HostId,
                ActionType = AdminActionType.PlayedTimeOverridden,
                ActionBy = managerUserId,
                Reason = $"Manager override played time: {request.OverrideReason}",
                Metadata = metadata,
                CreatedAt = now
            };

            _db.PlayerActionHistories.Add(overrideAudit);
            await _activeSessionRepository.UpdateAsync(session);
            await _activeSessionRepository.SaveChangesAsync();

            _logger.LogInformation(
                "[EC-11] Manager {ManagerId} overrode session {SessionId} at cafe {CafeId}: {PreviousMinutes}min → {NewMinutes}min (subtotal {PreviousSubtotal} → {NewSubtotal})",
                managerUserId, session.Id, cafeId, previousTotalMinutes, request.NewTotalMinutesPlayed, previousSubtotal, session.Subtotal);

            return new OverridePlayedTimeResponseDto
            {
                OverrideAuditId = overrideAudit.Id,
                SessionId = session.Id,
                DisputeAuditId = disputeAudit.Id,
                PreviousTotalMinutes = previousTotalMinutes,
                NewTotalMinutes = request.NewTotalMinutesPlayed,
                PreviousSubtotal = previousSubtotal,
                NewSubtotal = session.Subtotal,
                SubtotalDelta = session.Subtotal - previousSubtotal,
                NewTotalAmount = session.TotalAmount,
                PolicyApplied = "BR-REFUND-07 ManagerOverride",
                Status = "Overridden",
                OverriddenAt = overrideAudit.CreatedAt
            };
        }

        /// <summary>
        /// GAP-A: Kiểm tra xem <paramref name="ex"/> có phải unique constraint violation
        /// của Postgres không (SQLSTATE 23505). Khi 2 staff cùng submit component-check
        /// trên cùng session game, insert thứ 2 vi phạm unique index
        /// <c>IX_ComponentCheckResults_ActiveSessionGameId_GameComponentTemplateId</c>.
        /// </summary>
        private static bool IsUniqueViolation(DbUpdateException ex)
        {
            // Npgsql bọc PostgresException trong DbUpdateException; chuỗi
            // "duplicate key value violates unique constraint" hoặc SqlState 23505.
            // Check cả 2 cách để chắc chắn với cả InMemory (test) lẫn Postgres (prod).
            var inner = ex.InnerException;
            if (inner == null) return false;

            var message = inner.Message ?? string.Empty;
            if (message.Contains("23505", StringComparison.Ordinal)
                || message.Contains("unique constraint", StringComparison.OrdinalIgnoreCase)
                || message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Fallback: nếu provider nội bộ expose SqlState (Npgsql.PostgresException có property này).
            var sqlStateProp = inner.GetType().GetProperty("SqlState");
            if (sqlStateProp?.GetValue(inner) is string state && state == "23505")
            {
                return true;
            }

            return false;
        }
    }
}
