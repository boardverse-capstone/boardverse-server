using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using NetTopologySuite.Geometries;

namespace BoardVerse.Core.Helpers
{
    public static class GeoLocationHelper
    {
        public const double MinLatitude = -90;
        public const double MaxLatitude = 90;
        public const double MinLongitude = -180;
        public const double MaxLongitude = 180;

        public const double DefaultNearbyRadiusKm = 5;
        public const double MinNearbyRadiusKm = 0.1;
        public const double MaxNearbyRadiusKm = 50;

        public static void ValidateCoordinates(double latitude, double longitude)
        {
            if (latitude is < MinLatitude or > MaxLatitude)
            {
                throw new ArgumentOutOfRangeException(nameof(latitude), "Latitude must be between -90 and 90.");
            }

            if (longitude is < MinLongitude or > MaxLongitude)
            {
                throw new ArgumentOutOfRangeException(nameof(longitude), "Longitude must be between -180 and 180.");
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
    }
}
