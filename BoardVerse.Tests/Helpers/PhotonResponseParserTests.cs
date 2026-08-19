using BoardVerse.Services.Services.Geocoding;

namespace BoardVerse.Tests.Helpers;

public class PhotonResponseParserTests
{
    [Fact]
    public void Parse_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(PhotonClient.ParsePhoton(null));
        Assert.Null(PhotonClient.ParsePhoton(string.Empty));
        Assert.Null(PhotonClient.ParsePhoton("   "));
    }

    [Fact]
    public void Parse_InvalidJson_ReturnsNull()
    {
        Assert.Null(PhotonClient.ParsePhoton("not json {{{"));
    }

    [Fact]
    public void Parse_FeatureCollectionVnDistrict_ReturnsDistrictCityCountry()
    {
        // Photon trả FeatureCollection khi reverse geocode
        var json = """
        {
          "type": "FeatureCollection",
          "features": [
            {
              "type": "Feature",
              "geometry": { "type": "Point", "coordinates": [106.7008, 10.7769] },
              "properties": {
                "osm_id": 123456,
                "osm_type": "way",
                "name": "1 Đường Nguyễn Huệ",
                "city": "Thành phố Hồ Chí Minh",
                "district": "Quận 1",
                "country": "Việt Nam",
                "countrycode": "vn"
              }
            }
          ]
        }
        """;

        var result = PhotonClient.ParsePhoton(json);

        Assert.NotNull(result);
        Assert.Equal("Quận 1", result!.District);
        Assert.Equal("Thành phố Hồ Chí Minh", result.City);
        Assert.Equal("Việt Nam", result.Country);
        Assert.Equal("Quận 1, Thành phố Hồ Chí Minh, Việt Nam", result.DisplayName);
    }

    [Fact]
    public void Parse_FeatureWithoutDistrict_FallsBackToCityAsDisplayName()
    {
        // Một số vùng Photon chỉ trả `city` không có `district` (vd ngoại tỉnh VN)
        var json = """
        {
          "type": "FeatureCollection",
          "features": [
            {
              "properties": {
                "name": "Hồ Chí Minh",
                "state": "Hồ Chí Minh",
                "country": "Việt Nam"
              }
            }
          ]
        }
        """;

        var result = PhotonClient.ParsePhoton(json);

        Assert.NotNull(result);
        Assert.Null(result!.District);
        // Không có city → fallback sang state
        Assert.Equal("Hồ Chí Minh", result.City);
        Assert.Equal("Việt Nam", result.Country);
        Assert.Equal("Hồ Chí Minh, Việt Nam", result.DisplayName);
    }

    [Fact]
    public void Parse_EmptyFeatureCollection_ReturnsNull()
    {
        var json = """{"type":"FeatureCollection","features":[]}""";

        Assert.Null(PhotonClient.ParsePhoton(json));
    }

    [Fact]
    public void Parse_InternationalAddress_ReturnsEnglishLabels()
    {
        var json = """
        {
          "type": "FeatureCollection",
          "features": [
            {
              "properties": {
                "name": "Eiffel Tower",
                "city": "Paris",
                "country": "France"
              }
            }
          ]
        }
        """;

        var result = PhotonClient.ParsePhoton(json);

        Assert.NotNull(result);
        Assert.Null(result!.District);
        Assert.Equal("Paris", result.City);
        Assert.Equal("France", result.Country);
    }
}