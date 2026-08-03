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
    MultiAccountDismissed = 31
}
