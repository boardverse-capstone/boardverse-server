using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Messages;

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

        public static void SyncProfileActiveState(User user)
        {
            if (user.Profile != null)
            {
                user.Profile.IsActive = user.IsActive;
                user.Profile.UpdatedAt = DateTime.UtcNow;
            }
        }

        public static bool IsAccessRestricted(User user, DateTime utcNow, out string message)
        {
            TryClearExpiredSuspension(user, utcNow);

            if (user.AccountStatus == UserAccountStatus.Banned)
            {
                message = string.IsNullOrWhiteSpace(user.BlockReason)
                    ? ApiErrorMessages.AccountAccess.BannedPermanent
                    : ApiErrorMessages.AccountAccess.BannedPermanentWithReason(user.BlockReason);
                return true;
            }

            if (user.AccountStatus == UserAccountStatus.Suspended)
            {
                if (user.LockoutEndDate.HasValue && user.LockoutEndDate > utcNow)
                {
                    message = string.IsNullOrWhiteSpace(user.BlockReason)
                        ? ApiErrorMessages.AccountAccess.SuspendedUntil(user.LockoutEndDate.Value)
                        : ApiErrorMessages.AccountAccess.SuspendedUntilWithReason(
                            user.LockoutEndDate.Value,
                            user.BlockReason);
                    return true;
                }

                message = string.IsNullOrWhiteSpace(user.BlockReason)
                    ? ApiErrorMessages.AccountAccess.SuspendedIndefinite
                    : ApiErrorMessages.AccountAccess.SuspendedIndefiniteWithReason(user.BlockReason);
                return true;
            }

            if (!user.IsActive)
            {
                message = ApiErrorMessages.AccountAccess.AccountInactive;
                return true;
            }

            message = string.Empty;
            return false;
        }
    }
}
