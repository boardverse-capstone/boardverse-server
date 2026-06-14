namespace BoardVerse.Core.Messages
{
    public static class ApiErrorMessages
    {
        public static string AccountBlocked(string action, string? reason = null) =>
            string.IsNullOrWhiteSpace(reason)
                ? $"{action} denied. Your account has been blocked. Contact support for assistance."
                : $"{action} denied. Your account has been blocked. Reason: {reason}";

        public static class Auth
        {
            public const string RegisterDuplicate =
                "Registration failed. A user with the same username or email already exists.";

            public const string LoginTooManyAttempts =
                "Too many failed login attempts. Wait 15 minutes before trying again.";

            public const string LoginInvalidCredentials =
                "Login failed. Username/email or password is incorrect.";

            public const string GoogleTokenMissingEmail =
                "Google sign-in failed. The Google token does not include an email address.";

            public const string GoogleTokenValidationFailed =
                "Google sign-in failed. The Google token could not be validated.";

            public const string RefreshTokenInvalidOrExpired =
                "Refresh token is invalid or expired. Sign in again to obtain a new session.";

            public const string RefreshTokenUserMissing =
                "Refresh token is valid but the linked user account no longer exists.";

            public const string SendVerificationUserNotFound =
                "Cannot send verification email. No account exists for the provided email.";

            public const string VerifyEmailInvalidToken =
                "Email verification failed. The verification code is invalid.";

            public const string VerifyEmailTokenExpired =
                "Email verification failed. The verification code has expired. Request a new code.";

            public const string RequestPasswordResetUserNotFound =
                "Cannot start password reset. No account exists for the provided email.";

            public const string RequestPasswordResetEmailNotVerified =
                "Cannot reset password until the account email has been verified.";

            public const string ResetPasswordInvalidToken =
                "Password reset failed. The reset code is invalid.";

            public const string ResetPasswordTokenExpired =
                "Password reset failed. The reset code has expired. Request a new code.";

            public const string ChangePasswordUserNotFound =
                "Cannot change password because the signed-in account was not found.";

            public const string ChangePasswordNoLocalPassword =
                "This account uses Google sign-in only and has no local password to change.";

            public const string ChangePasswordCurrentIncorrect =
                "Current password is incorrect.";

            public const string ChangePasswordSameAsCurrent =
                "New password must be different from the current password.";

            public const string LinkGoogleAccountNotFound =
                "Cannot link Google account. No matching local account was found.";

            public const string ChangePasswordInvalidToken =
                "Cannot change password. Access token is missing a valid user identifier.";

            public const string LogoutInvalidToken =
                "Logout failed. Refresh token in request is invalid.";
        }

        public static class Profile
        {
            public const string UserNotFoundPublic =
                "Public profile not found for the requested user.";

            public const string UserNotFoundPrivate =
                "Profile not found for the signed-in user.";

            public const string ProfileDisabled =
                "This profile has been disabled and is no longer available.";

            public const string ProfileAlreadyExists =
                "Profile already exists. Use PUT to update instead of creating a new one.";

            public const string UserNotFoundCreate =
                "Cannot create profile because the user account was not found.";

            public const string UserNotFoundUpdate =
                "Cannot update profile because the user account was not found.";

            public const string UserNotFoundUpdateProgress =
                "Cannot update gamer progress because the user account was not found.";

            public const string UserNotFoundUpdateAvatar =
                "Cannot update avatar because the user account was not found.";

            public const string UserNotFoundKarma =
                "Cannot load karma state because the user account was not found.";

            public const string UserNotFoundCreateOrGet =
                "Cannot initialize profile because the user account was not found.";

            public const string UserNotFoundUpdateLocation =
                "Cannot update location because the user account was not found.";

            public const string UserNotFoundGetLocation =
                "Cannot load saved location because the user account was not found.";

            public const string InvalidLatitudeForLocationUpdate =
                "Location update failed. Latitude must be between -90 and 90.";

            public const string InvalidLongitudeForLocationUpdate =
                "Location update failed. Longitude must be between -180 and 180.";

            public const string ProfileNotFoundClearLocation =
                "Cannot clear saved location because the user profile was not found.";

            public const string NoSavedLocationToClear =
                "Cannot clear saved location because no location is stored on the profile.";
        }

        public static class AdminUsers
        {
            public const string InvalidRoleFilter =
                "Invalid role filter. Use Player, Manager, CafeStaff, or Admin.";

            public const string InvalidRoleValue =
                "Invalid role value. Use Player, Manager, CafeStaff, or Admin.";

            public static string UserNotFound(Guid id) =>
                $"Admin user lookup failed. No user exists with id '{id}'.";

            public const string CreateDuplicate =
                "Cannot create user. Another account already uses the same email or username.";

            public static string UsernameConflict(string username) =>
                $"Cannot update user. Username '{username}' is already taken.";

            public static string EmailConflict(string email) =>
                $"Cannot update user. Email '{email}' is already registered.";
        }

        public static class Cafe
        {
            public static string NotFound(Guid cafeId) =>
                $"Cafe not found or is not available. Cafe id: '{cafeId}'.";

            public static string ManagerForbidden(Guid cafeId) =>
                $"You are not the manager of cafe '{cafeId}' and cannot perform this action.";

            public static string InventoryManagerForbidden(Guid cafeId) =>
                $"You are not authorized to manage inventory for cafe '{cafeId}'.";

            public const string StaffUserNotFound =
                "Cannot add staff because the specified user was not found.";

            public const string StaffAdminOrManagerNotAllowed =
                "Admin and Manager accounts cannot be assigned as cafe staff.";

            public const string StaffAlreadyAssigned =
                "This user is already an active staff member of the cafe.";

            public const string StaffCreateUsernameRequired =
                "Username is required when creating a new staff account.";

            public const string StaffUsernameTooShort =
                "Staff username must be at least 3 characters.";

            public const string StaffUsernameTaken =
                "Cannot create staff account. Username is already taken.";

            public static string StaffNotFound(Guid cafeId, Guid staffId) =>
                $"Staff member '{staffId}' was not found in cafe '{cafeId}'.";

            public const string InvalidLatitudeForNearbySearch =
                "Nearby cafe search failed. Latitude must be between -90 and 90.";

            public const string InvalidLongitudeForNearbySearch =
                "Nearby cafe search failed. Longitude must be between -180 and 180.";

            public static string InvalidNearbySearchRadius(double minKm, double maxKm) =>
                $"Nearby cafe search failed. Radius must be between {minKm} and {maxKm} km.";

            public const string LocationCoordinatesPairRequired =
                "Cafe location update requires both latitude and longitude when either is provided.";

            public const string InvalidLatitudeForCafeUpdate =
                "Cafe location update failed. Latitude must be between -90 and 90.";

            public const string InvalidLongitudeForCafeUpdate =
                "Cafe location update failed. Longitude must be between -180 and 180.";

            public const string GameTemplateIdRequiredForNearbySearch =
                "Nearby cafe search requires a gameTemplateId when filtering by selected game.";

            public const string SavedLocationRequiredForNearbySearch =
                "Nearby cafe search from profile failed because no saved location was found. Update location via PUT /api/userprofile/me/location first.";
        }

        public static class Inventory
        {
            public static string MasterGameNotFound(Guid gameTemplateId) =>
                $"Master game '{gameTemplateId}' was not found or is inactive.";

            public const string GameAlreadyInInventory =
                "This game is already in the cafe inventory. Update the existing entry instead.";

            public const string GamePreviouslyRemoved =
                "This game was soft-deleted from inventory. Restore it instead of adding again.";

            public static string ItemNotFound(Guid cafeId, Guid inventoryId) =>
                $"Inventory item '{inventoryId}' was not found in cafe '{cafeId}'.";

            public static string ActiveItemNotFound(Guid cafeId, Guid inventoryId) =>
                $"Active inventory item '{inventoryId}' was not found in cafe '{cafeId}'.";

            public const string ItemAlreadyActive =
                "Inventory item is already active. Restore is not required.";

            public const string ActiveDuplicateOnRestore =
                "Cannot restore inventory item because an active entry for this game already exists.";

            public static string ComponentNotInGame(Guid componentId) =>
                $"Component '{componentId}' does not belong to the selected game.";

            public static string ComponentsInvalidForGame() =>
                "One or more component IDs do not belong to the selected game.";
        }

        public static class Pos
        {
            public static string AccessForbidden(Guid cafeId) =>
                $"POS access denied. You are not authorized to operate cafe '{cafeId}'.";

            public const string BarcodeRequired =
                "Cannot look up inventory box because barcode is empty.";

            public static string BoxNotFound(Guid cafeId, string barcode) =>
                $"Inventory box with barcode '{barcode}' was not found in cafe '{cafeId}'.";

            public static string TableNotFound(Guid cafeId, Guid tableId) =>
                $"Table '{tableId}' was not found in cafe '{cafeId}'.";

            public static string TableNotAvailableForGame(Guid tableId) =>
                $"Table '{tableId}' is reserved or in an event and cannot receive a game.";

            public static string BoxNotAvailable(string barcode, string status) =>
                $"Inventory box '{barcode}' is not available (current status: {status}).";

            public static string BoxAlreadyInSession(string barcode) =>
                $"Inventory box '{barcode}' is already assigned to an active play session.";

            public static string SessionNotFound(Guid cafeId, Guid sessionId) =>
                $"Active play session '{sessionId}' was not found in cafe '{cafeId}'.";
        }

        public static class BoardGame
        {
            public static string NotFound(Guid id) =>
                $"Board game '{id}' was not found or is inactive.";

            public static string MasterNotFound(Guid id) =>
                $"Master board game '{id}' was not found.";
        }

        public static class CafePartner
        {
            public static string ApplicationNotFound(Guid id) =>
                $"Cafe partner application '{id}' was not found.";

            public const string ApplicationNotFoundForManager =
                "No approved cafe partner application was found for the signed-in manager.";

            public const string RejectionReasonRequired =
                "Rejection reason is required when rejecting a cafe partner application.";

            public const string LinkedCafeMissing =
                "Cannot complete partner action because the linked cafe record is missing.";
        }

        public static class Email
        {
            public const string BrevoApiKeyMissing =
                "Email service is not configured. Set Brevo:ApiKey on the server.";

            public const string BrevoSenderMissing =
                "Email sender is not configured. Verify sender email in Brevo dashboard.";

            public const string BrevoConnectionFailed =
                "Cannot connect to Brevo API. Verify Brevo__ApiKey and network access.";

            public const string BrevoRequestTimedOut =
                "Brevo email request timed out. Try again later.";

            public static string BrevoApiFailed(int statusCode, string details) =>
                $"Brevo API rejected the email request ({statusCode}). {details}";
        }

        public static class Http
        {
            public static string Fallback(int statusCode, string path) => statusCode switch
            {
                400 => $"Request to '{path}' was invalid. Check query/body parameters.",
                401 => $"Authentication is required for '{path}'.",
                403 => $"You do not have permission to access '{path}'.",
                404 => $"No API route or resource matched '{path}'.",
                409 => $"Request to '{path}' conflicts with existing data.",
                429 => $"Too many requests to '{path}'. Slow down and retry later.",
                500 => $"An unexpected server error occurred while processing '{path}'.",
                _ => $"Request to '{path}' failed with status {statusCode}."
            };
        }

        public static class Controller
        {
            public const string InvalidUserIdClaim =
                "Cannot identify the signed-in user. Access token is missing a valid user id claim.";

            public const string ChangePasswordInvalidUserId =
                "Cannot change password. Access token is missing a valid user identifier.";
        }
    }
}
