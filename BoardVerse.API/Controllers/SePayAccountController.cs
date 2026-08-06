using BoardVerse.API.Controllers;
using BoardVerse.Core.DTOs.Payment;
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
        return Ok(accounts);
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
        return Ok(account);
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
        return Ok(account);
    }

    /// <summary>
    /// Tạo SePay account mới. [Role: Admin]
    /// </summary>
    /// <param name="request">Thông tin SePay account.</param>
    /// <response code="201">Tạo thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
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
            return Ok(account);
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
            return Ok(account);
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
        return Ok(account);
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
            return Ok(account);
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
            return Ok(account);
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
}
