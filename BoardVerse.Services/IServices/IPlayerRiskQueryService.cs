using BoardVerse.Core.DTOs.Admin;

namespace BoardVerse.Services.IServices;

/// <summary>
/// BR-RISK-09 — Query service cho admin dashboard xem risk details của player.
///
/// User-facing API: chỉ trả <c>RiskLevel</c> (low/medium/high/critical).
/// Admin-only API: trả full <see cref="PlayerRiskDetailDto"/> bao gồm RiskScore + Signals.
/// </summary>
public interface IPlayerRiskQueryService
{
    /// <summary>
    /// Lấy risk detail cho 1 user (admin only).
    /// </summary>
    /// <param name="userId">UserId cần xem.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="NotFoundException">User không tồn tại.</exception>
    Task<PlayerRiskDetailDto> GetPlayerRiskDetailAsync(Guid userId, CancellationToken ct = default);
}
