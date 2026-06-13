using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Helpers
{
    public static class ProfileCompletionRules
    {
        public static bool RequiresProfile(UserRole role) => role == UserRole.Player;

        public static bool ResolveHasProfile(UserRole role, bool hasActiveProfile) =>
            !RequiresProfile(role) || hasActiveProfile;
    }
}
