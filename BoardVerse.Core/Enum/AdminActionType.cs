namespace BoardVerse.Core.Enum;

/// <summary>
/// Loại admin action ghi vào PlayerActionHistory — theo BR-RISK-05.
/// </summary>
public enum AdminActionType
{
    // BVC wallet adjustments
    AdminCredit = 0,
    AdminDebit = 1,

    // Account status changes
    AccountStatusChange = 10,
    Warning = 11,
    Suspend = 12,
    Ban = 13,

    // Risk management
    RiskScoreReset = 20,
    VerifyRequired = 21,

    // Multi-account
    MultiAccountConfirmed = 30,
    MultiAccountDismissed = 31,

    // Phase 4 / EC-11 — Played time dispute audit (BR §XX §POS evidence).
    // Khi player cho rằng POS ghi nhầm giờ chơi (StartedAt/EndedAt).
    // Staff mở dispute → lưu evidence. Manager review/override → lưu lại.
    PlayedTimeDisputed = 40,    // Staff mở ticket: player yêu cầu xem lại giờ chơi.
    PlayedTimeOverridden = 41,  // Manager adjust Subtotal/TotalMinutes dựa trên evidence.
}
