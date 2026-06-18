using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Helpers
{
    public static class UserAccessHelper
    {
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

            user.AccountStatus = UserAccountStatus.Active;
            user.IsBlocked = false;
            user.LockoutEndDate = null;
            user.UpdatedAt = utcNow;
            return true;
        }

        public static bool IsAccessRestricted(User user, DateTime utcNow, out string message)
        {
            TryClearExpiredSuspension(user, utcNow);

            if (user.AccountStatus == UserAccountStatus.Banned || (user.IsBlocked && user.AccountStatus == UserAccountStatus.Banned))
            {
                message = string.IsNullOrWhiteSpace(user.BlockReason)
                    ? "Your account has been permanently banned."
                    : $"Your account has been permanently banned. Reason: {user.BlockReason}";
                return true;
            }

            if (user.AccountStatus == UserAccountStatus.Suspended
                && user.LockoutEndDate.HasValue
                && user.LockoutEndDate > utcNow)
            {
                message = string.IsNullOrWhiteSpace(user.BlockReason)
                    ? $"Your account is suspended until {user.LockoutEndDate:O}."
                    : $"Your account is suspended until {user.LockoutEndDate:O}. Reason: {user.BlockReason}";
                return true;
            }

            if (user.IsBlocked)
            {
                message = string.IsNullOrWhiteSpace(user.BlockReason)
                    ? "Your account has been blocked."
                    : $"Your account has been blocked. Reason: {user.BlockReason}";
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
