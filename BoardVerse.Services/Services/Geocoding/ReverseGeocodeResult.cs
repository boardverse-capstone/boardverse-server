namespace BoardVerse.Services.Services.Geocoding
{
    /// <summary>
    /// Kết quả reverse-geocode 1 tọa độ lat/lng.
    /// Mọi field đều nullable vì Nominatim có thể trả thiếu tuỳ khu vực.
    /// </summary>
    public sealed record ReverseGeocodeResult
    {
        /// <summary>Tên Quận/Huyện (administrative level 4-6 tuỳ quốc gia). Ở VN: "Quận 1", "Huyện Bình Chánh"...</summary>
        public string? District { get; init; }

        /// <summary>Tên Thành phố / Tỉnh (administrative level 1-3). Ở VN: "TP. Hồ Chí Minh", "Hà Nội"...</summary>
        public string? City { get; init; }

        /// <summary>Tên Quốc gia. Ở VN: "Việt Nam".</summary>
        public string? Country { get; init; }

        /// <summary>Địa chỉ đầy đủ ghép sẵn (vd: "Quận 1, TP. Hồ Chí Minh, Việt Nam").</summary>
        public string? DisplayName { get; init; }

        /// <summary>OSM type (vd: "residential", "city", "suburb").</summary>
        public string? OsmType { get; init; }

        /// <summary>OSM importance (0-1, càng cao càng quan trọng).</summary>
        public double? Importance { get; init; }
    }
}