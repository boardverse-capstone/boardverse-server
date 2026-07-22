using BoardVerse.Core.Enum;
using BoardVerse.Core.Helpers;

namespace BoardVerse.Tests.Helpers;

public class CafePartnerOperationalStatusHelperTests
{
    [Theory]
    [InlineData(CafePartnerOperationalStatus.Active, true, true)]
    [InlineData(CafePartnerOperationalStatus.Active, false, false)]
    [InlineData(CafePartnerOperationalStatus.Inactive, true, false)]
    [InlineData(CafePartnerOperationalStatus.DataBlank, true, false)]
    [InlineData(CafePartnerOperationalStatus.Banned, true, false)]
    [InlineData(null, true, false)]
    public void IsVisibleOnPlayerApp(CafePartnerOperationalStatus? status, bool isActive, bool expected)
    {
        Assert.Equal(expected, CafePartnerOperationalStatusHelper.IsVisibleOnPlayerApp(status, isActive));
    }

    [Theory]
    [InlineData(CafePartnerOperationalStatus.Inactive, true)]
    [InlineData(CafePartnerOperationalStatus.Banned, true)]
    [InlineData(CafePartnerOperationalStatus.Active, false)]
    [InlineData(CafePartnerOperationalStatus.DataBlank, false)]
    [InlineData(null, false)]
    public void IsTerminal(CafePartnerOperationalStatus? status, bool expected)
    {
        Assert.Equal(expected, CafePartnerOperationalStatusHelper.IsTerminal(status));
    }

    [Theory]
    [InlineData(CafePartnerOperationalStatus.DataBlank, true)]
    [InlineData(CafePartnerOperationalStatus.Active, true)]
    [InlineData(CafePartnerOperationalStatus.Inactive, true)]
    [InlineData(CafePartnerOperationalStatus.Banned, false)]
    [InlineData(null, false)]
    public void CanManagerEditProfile(CafePartnerOperationalStatus? status, bool expected)
    {
        Assert.Equal(expected, CafePartnerOperationalStatusHelper.CanManagerEditProfile(status));
    }

    [Theory]
    [InlineData(CafePartnerOperationalStatus.DataBlank, true)]
    [InlineData(CafePartnerOperationalStatus.Inactive, false)]
    [InlineData(CafePartnerOperationalStatus.Active, false)]
    public void CanManagerActivate(CafePartnerOperationalStatus? status, bool expected)
    {
        Assert.Equal(expected, CafePartnerOperationalStatusHelper.CanManagerActivate(status));
    }

    [Theory]
    [InlineData(CafePartnerOperationalStatus.Inactive, true)]
    [InlineData(CafePartnerOperationalStatus.Active, false)]
    [InlineData(CafePartnerOperationalStatus.DataBlank, false)]
    public void CanManagerReopen(CafePartnerOperationalStatus? status, bool expected)
    {
        Assert.Equal(expected, CafePartnerOperationalStatusHelper.CanManagerReopen(status));
    }

    [Theory]
    [InlineData(CafePartnerOperationalStatus.Active, true)]
    [InlineData(CafePartnerOperationalStatus.Inactive, false)]
    [InlineData(CafePartnerOperationalStatus.DataBlank, false)]
    public void CanManagerPause(CafePartnerOperationalStatus? status, bool expected)
    {
        Assert.Equal(expected, CafePartnerOperationalStatusHelper.CanManagerPause(status));
    }

    [Theory]
    [InlineData(CafePartnerOperationalStatus.DataBlank, true)]
    [InlineData(CafePartnerOperationalStatus.Active, true)]
    [InlineData(CafePartnerOperationalStatus.Inactive, false)]
    [InlineData(CafePartnerOperationalStatus.Banned, false)]
    public void CanManagerClosePermanently(CafePartnerOperationalStatus? status, bool expected)
    {
        Assert.Equal(expected, CafePartnerOperationalStatusHelper.CanManagerClosePermanently(status));
    }
}