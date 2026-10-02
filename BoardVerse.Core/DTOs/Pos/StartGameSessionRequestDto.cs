using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.Pos
{
    public class StartGameSessionRequestDto
    {
        [Required(ErrorMessage = ApiErrorMessages.Validation.TableIdRequired)]
        public Guid CafeTableId { get; set; }

        [Required(ErrorMessage = ApiErrorMessages.Validation.BarcodeRequired)]
        [StringLength(50, MinimumLength = 3, ErrorMessage = ApiErrorMessages.Validation.BarcodeLength)]
        public string Barcode { get; set; } = string.Empty;

        /// <summary>
        /// BR-13 (revised 2026-09-30) Option B: Optional UserId của 1 khách vãng lai được chỉ định làm
        /// "primary customer" (host thực sự của phiên walk-in). Khi set:
        ///   - session.HostId = PrimaryCustomerUserId
        ///   - Tạo ActiveSessionMember IsHost = true cho customer đó
        ///   - Guest slots khác / late members gộp bill về customer này (giống Reservation flow).
        /// Khi null:
        ///   - session.HostId = staff.UserId (audit/SignalR)
        ///   - session.IsWalkInSession = true, StartedByStaffId = staff.Id
        ///   - MapSession hiển thị HostName = "Khách vãng lai"
        ///   - Bill = tổng Subtotal + Penalty của tất cả guest slots (cash, không qua ví).
        /// </summary>
        public Guid? PrimaryCustomerUserId { get; set; }
    }
}
