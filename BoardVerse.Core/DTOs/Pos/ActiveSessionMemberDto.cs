namespace BoardVerse.Core.DTOs.Pos;

public class ActiveSessionMemberDto
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public bool IsGuestSlot { get; set; }
    public DateTime JoinedAt { get; set; }
    public DateTime? LeftAt { get; set; }
    /// <summary>BR-16: Tổng phút đã chơi của thành viên này.</summary>
    public int TotalMinutesPlayed { get; set; }
    /// <summary>BR-16: Tiền giờ chơi riêng của thành viên này.</summary>
    public decimal Subtotal { get; set; }
    /// <summary>BR-14: Tiền phạt của thành viên này (không áp dụng cho GuestSlot).</summary>
    public decimal PenaltyAmount { get; set; }
    public bool IsCheckedOut { get; set; }
    public DateTime? CheckedOutAt { get; set; }
    public Core.Enum.IndividualSessionStatus Status { get; set; }
}
