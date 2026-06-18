using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Helpers;

namespace BoardVerse.Tests.Helpers;

public class GeoLocationHelperTests
{
    [Theory]
    [InlineData(-91, 0)]
    [InlineData(91, 0)]
    [InlineData(0, -181)]
    [InlineData(0, 181)]
    public void ValidateCoordinates_OutOfRange_Throws(double lat, double lng)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GeoLocationHelper.ValidateCoordinates(lat, lng));
    }

    [Fact]
    public void ApplyCoordinates_Cafe_SetsLatLngAndPoint()
    {
        var cafe = new Cafe { Id = Guid.NewGuid(), Name = "Test", Address = "Addr" };

        GeoLocationHelper.ApplyCoordinates(cafe, 10.776889, 106.700806);

        Assert.Equal(10.776889, cafe.Latitude);
        Assert.Equal(106.700806, cafe.Longitude);
        Assert.NotNull(cafe.Location);
        Assert.Equal(4326, cafe.Location!.SRID);
    }

    [Fact]
    public void ApplyLastKnownLocation_Profile_UpdatesFields()
    {
        var profile = new UserProfile { UserId = Guid.NewGuid() };

        GeoLocationHelper.ApplyLastKnownLocation(profile, 10.0, 106.0, PlayerLocationSource.Gps);

        Assert.True(GeoLocationHelper.HasLastKnownLocation(profile));
        Assert.Equal(PlayerLocationSource.Gps, profile.LastLocationSource);
        Assert.NotNull(profile.LastLocationUpdatedAt);
    }

    [Fact]
    public void ClearLastKnownLocation_RemovesSavedCoordinates()
    {
        var profile = new UserProfile { UserId = Guid.NewGuid() };
        GeoLocationHelper.ApplyLastKnownLocation(profile, 10.0, 106.0, PlayerLocationSource.Manual);

        GeoLocationHelper.ClearLastKnownLocation(profile);

        Assert.False(GeoLocationHelper.HasLastKnownLocation(profile));
    }
}
