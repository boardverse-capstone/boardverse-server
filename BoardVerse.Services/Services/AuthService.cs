using BoardVerse.Core.DTOs.Auth.Requests;
using BoardVerse.Core.DTOs.Auth.Responses;
using BoardVerse.Core.DTOs.User;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.IServices;
using Google.Apis.Auth;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Security.Cryptography;

namespace BoardVerse.Services.Services
{
    public class AuthService : IAuthService
    {
        private readonly IAuthRepository _userRepository;
        private readonly IDistributedCache _distributedCache;
        private readonly IEmailService _emailService;
        private readonly string _jwtSecurityKey;
        private readonly string _jwtValidIssuer;
        private readonly string _jwtValidAudience;
        private readonly int _jwtExpiryInMinutes;
        private readonly int _refreshTokenExpiryDays = 30;
        private readonly string _googleClientId;

        public AuthService(IAuthRepository userRepository, IConfiguration configuration, IDistributedCache distributedCache, IEmailService emailService)
        {
            _userRepository = userRepository;
            _distributedCache = distributedCache;
            _emailService = emailService;

            var jwtSettings = configuration.GetSection("JwtSettings");
            _jwtSecurityKey = jwtSettings["SecurityKey"] ?? throw new ConfigurationMissingException("JwtSettings:SecurityKey not configured");
            _jwtValidIssuer = jwtSettings["ValidIssuer"] ?? throw new ConfigurationMissingException("JwtSettings:ValidIssuer not configured");
            _jwtValidAudience = jwtSettings["ValidAudience"] ?? throw new ConfigurationMissingException("JwtSettings:ValidAudience not configured");
            _jwtExpiryInMinutes = int.TryParse(jwtSettings["ExpiryInMinutes"], out var expiry) ? expiry : 1440;

            var googleAuth = configuration.GetSection("Authentication:Google");
            _googleClientId = googleAuth["ClientId"] ?? throw new ConfigurationMissingException("Authentication:Google:ClientId not configured");
        }

        // Rate limiting helper
        private bool IsLoginThrottled(string key)
        {
            var bytes = _distributedCache.Get(key);
            if (bytes == null) return false;
            if (!int.TryParse(System.Text.Encoding.UTF8.GetString(bytes), out var attempts)) return false;
            return attempts >= 5;
        }

