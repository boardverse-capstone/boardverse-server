using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho CurrentUserService — trích xuất userId từ JWT claims.
/// </summary>
public class CurrentUserServiceTests
{
    private static IHttpContextAccessor BuildAccessor(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, authenticationType: "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        var ctx = new DefaultHttpContext { User = principal };
        var accessor = new HttpContextAccessor { HttpContext = ctx };
        return accessor;
    }

    [Fact]
    public void GetCurrentUserId_NoHttpContext_ReturnsNull()
    {
        var accessor = new HttpContextAccessor { HttpContext = null };
        var sut = new CurrentUserService(accessor);

        Assert.Null(sut.GetCurrentUserId());
    }

    [Fact]
    public void GetCurrentUserId_NoClaims_ReturnsNull()
    {
        var accessor = BuildAccessor();
        var sut = new CurrentUserService(accessor);

        Assert.Null(sut.GetCurrentUserId());
    }

    [Fact]
    public void GetCurrentUserId_WithUidClaim_ReturnsUserId()
    {
        var userId = Guid.NewGuid();
        var accessor = BuildAccessor(new Claim("uid", userId.ToString()));
        var sut = new CurrentUserService(accessor);

        Assert.Equal(userId, sut.GetCurrentUserId());
    }

    [Fact]
    public void GetCurrentUserId_WithNameIdentifierClaim_ReturnsUserId()
    {
        var userId = Guid.NewGuid();
        var accessor = BuildAccessor(new Claim(ClaimTypes.NameIdentifier, userId.ToString()));
        var sut = new CurrentUserService(accessor);

        Assert.Equal(userId, sut.GetCurrentUserId());
    }

    [Fact]
    public void GetCurrentUserId_UidClaimTakesPriorityOverNameIdentifier()
    {
        var uidUserId = Guid.NewGuid();
        var niUserId = Guid.NewGuid();
        var accessor = BuildAccessor(
            new Claim("uid", uidUserId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, niUserId.ToString()));
        var sut = new CurrentUserService(accessor);

        Assert.Equal(uidUserId, sut.GetCurrentUserId());
    }

    [Fact]
    public void GetCurrentUserId_InvalidGuidClaim_ReturnsNull()
    {
        var accessor = BuildAccessor(new Claim("uid", "not-a-guid"));
        var sut = new CurrentUserService(accessor);

        Assert.Null(sut.GetCurrentUserId());
    }

    [Fact]
    public void GetCurrentUserId_EmptyGuidClaim_ReturnsEmptyGuid()
    {
        var accessor = BuildAccessor(new Claim("uid", Guid.Empty.ToString()));
        var sut = new CurrentUserService(accessor);

        Assert.Equal(Guid.Empty, sut.GetCurrentUserId());
    }
}
