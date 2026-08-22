using BoardVerse.Core.DTOs.Payment;
using System.Threading;
using BoardVerse.Core.Entities;

namespace BoardVerse.Services.IServices
{
    public interface ISePayAccountService
    {
        Task<SePayAccountDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<SePayAccountDto?> GetByCafeIdAsync(Guid cafeId, CancellationToken cancellationToken = default);
        Task<SePayAccountDto?> GetMasterAccountAsync(CancellationToken cancellationToken = default);

        // Internal: trả raw SePayAccount entity (bao gồm ApiKey/SecretKey/WebhookToken/AccountNumber
        // chưa mask) — CHỈ dùng cho payment flow nội bộ (PaymentService, SePayClient).
        // Không expose qua controller/DTO response.
        Task<SePayAccount?> GetRawMasterAccountAsync(CancellationToken cancellationToken = default);
        Task<SePayAccount?> GetRawByCafeIdAsync(Guid cafeId, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<SePayAccountDto>> GetAllAsync(SePayAccountQuery? query = null, CancellationToken cancellationToken = default);
        Task<SePayAccountDto> CreateAsync(CreateSePayAccountRequestDto request, CancellationToken cancellationToken = default);
        Task<SePayAccountDto> UpdateAsync(Guid id, UpdateSePayAccountRequestDto request, CancellationToken cancellationToken = default);
        Task<SePayAccountDto> SetEnvironmentAsync(Guid id, string environment, CancellationToken cancellationToken = default);
        Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

        // Manager endpoints - operates on cafe that manager owns
        Task<SePayAccountDto?> GetByManagerCafeAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Manager tạo payment account cho cafe của mình (4 field: bankCode, accountNumber, accountHolder, environment).
        /// KHÔNG yêu cầu Manager đăng ký SePay — chỉ cần khai TK ngân hàng thật của cafe.
        /// </summary>
        Task<SePayAccountDto> CreateByManagerCafeAsync(CreateCafePaymentAccountRequestDto request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Generate QR preview 10k cho Manager test payment account. KHÔNG tạo booking/session.
        /// Manager scan QR → CK thử → verify SePay detect giao dịch qua webhook.
        /// </summary>
        Task<CafePaymentQrPreviewDto> GenerateTestQrByManagerCafeAsync(CancellationToken cancellationToken = default);

        Task<SePayAccountDto> UpdateByManagerCafeAsync(UpdateSePayAccountRequestDto request, CancellationToken cancellationToken = default);
        Task<SePayAccountDto> SetEnvironmentByManagerCafeAsync(string environment, CancellationToken cancellationToken = default);

        /// <summary>
        /// Admin: Tra cứu BookingDeposit theo SePayTransactionId (mã giao dịch ngân hàng).
        /// Trả về thông tin deposit + booking + cafe liên kết. Dùng khi support khách hàng
        /// hoặc debug webhook mismatch. Trả null nếu không tìm thấy.
        /// </summary>
        Task<SePayTransactionLookupDto?> LookupBySePayTransactionIdAsync(string sePayTransactionId, CancellationToken cancellationToken = default);
    }
}
