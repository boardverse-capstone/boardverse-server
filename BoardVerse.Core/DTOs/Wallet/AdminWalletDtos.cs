using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Wallet;

/// <summary>
/// Một wallet trong danh sách admin (phân trang).
/// </summary>
public class AdminWalletSummaryDto
{
    public Guid UserId { get; set; }
    public string? UserEmail { get; set; }
    public long AvailableBalance { get; set; }
    public long HeldBalance { get; set; }
    public long TotalActiveDeposit { get; set; }
    public decimal RiskMultiplier { get; set; }
    public RiskLevel RiskLevel { get; set; }
    public bool IsCoolingOff { get; set; }
    public AccountStatus AccountStatus { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Danh sách tất cả wallets (phân trang) cho admin.
/// </summary>
public class AdminWalletPageDto
{
    public IReadOnlyList<AdminWalletSummaryDto> Items { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalItems / PageSize);
}

/// <summary>
/// Chi tiết wallet của một user (bao gồm thông tin user + ledger).
/// </summary>
public class AdminWalletDetailDto
{
    public Guid UserId { get; set; }
    public string? UserEmail { get; set; }
    public string? UserPhoneNumber { get; set; }
    public long AvailableBalance { get; set; }
    public long HeldBalance { get; set; }
    public long TotalActiveDeposit { get; set; }
    public decimal RiskMultiplier { get; set; }
    public int RiskScore { get; set; }
    public RiskLevel RiskLevel { get; set; }
    public bool IsCoolingOff { get; set; }
    public DateTime? CoolingOffExpiresAt { get; set; }
    public AccountStatus AccountStatus { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// W-05: Kết quả reconcile số dư ví.
/// Logic: SUM(TopUp + AdminCredit) - SUM(DepositHold + AdminDebit + DepositCapture + DepositForfeit) = availableBalance.
/// </summary>
public class WalletReconcileResultDto
{
    public Guid UserId { get; set; }
    public long WalletAvailableBalance { get; set; }
    public long LedgerCredits { get; set; }
    public long LedgerDebits { get; set; }
    public long ComputedAvailableBalance { get; set; }
    public bool IsBalanced { get; set; }
    public long Discrepancy => ComputedAvailableBalance - WalletAvailableBalance;
    public DateTime ReconciledAt { get; set; } = DateTime.UtcNow;
}
