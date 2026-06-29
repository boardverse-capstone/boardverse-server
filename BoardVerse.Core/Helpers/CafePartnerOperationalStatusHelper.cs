using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Helpers
{
    public static class CafePartnerOperationalStatusHelper
    {
        public static bool IsVisibleOnPlayerApp(CafePartnerOperationalStatus? status, bool isActive) =>
            isActive && status == CafePartnerOperationalStatus.Active;

        public static bool IsTerminal(CafePartnerOperationalStatus? status) =>
            status is CafePartnerOperationalStatus.Inactive or CafePartnerOperationalStatus.Banned;

        public static bool CanManagerEditProfile(CafePartnerOperationalStatus? status) =>
            status is CafePartnerOperationalStatus.DataBlank
                or CafePartnerOperationalStatus.Active
                or CafePartnerOperationalStatus.Inactive;

        public static bool CanManagerActivate(CafePartnerOperationalStatus? status) =>
            status == CafePartnerOperationalStatus.DataBlank;

        public static bool CanManagerReopen(CafePartnerOperationalStatus? status) =>
            status == CafePartnerOperationalStatus.Inactive;

        public static bool CanManagerPause(CafePartnerOperationalStatus? status) =>
            status == CafePartnerOperationalStatus.Active;

        public static bool CanManagerClosePermanently(CafePartnerOperationalStatus? status) =>
            status is CafePartnerOperationalStatus.DataBlank or CafePartnerOperationalStatus.Active;
    }
}
