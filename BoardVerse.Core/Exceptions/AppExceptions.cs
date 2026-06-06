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
        public BadRequestException(string message = "Bad request.") : base(message, 400) { }
        public BadRequestException(string message, Exception innerException) : base(message, 400, innerException) { }
    }

    public class UnauthorizedException : AppException
    {
        public UnauthorizedException(string message = "Unauthorized.") : base(message, 401) { }
        public UnauthorizedException(string message, Exception innerException) : base(message, 401, innerException) { }
    }

    public class ForbiddenException : AppException
    {
        public ForbiddenException(string message = "Forbidden.") : base(message, 403) { }
        public ForbiddenException(string message, Exception innerException) : base(message, 403, innerException) { }
    }

    public class NotFoundException : AppException
    {
        public NotFoundException(string message = "Not found.") : base(message, 404) { }
        public NotFoundException(string message, Exception innerException) : base(message, 404, innerException) { }
    }

    public class ConflictException : AppException
    {
        public ConflictException(string message = "Conflict.") : base(message, 409) { }
        public ConflictException(string message, Exception innerException) : base(message, 409, innerException) { }
    }

    public class UserBlockedException : ForbiddenException
    {
        public UserBlockedException(string message = "User is blocked.") : base(message) { }
    }

    public class InternalServerErrorException : AppException
    {
        public InternalServerErrorException(string message = "An internal server error occurred.") : base(message, 500) { }
        public InternalServerErrorException(string message, Exception innerException) : base(message, 500, innerException) { }
    }

    public class UserNotFoundException : NotFoundException
    {
        public UserNotFoundException(string message = "User not found.") : base(message) { }
    }

    public class UserAlreadyExistsException : ConflictException
    {
        public UserAlreadyExistsException(string message = "A user with the same credentials already exists.") : base(message) { }
    }

    public class EmailAlreadyExistsException : ConflictException
    {
        public EmailAlreadyExistsException(string message = "A user with this email already exists.") : base(message) { }
    }

    public class InvalidCredentialsException : UnauthorizedException
    {
        public InvalidCredentialsException(string message = "Invalid credentials.") : base(message) { }
    }

    public class TokenExpiredException : UnauthorizedException
    {
        public TokenExpiredException(string message = "Token expired.") : base(message) { }
    }

    public class InvalidTokenException : UnauthorizedException
    {
        public InvalidTokenException(string message = "Invalid token.") : base(message) { }
    }

    public class TooManyLoginAttemptsException : AppException
    {
        public TooManyLoginAttemptsException(string message = "Too many login attempts. Try again later.") : base(message, 429) { }
    }

    public class RefreshTokenExpiredException : TokenExpiredException
    {
        public RefreshTokenExpiredException(string message = "Refresh token expired.") : base(message) { }
    }

    public class RefreshTokenNotFoundException : NotFoundException
    {
        public RefreshTokenNotFoundException(string message = "Refresh token not found.") : base(message) { }
    }

    public class VerificationTokenExpiredException : TokenExpiredException
    {
        public VerificationTokenExpiredException(string message = "Verification token expired.") : base(message) { }
    }

    public class PasswordResetTokenExpiredException : TokenExpiredException
    {
        public PasswordResetTokenExpiredException(string message = "Password reset token expired.") : base(message) { }
    }

    public class EmailVerificationRequiredException : ForbiddenException
    {
        public EmailVerificationRequiredException(string message = "Email must be verified before requesting a password reset.") : base(message) { }
    }

    public class GoogleTokenValidationException : UnauthorizedException
    {
        public GoogleTokenValidationException(string message = "Failed to validate Google token.") : base(message) { }
        public GoogleTokenValidationException(string message, Exception innerException) : base(message, innerException) { }
    }

    public class ProfileNotFoundException : NotFoundException
    {
        public ProfileNotFoundException(string message = "Profile not found.") : base(message) { }
    }

    public class ProfileAlreadyExistsException : ConflictException
    {
        public ProfileAlreadyExistsException(string message = "Profile already exists.") : base(message) { }
    }

    public class ProfileDisabledException : ForbiddenException
    {
        public ProfileDisabledException(string message = "Profile is disabled.") : base(message) { }
    }

    public class ConfigurationMissingException : InternalServerErrorException
    {
        public ConfigurationMissingException(string message = "Required configuration is missing.") : base(message) { }
    }

    public class TenantNotFoundException : NotFoundException
    {
        public TenantNotFoundException(string message = "Tenant not found.") : base(message) { }
    }

    public class TenantAccessDeniedException : ForbiddenException
    {
        public TenantAccessDeniedException(string message = "Access to the tenant is denied.") : base(message) { }
    }

    public class InsufficientKarmaException : ForbiddenException
    {
        public InsufficientKarmaException(string message = "Insufficient karma.") : base(message) { }
    }

    public class TableAlreadyBookedException : ConflictException
    {
        public TableAlreadyBookedException(string message = "Table is already booked.") : base(message) { }
    }

    public class BookingNotFoundException : NotFoundException
    {
        public BookingNotFoundException(string message = "Booking not found.") : base(message) { }
    }

    public class InvalidInvoiceException : BadRequestException
    {
        public InvalidInvoiceException(string message = "Invoice is invalid.") : base(message) { }
    }

    public class EmailSendingException : InternalServerErrorException
    {
        public EmailSendingException(string message = "Failed to send email.") : base(message) { }
        public EmailSendingException(string message, Exception innerException) : base(message, innerException) { }
    }
}