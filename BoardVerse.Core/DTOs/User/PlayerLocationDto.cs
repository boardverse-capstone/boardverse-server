namespace BoardVerse.Core.DTOs.User
{
    /// <summary>
    /// Thông tin vị trí của player cho <c>GET/PUT /api/userprofile/me/location</c>.
    /// Mở rộng từ DTO cũ (chỉ có lat/lng) để trả kèm tên Quận/Thành phố/Quốc gia
    /// sau khi reverse-geocode qua Nominatim (BR-NEW-* UX).
    /// </summary>
    public class PlayerLocationDto
    {
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? Source { get; set; }
        public bool HasLocation { get; set; }

        // ====== Reverse-geocode (Nominatim) ======

        /// <summary>Tên Quận/Huyện (vd: "Quận 1", "Huyện Bình Chánh"). Có thể null nếu Nominatim không có data.</summary>
        public string? District { get; set; }

        /// <summary>Tên Thành phố / Tỉnh (vd: "TP. Hồ Chí Minh", "Hà Nội").</summary>
        public string? City { get; set; }

        /// <summary>Tên Quốc gia (vd: "Việt Nam").</summary>
        public string? Country { get; set; }

        /// <summary>Tên địa điểm ghép sẵn theo format "Quận, Thành phố, Quốc gia" — dùng để hiển thị nhanh trên UI.</summary>
        public string? DisplayName { get; set; }

        /// <summary>
        /// <c>true</c> nếu reverse-geocode đã chạy thành công (kể cả khi District/City null —
        /// ví dụ khu vực không có admin level). <c>false</c> nếu service lỗi / chưa từng lookup.
        /// </summary>
        public bool HasResolvedName { get; set; }
    }
}