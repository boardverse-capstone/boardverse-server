using System.Text.Json;

namespace BoardVerse.Services.Services.Geocoding
{
    /// <summary>
    /// Parse raw JSON trả về từ Nominatim `/reverse` (format=jsonv2) thành <see cref="ReverseGeocodeResult"/>.
    /// Public static để dễ test (xem <c>BoardVerse.Tests/Helpers/NominatimResponseParserTests.cs</c>).
    /// </summary>
    public static class NominatimResponseParser
    {
        /// <summary>
        /// Parse JSON trả về từ Nominatim.
        /// Trả <c>null</c> nếu JSON không hợp lệ hoặc thiếu field bắt buộc (lat/lon).
        /// </summary>
        public static ReverseGeocodeResult? Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                var lat = TryReadDouble(root, "lat");
                var lon = TryReadDouble(root, "lon");
                if (!lat.HasValue || !lon.HasValue)
                {
                    return null;
                }

                var osmType = TryReadString(root, "category");
                var importance = TryReadDouble(root, "importance");

                AddressBlock? address = null;
                if (root.TryGetProperty("address", out var addressElement) && addressElement.ValueKind == JsonValueKind.Object)
                {
                    address = ParseAddress(addressElement);
                }

                var displayName = TryReadString(root, "display_name");

                var district = address?.CityDistrict
                               ?? address?.County
                               ?? address?.StateDistrict
                               ?? address?.Municipality
                               ?? address?.Suburb;

                var city = address?.City
                           ?? address?.Town
                           ?? address?.Village
                           ?? address?.State;

                var country = address?.Country;

                var computedDisplayName = BuildDisplayName(district, city, country) ?? displayName;

                return new ReverseGeocodeResult
                {
                    District = district,
                    City = city,
                    Country = country,
                    DisplayName = computedDisplayName,
                    OsmType = osmType,
                    Importance = importance
                };
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static AddressBlock? ParseAddress(JsonElement element)
        {
            string? Get(string key) =>
                element.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
                    ? v.GetString()
                    : null;

            return new AddressBlock
            {
                Suburb = Get("suburb"),
                CityDistrict = Get("city_district"),
                County = Get("county"),
                StateDistrict = Get("state_district"),
                Municipality = Get("municipality"),
                City = Get("city"),
                Town = Get("town"),
                Village = Get("village"),
                State = Get("state"),
                Country = Get("country")
            };
        }

        private static string? BuildDisplayName(string? district, string? city, string? country)
        {
            var parts = new[] { district, city, country }
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p!.Trim())
                .ToList();
            return parts.Count == 0 ? null : string.Join(", ", parts);
        }

        private static string? TryReadString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.String)
            {
                return null;
            }
            return prop.GetString();
        }

        private static double? TryReadDouble(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var prop))
            {
                return null;
            }
            return prop.ValueKind switch
            {
                JsonValueKind.Number => prop.GetDouble(),
                JsonValueKind.String => double.TryParse(prop.GetString(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var n) ? n : null,
                _ => null
            };
        }

        private sealed class AddressBlock
        {
            public string? Suburb { get; init; }
            public string? CityDistrict { get; init; }
            public string? County { get; init; }
            public string? StateDistrict { get; init; }
            public string? Municipality { get; init; }
            public string? City { get; init; }
            public string? Town { get; init; }
            public string? Village { get; init; }
            public string? State { get; init; }
            public string? Country { get; init; }
        }
    }
}