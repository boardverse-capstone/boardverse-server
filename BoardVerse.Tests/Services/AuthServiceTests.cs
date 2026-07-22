using BoardVerse.Core.DTOs.Auth.Requests;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Moq;

namespace BoardVerse.Tests.Services;

public class AuthServiceTests
{
    private readonly Mock<IAuthRepository> _userRepo = new();
    private readonly Mock<IDistributedCache> _cache = new();
    private readonly Mock<IEmailService> _emailService = new();

    private AuthService CreateService(IConfiguration? config = null)
    {
        config ??= BuildConfig();
        return new AuthService(_userRepo.Object, config, _cache.Object, _emailService.Object);
    }

    private static IConfiguration BuildConfig()
    {
        var dict = new Dictionary<string, string?>
        {
            ["JwtSettings:SecurityKey"] = "this-is-a-test-jwt-security-key-with-enough-length-1234567890",
            ["JwtSettings:ValidIssuer"] = "BoardVerse.Tests",
            ["JwtSettings:ValidAudience"] = "BoardVerse.Tests.Audience",
            ["JwtSettings:ExpiryInMinutes"] = "60",
            ["Authentication:Google:ClientId"] = "google-test-client-id"
        };
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static User BuildUser(string username = "alice", string email = "alice@boardverse.test", bool withPassword = true)
    {
        return new User
        {
            Id = Guid.NewGuid(),
            Username = username,
            Email = email,
            PasswordHash = withPassword ? BCrypt.Net.BCrypt.HashPassword("ValidPass123!") : null,
            Role = UserRole.Player,
            Provider = "Local",
            IsActive = true,
            AccountStatus = UserAccountStatus.Active
        };
    }

    #region RegisterAsync

    [Fact]
    public async Task RegisterAsync_WhenUserExists_ThrowsUserAlreadyExists()
    {
        _userRepo.Setup(r => r.UserExistsAsync("alice@boardverse.test", "alice")).ReturnsAsync(true);
        var svc = CreateService();

        await Assert.ThrowsAsync<UserAlreadyExistsException>(() => svc.RegisterAsync(new RegisterRequestDto
        {
            Username = "alice",
            Email = "alice@boardverse.test",
            Password = "ValidPass123!",
            DateOfBirth = new DateOnly(2000, 1, 1)
        }));
    }

    [Fact]
    public async Task RegisterAsync_ValidRequest_HashesPasswordAndReturnsTokens()
    {
        _userRepo.Setup(r => r.UserExistsAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);

        User? captured = null;
        _userRepo.Setup(r => r.AddUserAsync(It.IsAny<User>()))
            .Callback<User>(u => captured = u)
            .Returns(Task.CompletedTask);
        _userRepo.Setup(r => r.HasActiveProfileAsync(It.IsAny<Guid>())).ReturnsAsync(false);

        var svc = CreateService();

        var result = await svc.RegisterAsync(new RegisterRequestDto
        {
            Username = "alice",
            Email = "alice@boardverse.test",
            Password = "ValidPass123!",
            DateOfBirth = new DateOnly(2000, 1, 1)
        });

        Assert.NotNull(captured);
        Assert.NotNull(captured!.PasswordHash);
        Assert.False(string.IsNullOrWhiteSpace(captured.PasswordHash));
        Assert.NotEqual("ValidPass123!", captured.PasswordHash);
        Assert.False(string.IsNullOrWhiteSpace(result.Token));
        Assert.False(string.IsNullOrWhiteSpace(result.RefreshToken));
        Assert.NotNull(captured.EmailVerificationToken);
    }

    #endregion

    #region LoginAsync

    [Fact]
    public async Task LoginAsync_WhenUserNotFound_ThrowsInvalidCredentials()
    {
        // bypass throttle by setting Testing env
        var config = BuildConfig();
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        try
        {
            _userRepo.Setup(r => r.GetByUsernameOrEmailAsync("alice")).ReturnsAsync((User?)null);

            var svc = new AuthService(_userRepo.Object, config, _cache.Object, _emailService.Object);

            await Assert.ThrowsAsync<InvalidCredentialsException>(() => svc.LoginAsync(new LoginRequestDto
            {
                UsernameOrEmail = "alice",
                Password = "ValidPass123!"
            }));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
        }
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_ThrowsInvalidCredentials()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        try
        {
            _userRepo.Setup(r => r.GetByUsernameOrEmailAsync("alice")).ReturnsAsync(BuildUser());

            var svc = CreateService();

            await Assert.ThrowsAsync<InvalidCredentialsException>(() => svc.LoginAsync(new LoginRequestDto
            {
                UsernameOrEmail = "alice",
                Password = "WrongPassword1!"
            }));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
        }
    }

    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsToken()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        try
        {
            var user = BuildUser("alice");
            _userRepo.Setup(r => r.GetByUsernameOrEmailAsync("alice")).ReturnsAsync(user);
            _userRepo.Setup(r => r.HasActiveProfileAsync(user.Id)).ReturnsAsync(true);

            var svc = CreateService();

            var result = await svc.LoginAsync(new LoginRequestDto
            {
                UsernameOrEmail = "alice",
                Password = "ValidPass123!"
            });

            Assert.False(string.IsNullOrWhiteSpace(result.Token));
            Assert.False(string.IsNullOrWhiteSpace(result.RefreshToken));
            Assert.True(result.HasProfile);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
        }
    }

