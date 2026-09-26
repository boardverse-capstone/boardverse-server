using BoardVerse.Core.DTOs.CafePartner;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Helpers;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using Microsoft.Extensions.Logging;
using Moq;

using System.Threading;
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

        applicationRepo.Setup(r => r.HasOpenApplicationByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        authRepo.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);
        authRepo.Setup(r => r.GetByIdAsync(managerId, It.IsAny<CancellationToken>())).ReturnsAsync(new User
        {
            Id = managerId,
            Email = email,
            Username = "player",
            Role = UserRole.Player,
            Provider = "Local",
            PasswordHash = "hash"
        });
        applicationRepo.Setup(r => r.HasSevereDuplicateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        applicationRepo.Setup(r => r.AddAsync(It.IsAny<CafePartnerApplication>(), It.IsAny<CancellationToken>()))
            .Callback<CafePartnerApplication, CancellationToken>((app, _) => app.Id = Guid.NewGuid());
        applicationRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new CafePartnerApplication
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
        applicationRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

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

        applicationRepo.Setup(r => r.HasOpenApplicationByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        authRepo.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new User
        {
            Id = managerId,
            Email = email,
            Username = "testuser",
            Role = UserRole.Player,
            Provider = "Local",
            PasswordHash = "hash"
        });
        authRepo.Setup(r => r.GetByIdAsync(managerId, It.IsAny<CancellationToken>())).ReturnsAsync(new User
        {
            Id = managerId,
            Email = email,
            Username = "testuser",
            Role = UserRole.Player,
            Provider = "Local",
            PasswordHash = "hash"
        });
        applicationRepo.Setup(r => r.HasSevereDuplicateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        applicationRepo.Setup(r => r.AddAsync(It.IsAny<CafePartnerApplication>(), It.IsAny<CancellationToken>()))
            .Callback<CafePartnerApplication, CancellationToken>((app, _) => app.Id = Guid.NewGuid());
        applicationRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new CafePartnerApplication
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
        applicationRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

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

    [Fact]
    public async Task ManagerSetOperationalStatusAsync_WithBannedStatus_ThrowsBadRequest()
    {
        var managerId = Guid.NewGuid();
        var cafe = BuildCafeForManager(managerId, CafePartnerOperationalStatus.DataBlank);

        var service = BuildServiceForManagerStatus(cafe);

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            service.ManagerSetOperationalStatusAsync(
                managerId,
                new ManagerSetCafeOperationalStatusRequestDto { Status = "BANNED" }));

        Assert.Equal(ApiErrorMessages.CafePartner.ManagerCannotSetBannedStatus, ex.Message);
    }

    [Fact]
    public async Task ManagerSetOperationalStatusAsync_FromBanned_ThrowsInvalidStatus()
    {
        var managerId = Guid.NewGuid();
        var cafe = BuildCafeForManager(managerId, CafePartnerOperationalStatus.Banned);

        var service = BuildServiceForManagerStatus(cafe);

        var ex = await Assert.ThrowsAsync<CafePartnerApplicationInvalidStatusException>(() =>
            service.ManagerSetOperationalStatusAsync(
                managerId,
                new ManagerSetCafeOperationalStatusRequestDto { Status = "ACTIVE" }));

        Assert.Equal(ApiErrorMessages.CafePartner.ManagerCannotSetOperationalStatusFromBanned, ex.Message);
    }

    [Fact]
    public async Task ManagerSetOperationalStatusAsync_ToInactive_WithActiveSessions_Throws()
    {
        var managerId = Guid.NewGuid();
        var cafe = BuildCafeForManager(managerId, CafePartnerOperationalStatus.Active);

        var activeSessionRepo = new Mock<IActiveSessionRepository>();
        activeSessionRepo
            .Setup(r => r.GetActiveSessionsAsync(cafe.Id, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ActiveSession> { new() { Id = Guid.NewGuid() } });

        var service = BuildServiceForManagerStatus(cafe, activeSessionRepo);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.ManagerSetOperationalStatusAsync(
                managerId,
                new ManagerSetCafeOperationalStatusRequestDto { Status = "INACTIVE" }));

        Assert.Equal(ApiErrorMessages.CafePartner.CannotChangeOperationalStatusWithActiveSessions, ex.Message);
    }

    [Fact]
    public async Task ManagerSetOperationalStatusAsync_ToInactive_WithNoSessions_Succeeds()
    {
        var managerId = Guid.NewGuid();
        var cafe = BuildCafeForManager(managerId, CafePartnerOperationalStatus.Active);

        var service = BuildServiceForManagerStatus(cafe);

        var result = await service.ManagerSetOperationalStatusAsync(
            managerId,
            new ManagerSetCafeOperationalStatusRequestDto
            {
                Status = "INACTIVE",
                Reason = "Đóng để sửa chữa"
            });

        Assert.Equal(CafePartnerStatusMapper.ToApiOperationalStatus(CafePartnerOperationalStatus.Inactive), result.OperationalStatus);
        Assert.Equal("Đóng để sửa chữa", result.OperationalStatusReason);
    }

    [Fact]
    public async Task ManagerSetOperationalStatusAsync_NoOp_WhenSameStatus()
    {
        var managerId = Guid.NewGuid();
        var cafe = BuildCafeForManager(managerId, CafePartnerOperationalStatus.Active);

        var service = BuildServiceForManagerStatus(cafe);

        var result = await service.ManagerSetOperationalStatusAsync(
            managerId,
            new ManagerSetCafeOperationalStatusRequestDto { Status = "ACTIVE" });

        Assert.Equal(CafePartnerStatusMapper.ToApiOperationalStatus(CafePartnerOperationalStatus.Active), result.OperationalStatus);
    }

    [Fact]
    public async Task ManagerSetOperationalStatusAsync_InvalidStatus_ThrowsBadRequest()
    {
        var managerId = Guid.NewGuid();
        var cafe = BuildCafeForManager(managerId, CafePartnerOperationalStatus.DataBlank);

        var service = BuildServiceForManagerStatus(cafe);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.ManagerSetOperationalStatusAsync(
                managerId,
                new ManagerSetCafeOperationalStatusRequestDto { Status = "WAT" }));
    }

    private static Cafe BuildCafeForManager(Guid managerId, CafePartnerOperationalStatus status)
    {
        return new Cafe
        {
            Id = Guid.NewGuid(),
            ManagerId = managerId,
            Name = "Unit Test Cafe",
            Address = "123 Test Street",
            PartnerOperationalStatus = status,
            IsActive = status == CafePartnerOperationalStatus.Active,
            PartnerApplication = new CafePartnerApplication
            {
                Id = Guid.NewGuid(),
                CafeName = "Unit Test Cafe",
                RepresentativeEmail = "manager@unittest.local",
                Status = CafePartnerApplicationStatus.Approved
            },
            Tables = new List<CafeTable>(),
            Inventories = new List<CafeGameInventory>()
        };
    }

    private static CafePartnerApplicationService BuildServiceForManagerStatus(
        Cafe cafe,
        Mock<IActiveSessionRepository>? activeSessionRepo = null)
    {
        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetPartnerCafeByManagerIdAsync(cafe.ManagerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cafe);
        cafeRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var applicationRepo = new Mock<ICafePartnerApplicationRepository>();
        var authRepo = new Mock<IAuthRepository>();
        var emailService = new Mock<IEmailService>();
        var logger = new Mock<ILogger<CafePartnerApplicationService>>();

        activeSessionRepo ??= new Mock<IActiveSessionRepository>();
        activeSessionRepo
            .Setup(r => r.GetActiveSessionsAsync(It.IsAny<Guid>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ActiveSession>());

        return new CafePartnerApplicationService(
            applicationRepo.Object,
            authRepo.Object,
            cafeRepo.Object,
            activeSessionRepo.Object,
            emailService.Object,
            logger.Object);
    }
}
