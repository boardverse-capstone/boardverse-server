using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services.Payments;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services
{
    /// <summary>
    /// Settlement = giải ngân tiền cọc từ BoardVerse master về tài khoản cafe manager.
    /// BR-09 + BR-18: Tiền cọc được cấn trừ 1 lần khi phiên PAID → release to cafe manager.
    /// Retry: khi SePay fail → set CafeSettlement.Status = Failed, giữ BookingDeposit.Status = Paid
    /// để <see cref="BoardVerse.API.BackgroundServices.SettlementRetryJob"/> có thể retry.
    /// </summary>
    public class SettlementService : ISettlementService
    {
        private readonly IBookingDepositRepository _depositRepository;
        private readonly ICafeSettlementRepository _settlementRepository;
        private readonly ICafeRepository _cafeRepository;
        private readonly IPaymentMasterAccountRepository _masterAccountRepository;
        private readonly IActiveSessionRepository _activeSessionRepository;
        private readonly ISePayClient _sePayClient;
        private readonly ILogger<SettlementService> _logger;

        public SettlementService(
            IBookingDepositRepository depositRepository,
            ICafeSettlementRepository settlementRepository,
            ICafeRepository cafeRepository,
            IPaymentMasterAccountRepository masterAccountRepository,
            IActiveSessionRepository activeSessionRepository,
            ISePayClient sePayClient,
            ILogger<SettlementService> logger)
        {
            _depositRepository = depositRepository;
            _settlementRepository = settlementRepository;
            _cafeRepository = cafeRepository;
            _masterAccountRepository = masterAccountRepository;
            _activeSessionRepository = activeSessionRepository;
            _sePayClient = sePayClient;
            _logger = logger;
        }

        public async Task<CafeSettlement> ReleaseSessionDepositAsync(Guid cafeId, Guid sessionId, Guid activeSessionId)
        {
            var cafe = await _cafeRepository.GetActiveByIdAsync(cafeId)
                ?? throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));

            var session = await _activeSessionRepository.GetByIdAsync(activeSessionId)
                ?? throw new NotFoundException(ApiErrorMessages.Pos.SessionNotFound(cafeId, activeSessionId));

            if (session.Status != GroupSessionStatus.Paid)
            {
                throw new ConflictException("Phiên đã thanh toán mới được giải ngân deposit.");
            }

            if (session.DepositAppliedAmount <= 0)
            {
                throw new ConflictException(ApiErrorMessages.Pos.DepositMissingForSettlement);
            }

            var masterAccount = await _masterAccountRepository.GetActiveAsync()
                ?? throw new ConflictException(ApiErrorMessages.Pos.MasterAccountNotConfigured);

            // Gap 4 (fix): Destination = cafe manager's SePay bank account, KHÔNG phải master account.
            // Cafe đã có SePayAccountNumber + SePayBankCode cho VietQR fallback — dùng luôn cho transfer.
            if (string.IsNullOrWhiteSpace(cafe.SePayAccountNumber) || string.IsNullOrWhiteSpace(cafe.SePayBankCode))
            {
                throw new ConflictException(
                    $"Cafe '{cafe.Name}' chưa được cấu hình SePay bank (SePayAccountNumber/SePayBankCode). Vui lòng cấu hình trong POS trước khi giải ngân.");
            }

            var deposit = await _depositRepository.GetByActiveSessionIdAsync(activeSessionId)
                ?? throw new NotFoundException(ApiErrorMessages.Pos.DepositMissingForSettlement);

            if (deposit.Status != BookingDepositStatus.Paid)
            {
                throw new ConflictException(ApiErrorMessages.Pos.DepositNotPaid);
            }

            var netTransfer = deposit.Amount;
            var settlement = new CafeSettlement
            {
                CafeId = cafeId,
                CafeManagerId = cafe.ManagerId,
                ActiveSessionId = activeSessionId,
                BookingDepositId = deposit.Id,
                DepositAmount = deposit.Amount,
                FeeAmount = 0,
                NetTransferAmount = netTransfer,
                Status = CafeSettlementStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            await _settlementRepository.AddAsync(settlement);
            await _settlementRepository.SaveChangesAsync();

            // Gap 3 (fix): KHÔNG set Released ở đây. Đợi SePay success rồi mới released.
            // Khi fail → giữ Status=Paid để SettlementRetryJob retry.
            try
            {
                var transferRequest = new CreateTransferRequest(
                    ToBankAccount: cafe.SePayBankCode,
                    ToAccountNumber: cafe.SePayAccountNumber,
                    Amount: netTransfer,
                    Description: $"BoardVerse settlement - session {activeSessionId}",
                    ReferenceId: $"settlement_{settlement.Id:N}");

                var transferResponse = await _sePayClient.CreateTransferAsync(transferRequest);

                settlement.Status = CafeSettlementStatus.Succeeded;
                settlement.SePayTransferId = transferResponse.TransferId ?? settlement.SePayTransferId;
                settlement.TransferredAt = DateTime.UtcNow;

                // Chỉ set deposit = Released khi transfer succeed.
                deposit.Status = BookingDepositStatus.Released;
                deposit.ReleasedAt = DateTime.UtcNow;
                deposit.SePayTransferId = transferResponse.TransferId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SePay transfer failed for settlement {SettlementId}. Will retry later.", settlement.Id);
                settlement.Status = CafeSettlementStatus.Failed;
                settlement.FailureReason = ex.Message;
                // Deposit vẫn ở Paid — sẽ được retry bởi SettlementRetryJob.
            }
            finally
            {
                settlement.UpdatedAt = DateTime.UtcNow;
                await _settlementRepository.UpdateAsync(settlement);
                await _settlementRepository.SaveChangesAsync();

                deposit.UpdatedAt = DateTime.UtcNow;
                await _depositRepository.UpdateAsync(deposit);
                await _depositRepository.SaveChangesAsync();

                _logger.LogInformation(
                    "Settlement {SettlementId} for cafe {CafeId} session {SessionId}: Status={Status}, Amount={Amount}",
                    settlement.Id, cafeId, activeSessionId, settlement.Status, netTransfer);
            }

            return settlement;
        }

        public async Task<IReadOnlyList<CafeSettlement>> GetPendingSettlementsAsync(Guid cafeId)
        {
            return await _settlementRepository.GetPendingAsync(cafeId);
        }
    }
}