    #endregion

    #region ExchangeRefreshTokenAsync

    [Fact]
    public async Task ExchangeRefreshTokenAsync_WhenTokenMissing_ThrowsExpired()
    {
        _userRepo.Setup(r => r.GetActiveRefreshTokenAsync("bad")).ReturnsAsync((RefreshToken?)null);

        var svc = CreateService();

        await Assert.ThrowsAsync<RefreshTokenExpiredException>(() =>
            svc.ExchangeRefreshTokenAsync(new RefreshTokenRequestDto { RefreshToken = "bad" }));
    }

    [Fact]
    public async Task ExchangeRefreshTokenAsync_WhenActiveTokenIsUsable_RotatesCorrectly()
    {
        // Validates success path; entity validation cannot be tested for expired RT
        var userId = Guid.NewGuid();
        var rt = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = "valid",
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
        _userRepo.Setup(r => r.GetActiveRefreshTokenAsync("valid")).ReturnsAsync(rt);
        _userRepo.Setup(r => r.GetByIdAsync(userId)).ReturnsAsync(BuildUser());
        _userRepo.Setup(r => r.HasActiveProfileAsync(userId)).ReturnsAsync(false);

        var svc = CreateService();

        var result = await svc.ExchangeRefreshTokenAsync(new RefreshTokenRequestDto { RefreshToken = "valid" });

        Assert.NotEmpty(result.Token);
    }

    [Fact]
    public async Task ExchangeRefreshTokenAsync_WhenMissing_ThrowsExpired()
    {
        _userRepo.Setup(r => r.GetActiveRefreshTokenAsync("missing")).ReturnsAsync((RefreshToken?)null);

        var svc = CreateService();

        await Assert.ThrowsAsync<RefreshTokenExpiredException>(() =>
            svc.ExchangeRefreshTokenAsync(new RefreshTokenRequestDto { RefreshToken = "missing" }));
    }

    [Fact]
    public async Task ExchangeRefreshTokenAsync_ValidToken_RotatesTokens()
    {
        var userId = Guid.NewGuid();
        var oldRt = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = "valid",
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        };
        _userRepo.Setup(r => r.GetActiveRefreshTokenAsync("valid")).ReturnsAsync(oldRt);
        _userRepo.Setup(r => r.GetByIdAsync(userId)).ReturnsAsync(BuildUser());
        _userRepo.Setup(r => r.HasActiveProfileAsync(userId)).ReturnsAsync(false);

        var svc = CreateService();

        var result = await svc.ExchangeRefreshTokenAsync(new RefreshTokenRequestDto { RefreshToken = "valid" });

