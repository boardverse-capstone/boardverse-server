using BoardVerse.Core.DTOs.Auth.Requests;
using BoardVerse.Core.DTOs.Auth.Responses;
using BoardVerse.Core.DTOs.User;

namespace BoardVerse.Services.IServices
{
    public interface IAuthService
    {
        Task<LoginResponseDto> RegisterAsync(RegisterRequestDto request);
        Task<LoginResponseDto> LoginAsync(LoginRequestDto request);
        Task<LoginResponseDto> GoogleLoginAsync(GoogleAuthRequestDto request);

        // Refresh tokens
        Task<RefreshTokenResponseDto> ExchangeRefreshTokenAsync(RefreshTokenRequestDto request);
        Task RevokeRefreshTokenAsync(string refreshToken);

        // Email verification
        Task<string> SendEmailVerificationAsync(SendEmailVerificationRequestDto request);
        Task VerifyEmailAsync(VerifyEmailRequestDto request);

        // Password reset
        Task<string> RequestPasswordResetAsync(RequestPasswordResetDto request);
        Task ResetPasswordAsync(ResetPasswordDto request);

        // Change password for authenticated users
        Task ChangePasswordAsync(Guid userId, ChangePasswordDto request);

        // Link Google account to existing user
        Task<LoginResponseDto> LinkGoogleAccountAsync(LinkGoogleRequestDto request);
    }
}
