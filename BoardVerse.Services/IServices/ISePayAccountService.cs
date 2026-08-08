using BoardVerse.Core.DTOs.Payment;
using BoardVerse.Core.Entities;

namespace BoardVerse.Services.IServices
{
    public interface ISePayAccountService
    {
        Task<SePayAccountDto?> GetByIdAsync(Guid id);
        Task<SePayAccountDto?> GetByCafeIdAsync(Guid cafeId);
        Task<SePayAccountDto?> GetMasterAccountAsync();

        // Internal: trả raw SePayAccount entity (bao gồm ApiKey/SecretKey/WebhookToken/AccountNumber
        // chưa mask) — CHỈ dùng cho payment flow nội bộ (PaymentService, SePayClient).
        // Không expose qua controller/DTO response.
        Task<SePayAccount?> GetRawMasterAccountAsync();
        Task<SePayAccount?> GetRawByCafeIdAsync(Guid cafeId);

        Task<IReadOnlyList<SePayAccountDto>> GetAllAsync(SePayAccountQuery? query = null);
        Task<SePayAccountDto> CreateAsync(CreateSePayAccountRequestDto request);
        Task<SePayAccountDto> UpdateAsync(Guid id, UpdateSePayAccountRequestDto request);
        Task<SePayAccountDto> SetEnvironmentAsync(Guid id, string environment);
        Task DeleteAsync(Guid id);

        // Manager endpoints - operates on cafe that manager owns
        Task<SePayAccountDto?> GetByManagerCafeAsync();

        /// <summary>
        /// Manager tạo payment account cho cafe của mình (4 field: bankCode, accountNumber, accountHolder, environment).
        /// KHÔNG yêu cầu Manager đăng ký SePay — chỉ cần khai TK ngân hàng thật của cafe.
        /// </summary>
        Task<SePayAccountDto> CreateByManagerCafeAsync(CreateCafePaymentAccountRequestDto request);

        /// <summary>
        /// Generate QR preview 10k cho Manager test payment account. KHÔNG tạo booking/session.
        /// Manager scan QR → CK thử → verify SePay detect giao dịch qua webhook.
        /// </summary>
        Task<CafePaymentQrPreviewDto> GenerateTestQrByManagerCafeAsync();

        Task<SePayAccountDto> UpdateByManagerCafeAsync(UpdateSePayAccountRequestDto request);
        Task<SePayAccountDto> SetEnvironmentByManagerCafeAsync(string environment);
    }
}
