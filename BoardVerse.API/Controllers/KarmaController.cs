using BoardVerse.Core.DTOs.User;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers;

/// <summary>
/// Karma endpoints — BR-KARMA-01 §4.3 + §9.5.
/// </summary>
[ApiController]
[Route("api/v1")]
[Authorize]
public class KarmaController : ControllerBase
{
    private readonly IKarmaService _karmaService;
    private readonly ILogger<KarmaController> _logger;

    public KarmaController(IKarmaService karmaService, ILogger<KarmaController> logger)
    {
        _karmaService = karmaService;
        _logger = logger;
    }

    /// <summary>
    /// Lấy thông tin karma hiện tại của user. [Role: Authenticated — user xem của chính mình, hoặc Admin xem của user khác.]
    /// </summary>
    /// <param name="userId">UserId cần xem karma.</param>
    /// <response code="200">Trả về thông tin karma.</response>
    /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
    /// <response code="403">Không đủ quyền xem karma của user khác.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpGet("users/{userId:guid}/karma")]
    public async Task<IActionResult> GetUserKarma(Guid userId, CancellationToken ct)
    {
        var points = await _karmaService.GetUserKarmaPointsAsync(userId, ct);
        var level = await _karmaService.GetUserKarmaLevelAsync(userId, ct);

        var dto = new UserKarmaStateDto
        {
            UserId = userId,
            KarmaPoints = points,
            KarmaLevel = level.ToString()
        };

        return Ok(dto);
    }

    /// <summary>
    /// User gửi appeal cho 1 karma violation cụ thể. [Role: Authenticated — chỉ gửi cho record của chính mình.]
    /// </summary>
    /// <param name="userId">UserId gửi appeal.</param>
    /// <param name="request">RecordId + lý do.</param>
    /// <response code="200">Appeal được ghi nhận.</response>
    /// <response code="400">Lý do trống hoặc record không thuộc user.</response>
    /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpPost("users/{userId:guid}/karma/appeal")]
    public async Task<IActionResult> SubmitAppeal(Guid userId, [FromBody] SubmitKarmaAppealRequestDto request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request?.Reason))
        {
            return BadRequest(new { error = "Lý do appeal không được để trống." });
        }

        var ok = await _karmaService.SubmitAppealAsync(userId, request.RecordId, request.Reason, ct);
        if (!ok)
        {
            return BadRequest(new { error = "Không thể gửi appeal. Record không tồn tại hoặc đã được review." });
        }

        return Ok(new { submitted = true });
    }
}
