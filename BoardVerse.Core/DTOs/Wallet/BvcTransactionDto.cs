using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Wallet;

/// <summary>
/// Một entry trong sổ cái BVC — read-only view cho mobile (BR § III.2).
/// Append-only, không bao giờ expose đường sửa.
/// </summary>
public class BvcTransactionDto
{
    public Guid Id { get; set; }

    public LedgerEntryType Type { get; set; }

    /// <summary>Số BVC (luôn dương). Dấu được quyết định bởi <see cref="Type"/>.</summary>
    public long Amount { get; set; }

    public Guid? RelatedLobbyId { get; set; }
    public Guid? RelatedBookingId { get; set; }
    public string? RelatedPaymentRef { get; set; }

    /// <summary><see cref="Wallet.AvailableBalance"/> sau giao dịch.</summary>
    public long BalanceSnapshot { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }
}
