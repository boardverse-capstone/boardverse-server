namespace BoardVerse.Core.DTOs.Tournament;

/// <summary>
/// Options khi Manager bấm Start tournament với số người không đủ.
///
/// 3 strategies:
/// - AllowPartialStart = true: bỏ qua MinParticipants check, dùng ActualPreliminaryRounds.
/// - ReducedRounds = N: shorten rounds Swiss thay vì dùng PreliminaryRounds config.
///   Nếu null → auto-calculate bằng TournamentRoundsCalculator.
/// - AutoShortenMode:
///   - "Auto": hệ thống tự tính số rounds tối ưu theo N participants.
///   - "Manual": Manager chỉ định ReducedRounds cụ thể.
/// </summary>
public class StartTournamentOptionsDto
{
    /// <summary>Bỏ qua MinParticipants check. Manager xác nhận có ý thức.</summary>
    public bool AllowPartialStart { get; set; } = false;

    /// <summary>Override số rounds Swiss. Null = dùng Auto-shorten.</summary>
    public int? ReducedRounds { get; set; }

    /// <summary>"Auto" hoặc "Manual". Mặc định "Auto".</summary>
    public string AutoShortenMode { get; set; } = "Auto";

    /// <summary>Lý do (bắt buộc khi AllowPartialStart = true, để audit).</summary>
    public string? Reason { get; set; }
}