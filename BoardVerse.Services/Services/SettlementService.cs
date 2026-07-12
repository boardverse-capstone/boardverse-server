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
                throw new ConflictException("Chỉ phiên đã thanh toán mới giải ngân deposit.");
            }

            if (session.DepositAppliedAmount <= 0)
            {
                throw new ConflictException(ApiErrorMessages.Pos.DepositMissingForSettlement);
            }

            var masterAccount = await _masterAccountRepository.GetActiveAsync()
                ?? throw new ConflictException(ApiErrorMessages.Pos.MasterAccountNotConfigured);

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

            deposit.Status = BookingDepositStatus.Released;
            deposit.ReleasedAt = DateTime.UtcNow;
            deposit.SePayTransferId = $"sepay_pending_{settlement.Id}";
            deposit.UpdatedAt = DateTime.UtcNow;

            await _depositRepository.UpdateAsync(deposit);
            await _depositRepository.SaveChangesAsync();

            try
            {
                var transferRequest = new CreateTransferRequest(
                    ToBankAccount: masterAccount.AccountHolder,
                    ToAccountNumber: masterAccount.MaskedAccountNumber ?? masterAccount.VirtualAccountNumber ?? string.Empty,
                    Amount: netTransfer,
                    Description: $"BoardVerse settlement - session {activeSessionId}",
                    ReferenceId: $"settlement_{settlement.Id:N}");

                var transferResponse = await _sePayClient.CreateTransferAsync(transferRequest);

                settlement.Status = CafeSettlementStatus.Succeeded;
                settlement.SePayTransferId = transferResponse.TransferId ?? settlement.SePayTransferId;
                settlement.TransferredAt = DateTime.UtcNow;
                deposit.SePayTransferId = transferResponse.TransferId ?? deposit.SePayTransferId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SePay transfer failed for settlement {SettlementId}", settlement.Id);
                settlement.Status = CafeSettlementStatus.Failed;
                settlement.FailureReason = ex.Message;
            }
            finally
            {
                settlement.UpdatedAt = DateTime.UtcNow;
                await _settlementRepository.UpdateAsync(settlement);
                await _settlementRepository.SaveChangesAsync();

                deposit.UpdatedAt = DateTime.UtcNow;
                await _depositRepository.UpdateAsync(deposit);
                await _depositRepository.SaveChangesAsync();
            }

            return settlement;
        }

        public async Task<IReadOnlyList<CafeSettlement>> GetPendingSettlementsAsync(Guid cafeId)
        {
            return await _settlementRepository.GetPendingAsync(cafeId);
        }
    }
}