        Assert.True(oldRt.IsRevoked);
        Assert.NotNull(result.Token);
        Assert.NotNull(result.RefreshToken);
        Assert.NotEqual("valid", result.RefreshToken);
    }

    #endregion

    #region Revoke / Email / Password

    [Fact]
    public async Task RevokeRefreshTokenAsync_WhenMissing_NoOp()
    {
        _userRepo.Setup(r => r.GetActiveRefreshTokenAsync("missing")).ReturnsAsync((RefreshToken?)null);

        var svc = CreateService();

        await svc.RevokeRefreshTokenAsync("missing"); // does not throw

        _userRepo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task RevokeRefreshTokenAsync_WhenExists_RevokesAndSaves()
    {
        var rt = new RefreshToken { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Token = "x", ExpiresAt = DateTime.UtcNow.AddDays(1) };
        _userRepo.Setup(r => r.GetActiveRefreshTokenAsync("x")).ReturnsAsync(rt);

        var svc = CreateService();

        await svc.RevokeRefreshTokenAsync("x");

        Assert.True(rt.IsRevoked);
        Assert.NotNull(rt.RevokedAt);
        _userRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task SendEmailVerificationAsync_WhenUserNotFound_ThrowsUserNotFound()
    {
        _userRepo.Setup(r => r.GetByEmailAsync("missing@test.com")).ReturnsAsync((User?)null);

        var svc = CreateService();

        await Assert.ThrowsAsync<UserNotFoundException>(() =>
            svc.SendEmailVerificationAsync(new SendEmailVerificationRequestDto { Email = "missing@test.com" }));
    }

    [Fact]
    public async Task SendEmailVerificationAsync_ValidUser_SendsEmailAndSaves()
    {
        var user = BuildUser();
        user.IsEmailVerified = false;
        _userRepo.Setup(r => r.GetByEmailAsync(user.Email)).ReturnsAsync(user);

        var svc = CreateService();

        var result = await svc.SendEmailVerificationAsync(new SendEmailVerificationRequestDto { Email = user.Email });

        Assert.NotNull(user.EmailVerificationToken);
        Assert.NotNull(user.EmailVerificationTokenExpiresAt);
        _emailService.Verify(s => s.SendEmailAsync(user.Email, It.IsAny<string>(), It.IsAny<string>(), false), Times.Once);
        Assert.False(string.IsNullOrWhiteSpace(result));
    }

    [Fact]
    public async Task VerifyEmailAsync_WhenTokenMissing_ThrowsInvalidToken()
    {
        _userRepo.Setup(r => r.GetByEmailVerificationTokenAsync("bad")).ReturnsAsync((User?)null);

        var svc = CreateService();

        await Assert.ThrowsAsync<InvalidTokenException>(() =>
            svc.VerifyEmailAsync(new VerifyEmailRequestDto { Token = "bad" }));
    }

    [Fact]
    public async Task VerifyEmailAsync_WhenExpired_ThrowsExpired()
    {
        var user = BuildUser();
        user.EmailVerificationToken = "123456";
        user.EmailVerificationTokenExpiresAt = DateTime.UtcNow.AddMinutes(-5);
        _userRepo.Setup(r => r.GetByEmailVerificationTokenAsync("123456")).ReturnsAsync(user);

        var svc = CreateService();

        await Assert.ThrowsAsync<VerificationTokenExpiredException>(() =>
            svc.VerifyEmailAsync(new VerifyEmailRequestDto { Token = "123456" }));
    }

    [Fact]
    public async Task VerifyEmailAsync_ValidToken_VerifiesAndSaves()
    {
        var user = BuildUser();
        user.EmailVerificationToken = "123456";
        user.EmailVerificationTokenExpiresAt = DateTime.UtcNow.AddMinutes(5);
        _userRepo.Setup(r => r.GetByEmailVerificationTokenAsync("123456")).ReturnsAsync(user);

        var svc = CreateService();

        await svc.VerifyEmailAsync(new VerifyEmailRequestDto { Token = "123456" });

        Assert.True(user.IsEmailVerified);
        Assert.Null(user.EmailVerificationToken);
    }

    [Fact]
    public async Task RequestPasswordResetAsync_WhenUserMissing_Throws()
    {
        _userRepo.Setup(r => r.GetByEmailAsync("none@test.com")).ReturnsAsync((User?)null);

        var svc = CreateService();

        await Assert.ThrowsAsync<UserNotFoundException>(() =>
            svc.RequestPasswordResetAsync(new RequestPasswordResetDto { Email = "none@test.com" }));
    }

    [Fact]
    public async Task RequestPasswordResetAsync_WhenEmailNotVerified_Throws()
    {
        var user = BuildUser();
        user.IsEmailVerified = false;
        _userRepo.Setup(r => r.GetByEmailAsync(user.Email)).ReturnsAsync(user);

        var svc = CreateService();

        await Assert.ThrowsAsync<EmailVerificationRequiredException>(() =>
            svc.RequestPasswordResetAsync(new RequestPasswordResetDto { Email = user.Email }));
    }

    [Fact]
    public async Task RequestPasswordResetAsync_Valid_SendsEmailAndSaves()
    {
        var user = BuildUser();
        user.IsEmailVerified = true;
        _userRepo.Setup(r => r.GetByEmailAsync(user.Email)).ReturnsAsync(user);

        var svc = CreateService();

        await svc.RequestPasswordResetAsync(new RequestPasswordResetDto { Email = user.Email });

        Assert.NotNull(user.PasswordResetToken);
        _emailService.Verify(s => s.SendEmailAsync(user.Email, It.IsAny<string>(), It.IsAny<string>(), false), Times.Once);
    }

    [Fact]
    public async Task ResetPasswordAsync_WhenTokenInvalid_ThrowsInvalidToken()
    {
        _userRepo.Setup(r => r.GetByPasswordResetTokenAsync("bad")).ReturnsAsync((User?)null);

        var svc = CreateService();

        await Assert.ThrowsAsync<InvalidTokenException>(() =>
            svc.ResetPasswordAsync(new ResetPasswordDto { Token = "bad", NewPassword = "NewValid1!" }));
    }

    [Fact]
    public async Task ResetPasswordAsync_ValidRequest_HashesNewPassword()
    {
        var user = BuildUser();
        user.PasswordResetToken = "654321";
        user.PasswordResetTokenExpiresAt = DateTime.UtcNow.AddMinutes(10);
        _userRepo.Setup(r => r.GetByPasswordResetTokenAsync("654321")).ReturnsAsync(user);

        var svc = CreateService();

        await svc.ResetPasswordAsync(new ResetPasswordDto { Token = "654321", NewPassword = "NewValid1!" });

        Assert.NotEqual("NewValid1!", user.PasswordHash);
        Assert.True(BCrypt.Net.BCrypt.Verify("NewValid1!", user.PasswordHash!));
        Assert.Null(user.PasswordResetToken);
    }

    #endregion

    #region ChangePasswordAsync

    [Fact]
    public async Task ChangePasswordAsync_WhenUserMissing_Throws()
    {
        _userRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((User?)null);

        var svc = CreateService();

        await Assert.ThrowsAsync<UserNotFoundException>(() =>
            svc.ChangePasswordAsync(Guid.NewGuid(), new ChangePasswordDto
            {
                CurrentPassword = "ValidPass123!",
                NewPassword = "NewValidPass1!",
                ConfirmNewPassword = "NewValidPass1!"
            }));
    }

    [Fact]
    public async Task ChangePasswordAsync_OAuthUserWithoutPassword_ThrowsBadRequest()
    {
        var user = BuildUser(withPassword: false);
        _userRepo.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);

        var svc = CreateService();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            svc.ChangePasswordAsync(user.Id, new ChangePasswordDto
            {
                CurrentPassword = "ValidPass123!",
                NewPassword = "NewValidPass1!",
                ConfirmNewPassword = "NewValidPass1!"
            }));
    }

    [Fact]
    public async Task ChangePasswordAsync_CurrentPasswordWrong_Throws()
    {
        var user = BuildUser();
        _userRepo.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);

        var svc = CreateService();

        await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            svc.ChangePasswordAsync(user.Id, new ChangePasswordDto
            {
                CurrentPassword = "WrongPass123!",
                NewPassword = "NewValidPass1!",
                ConfirmNewPassword = "NewValidPass1!"
            }));
    }

    [Fact]
    public async Task ChangePasswordAsync_NewSameAsCurrent_Throws()
    {
        var user = BuildUser();
        _userRepo.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);

        var svc = CreateService();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            svc.ChangePasswordAsync(user.Id, new ChangePasswordDto
            {
                CurrentPassword = "ValidPass123!",
                NewPassword = "ValidPass123!",
                ConfirmNewPassword = "ValidPass123!"
            }));
    }

    [Fact]
    public async Task ChangePasswordAsync_Valid_UpdatesHash()
    {
        var user = BuildUser();
        var oldHash = user.PasswordHash;
        _userRepo.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);

        var svc = CreateService();

        await svc.ChangePasswordAsync(user.Id, new ChangePasswordDto
        {
            CurrentPassword = "ValidPass123!",
            NewPassword = "NewValidPass1!",
            ConfirmNewPassword = "NewValidPass1!"
        });

        Assert.NotEqual(oldHash, user.PasswordHash);
        Assert.True(BCrypt.Net.BCrypt.Verify("NewValidPass1!", user.PasswordHash!));
    }

    #endregion
}