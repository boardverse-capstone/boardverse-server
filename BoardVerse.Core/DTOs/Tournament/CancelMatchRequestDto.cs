namespace BoardVerse.Core.DTOs.Tournament;

/// <summary>
/// Manager gửi lý do hủy ván đấu (lưu vào Notes để audit + truy vết).
/// </summary>
public class CancelMatchRequestDto
{
    /// <summary>Lý do hủy. Bắt buộc, tối đa 500 ký tự.</summary>
    public string Reason { get; set; } = string.Empty;
}