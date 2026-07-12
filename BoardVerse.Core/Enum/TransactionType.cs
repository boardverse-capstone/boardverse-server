namespace BoardVerse.Core.Enum;

/// <summary>
/// Loại transaction.
/// </summary>
public enum TransactionType
{
    BookingDeposit = 0,
    DepositOut = 1,
    GameRental = 2,
    Penalty = 3,
    Refund = 4,
    Other = 99
}
