using BoardVerse.Core.DTOs.Pos;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;

namespace BoardVerse.Services.Services
{
    public class CafePosService : ICafePosService
    {
        private readonly ICafePosRepository _posRepository;
        private readonly ICafeRepository _cafeRepository;
        private readonly IBookingDepositRepository _depositRepository;

        public CafePosService(
            ICafePosRepository posRepository,
            ICafeRepository cafeRepository,
            IBookingDepositRepository depositRepository)
        {
            _posRepository = posRepository;
            _cafeRepository = cafeRepository;
            _depositRepository = depositRepository;
        }

        public async Task<IReadOnlyList<CafeTableStatusDto>> GetTablesAsync(
            Guid cafeId,
            Guid userId,
            string userRole)
        {
            await EnsurePosAccessAsync(cafeId, userId, userRole);

            var tables = await _posRepository.GetActiveTablesAsync(cafeId);
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
            session.Host = null!;
            session.Members = [hostMember];

            return MapSession(session, now);
        }

        /// <summary>
        /// Host-led check-in: Quét một lần mã đặt chỗ (BookingCode) để kích hoạt phiên chơi cho cả nhóm.
        /// MDC Happy Path Step 9: "Quét một lần mã định danh đặt chỗ trên ứng dụng của người chơi khởi tạo để thực hiện thủ tục vào quán cho cả nhóm"
        /// BR-05: Booking CONFIRMED mới được check-in
        /// BR-06: Quá 30 phút không check-in → Booking EXPIRED
        /// BR-09: Deposit được bảo lưu, cấn trừ vào hóa đơn tổng khi kết thúc phiên
        /// </summary>
        public async Task<ActiveSessionDto> StartSessionFromBookingAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            StartSessionFromBookingRequestDto request)
        {
            await EnsurePosAccessAsync(cafeId, userId, userRole);

            // BR-05: Tìm booking deposit bằng mã đặt chỗ (BookingCode = OrderId)
            var deposit = await _depositRepository.GetByBookingCodeAsync(request.BookingCode.Trim());
            if (deposit == null)
            {
                throw new NotFoundException($"Không tìm thấy đơn đặt chỗ với mã '{request.BookingCode}'.");
            }

            // BR-05: Chỉ cho phép check-in khi booking đã CONFIRMED
            if (deposit.Status != BookingDepositStatus.Paid)
            {
                throw new ConflictException($"Đơn đặt chỗ không ở trạng thái đã thanh toán (trạng thái hiện tại: {deposit.Status}). Vui lòng kiểm tra lại mã đặt chỗ hoặc liên hệ khách hàng.");
            }

            // BR-05: Kiểm tra ghế khả dụng
            if (deposit.CafeId != cafeId)
            {
                throw new ConflictException($"Đơn đặt chỗ này không thuộc quán này.");
            }

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

            // Tạo session - BookingDeposit link sẽ set sau khi session được tạo
            var session = new ActiveSession
            {
                Id = Guid.NewGuid(),
                CafeId = cafeId,
                CafeTableId = table.Id,
                CafeInventoryBoxId = box.Id,
                GameTemplateId = gameTemplateId,
                HostId = deposit.CafeManagerId, // Host = người đã đặt chỗ (đã cọc)
                LobbyId = null, // Có thể liên kết Lobby nếu có
                Status = GroupSessionStatus.Active,
                StartedAt = now,
                CreatedAt = now
            };

            // Tạo ActiveSessionMember cho Host
            var hostMember = new ActiveSessionMember
            {
                Id = Guid.NewGuid(),
                ActiveSessionId = session.Id,
                UserId = deposit.CafeManagerId,
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

            await _posRepository.AddSessionAsync(session);
            await _posRepository.AddSessionMemberAsync(hostMember);
            await _posRepository.AddSessionGameAsync(sessionGame);
            await _posRepository.SaveChangesAsync();

            // Link BookingDeposit với ActiveSession sau khi session được tạo
            deposit.ActiveSessionId = session.Id;
            deposit.UpdatedAt = now;
            await _posRepository.UpdateDepositAsync(deposit);
            await _posRepository.SaveChangesAsync();

            session.CafeTable = table;
            session.CafeInventoryBox = box;
            session.GameTemplate = box.CafeGameInventory.GameTemplate;
            session.Host = null!;
            session.Members = [hostMember];

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

            session.CafeInventoryBox.Status = CafeGameInventoryStatus.Available;
            session.CafeInventoryBox.UpdatedAt = now;

            var otherSessionsOnTable = await _posRepository.GetActiveSessionsAsync(cafeId, null);
            var tableStillBusy = otherSessionsOnTable.Any(s =>
                s.Id != session.Id && s.CafeTableId == session.CafeTableId);

            if (!tableStillBusy && session.CafeTable.Status == CafeTableStatus.InUse)
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

            var components = sessionGame.GameTemplate.Components.ToList();

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
    }
}
