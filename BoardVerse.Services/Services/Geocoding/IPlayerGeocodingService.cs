using BoardVerse.Core.Settings;

namespace BoardVerse.Services.Services.Geocoding
{
    /// <summary>
    /// Service reverse-geocode tọa độ GPS sang tên Quận/Thành phố.
    /// Tầng abstraction giữa <see cref="IGeocodingClient"/> (HTTP thô) và service gọi (<c>UserProfileService</c>).
    /// Chịu trách nhiệm:
    ///  - Cache kết quả in-memory (IMemoryCache) tránh spam Nominatim.
    ///  - Quantization toạ độ để cache hit ngay cả khi GPS dao động.
    ///  - Fallback <c>null</c> an toàn khi Nominatim lỗi (không throw).
    /// </summary>
    public interface IPlayerGeocodingService
    {
        /// <summary>
        /// Reverse-geocode 1 cặp toạ độ. Trả <c>null</c> nếu Nominatim không khả dụng / không tìm thấy.
        /// </summary>
        Task<ReverseGeocodeResult?> ReverseGeocodeAsync(
            double latitude,
            double longitude,
            CancellationToken cancellationToken = default);
    }
}