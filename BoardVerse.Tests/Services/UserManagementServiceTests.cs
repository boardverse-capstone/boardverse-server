using BoardVerse.Core.DTOs.User;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.Services;
using Moq;

namespace BoardVerse.Tests.Services;

public class UserManagementServiceTests
{
    [Fact]
    public async Task GetAllAsync_InvalidRole_ThrowsBadRequest()
    {
        var repo = new Mock<IUserManagementRepository>();
        var service = new UserManagementService(repo.Object);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.GetAllAsync(new AdminUserQueryDto { Role = "NotARealRole" }));
    }

    [Fact]
    public async Task GetAsync_UserNotFound_ThrowsUserNotFound()
    {
        var repo = new Mock<IUserManagementRepository>();
        repo.Setup(r => r.GetByIdWithProfileAsync(It.IsAny<Guid>())).ReturnsAsync((User?)null);

        var service = new UserManagementService(repo.Object);

        await Assert.ThrowsAsync<UserNotFoundException>(() =>
            service.GetAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task CreateAsync_DuplicateEmail_ThrowsUserAlreadyExists()
    {
        var repo = new Mock<IUserManagementRepository>();
        repo.Setup(r => r.UserExistsAsync("dup@test.dev", "dupuser")).ReturnsAsync(true);

        var service = new UserManagementService(repo.Object);

        await Assert.ThrowsAsync<UserAlreadyExistsException>(() =>
            service.CreateAsync(new AdminCreateUserDto
            {
                Email = "dup@test.dev",
                Username = "dupuser",
                Role = "Player"
            }));
    }

    [Fact]
    public async Task CreateAsync_ValidRequest_CreatesUser()
    {
        var repo = new Mock<IUserManagementRepository>();
        repo.Setup(r => r.UserExistsAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);

        var service = new UserManagementService(repo.Object);
        var result = await service.CreateAsync(new AdminCreateUserDto
        {
            Email = "new@test.dev",
            Username = "newuser",
            Role = "Player",
            Password = "Test@123"
        });

        Assert.Equal("new@test.dev", result.Email);
        Assert.Equal("Player", result.Role);
        repo.Verify(r => r.AddUserAsync(It.IsAny<User>()), Times.Once);
        repo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }
}
