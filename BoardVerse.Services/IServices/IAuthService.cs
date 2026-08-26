using BoardVerse.Core.DTOs.Auth.Requests;
using BoardVerse.Core.DTOs.Auth.Responses;
using BoardVerse.Core.DTOs.User;

using System.Threading;
namespace BoardVerse.Services.IServices
{
    public interface IAuthService
    {
        Task<LoginResponseDto> RegisterAsync(RegisterRequestDto request, CancellationToken cancellationToken = default);
        Task<LoginResponseDto> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default);
        Task<LoginResponseDto> GoogleLoginAsync(GoogleAuthRequestDto request, CancellationToken cancellationToken = default);

        // Refresh tokens
        Task<RefreshTokenResponseDto> ExchangeRefreshTokenAsync(RefreshTokenRequestDto request, CancellationToken cancellationToken = default);
        Task RevokeRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);

        // Email verification
        Task<string> SendEmailVerificationAsync(SendEmailVerificationRequestDto request, CancellationToken cancellationToken = default);
        Task VerifyEmailAsync(VerifyEmailRequestDto request, CancellationToken cancellationToken = default);

        // Password reset
        Task<string> RequestPasswordResetAsync(RequestPasswordResetDto request, CancellationToken cancellationToken = default);
        Task ResetPasswordAsync(ResetPasswordDto request, CancellationToken cancellationToken = default);

        // Change password for authenticated users
        Task ChangePasswordAsync(Guid userId, ChangePasswordDto request, CancellationToken cancellationToken = default);

        // Link Google account to existing user
        Task<LoginResponseDto> LinkGoogleAccountAsync(LinkGoogleRequestDto request, CancellationToken cancellationToken = default);
    }
}
