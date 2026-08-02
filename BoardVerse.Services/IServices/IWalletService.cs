using BoardVerse.Core.DTOs.Wallet;

namespace BoardVerse.Services.IServices;

public interface IWalletService
{
    /// <summary>
    /// Auto-create wallet cho user nếu chưa có, return wallet hiện tại.
    /// Idempotent — nhiều lần gọi trả về cùng instance.
    /// </summary>
    Task<WalletDto> GetOrCreateWalletAsync(Guid userId, bool includeHeld);

    /// <summary>
    /// Lấy số dư ví (không auto-create). Trả NotFound nếu user chưa có ví.
    /// </summary>
    Task<WalletDto> GetWalletAsync(Guid userId, bool includeHeld);

    /// <summary>
    /// Tạo đơn top-up BVC. Phase 1: trả quote (amount BVC = amountVnd/1000)
    /// và PaymentUrl từ SePay master. Webhook xử lý ở phase sau.
    /// Idempotent theo IdempotencyKey (BR § XVII.1).
    /// </summary>
    Task<TopUpResponseDto> CreateTopUpAsync(Guid userId, TopUpRequestDto request);

    /// <summary>
    /// Lịch sử ledger entry (read-only) theo user, phân trang.
    /// </summary>
    Task<BvcTransactionPageDto> GetTransactionsAsync(Guid userId, int page, int pageSize);

    /// <summary>
    /// Hold BVC cho reservation (BR § III.2 / IV).
    /// Trừ availableBalance, cộng heldBalance, ghi ledger DEPOSIT_HOLD.
    /// Idempotent theo <paramref name="idempotencyKey"/>.
    /// Throw InsufficientBalanceException nếu thiếu.
    /// </summary>
    Task<BvcHoldResult> HoldDepositAsync(
        Guid userId,
        long amountBvc,
        Guid? relatedLobbyId,
        Guid? relatedReservationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Release BVC từ held về available (BR § III.2 / X).
    /// Trừ heldBalance, cộng availableBalance, ghi ledger DEPOSIT_RELEASE.
    /// Idempotent theo <paramref name="idempotencyKey"/>.
    /// </summary>
    Task<BvcHoldResult> ReleaseDepositAsync(
        Guid userId,
        long amountBvc,
        Guid? relatedLobbyId,
        Guid? relatedReservationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Capture BVC đã giữ về doanh thu quán (BR § III.2 / XV).
    /// Trừ heldBalance, không cộng lại available, ghi ledger DEPOSIT_CAPTURE.
    /// Idempotent theo <paramref name="idempotencyKey"/>.
    /// </summary>
    Task<BvcHoldResult> CaptureDepositAsync(
        Guid userId,
        long amountBvc,
        Guid? relatedLobbyId,
        Guid? relatedReservationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Forfeit BVC (no-show / hủy sát giờ) — phase 5.
    /// Trừ heldBalance, ghi ledger DEPOSIT_FORFEIT.
    /// Idempotent theo <paramref name="idempotencyKey"/>.
    /// </summary>
    Task<BvcHoldResult> ForfeitDepositAsync(
        Guid userId,
        long amountBvc,
        Guid? relatedLobbyId,
        Guid? relatedReservationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Phase 2: Xử lý SePay webhook cho BVC top-up (OrderId prefix BVC-XXX).
    /// Idempotent theo OrderId. Cùng OrderId + success → chỉ cộng ví 1 lần.
    /// </summary>
    Task HandleTopUpWebhookAsync(
        string orderId,
        string gatewayTransactionId,
        long amountBvc,
        string status,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Admin/support tặng/trừ BVC thủ công (compensation, penalty, manual refund).
    /// Ghi ledger AdminCredit (+) / AdminDebit (-). KHÔNG qua SePay.
    /// Idempotent theo <paramref name="idempotencyKey"/>.
    /// BR-RISK-05: admin action ghi audit log + PlayerActionHistory.
    /// </summary>
    Task<BvcHoldResult> AdminAdjustBalanceAsync(
        Guid targetUserId,
        long amountBvc,
        bool isCredit,
        Guid adminUserId,
        string reason,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Background job: expire các top-up request status=Pending quá ExpiresAt.
    /// Idempotent: chỉ chuyển status Pending → Expired, không cộng/trừ ví.
    /// Cluster-safe: dùng batch transaction + FOR UPDATE SKIP LOCKED bên trong.
    /// </summary>
    Task<int> ExpirePendingTopUpsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Kết quả của Hold/Release/Capture/Forfeit. Trả snapshot để client confirm ledger entry đã ghi.
/// </summary>
public class BvcHoldResult
{
    public Guid LedgerEntryId { get; set; }
    public long NewAvailableBalance { get; set; }
    public long NewHeldBalance { get; set; }
    public long BalanceSnapshot { get; set; }
    public bool WasIdempotentReplay { get; set; }
}

