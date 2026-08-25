namespace BoardVerse.Core.DTOs.Pos;

public class ActiveSessionMemberDto
{
 public Guid Id { get; set; }
 public Guid? UserId { get; set; }
 public string UserName { get; set; } = string.Empty;
 public bool IsGuestSlot { get; set; }
 /// <summary>Số điện thoại (chỉ áp dụng cho Guest_Slot).</summary>
 public string? PhoneNumber { get; set; }
 public DateTime JoinedAt { get; set; }
 public DateTime? LeftAt { get; set; }
 /// <summary>BR-16: Tổng phút đã chơi của thành viên này.</summary>
 public int TotalMinutesPlayed { get; set; }
 /// <summary>BR-16: Tiền giờ chơi riêng của thành viên này.</summary>
 public decimal Subtotal { get; set; }
 /// <summary>BR-14: Tiền phạt của thành viên này (không áp dụng cho GuestSlot).</summary>
 public decimal PenaltyAmount { get; set; }
 /// <summary>
 /// BR-15 + BR-22: Số tiền deposit (cọc) đã trừ cho thành viên này.
 /// </summary>
 public decimal DepositAppliedAmount { get; set; }
 /// <summary>
 /// BR-15: Tổng hóa đơn cuối cùng phải thanh toán của thành viên này.
 /// Công thức: Subtotal + PenaltyAmount - DepositAppliedAmount.
 /// Field này PHẢI hiển thị ở POS checkout để staff thấy từng member phải trả bao nhiêu.
 /// </summary>
 public decimal TotalAmount { get; set; }
 public bool IsCheckedOut { get; set; }
 public DateTime? CheckedOutAt { get; set; }
 public Core.Enum.IndividualSessionStatus Status { get; set; }
}
