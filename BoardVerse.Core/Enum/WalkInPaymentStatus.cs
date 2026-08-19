namespace BoardVerse.Core.Enum;

/// <summary>
/// Payment status for WalkInBooking — only UNPAID / PAID in MVP (no refund).
/// </summary>
public enum WalkInPaymentStatus
{
    Unpaid = 0,
    Paid = 1
}
