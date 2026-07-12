using BoardVerse.Core.DTOs.PaymentMasterAccount;
using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers
{
    [ApiController]
    [Route("api/admin/payment-master-accounts")]
    [Authorize(Roles = "Admin")]
    public class PaymentMasterAccountController : BaseApiController
    {
        private readonly IPaymentMasterAccountRepository _repository;

        public PaymentMasterAccountController(IPaymentMasterAccountRepository repository)
        {
            _repository = repository;
        }

        /// <summary>
        /// Lấy danh sách master account. [Role: Admin]
        /// </summary>
        /// <response code="200">Danh sách master account.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không có quyền Admin.</response>
        [HttpGet]
        public async Task<IActionResult> GetAllMasterAccounts()
        {
            var accounts = await _repository.GetAllAsync();
            var dtos = accounts.Select(a => new PaymentMasterAccountDto
            {
                Id = a.Id,
                Provider = a.Provider,
                AccountHolder = a.AccountHolder,
                BankCode = a.BankCode,
                MaskedAccountNumber = a.MaskedAccountNumber,
                VirtualAccountNumber = a.VirtualAccountNumber,
                QrContent = a.QrContent,
                IsActive = a.IsActive,
                CreatedAt = a.CreatedAt
            }).ToList();

            return this.NewResponse(200, "Lấy danh sách master account thành công.", new { Data = dtos });
        }

        /// <summary>
        /// Lấy chi tiết master account. [Role: Admin]
        /// </summary>
        /// <param name="id">Mã master account.</param>
        /// <response code="200">Chi tiết master account.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không có quyền Admin.</response>
        /// <response code="404">Không tìm thấy.</response>
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetMasterAccount(Guid id)
        {
            var a = await _repository.GetByIdAsync(id);
            if (a == null)
            {
                return this.NewResponse(404, $"Không tìm thấy PaymentMasterAccount '{id}'.", null);
            }

            return this.NewResponse(200, "Lấy chi tiết master account thành công.", new PaymentMasterAccountDto
            {
                Id = a.Id,
                Provider = a.Provider,
                AccountHolder = a.AccountHolder,
                BankCode = a.BankCode,
                MaskedAccountNumber = a.MaskedAccountNumber,
                VirtualAccountNumber = a.VirtualAccountNumber,
                QrContent = a.QrContent,
                IsActive = a.IsActive,
                CreatedAt = a.CreatedAt
            });
        }

        /// <summary>
        /// Cập nhật master account. [Role: Admin]
        /// </summary>
        /// <param name="id">Mã master account.</param>
        /// <param name="request">Thông tin cần cập nhật.</param>
        /// <response code="200">Cập nhật thành công.</response>
        /// <response code="400">Dữ liệu không hợp lệ.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không có quyền Admin.</response>
        /// <response code="404">Không tìm thấy.</response>
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> UpdateMasterAccount(Guid id, [FromBody] UpdatePaymentMasterAccountRequestDto request)
        {
            var account = await _repository.GetByIdAsync(id);
            if (account == null)
            {
                return this.NewResponse(404, $"Không tìm thấy PaymentMasterAccount '{id}'.", null);
            }

            account.Provider = request.Provider;
            account.AccountHolder = request.AccountHolder;
            account.BankCode = request.BankCode;
            account.MaskedAccountNumber = request.MaskedAccountNumber;
            account.VirtualAccountNumber = request.VirtualAccountNumber;
            account.QrContent = request.QrContent;
            account.IsActive = request.IsActive;

            await _repository.UpdateAsync(account);
            await _repository.SaveChangesAsync();

            return this.NewResponse(200, "Cập nhật master account thành công.", new { account.Id });
        }

        /// <summary>
        /// Xóa master account (soft delete). [Role: Admin]
        /// </summary>
        /// <param name="id">Mã master account.</param>
        /// <response code="200">Xóa thành công.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không có quyền Admin.</response>
        /// <response code="404">Không tìm thấy.</response>
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> DeleteMasterAccount(Guid id)
        {
            var account = await _repository.GetByIdAsync(id);
            if (account == null)
            {
                return this.NewResponse(404, $"Không tìm thấy PaymentMasterAccount '{id}'.", null);
            }

            await _repository.DeleteAsync(id);
            await _repository.SaveChangesAsync();

            return this.NewResponse(200, "Xóa master account thành công.", new { });
        }

        /// <summary>
        /// Tạo master account dùng để nhận/tạm giữ deposit. [Role: Admin]
        /// </summary>
        /// <param name="request">Thông tin master account.</param>
        /// <response code="201">Tạo master account thành công.</response>
        /// <response code="400">Dữ liệu không hợp lệ.</response>
        /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
        /// <response code="403">Tài khoản không có quyền Admin.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost]
        public async Task<IActionResult> CreateMasterAccount([FromBody] CreatePaymentMasterAccountRequestDto request)
        {
            var masterAccount = new PaymentMasterAccount
            {
                Provider = request.Provider,
                AccountHolder = request.AccountHolder,
                BankCode = request.BankCode,
                MaskedAccountNumber = request.MaskedAccountNumber,
                VirtualAccountNumber = request.VirtualAccountNumber,
                QrContent = request.QrContent,
                WebhookSecret = request.WebhookSecret,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            await _repository.AddAsync(masterAccount);
            await _repository.SaveChangesAsync();

            return this.NewResponse(201, "Tạo master account thành công.", new { masterAccount.Id });
        }
    }
}
