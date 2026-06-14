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

        public CafePosService(ICafePosRepository posRepository, ICafeRepository cafeRepository)
        {
            _posRepository = posRepository;
            _cafeRepository = cafeRepository;
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
                StartedAt = now,
                IsActive = true,
                CreatedAt = now
            };

            box.Status = CafeGameInventoryStatus.InUse;
            box.UpdatedAt = now;

            if (table.Status == CafeTableStatus.Available)
            {
                table.Status = CafeTableStatus.InUse;
                table.UpdatedAt = now;
            }

            await _posRepository.AddSessionAsync(session);
            await _posRepository.SaveChangesAsync();

            session.CafeTable = table;
            session.CafeInventoryBox = box;
            session.GameTemplate = box.CafeGameInventory.GameTemplate;

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
            session.IsActive = false;
            session.EndedAt = now;

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
            var playTime = session.GameTemplate?.PlayTime ?? 0;
            var elapsedMinutes = (int)Math.Floor((utcNow - session.StartedAt).TotalMinutes);
            var remaining = playTime > 0
                ? (int)Math.Max(0, Math.Ceiling((double)playTime - elapsedMinutes))
                : 0;

            return new ActiveSessionDto
            {
                Id = session.Id,
                CafeTableId = session.CafeTableId,
                TableName = session.CafeTable?.Name ?? string.Empty,
                CafeInventoryBoxId = session.CafeInventoryBoxId,
                BoxBarcode = session.CafeInventoryBox?.Barcode ?? string.Empty,
                GameTemplateId = session.GameTemplateId,
                GameName = session.GameTemplate?.Name ?? string.Empty,
                DefaultPlayTimeMinutes = playTime,
                StartedAt = session.StartedAt,
                ElapsedMinutes = Math.Max(0, elapsedMinutes),
                EstimatedRemainingMinutes = remaining
            };
        }
    }
}
