namespace BoardVerse.Services.Services.Geocoding
{
    /// <summary>
    /// HTTP client gọi Nominatim reverse-geocoding API.
    /// Implementation chỉ lo HTTP; cache + parse địa chỉ + mapping sang district/city do <see cref="IPlayerGeocodingService"/>.
    /// </summary>
    public interface IGeocodingClient
    {
        /// <summary>
        /// Gọi Nominatim `/reverse?lat=...&lon=...&format=jsonv2&addressdetails=1&accept-language=...`
        /// và trả raw JSON string (caller sẽ parse).
        /// </summary>
        /// <param name="latitude">Vĩ độ (WGS84).</param>
        /// <param name="longitude">Kinh độ (WGS84).</param>
        /// <param name="cancellationToken">Token huỷ.</param>
        /// <returns>Raw JSON, hoặc <c>null</c> nếu request fail hết retry (caller xử lý fallback).</returns>
        Task<string?> ReverseGeocodeRawAsync(
            double latitude,
            double longitude,
            CancellationToken cancellationToken = default);
    }
}