        private void IncrementLoginAttempts(string key)
        {
            var bytes = _distributedCache.Get(key);
            int attempts = 0;
            if (bytes != null)
            {
                if (!int.TryParse(System.Text.Encoding.UTF8.GetString(bytes), out attempts)) attempts = 0;
            }
            attempts++;
            var newBytes = System.Text.Encoding.UTF8.GetBytes(attempts.ToString());
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15)
            };
            _distributedCache.Set(key, newBytes, options);
        }

        public async Task<LoginResponseDto> RegisterAsync(RegisterRequestDto request)
        {
            var existingUser = await _userRepository.UserExistsAsync(request.Email, request.Username);
            if (existingUser)
            {
                throw new UserAlreadyExistsException("A user with the same username or email already exists.");
            }

            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = request.Username,
                Email = request.Email,
                PhoneNumber = request.PhoneNumber,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                Role = UserRole.Player,
                Provider = "Local",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _userRepository.AddUserAsync(user);
            await _userRepository.SaveChangesAsync();

            // Create email verification token
            var emailToken = Guid.NewGuid().ToString();
            user.EmailVerificationToken = emailToken;
            user.EmailVerificationTokenExpiresAt = DateTime.UtcNow.AddHours(24);
            user.LastLoginAt = DateTime.UtcNow;
            await _userRepository.SaveChangesAsync();

            // Generate tokens for automatic login
            return new LoginResponseDto
            {
                Token = GenerateJwtToken(user),
                RefreshToken = await CreateAndSaveRefreshTokenAsync(user.Id)
            };
        }

        public async Task<LoginResponseDto> LoginAsync(LoginRequestDto request)
        {
            var throttleKey = $"login:{request.UsernameOrEmail}";
            if (IsLoginThrottled(throttleKey))
            {
                throw new TooManyLoginAttemptsException();
            }

            var user = await _userRepository.GetByUsernameOrEmailAsync(request.UsernameOrEmail);
            if (user == null || string.IsNullOrWhiteSpace(user.PasswordHash))
            {
                IncrementLoginAttempts(throttleKey);
                throw new InvalidCredentialsException("Invalid username/email or password.");
            }

            if (user.IsBlocked)
            {
                throw new UserBlockedException(user.BlockReason ?? "User is blocked.");
            }

            if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            {
                IncrementLoginAttempts(throttleKey);
                throw new InvalidCredentialsException("Invalid username/email or password.");
            }

            user.LastLoginAt = DateTime.UtcNow;
            user.UpdatedAt = DateTime.UtcNow;
            await _userRepository.SaveChangesAsync();

            return new LoginResponseDto
            {
                Token = GenerateJwtToken(user),
                RefreshToken = await CreateAndSaveRefreshTokenAsync(user.Id)
            };
        }

        public async Task<LoginResponseDto> GoogleLoginAsync(GoogleAuthRequestDto request)
        {
            try
            {
                var payload = await GoogleJsonWebSignature.ValidateAsync(request.IdToken, new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { _googleClientId }
                });

                if (string.IsNullOrWhiteSpace(payload.Email))
                {
                    throw new InvalidTokenException("Google token missing email.");
                }

                // Check by provider id first
                var user = await _userRepository.GetByProviderAsync("Google", payload.Subject);

                // If not found, check by email to link accounts
                if (user == null)
                {
                    user = await _userRepository.GetByEmailAsync(payload.Email);
                }

                if (user == null)
                {
                    user = new User
                    {
                        Id = Guid.NewGuid(),
                        Username = !string.IsNullOrWhiteSpace(payload.Name) ? payload.Name : (payload.Email?.Split('@').FirstOrDefault() ?? string.Empty),
                        Email = payload.Email,
                        Role = UserRole.Player,
                        Provider = "Google",
                        ProviderId = payload.Subject,
                        IsEmailVerified = true,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    await _userRepository.AddUserAsync(user);
                    await _userRepository.SaveChangesAsync();
                }
                else
                {
                    if (user.IsBlocked)
                    {
                        throw new UserBlockedException(user.BlockReason ?? "User is blocked.");
                    }

                    // If user exists with same email but no provider set, link account
                    if (user.Provider == "Local" && string.IsNullOrWhiteSpace(user.ProviderId))
                    {
                        user.Provider = "Google";
                        user.ProviderId = payload.Subject;
                        user.IsEmailVerified = true;
                        user.UpdatedAt = DateTime.UtcNow;
                        await _userRepository.SaveChangesAsync();
                    }
                }

                user.LastLoginAt = DateTime.UtcNow;
                user.UpdatedAt = DateTime.UtcNow;
                await _userRepository.SaveChangesAsync();

                return new LoginResponseDto
                {
                    Token = GenerateJwtToken(user),
                    RefreshToken = await CreateAndSaveRefreshTokenAsync(user.Id)
                };
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new GoogleTokenValidationException("Failed to validate Google token.", ex);
            }
        }

        public async Task<RefreshTokenResponseDto> ExchangeRefreshTokenAsync(RefreshTokenRequestDto request)
        {
            var rt = await _userRepository.GetActiveRefreshTokenAsync(request.RefreshToken);
            if (rt == null || rt.ExpiresAt < DateTime.UtcNow)
            {
                throw new RefreshTokenExpiredException("Invalid or expired refresh token.");
            }

            var user = await _userRepository.GetByIdAsync(rt.UserId);
            if (user == null)
            {
                throw new UserNotFoundException("User not found for refresh token.");
            }

            if (user.IsBlocked)
            {
                throw new UserBlockedException(user.BlockReason ?? "User is blocked.");
            }

            // Optionally revoke old refresh token and issue a new one
            rt.IsRevoked = true;
            rt.RevokedAt = DateTime.UtcNow;
            await _userRepository.SaveChangesAsync();

            var newAccessToken = GenerateJwtToken(user);
            var newRefreshToken = await CreateAndSaveRefreshTokenAsync(user.Id);

            return new RefreshTokenResponseDto
            {
                Token = newAccessToken,
                RefreshToken = newRefreshToken
            };
        }

        public async Task RevokeRefreshTokenAsync(string refreshToken)
        {
            var rt = await _userRepository.GetActiveRefreshTokenAsync(refreshToken);
            if (rt == null) return;

            rt.IsRevoked = true;
            rt.RevokedAt = DateTime.UtcNow;

            // Optionally blacklist the access token string too (caller should provide that if needed)
            await _userRepository.SaveChangesAsync();
        }

        public async Task<string> SendEmailVerificationAsync(SendEmailVerificationRequestDto request)
        {
            var user = await _userRepository.GetByEmailAsync(request.Email);
            if (user == null) throw new UserNotFoundException();
            if (user.IsBlocked) throw new UserBlockedException(user.BlockReason ?? "User is blocked.");

            var token = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            user.EmailVerificationToken = token;
            user.EmailVerificationTokenExpiresAt = DateTime.UtcNow.AddMinutes(5);
            user.UpdatedAt = DateTime.UtcNow;
            await _userRepository.SaveChangesAsync();

            var subject = "BoardVerse email verification";
            var body = $"Your BoardVerse verification code is: {token}\n\nThis code expires in 5 minutes.";
            await _emailService.SendEmailAsync(user.Email, subject, body, false);

            return "Verification email sent.";
        }

        public async Task VerifyEmailAsync(VerifyEmailRequestDto request)
        {
            var user = await _userRepository.GetByEmailVerificationTokenAsync(request.Token);
            if (user == null) throw new InvalidTokenException("Invalid verification token.");
            if (user.IsBlocked) throw new UserBlockedException(user.BlockReason ?? "User is blocked.");

            if (user.EmailVerificationTokenExpiresAt < DateTime.UtcNow) throw new VerificationTokenExpiredException();

            user.IsEmailVerified = true;
            user.EmailVerificationToken = null;
            user.EmailVerificationTokenExpiresAt = null;
            user.UpdatedAt = DateTime.UtcNow;
            await _userRepository.SaveChangesAsync();
        }

        public async Task<string> RequestPasswordResetAsync(RequestPasswordResetDto request)
        {
            var user = await _userRepository.GetByEmailAsync(request.Email);
            if (user == null) throw new UserNotFoundException();
            if (!user.IsEmailVerified) throw new EmailVerificationRequiredException();
            if (user.IsBlocked) throw new UserBlockedException(user.BlockReason ?? "User is blocked.");

            var token = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            user.PasswordResetToken = token;
            user.PasswordResetTokenExpiresAt = DateTime.UtcNow.AddMinutes(5);
            user.UpdatedAt = DateTime.UtcNow;
            await _userRepository.SaveChangesAsync();

            var subject = "BoardVerse password reset";
            var body = $"Your BoardVerse password reset code is: {token}\n\nThis code expires in 5 minutes.";
            await _emailService.SendEmailAsync(user.Email, subject, body, false);

            return "Password reset email sent.";
        }

        public async Task ResetPasswordAsync(ResetPasswordDto request)
        {
            var user = await _userRepository.GetByPasswordResetTokenAsync(request.Token);
            if (user == null) throw new InvalidTokenException("Invalid password reset token.");
            if (user.IsBlocked) throw new UserBlockedException(user.BlockReason ?? "User is blocked.");

            if (user.PasswordResetTokenExpiresAt < DateTime.UtcNow) throw new PasswordResetTokenExpiredException();

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
            user.PasswordResetToken = null;
            user.PasswordResetTokenExpiresAt = null;
            user.UpdatedAt = DateTime.UtcNow;
            await _userRepository.SaveChangesAsync();
        }

        public async Task ChangePasswordAsync(Guid userId, ChangePasswordDto request)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null) throw new UserNotFoundException();
            if (user.IsBlocked) throw new UserBlockedException(user.BlockReason ?? "User is blocked.");
            if (string.IsNullOrWhiteSpace(user.PasswordHash)) throw new BadRequestException("This account does not have a local password.");

            if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
            {
                throw new InvalidCredentialsException("Current password is incorrect.");
            }

            if (BCrypt.Net.BCrypt.Verify(request.NewPassword, user.PasswordHash))
            {
                throw new BadRequestException("New password must be different from the current password.");
            }

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
            user.UpdatedAt = DateTime.UtcNow;
            await _userRepository.SaveChangesAsync();
        }

        public async Task<LoginResponseDto> LinkGoogleAccountAsync(LinkGoogleRequestDto request)
        {
            try
            {
                var payload = await GoogleJsonWebSignature.ValidateAsync(request.IdToken, new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { _googleClientId }
                });

                if (string.IsNullOrWhiteSpace(payload.Email))
                {
                    throw new InvalidTokenException("Google token missing email.");
                }

                // Find existing user by email
                var user = await _userRepository.GetByEmailAsync(payload.Email);
                if (user == null) throw new UserNotFoundException("No local account found to link.");
                if (user.IsBlocked) throw new UserBlockedException(user.BlockReason ?? "User is blocked.");

                user.Provider = "Google";
                user.ProviderId = payload.Subject;
                user.IsEmailVerified = true;
                user.UpdatedAt = DateTime.UtcNow;
                await _userRepository.SaveChangesAsync();

                return new LoginResponseDto
                {
                    Token = GenerateJwtToken(user),
                    RefreshToken = await CreateAndSaveRefreshTokenAsync(user.Id)
                };
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new GoogleTokenValidationException("Failed to validate Google token.", ex);
            }
        }

        // Helper methods
        private string GenerateJwtToken(User user)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSecurityKey));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Name, user.Username),
                new(ClaimTypes.Email, user.Email),
                new(ClaimTypes.Role, user.Role.ToString()),
                new("provider", user.Provider)
            };

            if (!string.IsNullOrWhiteSpace(user.ProviderId))
            {
                claims.Add(new Claim("provider_id", user.ProviderId));
            }

            var token = new JwtSecurityToken(
                issuer: _jwtValidIssuer,
                audience: _jwtValidAudience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(_jwtExpiryInMinutes),
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private async Task<string> CreateAndSaveRefreshTokenAsync(Guid userId)
        {
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            var refreshToken = new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Token = token,
                ExpiresAt = DateTime.UtcNow.AddDays(_refreshTokenExpiryDays),
                IsRevoked = false,
                CreatedAt = DateTime.UtcNow
            };

            await _userRepository.AddRefreshTokenAsync(refreshToken);
            await _userRepository.SaveChangesAsync();
            return token;
        }
    }
}
