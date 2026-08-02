using BoardVerse.Core.DTOs.Pos;

namespace BoardVerse.Services.IServices
{
    public interface ICafePosService
    {
        Task<IReadOnlyList<CafeTableStatusDto>> GetTablesAsync(Guid cafeId, Guid userId, string userRole);

        /// <summary>
        /// Legacy overload — đồng bộ chỉ tên bàn (giữ nguyên SeatCount cũ, default 4 cho bàn mới).
        /// </summary>
        Task SyncTablesAsync(Guid cafeId, Guid managerId, IReadOnlyList<string> tableNames);

        /// <summary>
        /// Overload mới — đồng bộ cả Name + SeatCount + SortOrder trong một lần PUT.
        /// PUT /api/cafes/{cafeId}/pos/tables shape mới.
        /// </summary>
        Task SyncTablesAsync(Guid cafeId, Guid managerId, IReadOnlyList<CafeTableSyncItem> tables);

        Task<CafeTableStatusDto> UpdateCafeTableAsync(
            Guid cafeId,
            Guid managerId,
            Guid tableId,
            UpdateCafeTableRequestDto request);
        Task<IReadOnlyList<CafeInventoryBoxDto>> GetBoxesAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid? gameTemplateId);
        Task<CafeInventoryBoxDto> GetBoxByBarcodeAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            string barcode);
        Task<IReadOnlyList<ActiveSessionDto>> GetActiveSessionsAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid? gameTemplateId);
        Task<ActiveSessionDto> StartGameSessionAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            StartGameSessionRequestDto request);

        /// <summary>
        /// POS check-in: Staff quét QR (ReservationCode hoặc BookingCode legacy) để kích hoạt phiên chơi.
        /// BR §21A.7 — Host-led check-in.
        /// </summary>
        Task<ActiveSessionDto> CheckInByCodeAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            CheckInRequestDto request);

        /// <summary>
        /// Preview booking info trước khi check-in.
        /// AC 1.1: Hiển thị danh sách thành viên + game info TRƯỚC khi check-in.
        /// </summary>
        Task<BookingPreviewDto> GetBookingPreviewAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            string bookingCode);

        Task<ActiveSessionDto> EndGameSessionAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid sessionId);

        // BR-12: Component Checklist
        Task<ComponentChecklistDto> GetComponentChecklistAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid sessionGameId);
        Task<ComponentChecklistDto> SubmitComponentCheckAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            SubmitComponentCheckRequestDto request);

        // Return Game: tính surcharge_fine
        Task<ReturnGameResponseDto> ReturnGameAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid sessionId,
            ReturnGameRequestDto request);
    }
}
