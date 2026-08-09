using BoardVerse.API.Controllers;
using BoardVerse.Core.DTOs.Payment;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers;

[ApiController]
[Route("api/sepay-accounts")]
// C2: Không đặt [Authorize(Roles="Admin")] ở class-level vì các endpoint Manager con
// sẽ yêu cầu cả Admin AND Manager (AND-combined), khiến Manager không gọi được.
[Authorize]
public class SePayAccountController : BaseApiController
{
    private readonly ISePayAccountService _sePayAccountService;
    private readonly ILogger<SePayAccountController> _logger;

    public SePayAccountController(
        ISePayAccountService sePayAccountService,
        ILogger<SePayAccountController> logger)
    {
        _sePayAccountService = sePayAccountService;
        _logger = logger;
    }

    /// <summary>
    /// Lấy tất cả SePay accounts. [Role: Admin]
    /// </summary>
    /// <param name="query">Filter options: AccountType, CafeId, IsActive</param>
    /// <response code="200">Danh sách SePay accounts.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có quyền Admin.</response>
    [HttpGet]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(IReadOnlyList<SePayAccountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll([FromQuery] SePayAccountQuery query)
    {
        var accounts = await _sePayAccountService.GetAllAsync(query);
        return this.NewResponse(200, ApiSuccessMessages.Payment.SePayAccountsRetrieved, accounts);
    }

    /// <summary>
    /// Lấy SePay account theo ID. [Role: Admin]
    /// </summary>
    /// <param name="id">SePay account ID.</param>
    /// <response code="200">Thông tin SePay account.</response>
    /// <response code="404">Không tìm thấy.</response>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(SePayAccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var account = await _sePayAccountService.GetByIdAsync(id);
        if (account == null)
        {
            return NotFound(new { message = ApiErrorMessages.Payment.SePayAccountNotFound(id) });
        }
            return this.NewResponse(200, ApiSuccessMessages.Payment.SePayAccountRetrieved, account);
    }

    /// <summary>
    /// Lấy Master Account. [Role: Admin]
    /// </summary>
    [HttpGet("master")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(SePayAccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMasterAccount()
    {
        var account = await _sePayAccountService.GetMasterAccountAsync();
        if (account == null)
        {
            return NotFound(new { message = ApiErrorMessages.Payment.SePayMasterAccountNotCreated });
        }
        return this.NewResponse(200, ApiSuccessMessages.Payment.SePayMasterAccountRetrieved, account);
    }

    /// <summary>
    /// Tạo SePay account đầy đủ. [Role: Admin]
    /// Manager KHÔNG dùng endpoint này — Manager dùng <c>POST /api/sepay-accounts/my-cafe</c>
    /// với chỉ 4 field (bank info).
    /// </summary>
    /// <param name="request">Thông tin SePay account đầy đủ (AccountType, CafeId, bank info, SePay credentials optional).</param>
    /// <response code="201">Tạo thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ (vd: thiếu CafeId cho AccountType=Cafe).</response>
    /// <response code="409">Master/Cafe account đã tồn tại.</response>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(SePayAccountDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateSePayAccountRequestDto request)
    {
        try
        {
            var account = await _sePayAccountService.CreateAsync(request);
            return CreatedAtAction(nameof(GetById), new { id = account.Id }, account);
        }
        catch (ArgumentException ex)
        {
            return this.NewResponse(400, ex.Message, null);
        }
        catch (InvalidOperationException ex)
        {
            return this.NewResponse(409, ex.Message, null);
        }
    }

    /// <summary>
    /// Cập nhật SePay account. [Role: Admin]
    /// </summary>
    /// <param name="id">SePay account ID.</param>
    /// <param name="request">Thông tin cần cập nhật.</param>
    /// <response code="200">Cập nhật thành công.</response>
    /// <response code="404">Không tìm thấy.</response>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(SePayAccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSePayAccountRequestDto request)
    {
        try
        {
            var account = await _sePayAccountService.UpdateAsync(id, request);
            return this.NewResponse(200, ApiSuccessMessages.Payment.SePayAccountUpdated, account);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = ApiErrorMessages.Payment.SePayAccountNotFound(id) });
        }
    }

