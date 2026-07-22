using System;

namespace BoardVerse.Core.Exceptions
{
    public class AppException : Exception
    {
        public int StatusCode { get; }

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
}
