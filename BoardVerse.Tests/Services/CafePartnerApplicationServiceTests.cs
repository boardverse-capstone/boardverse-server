using BoardVerse.Core.DTOs.CafePartner;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace BoardVerse.Tests.Services;

public class CafePartnerApplicationServiceTests
{
    [Fact]
    public async Task SubmitAsync_WithValidRequest_ReturnsApplicationResponse()
    {
        var managerId = Guid.NewGuid();
        var email = "manager@unittest.local";

        var cafeRepo = new Mock<ICafeRepository>();
        var applicationRepo = new Mock<ICafePartnerApplicationRepository>();
        var authRepo = new Mock<IAuthRepository>();
        var activeSessionRepo = new Mock<IActiveSessionRepository>();
        var emailService = new Mock<IEmailService>();
        var logger = new Mock<ILogger<CafePartnerApplicationService>>();

        applicationRepo.Setup(r => r.HasOpenApplicationByEmailAsync(It.IsAny<string>())).ReturnsAsync(false);
        authRepo.Setup(r => r.GetByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);
        authRepo.Setup(r => r.GetByIdAsync(managerId)).ReturnsAsync(new User
        {
            Id = managerId,
            Email = email,
            Username = "player",
            Role = UserRole.Player,
            Provider = "Local",
            PasswordHash = "hash"
        });
        applicationRepo.Setup(r => r.HasSevereDuplicateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>())).ReturnsAsync(false);
        applicationRepo.Setup(r => r.AddAsync(It.IsAny<CafePartnerApplication>()))
            .Callback<CafePartnerApplication>(app => app.Id = Guid.NewGuid());
        applicationRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>()))
            .ReturnsAsync((Guid id) => new CafePartnerApplication
            {
                Id = id,
                CafeName = "Unit Test Cafe",
                Address = "123 Unit Test Street",
                PhoneNumber = "0901234567",
                RepresentativeEmail = email.ToLowerInvariant(),
                BusinessLicense = "LICENSE123",
                BusinessLicenseImageUrl = "https://example.com/license.jpg",
                Status = CafePartnerApplicationStatus.PendingApproval,
                SubmittedByUserId = managerId,
                SubmittedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        applicationRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        var service = new CafePartnerApplicationService(
            applicationRepo.Object,
            authRepo.Object,
            cafeRepo.Object,
            activeSessionRepo.Object,
            emailService.Object,
            logger.Object);

        var request = new SubmitCafePartnerApplicationRequestDto
        {
            CafeName = "Unit Test Cafe",
            Address = "123 Unit Test Street",
            Latitude = 10.0,
            Longitude = 106.0,
            PhoneNumber = "0901234567",
            RepresentativeEmail = email,
            BusinessLicense = "LICENSE123",
            BusinessLicenseImageUrl = "https://example.com/license.jpg"
        };

        var response = await service.SubmitAsync(request, managerId);

        Assert.NotNull(response);
        Assert.Equal("Unit Test Cafe", response.CafeName);
        Assert.Equal("123 Unit Test Street", response.Address);
    }

    [Fact]
    public async Task SubmitAsync_WithEligibleExistingUser_ReturnsApplicationResponse()
    {
        var managerId = Guid.NewGuid();
        var email = "manager@unittest.local";

        var cafeRepo = new Mock<ICafeRepository>();
        var applicationRepo = new Mock<ICafePartnerApplicationRepository>();
        var authRepo = new Mock<IAuthRepository>();
        var activeSessionRepo = new Mock<IActiveSessionRepository>();
        var emailService = new Mock<IEmailService>();
        var logger = new Mock<ILogger<CafePartnerApplicationService>>();

        applicationRepo.Setup(r => r.HasOpenApplicationByEmailAsync(It.IsAny<string>())).ReturnsAsync(false);
        authRepo.Setup(r => r.GetByEmailAsync(It.IsAny<string>())).ReturnsAsync(new User
        {
            Id = managerId,
            Email = email,
            Username = "testuser",
            Role = UserRole.Player,
            Provider = "Local",
            PasswordHash = "hash"
        });
        authRepo.Setup(r => r.GetByIdAsync(managerId)).ReturnsAsync(new User
        {
            Id = managerId,
            Email = email,
            Username = "testuser",
            Role = UserRole.Player,
            Provider = "Local",
            PasswordHash = "hash"
        });
        applicationRepo.Setup(r => r.HasSevereDuplicateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>())).ReturnsAsync(false);
        applicationRepo.Setup(r => r.AddAsync(It.IsAny<CafePartnerApplication>()))
            .Callback<CafePartnerApplication>(app => app.Id = Guid.NewGuid());
        applicationRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>()))
            .ReturnsAsync((Guid id) => new CafePartnerApplication
            {
                Id = id,
                CafeName = "Unit Test Cafe",
                Address = "123 Unit Test Street",
                PhoneNumber = "0901234567",
                RepresentativeEmail = email.ToLowerInvariant(),
                BusinessLicense = "LICENSE123",
                BusinessLicenseImageUrl = "https://example.com/license.jpg",
                Status = CafePartnerApplicationStatus.PendingApproval,
                SubmittedByUserId = managerId,
                SubmittedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        applicationRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        var service = new CafePartnerApplicationService(
            applicationRepo.Object,
            authRepo.Object,
            cafeRepo.Object,
            activeSessionRepo.Object,
            emailService.Object,
            logger.Object);

        var request = new SubmitCafePartnerApplicationRequestDto
        {
            CafeName = "Unit Test Cafe",
            Address = "123 Unit Test Street",
            Latitude = 10.0,
            Longitude = 106.0,
            PhoneNumber = "0901234567",
            RepresentativeEmail = email,
            BusinessLicense = "LICENSE123",
            BusinessLicenseImageUrl = "https://example.com/license.jpg"
        };

        var response = await service.SubmitAsync(request, managerId);

        Assert.NotNull(response);
        Assert.Equal("Unit Test Cafe", response.CafeName);
    }
}
