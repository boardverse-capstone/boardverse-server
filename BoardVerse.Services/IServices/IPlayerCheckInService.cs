using BoardVerse.Core.DTOs.Reservation;

namespace BoardVerse.Services.IServices;

public interface IPlayerCheckInService
{
    /// <summary>
    /// Player scan QR POS (token 16-char) để check-in vào reservation (BR §21A.7).
    /// Tái sử dụng ICafePosService.CheckInByCodeAsync — token chỉ là "vỏ bọc" route.
    /// </summary>
    Task<PlayerScanTokenResponseDto> CheckInByTokenAsync(
        Guid playerUserId,
        PlayerScanTokenRequestDto request);
}