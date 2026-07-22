using BoardVerse.Core.Enum;
using BoardVerse.Core.Helpers;

namespace BoardVerse.Tests.Helpers;

public class ProfileCompletionRulesTests
{
    [Theory]
    [InlineData(UserRole.Player)]
    public void RequiresProfile_Player_ReturnsTrue(UserRole role)
    {
        Assert.True(ProfileCompletionRules.RequiresProfile(role));
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Manager)]
    [InlineData(UserRole.CafeStaff)]
    public void RequiresProfile_NonPlayer_ReturnsFalse(UserRole role)
    {
        Assert.False(ProfileCompletionRules.RequiresProfile(role));
    }

    [Fact]
    public void ResolveHasProfile_PlayerWithProfile_ReturnsTrue()
    {
        Assert.True(ProfileCompletionRules.ResolveHasProfile(UserRole.Player, true));
    }

    [Fact]
    public void ResolveHasProfile_PlayerWithoutProfile_ReturnsFalse()
    {
        Assert.False(ProfileCompletionRules.ResolveHasProfile(UserRole.Player, false));
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Manager)]
    [InlineData(UserRole.CafeStaff)]
    public void ResolveHasProfile_NonPlayer_AlwaysTrue(UserRole role)
    {
        Assert.True(ProfileCompletionRules.ResolveHasProfile(role, false));
    }
}