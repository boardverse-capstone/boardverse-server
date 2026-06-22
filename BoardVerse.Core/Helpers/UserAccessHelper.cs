using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Helpers
{
    public static class UserAccessHelper
    {
        public static void ClearModerationState(User user, DateTime utcNow)
        {
            user.AccountStatus = UserAccountStatus.Active;
            user.LockoutEndDate = null;
            user.BlockReason = null;
            user.BlockedAt = null;
            user.UpdatedAt = utcNow;
        }

        public static bool TryClearExpiredSuspension(User user, DateTime utcNow)
        {
            if (user.AccountStatus != UserAccountStatus.Suspended || user.LockoutEndDate == null)
            {
                return false;
            }

            if (user.LockoutEndDate > utcNow)
            {
                return false;
            }

            ClearModerationState(user, utcNow);
            return true;
        }

        public static bool IsAccessRestricted(User user, DateTime utcNow, out string message)
        {
            TryClearExpiredSuspension(user, utcNow);

            if (user.AccountStatus == UserAccountStatus.Banned)
            {
                message = string.IsNullOrWhiteSpace(user.BlockReason)
                    ? "Your account has been permanently banned."
                    : $"Your account has been permanently banned. Reason: {user.BlockReason}";
                return true;
            }

            if (user.AccountStatus == UserAccountStatus.Suspended)
            {
                if (user.LockoutEndDate.HasValue && user.LockoutEndDate > utcNow)
                {
                    message = string.IsNullOrWhiteSpace(user.BlockReason)
                        ? $"Your account is suspended until {user.LockoutEndDate:O}."
                        : $"Your account is suspended until {user.LockoutEndDate:O}. Reason: {user.BlockReason}";
                    return true;
                }

                message = string.IsNullOrWhiteSpace(user.BlockReason)
                    ? "Your account is suspended."
                    : $"Your account is suspended. Reason: {user.BlockReason}";
                return true;
            }

            if (!user.IsActive)
            {
                message = "Your account is deactivated. Contact support to reactivate your account.";
                return true;
            }

            message = string.Empty;
            return false;
        }
    }
}
