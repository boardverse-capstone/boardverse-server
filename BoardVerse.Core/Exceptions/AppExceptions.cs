using System;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.Exceptions
{
    public class AppException : Exception
    {
        public int StatusCode { get; }

        /// <summary>
        /// Machine-readable error code (e.g. <c>InsufficientSeatsForMerge</c>, <c>RATE_LIMIT_EXCEEDED</c>).
        /// Optional. Khi null, response chỉ trả <c>message</c> mà không có trường <c>errorCode</c>.
        /// Convention: PascalCase identifier do domain quyết định (vd: <c>InsufficientSeatsForMerge</c>,
        /// <c>NoActiveMembersToTransfer</c>). Tránh dùng UPPER_SNAKE_CASE để đồng nhất với class name.
        /// </summary>
        public string? ErrorCode { get; }

        public AppException(string message, int statusCode)
            : base(message)
        {
            StatusCode = statusCode;
        }

        public AppException(string message, int statusCode, Exception innerException)
            : base(message, innerException)
        {
            StatusCode = statusCode;
        }

        public AppException(string message, int statusCode, string errorCode)
            : base(message)
        {
            StatusCode = statusCode;
            ErrorCode = errorCode;
        }

        public AppException(string message, int statusCode, string errorCode, Exception innerException)
            : base(message, innerException)
        {
            StatusCode = statusCode;
            ErrorCode = errorCode;
        }
    }

    public class BadRequestException : AppException
    {
        public BadRequestException(string message = "Yêu cầu không hợp lệ.") : base(message, 400) { }
        public BadRequestException(string message, Exception innerException) : base(message, 400, innerException) { }
    }

    public class UnauthorizedException : AppException
    {
        public UnauthorizedException(string message = "Chưa xác thực.") : base(message, 401) { }
        public UnauthorizedException(string message, Exception innerException) : base(message, 401, innerException) { }
    }

    public class ForbiddenException : AppException
    {
        public ForbiddenException(string message = "Không có quyền truy cập.") : base(message, 403) { }
        public ForbiddenException(string message, Exception innerException) : base(message, 403, innerException) { }
    }

    public class NotFoundException : AppException
    {
        public NotFoundException(string message = "Không tìm thấy.") : base(message, 404) { }
        public NotFoundException(string message, Exception innerException) : base(message, 404, innerException) { }
    }

    public class ConflictException : AppException
    {
        public ConflictException(string message = "Xung đột dữ liệu.") : base(message, 409) { }
        public ConflictException(string message, Exception innerException) : base(message, 409, innerException) { }
        public ConflictException(string message, string errorCode) : base(message, 409, errorCode) { }
        public ConflictException(string message, string errorCode, Exception innerException) : base(message, 409, errorCode, innerException) { }
    }

    public class UserBlockedException : ForbiddenException
    {
        public UserBlockedException(string message = "Tài khoản đã bị khóa.") : base(message) { }
    }

    public class InternalServerErrorException : AppException
    {
        public InternalServerErrorException(string message = "Đã xảy ra lỗi máy chủ nội bộ.") : base(message, 500) { }
        public InternalServerErrorException(string message, Exception innerException) : base(message, 500, innerException) { }
    }

    public class UserNotFoundException : NotFoundException
    {
        public UserNotFoundException(string message = "Không tìm thấy người dùng.") : base(message) { }
    }

    public class UserAlreadyExistsException : ConflictException
    {
        public UserAlreadyExistsException(string message = "Đã tồn tại người dùng với thông tin này.") : base(message) { }
    }

    public class EmailAlreadyExistsException : ConflictException
    {
        public EmailAlreadyExistsException(string message = "Email này đã được sử dụng.") : base(message) { }
    }

    public class InvalidCredentialsException : UnauthorizedException
    {
        public InvalidCredentialsException(string message = "Thông tin đăng nhập không hợp lệ.") : base(message) { }
    }

    public class TokenExpiredException : UnauthorizedException
    {
        public TokenExpiredException(string message = "Token đã hết hạn.") : base(message) { }
    }

    public class InvalidTokenException : UnauthorizedException
    {
        public InvalidTokenException(string message = "Token không hợp lệ.") : base(message) { }
    }

    public class TooManyLoginAttemptsException : AppException
    {
        public TooManyLoginAttemptsException(string message = "Đăng nhập sai quá nhiều lần. Vui lòng thử lại sau.") : base(message, 429) { }
    }

    public class RefreshTokenExpiredException : TokenExpiredException
    {
        public RefreshTokenExpiredException(string message = "Refresh token đã hết hạn.") : base(message) { }
    }

    public class RefreshTokenNotFoundException : NotFoundException
    {
        public RefreshTokenNotFoundException(string message = "Không tìm thấy refresh token.") : base(message) { }
    }

    public class VerificationTokenExpiredException : TokenExpiredException
    {
        public VerificationTokenExpiredException(string message = "Mã xác minh đã hết hạn.") : base(message) { }
    }

    public class PasswordResetTokenExpiredException : TokenExpiredException
    {
        public PasswordResetTokenExpiredException(string message = "Mã đặt lại mật khẩu đã hết hạn.") : base(message) { }
    }

    public class EmailVerificationRequiredException : ForbiddenException
    {
        public EmailVerificationRequiredException(string message = "Email phải được xác minh trước khi đặt lại mật khẩu.") : base(message) { }
    }

    public class GoogleTokenValidationException : UnauthorizedException
    {
        public GoogleTokenValidationException(string message = "Không thể xác thực token Google.") : base(message) { }
        public GoogleTokenValidationException(string message, Exception innerException) : base(message, innerException) { }
    }

    public class ProfileNotFoundException : NotFoundException
    {
        public ProfileNotFoundException(string message = "Không tìm thấy hồ sơ.") : base(message) { }
    }

    public class ProfileAlreadyExistsException : ConflictException
    {
        public ProfileAlreadyExistsException(string message = "Hồ sơ đã tồn tại.") : base(message) { }
    }

    public class ProfileDisabledException : ForbiddenException
    {
        public ProfileDisabledException(string message = "Hồ sơ đã bị vô hiệu hóa.") : base(message) { }
    }

    public class ConfigurationMissingException : InternalServerErrorException
    {
        public ConfigurationMissingException(string message = "Thiếu cấu hình bắt buộc.") : base(message) { }
    }

    public class TenantNotFoundException : NotFoundException
    {
        public TenantNotFoundException(string message = "Không tìm thấy tenant.") : base(message) { }
    }

    public class TenantAccessDeniedException : ForbiddenException
    {
        public TenantAccessDeniedException(string message = "Từ chối truy cập tenant.") : base(message) { }
    }

    public class InsufficientKarmaException : ForbiddenException
    {
        public InsufficientKarmaException(string message = "Karma không đủ.") : base(message) { }
    }

    public class TableAlreadyBookedException : ConflictException
    {
        public TableAlreadyBookedException(string message = "Bàn đã được đặt.") : base(message) { }
    }

    public class BookingNotFoundException : NotFoundException
    {
        public BookingNotFoundException(string message = "Không tìm thấy đặt chỗ.") : base(message) { }
    }

    public class InvalidInvoiceException : BadRequestException
    {
        public InvalidInvoiceException(string message = "Hóa đơn không hợp lệ.") : base(message) { }
    }

    public class EmailSendingException : InternalServerErrorException
    {
        public EmailSendingException(string message = "Gửi email thất bại.") : base(message) { }
        public EmailSendingException(string message, Exception innerException) : base(message, innerException) { }
    }

    public class CafePartnerApplicationNotFoundException : NotFoundException
    {
        public CafePartnerApplicationNotFoundException(string message = "Không tìm thấy đơn đăng ký đối tác.") : base(message) { }
    }

    public class OpenCafePartnerApplicationExistsException : ConflictException
    {
        public OpenCafePartnerApplicationExistsException(string message = "Đã có đơn đối tác đang mở với email này.") : base(message) { }
    }

    public class CafePartnerEmailNotEligibleException : ConflictException
    {
        public CafePartnerEmailNotEligibleException(string message = "Email này không thể dùng cho đơn đăng ký đối tác.") : base(message) { }
    }

    public class CafePartnerApplicationInvalidStatusException : BadRequestException
    {
        public CafePartnerApplicationInvalidStatusException(string message = "Trạng thái đơn không cho phép thao tác này.") : base(message) { }
    }

    public class CafePartnerApplicationEmailMismatchException : BadRequestException
    {
        public CafePartnerApplicationEmailMismatchException(string message = "Email đại diện không khớp với đơn này.") : base(message) { }
    }

    public class CafePartnerActivationRequirementsNotMetException : BadRequestException
    {
        public CafePartnerActivationRequirementsNotMetException(string message = "Chưa đủ điều kiện kích hoạt quán.") : base(message) { }
    }

    public class SevereDataDuplicationException : ConflictException
    {
        public SevereDataDuplicationException(string message = "Mã số thuế hoặc Địa chỉ này đã được đăng ký trên hệ thống. Vui lòng kiểm tra lại.") : base(message) { }
    }

    public class BoardGameNotFoundException : NotFoundException
    {
        public BoardGameNotFoundException(string message = "Không tìm thấy board game.") : base(message) { }
    }

    public class PaymentException : InternalServerErrorException
    {
        public PaymentException(string message = "Thanh toán thất bại.") : base(message) { }
        public PaymentException(string message, Exception innerException) : base(message, innerException) { }
    }

    public class TooManyRequestsException : AppException
    {
        public TooManyRequestsException(string message = "Quá nhiều yêu cầu. Vui lòng thử lại sau.") : base(message, 429) { }
        public TooManyRequestsException(string message, Exception innerException) : base(message, 429, innerException) { }
    }

    /// <summary>
    /// GAP-5 Fix: Exception khi player không đủ BVC để thanh toán.
    /// Chứa thêm thông tin để client hiển thị top-up guidance.
    /// </summary>
    public class InsufficientBvcBalanceException : BadRequestException
    {
        public long CurrentBalance { get; }
        public long RequiredBalance { get; }
        public long MissingAmount { get; }

        public InsufficientBvcBalanceException(long currentBalance, long requiredBalance)
            : base(ApiErrorMessages.Session.InsufficientBvcBalance)
        {
            CurrentBalance = currentBalance;
            RequiredBalance = requiredBalance;
            MissingAmount = requiredBalance - currentBalance;
        }
    }

    /// <summary>
    /// Exception khi ghép lobby nhưng tổng active members sau khi ghép vượt quá sức chứa ghế
    /// của quán trong khung giờ đó. Áp dụng cho cả <c>CreateLobbyMergeRequest</c> và
    /// <c>ApproveLobbyMergeRequest</c> (re-validate vì member có thể đổi giữa 2 thời điểm).
    ///
    /// Status: 409 (Conflict).
    /// Error code: <c>InsufficientSeatsForMerge</c>.
    ///
    /// <b>Quy tắc BR-EXCEPTION-4 (fix 2026-10-02):</b> <c>targetActiveMembers + sourceActiveMembers &lt;= targetSeatCapacity</c>.
    /// Nếu vi phạm → throw exception này để trả 409 với thông tin cụ thể cho staff.
    /// </summary>
    /// <remarks>
    /// Được test qua <c>LobbyMergeServiceTests.CreateMergeRequestAsync_*</c> và
    /// <c>LobbyMergeServiceTests.ApproveMergeAsync_*</c>. <c>targetActiveMembers</c> được tính
    /// tại THỜI ĐIỂM validate (Create hay Approve), không dùng snapshot từ Create.
    /// </remarks>
    public class InsufficientSeatsForMergeException : ConflictException
    {
        /// <summary>Số thành viên ACTIVE hiện tại của target lobby TRƯỚC khi merge.</summary>
        public int TargetActiveMembers { get; }

        /// <summary>Số thành viên ACTIVE từ source lobby sẽ được transfer (vô hoặc selected subset).</summary>
        public int SourceActiveMembers { get; }

        /// <summary>Sức chứa tối đa của quán trong khung giờ target (<c>SeatInventory.TotalSeats</c>).</summary>
        public int TargetSeatCapacity { get; }

        /// <summary>Tổng active members sau khi merge (= <c>TargetActiveMembers + SourceActiveMembers</c>).</summary>
        public int CombinedCount => TargetActiveMembers + SourceActiveMembers;

        public InsufficientSeatsForMergeException(
            int targetActiveMembers,
            int sourceActiveMembers,
            int targetSeatCapacity,
            string message)
            : base(message, "InsufficientSeatsForMerge")
        {
            TargetActiveMembers = targetActiveMembers;
            SourceActiveMembers = sourceActiveMembers;
            TargetSeatCapacity = targetSeatCapacity;
        }
    }
}