    /// <summary>
    /// Xóa SePay account. [Role: Admin]
    /// </summary>
    /// <param name="id">SePay account ID.</param>
    /// <response code="204">Xóa thành công.</response>
    /// <response code="404">Không tìm thấy.</response>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            await _sePayAccountService.DeleteAsync(id);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = ApiErrorMessages.Payment.SePayAccountNotFound(id) });
        }
    }

    /// <summary>
    /// Chuyển đổi môi trường SePay (Test ↔ Production). [Role: Admin]
    /// </summary>
    /// <param name="id">SePay account ID.</param>
    /// <param name="dto">Thông tin môi trường mới.</param>
    /// <response code="200">Cập nhật thành công.</response>
    /// <response code="400">Môi trường không hợp lệ.</response>
    /// <response code="404">Không tìm thấy.</response>
    [HttpPut("{id:guid}/environment")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(SePayAccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetEnvironment(Guid id, [FromBody] SetEnvironmentRequestDto dto)
    {
        try
        {
            var account = await _sePayAccountService.SetEnvironmentAsync(id, dto.Environment);
            return this.NewResponse(200, ApiSuccessMessages.Payment.SePayEnvironmentUpdated, account);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = ApiErrorMessages.Payment.SePayAccountNotFound(id) });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    #region Manager Endpoints - Cafe SePay Account

    /// <summary>
    /// Lấy SePay account của cafe mà Manager đang quản lý. [Role: Manager]
    /// </summary>
    /// <response code="200">Thông tin SePay account của cafe.</response>
    /// <response code="404">Cafe chưa có SePay account.</response>
    [HttpGet("my-cafe")]
    [Authorize(Roles = "Manager")]
    [ProducesResponseType(typeof(SePayAccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyCafeAccount()
    {
        var account = await _sePayAccountService.GetByManagerCafeAsync();
        if (account == null)
        {
            return NotFound(new { message = ApiErrorMessages.Payment.SePayCafeNotConfigured });
        }
            return this.NewResponse(200, ApiSuccessMessages.Payment.SePayAccountRetrieved, account);
    }

    /// <summary>
    /// Tạo payment account cho cafe mà Manager đang quản lý. [Role: Manager]
    /// <para>
    /// Manager CHỈ CẦN cung cấp 4 field: <c>bankCode</c>, <c>accountNumber</c>, <c>accountHolder</c>, <c>environment</c> (optional).
    /// KHÔNG cần đăng ký SePay merchant — BoardVerse sẽ tự phát hiện giao dịch vào TK ngân hàng thật của cafe
    /// thông qua SePay webhook (bank_mode=all) và sinh VietQR từ bank info này.
    /// </para>
    /// <para>
    /// Sau khi Manager tạo xong, <b>admin BoardVerse</b> vào SePay dashboard link TK ngân hàng này vào
    /// SePay company master để webhook phát hiện được giao dịch. Bước này chỉ làm 1 lần, không cần Manager đụng.
    /// </para>
    /// <para>Nếu cafe đã có payment account → 409 Conflict, dùng <c>PUT /api/sepay-accounts/my-cafe</c> để cập nhật.</para>
    /// </summary>
    /// <param name="request">Thông tin TK ngân hàng của cafe. Cả 3 field <c>bankCode</c>, <c>accountNumber</c>, <c>accountHolder</c> đều bắt buộc.</param>
    /// <response code="201">Tạo thành công, trả về payment account của cafe (đã mask số TK).</response>
    /// <response code="400">Thiếu bankCode/accountNumber/accountHolder.</response>
    /// <response code="404">Manager không quản lý cafe nào.</response>
    /// <response code="409">Cafe đã có payment account.</response>
    [HttpPost("my-cafe")]
    [Authorize(Roles = "Manager")]
    [ProducesResponseType(typeof(SePayAccountDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateMyCafeAccount([FromBody] CreateCafePaymentAccountRequestDto request)
    {
        try
        {
            var account = await _sePayAccountService.CreateByManagerCafeAsync(request);
            return CreatedAtAction(nameof(GetMyCafeAccount), new { }, account);
        }
        catch (ArgumentException ex)
        {
            return this.NewResponse(400, ex.Message, null);
        }
        catch (NotFoundException ex)
        {
            return this.NewResponse(404, ex.Message, null);
        }
        catch (InvalidOperationException ex)
        {
            return this.NewResponse(409, ex.Message, null);
        }
    }

    /// <summary>
    /// Generate QR test cho Manager verify payment account. [Role: Manager]
    /// <para>
    /// Manager scan QR 10k → CK thử → verify SePay detect được giao dịch.
    /// KHÔNG tạo booking/session, KHÔNG cần tạo deposit giả.
    /// </para>
    /// <para>
    /// <b>Workflow test:</b><br/>
    /// 1. Manager gọi endpoint này.<br/>
    /// 2. Server gen VietQR URL với số tiền cố định 10.000 VND + transfer content unique.<br/>
    /// 3. Manager mở app ngân hàng, quét QR, CK 10k với nội dung đúng.<br/>
    /// 4. SePay webhook sẽ detect giao dịch trong 1-2 phút.<br/>
    /// 5. Nếu KHÔNG detect được → admin BoardVerse chưa link TK vào SePay company.
    /// </para>
    /// </summary>
    /// <response code="200">Trả QR URL + test transfer content + hướng dẫn.</response>
    /// <response code="404">Manager không quản lý cafe, hoặc cafe chưa có payment account.</response>
    /// <response code="409">Bank info trong DB thiếu (lỗi data integrity).</response>
    [HttpGet("my-cafe/qr-preview")]
    [Authorize(Roles = "Manager")]
    [ProducesResponseType(typeof(CafePaymentQrPreviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetMyCafeQrPreview()
    {
        try
        {
            var preview = await _sePayAccountService.GenerateTestQrByManagerCafeAsync();
            return this.NewResponse(200, ApiSuccessMessages.Payment.SePayQrPreviewGenerated, preview);
        }
        catch (NotFoundException ex)
        {
            return this.NewResponse(404, ex.Message, null);
        }
        catch (InvalidOperationException ex)
        {
            return this.NewResponse(409, ex.Message, null);
        }
    }

    /// <summary>
    /// Cập nhật SePay account của cafe mà Manager đang quản lý. [Role: Manager]
    /// </summary>
    /// <param name="request">Thông tin cần cập nhật.</param>
    /// <response code="200">Cập nhật thành công.</response>
    /// <response code="404">Cafe chưa có SePay account.</response>
    [HttpPut("my-cafe")]
    [Authorize(Roles = "Manager")]
    [ProducesResponseType(typeof(SePayAccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateMyCafeAccount([FromBody] UpdateSePayAccountRequestDto request)
    {
        try
        {
            var account = await _sePayAccountService.UpdateByManagerCafeAsync(request);
            return this.NewResponse(200, ApiSuccessMessages.Payment.SePayAccountUpdated, account);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = ApiErrorMessages.Payment.SePayCafeNotConfigured });
        }
    }

    /// <summary>
    /// Chuyển đổi môi trường SePay (Test ↔ Production) của cafe mà Manager đang quản lý. [Role: Manager]
    /// </summary>
    /// <param name="dto">Thông tin môi trường mới.</param>
    /// <response code="200">Cập nhật thành công.</response>
    /// <response code="400">Môi trường không hợp lệ.</response>
    /// <response code="404">Cafe chưa có SePay account.</response>
    [HttpPut("my-cafe/environment")]
    [Authorize(Roles = "Manager")]
    [ProducesResponseType(typeof(SePayAccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetMyCafeEnvironment([FromBody] SetEnvironmentRequestDto dto)
    {
        try
        {
            var account = await _sePayAccountService.SetEnvironmentByManagerCafeAsync(dto.Environment);
            return this.NewResponse(200, ApiSuccessMessages.Payment.SePayEnvironmentUpdated, account);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = ApiErrorMessages.Payment.SePayCafeNotConfigured });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    #endregion

    /// <summary>
    /// Admin tra cứu SePay transaction theo mã giao dịch ngân hàng (SePayTransactionId).
    /// Dùng khi support khách hàng hoặc debug webhook không match. [Role: Admin]
    /// </summary>
    /// <param name="sePayTransactionId">Mã giao dịch SePay (thường do SePay gửi về trong webhook).</param>
    /// <response code="200">Tìm thấy BookingDeposit khớp.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Tài khoản không có quyền Admin.</response>
    /// <response code="404">Không tìm thấy BookingDeposit với transactionId này.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("lookup/transaction/{sePayTransactionId}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> LookupBySePayTransactionId(string sePayTransactionId)
    {
        if (string.IsNullOrWhiteSpace(sePayTransactionId))
        {
            throw new BadRequestException("sePayTransactionId là bắt buộc.");
        }

        var lookup = await _sePayAccountService.LookupBySePayTransactionIdAsync(sePayTransactionId);
        if (lookup == null)
        {
            throw new NotFoundException(
                $"Không tìm thấy BookingDeposit với SePayTransactionId='{sePayTransactionId}'.");
        }
        return this.NewResponse(200, "Tra cứu SePay transaction thành công.", lookup);
    }
}
