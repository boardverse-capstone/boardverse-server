using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Messages;
using NetTopologySuite.Geometries;

namespace BoardVerse.Core.Helpers
{
    public static class GeoLocationHelper
    {
        public const double MinLatitude = -90;
        public const double MaxLatitude = 90;
        public const double MinLongitude = -180;
        public const double MaxLongitude = 180;

        public const double DefaultNearbyRadiusKm = 15;
        public const double MinNearbyRadiusKm = 0.1;
        public const double MaxNearbyRadiusKm = 50;

        public static void ValidateCoordinates(double latitude, double longitude)
        {
            if (latitude is < MinLatitude or > MaxLatitude)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(latitude),
                    ApiErrorMessages.Validation.LatitudeRange);
            }

            if (longitude is < MinLongitude or > MaxLongitude)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(longitude),
                    ApiErrorMessages.Validation.LongitudeRange);
            }
        }

        public static Point ToPoint(double latitude, double longitude)
        {
            ValidateCoordinates(latitude, longitude);
            return new Point(longitude, latitude) { SRID = 4326 };
        }

        public static void ApplyCoordinates(Cafe cafe, double latitude, double longitude)
        {
            ValidateCoordinates(latitude, longitude);
            cafe.Latitude = latitude;
            cafe.Longitude = longitude;
            cafe.Location = ToPoint(latitude, longitude);
        }

        public static void ApplyCoordinates(CafePartnerApplication application, double latitude, double longitude)
        {
            ValidateCoordinates(latitude, longitude);
            application.Latitude = latitude;
            application.Longitude = longitude;
        }

        public static bool HasCoordinates(Cafe cafe) =>
            cafe.Latitude.HasValue && cafe.Longitude.HasValue && cafe.Location != null;

        public static void ApplyLastKnownLocation(
            UserProfile profile,
            double latitude,
            double longitude,
            PlayerLocationSource source)
        {
            ValidateCoordinates(latitude, longitude);
            profile.LastKnownLatitude = latitude;
            profile.LastKnownLongitude = longitude;
            profile.LastLocationUpdatedAt = DateTime.UtcNow;
            profile.LastLocationSource = source;
        }

        public static void ClearLastKnownLocation(UserProfile profile)
        {
            profile.LastKnownLatitude = null;
            profile.LastKnownLongitude = null;
            profile.LastLocationUpdatedAt = null;
            profile.LastLocationSource = null;
        }

        public static bool HasLastKnownLocation(UserProfile profile) =>
            profile.LastKnownLatitude.HasValue && profile.LastKnownLongitude.HasValue;

        /// <summary>
        /// Tính khoảng cách giữa 2 điểm (lat1, lon1) và (lat2, lon2) bằng Haversine formula.
        /// LOBBY-P0-FIX-9: Clamp a ∈ [0, 1] để tránh NaN ở vĩ độ cao.
        /// </summary>
        public static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
        {
            const double earthRadiusKm = 6371.0;
            var lat1Rad = lat1 * Math.PI / 180.0;
            var lat2Rad = lat2 * Math.PI / 180.0;
            var dLat = (lat2 - lat1) * Math.PI / 180.0;
            var dLon = (lon2 - lon1) * Math.PI / 180.0;

            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(lat1Rad) * Math.Cos(lat2Rad)
                   * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            a = Math.Min(1.0, Math.Max(0.0, a));
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return earthRadiusKm * c;
        }
    }
}
