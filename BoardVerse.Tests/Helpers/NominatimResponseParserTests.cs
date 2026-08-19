using BoardVerse.Services.Services.Geocoding;

namespace BoardVerse.Tests.Helpers;

public class NominatimResponseParserTests
{
    [Fact]
    public void Parse_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(NominatimResponseParser.Parse(null));
        Assert.Null(NominatimResponseParser.Parse(string.Empty));
        Assert.Null(NominatimResponseParser.Parse("   "));
    }

    [Fact]
    public void Parse_InvalidJson_ReturnsNull()
    {
        Assert.Null(NominatimResponseParser.Parse("not json {{{"));
    }

    [Fact]
    public void Parse_MissingLatLon_ReturnsNull()
    {
        var json = """{"display_name":"somewhere","address":{"city":"Hanoi"}}""";
        Assert.Null(NominatimResponseParser.Parse(json));
    }

    [Fact]
    public void Parse_FullVnAddress_ReturnsDistrictCityCountry()
    {
        // Sample từ Nominatim trả về cho Quận 1, TP.HCM
        var json = """
        {
          "place_id": 123,
          "licence": "Data © OpenStreetMap contributors, ODbL 1.0.",
          "osm_type": "way",
          "osm_id": 456,
          "category": "place",
          "type": "house",
          "lat": "10.776889",
          "lon": "106.700806",
          "display_name": "1, Đường Nguyễn Huệ, Phường Bến Nghé, Quận 1, Thành phố Hồ Chí Minh, Việt Nam",
          "importance": 0.5,
          "address": {
            "house_number": "1",
            "road": "Đường Nguyễn Huệ",
            "suburb": "Phường Bến Nghé",
            "city_district": "Quận 1",
            "city": "Thành phố Hồ Chí Minh",
            "country": "Việt Nam",
            "country_code": "vn"
          }
        }
        """;

        var result = NominatimResponseParser.Parse(json);

        Assert.NotNull(result);
        Assert.Equal("Quận 1", result!.District);
        Assert.Equal("Thành phố Hồ Chí Minh", result.City);
        Assert.Equal("Việt Nam", result.Country);
        Assert.Equal("Quận 1, Thành phố Hồ Chí Minh, Việt Nam", result.DisplayName);
        Assert.Equal("place", result.OsmType);
        Assert.Equal(0.5, result.Importance);
    }

    [Fact]
    public void Parse_HanoiAddress_UsesCountyAsDistrict()
    {
        // Khu vực không có city_district → fallback sang county
        var json = """
        {
          "lat": "21.028511",
          "lon": "105.804817",
          "display_name": "Hoàn Kiếm, Hà Nội, Việt Nam",
          "address": {
            "county": "Hoàn Kiếm",
            "city": "Hà Nội",
            "country": "Việt Nam"
          }
        }
        """;

        var result = NominatimResponseParser.Parse(json);

        Assert.NotNull(result);
        Assert.Equal("Hoàn Kiếm", result!.District);
        Assert.Equal("Hà Nội", result.City);
    }

    [Fact]
    public void Parse_MissingAddress_ReturnsLatLonOnly()
    {
        var json = """{"lat":"10.0","lon":"106.0","display_name":"somewhere"}""";

        var result = NominatimResponseParser.Parse(json);

        Assert.NotNull(result);
        Assert.Null(result!.District);
        Assert.Null(result.City);
        Assert.Null(result.Country);
        // DisplayName fallback sang raw display_name khi không có address block
        Assert.Equal("somewhere", result.DisplayName);
        // Không có "category" trong JSON → OsmType phải null
        Assert.Null(result.OsmType);
    }
}