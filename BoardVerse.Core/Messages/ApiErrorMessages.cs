using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Messages
{
    public static class ApiErrorMessages
    {
        public static string AccountBlocked(string action, string? reason = null) =>
            string.IsNullOrWhiteSpace(reason)
                ? $"{action} bị từ chối. Tài khoản của bạn đã bị khóa. Vui lòng liên hệ hỗ trợ."
                : $"{action} bị từ chối. Tài khoản của bạn đã bị khóa. Lý do: {reason}";

        public static class Auth
        {
            public const string RegisterDuplicate =
                "Đăng ký thất bại. Tên đăng nhập hoặc email đã được sử dụng.";

            public const string LoginTooManyAttempts =
                "Đăng nhập sai quá nhiều lần. Vui lòng thử lại sau 15 phút.";

            public const string LoginInvalidCredentials =
                "Đăng nhập thất bại. Tên đăng nhập/email hoặc mật khẩu không đúng.";

            public const string GoogleTokenMissingEmail =
                "Đăng nhập Google thất bại. Token Google không chứa địa chỉ email.";

            public const string GoogleTokenValidationFailed =
                "Đăng nhập Google thất bại. Không thể xác thực token Google.";

            public const string RefreshTokenInvalidOrExpired =
                "Refresh token không hợp lệ hoặc đã hết hạn. Vui lòng đăng nhập lại.";

            public const string RefreshTokenUserMissing =
                "Refresh token hợp lệ nhưng tài khoản liên kết không còn tồn tại.";

            public const string SendVerificationUserNotFound =
                "Không thể gửi email xác minh. Không tìm thấy tài khoản với email này.";

            public const string VerifyEmailInvalidToken =
                "Xác minh email thất bại. Mã xác minh không hợp lệ.";

            public const string VerifyEmailTokenExpired =
                "Xác minh email thất bại. Mã xác minh đã hết hạn. Vui lòng yêu cầu mã mới.";

            public const string RequestPasswordResetUserNotFound =
                "Không thể đặt lại mật khẩu. Không tìm thấy tài khoản với email này.";

            public const string RequestPasswordResetEmailNotVerified =
                "Không thể đặt lại mật khẩu cho đến khi email đã được xác minh.";

            public const string ResetPasswordInvalidToken =
                "Đặt lại mật khẩu thất bại. Mã đặt lại không hợp lệ.";

            public const string ResetPasswordTokenExpired =
                "Đặt lại mật khẩu thất bại. Mã đặt lại đã hết hạn. Vui lòng yêu cầu mã mới.";

            public const string ChangePasswordUserNotFound =
                "Không thể đổi mật khẩu vì không tìm thấy tài khoản đang đăng nhập.";

            public const string ChangePasswordNoLocalPassword =
                "Tài khoản này chỉ đăng nhập bằng Google và không có mật khẩu cục bộ để đổi.";

            public const string ChangePasswordCurrentIncorrect =
                "Mật khẩu hiện tại không đúng.";

            public const string ChangePasswordSameAsCurrent =
                "Mật khẩu mới phải khác mật khẩu hiện tại.";

            public const string LinkGoogleAccountNotFound =
                "Không thể liên kết Google. Không tìm thấy tài khoản cục bộ tương ứng.";

            public const string ChangePasswordInvalidToken =
                "Không thể đổi mật khẩu. Access token thiếu mã định danh người dùng hợp lệ.";

            public const string LogoutInvalidToken =
                "Đăng xuất thất bại. Refresh token trong yêu cầu không hợp lệ.";

            public const string VerificationEmailSent = "Đã gửi email xác minh.";
            public const string PasswordResetEmailSent = "Đã gửi email đặt lại mật khẩu.";
        }

        public static class Profile
        {
            public const string UserNotFoundPublic =
                "Không tìm thấy hồ sơ công khai của người dùng này.";

            public const string UserNotFoundPrivate =
                "Không tìm thấy hồ sơ của tài khoản đang đăng nhập.";

            public const string ProfileDisabled =
                "Hồ sơ này đã bị vô hiệu hóa và không còn khả dụng.";

            public const string ProfileAlreadyExists =
                "Hồ sơ đã tồn tại. Dùng PUT để cập nhật thay vì tạo mới.";

            public const string UserNotFoundCreate =
                "Không thể tạo hồ sơ vì không tìm thấy tài khoản người dùng.";

            public const string UserNotFoundUpdate =
                "Không thể cập nhật hồ sơ vì không tìm thấy tài khoản người dùng.";

            public const string UserNotFoundUpdateProgress =
                "Không thể cập nhật tiến trình vì không tìm thấy tài khoản người dùng.";

            public const string UserNotFoundUpdateAvatar =
                "Không thể cập nhật avatar vì không tìm thấy tài khoản người dùng.";

            public const string UserNotFoundKarma =
                "Không thể tải trạng thái karma vì không tìm thấy tài khoản người dùng.";

            public const string UserNotFoundCreateOrGet =
                "Không thể khởi tạo hồ sơ vì không tìm thấy tài khoản người dùng.";

            public const string UserNotFoundUpdateLocation =
                "Không thể cập nhật vị trí vì không tìm thấy tài khoản người dùng.";

            public const string UserNotFoundGetLocation =
                "Không thể tải vị trí đã lưu vì không tìm thấy tài khoản người dùng.";

            public const string InvalidLatitudeForLocationUpdate =
                "Cập nhật vị trí thất bại. Vĩ độ phải nằm trong khoảng -90 đến 90.";

            public const string InvalidLongitudeForLocationUpdate =
                "Cập nhật vị trí thất bại. Kinh độ phải nằm trong khoảng -180 đến 180.";

            public const string ProfileNotFoundClearLocation =
                "Không thể xóa vị trí đã lưu vì không tìm thấy hồ sơ người dùng.";

            public const string NoSavedLocationToClear =
                "Không thể xóa vị trí vì hồ sơ chưa lưu vị trí nào.";
        }

        public static class AdminUsers
        {
            public const string InvalidRoleFilter =
                "Bộ lọc vai trò không hợp lệ. Dùng Player, Manager, CafeStaff hoặc Admin.";

            public const string InvalidRoleValue =
                "Giá trị vai trò không hợp lệ. Dùng Player, Manager, CafeStaff hoặc Admin.";

            public static string UserNotFound(Guid id) =>
                $"Không tìm thấy người dùng với id '{id}'.";

            public const string CreateDuplicate =
                "Không thể tạo người dùng. Email hoặc tên đăng nhập đã được sử dụng.";

            public static string UsernameConflict(string username) =>
                $"Không thể cập nhật. Tên đăng nhập '{username}' đã được sử dụng.";

            public static string EmailConflict(string email) =>
                $"Không thể cập nhật. Email '{email}' đã được đăng ký.";
        }

        public static class Cafe
        {
            public static string NotFound(Guid cafeId) =>
                $"Không tìm thấy quán hoặc quán không khả dụng. Mã quán: '{cafeId}'.";

            public static string CafeRecordNotFound(Guid cafeId) =>
                $"Không tìm thấy quán '{cafeId}'.";

            public static string ManagerForbidden(Guid cafeId) =>
                $"Bạn không phải quản lý của quán '{cafeId}' và không thể thực hiện thao tác này.";

            public static string InventoryManagerForbidden(Guid cafeId) =>
                $"Bạn không có quyền quản lý kho của quán '{cafeId}'.";

            public const string StaffUserNotFound =
                "Không thể thêm nhân viên vì không tìm thấy người dùng được chỉ định.";

            public const string StaffAdminOrManagerNotAllowed =
                "Tài khoản Admin và Manager không thể được gán làm nhân viên quán.";

            public const string StaffAlreadyAssigned =
                "Người dùng này đã là nhân viên của quán.";

            public const string StaffCreateUsernameRequired =
                "Tên đăng nhập là bắt buộc khi tạo tài khoản nhân viên mới.";

            public const string StaffUsernameTooShort =
                "Tên đăng nhập nhân viên phải có ít nhất 3 ký tự.";

            public const string StaffUsernameTaken =
                "Không thể tạo tài khoản nhân viên. Tên đăng nhập đã được sử dụng.";

            public static string StaffNotFound(Guid cafeId, Guid staffId) =>
                $"Không tìm thấy nhân viên '{staffId}' trong quán '{cafeId}'.";

            public const string InvalidLatitudeForNearbySearch =
                "Tìm quán gần bạn thất bại. Vĩ độ phải nằm trong khoảng -90 đến 90.";

            public const string InvalidLongitudeForNearbySearch =
                "Tìm quán gần bạn thất bại. Kinh độ phải nằm trong khoảng -180 đến 180.";

            public static string InvalidNearbySearchRadius(double minKm, double maxKm) =>
                $"Tìm quán gần bạn thất bại. Bán kính phải nằm trong khoảng {minKm} đến {maxKm} km.";

            public const string LocationCoordinatesPairRequired =
                "Cập nhật vị trí quán yêu cầu cả vĩ độ và kinh độ khi cung cấp một trong hai.";

            public const string InvalidLatitudeForCafeUpdate =
                "Cập nhật vị trí quán thất bại. Vĩ độ phải nằm trong khoảng -90 đến 90.";

            public const string InvalidLongitudeForCafeUpdate =
                "Cập nhật vị trí quán thất bại. Kinh độ phải nằm trong khoảng -180 đến 180.";

            public const string GameTemplateIdRequiredForNearbySearch =
                "Tìm quán gần bạn theo game yêu cầu gameTemplateId.";

            public const string SavedLocationRequiredForNearbySearch =
                "Tìm quán gần bạn từ hồ sơ thất bại vì chưa lưu vị trí. Hãy cập nhật vị trí qua PUT /api/userprofile/me/location trước.";

            public const string NoNearbyCafesWithSelectedGameMessage =
                "Không tìm thấy địa điểm phù hợp có sẵn tựa game này xung quanh bạn.";

            public const string StaffPromoteHint =
                "Hãy gọi POST /api/cafes/{cafeId}/staff/promote trước, sau đó POST /api/cafes/{cafeId}/staff để liên kết.";

            public const string StaffLinkHint =
                "Liên kết họ với quán này qua POST /api/cafes/{cafeId}/staff (chỉ cần email).";

            public static string StaffAlreadyCafeStaffMustLink(string email) =>
                $"Người dùng '{email}' đã là CafeStaff. {StaffLinkHint}";

            public static string StaffWrongRoleMustPromote(string email, string role) =>
                $"Người dùng '{email}' có vai trò '{role}' và chưa phải CafeStaff. {StaffPromoteHint}";

            public static string StaffWrongRoleMustLink(string email, string role) =>
                $"Người dùng '{email}' có vai trò '{role}'. {StaffLinkHint}";
        }

        public static class Inventory
        {
            public static string MasterGameNotFound(Guid gameTemplateId) =>
                $"Không tìm thấy game master '{gameTemplateId}' hoặc game đã bị vô hiệu hóa.";

            public const string GameAlreadyInInventory =
                "Game này đã có trong kho quán. Hãy cập nhật mục hiện có.";

            public const string GamePreviouslyRemoved =
                "Game này đã bị xóa mềm khỏi kho. Hãy khôi phục thay vì thêm mới.";

            public static string ItemNotFound(Guid cafeId, Guid inventoryId) =>
                $"Không tìm thấy mục kho '{inventoryId}' trong quán '{cafeId}'.";

            public static string ActiveItemNotFound(Guid cafeId, Guid inventoryId) =>
                $"Không tìm thấy mục kho đang hoạt động '{inventoryId}' trong quán '{cafeId}'.";

            public const string ItemAlreadyActive =
                "Mục kho đã đang hoạt động. Không cần khôi phục.";

            public const string ActiveDuplicateOnRestore =
                "Không thể khôi phục vì đã có mục kho đang hoạt động cho game này.";

            public static string ComponentNotInGame(Guid componentId) =>
                $"Linh kiện '{componentId}' không thuộc game đã chọn.";

            public static string ComponentsInvalidForGame() =>
                "Một hoặc nhiều mã linh kiện không thuộc game đã chọn.";
        }

        public static class Pos
        {
            public static string AccessForbidden(Guid cafeId) =>
                $"Từ chối truy cập POS. Bạn không có quyền vận hành quán '{cafeId}'.";

            public const string BarcodeRequired =
                "Không thể tra cứu hộp game vì mã vạch trống.";

            public static string BoxNotFound(Guid cafeId, string barcode) =>
                $"Không tìm thấy hộp game với mã vạch '{barcode}' trong quán '{cafeId}'.";

            public static string TableNotFound(Guid cafeId, Guid tableId) =>
                $"Không tìm thấy bàn '{tableId}' trong quán '{cafeId}'.";

            public static string TableNotAvailableForGame(Guid tableId) =>
                $"Bàn '{tableId}' đang được giữ hoặc trong sự kiện và không thể nhận game.";

            public static string BoxNotAvailable(string barcode, string status) =>
                $"Hộp game '{barcode}' không khả dụng (trạng thái hiện tại: {status}).";

            public static string BoxAlreadyInSession(string barcode) =>
                $"Hộp game '{barcode}' đang được gán cho một phiên chơi đang hoạt động.";

            public static string SessionNotFound(Guid cafeId, Guid sessionId) =>
                $"Không tìm thấy phiên chơi '{sessionId}' trong quán '{cafeId}'.";
        }

        public static class BoardGame
        {
            public static string NotFound(Guid id) =>
                $"Không tìm thấy board game '{id}' hoặc game đã bị vô hiệu hóa.";

            public static string MasterNotFound(Guid id) =>
                $"Không tìm thấy board game master '{id}'.";

            public static string SoloPlayNotSupported(Guid id, int minPlayers) =>
                $"Không hỗ trợ chơi solo cho board game '{id}'. Số người chơi tối thiểu là {minPlayers}; hãy chọn chế độ nhóm.";
        }

        public static class Bgg
        {
            public const string SearchQueryTooShort =
                "Tìm kiếm BGG thất bại: từ khóa phải có ít nhất 2 ký tự.";

            public const string SearchUpstreamUnavailable =
                "Tìm kiếm BGG thất bại: API BoardGameGeek không phản hồi hoặc trả dữ liệu không hợp lệ.";

            public const string PreviewInvalidBggId =
                "Xem trước game BGG thất bại: bggId phải là số nguyên dương.";

            public static string PreviewGameNotFound(int bggId) =>
                $"Xem trước game BGG thất bại: không tìm thấy hoặc không tải được game BGG id {bggId}.";

            public const string ImportInvalidBggId =
                "Import BGG thất bại: bggId phải là số nguyên dương.";

            public static string ImportGameNotFound(int bggId) =>
                $"Import BGG thất bại: không tìm thấy hoặc không tải được game BGG id {bggId}.";

            public static string ImportNoComponentsResolved(int bggId) =>
                $"Import BGG thất bại: không thể xác định linh kiện cho game BGG {bggId}.";

            public static string ImportAlreadyExists(Guid gameTemplateId, int bggId) =>
                $"Import BGG thất bại: game đã tồn tại (template '{gameTemplateId}' / BGG {bggId}). Đặt overwriteExisting=true để làm mới từ BGG.";
        }

        public static class CafePartner
        {
            public static string ApplicationNotFound(Guid id) =>
                $"Không tìm thấy đơn đăng ký đối tác '{id}'.";

            public const string ApplicationNotFoundForManager =
                "Không tìm thấy đơn đăng ký đối tác đã được duyệt cho quản lý đang đăng nhập.";

            public const string RejectionReasonRequired =
                "Lý do từ chối là bắt buộc khi từ chối đơn đăng ký đối tác.";

            public const string LinkedCafeMissing =
                "Không thể hoàn tất thao tác đối tác vì thiếu bản ghi quán liên kết.";

            public const string InvalidOperationalStatus =
                "Trạng thái vận hành không hợp lệ. Dùng DATA_BLANK, ACTIVE, INACTIVE hoặc BANNED.";

            public const string BanReasonRequired =
                "Lý do là bắt buộc khi đặt trạng thái quán là BANNED.";

            public const string CafePermanentlyClosed =
                "Quán này đã đóng vĩnh viễn và không thể chỉnh sửa.";

            public const string CafeBannedByAdmin =
                "Quán này đã bị quản trị viên cấm hoạt động.";

            public const string CannotCloseWithActiveBookings =
                "Không thể đóng quán khi còn phiên bàn đang chạy.";

            public const string CannotPauseWithActiveSessions =
                "Không thể tạm dừng quán khi còn phiên bàn đang chạy.";

            public const string ClosedByManagerReason =
                "Quản lý đóng quán.";

            public const string OnlyActiveCafesCanBePaused =
                "Chỉ quán ACTIVE mới có thể tạm dừng.";

            public const string OnlyDataBlankCafesCanBeActivated =
                "Chỉ quán DATA_BLANK mới có thể kích hoạt.";

            public const string OnlyInactiveCafesCanBeReopened =
                "Chỉ quán INACTIVE mới có thể mở lại.";

            public const string UseReopenForInactiveCafes =
                "Quán đang INACTIVE. Dùng POST /api/manager/cafes/me/reopen để mở lại.";

            public const string OpenApplicationExists =
                "Đã có đơn đang mở với email này.";

            public const string EmailNotEligibleForApplication =
                "Email này đã đăng ký vai trò hệ thống và không thể dùng cho đơn đối tác mới.";

            public const string OnlyPendingApprovalCanBeApproved =
                "Chỉ đơn PENDING_APPROVAL mới có thể được phê duyệt.";

            public const string BusinessLicenseImageRequired =
                "Ảnh giấy phép kinh doanh là bắt buộc trước khi phê duyệt.";

            public static string EmailUsedByRoleAccount(string role) =>
                $"Email đã được dùng bởi tài khoản {role}.";

            public const string EmailAlreadyManagesPartnerCafe =
                "Email này đã quản lý một quán đối tác.";

            public const string OnlyPendingApprovalCanBeRejected =
                "Chỉ đơn PENDING_APPROVAL mới có thể bị từ chối.";

            public const string PauseBeforeEditingProfile =
                "Hãy tạm dừng quán trước khi chỉnh sửa hồ sơ vận hành.";

            public const string CafeNameLengthInvalid =
                "Tên quán phải từ 5 đến 100 ký tự.";

            public const string PhoneNumberInvalid =
                "Số điện thoại phải là số Việt Nam hợp lệ gồm 10–11 chữ số.";

            public const string BusinessLicenseAlphanumeric =
                "Giấy phép kinh doanh chỉ được chứa chữ và số.";

            public const string BusinessLicenseImageFormatInvalid =
                "Ảnh giấy phép kinh doanh phải là JPEG, PNG hoặc PDF.";

            public const string TableCountMustBePositive =
                "Số bàn phải lớn hơn 0.";

            public const string PrivateRoomCountCannotBeNegative =
                "Số phòng riêng không được âm.";

            public const string GamesOwnedMustBePositive =
                "Số game sở hữu phải lớn hơn 0.";

            public static string MinSpaceImagesRequired(int min) =>
                $"Cần ít nhất {min} ảnh không gian.";

            public const string SpaceImagesFormatInvalid =
                "Ảnh không gian phải là JPEG hoặc PNG.";

            public static string MinPublicTablesRequired(int min) =>
                $"Cần tối thiểu {min} bàn công cộng.";

            public static string MinGamesOwnedRequired(int min) =>
                $"Cần tối thiểu {min} game sở hữu.";

            public static string MinSpaceImagesActivationRequired(int min) =>
                $"Cần tối thiểu {min} ảnh không gian hợp lệ.";

            public const string TableLayoutRequired =
                "Phải cấu hình sơ đồ bàn cho tất cả bàn công cộng đã khai báo.";

            public const string PopularGamesListRequired =
                "Danh sách game phổ biến là bắt buộc.";

            public const string WeekdayHoursInvalid =
                "Giờ mở cửa ngày thường phải trước giờ đóng cửa.";

            public const string WeekendHoursInvalid =
                "Giờ mở cửa cuối tuần phải trước giờ đóng cửa.";

            public static string TimeFormatInvalid(string fieldName) =>
                $"{fieldName} phải theo định dạng HH:mm.";

            public const string SubmitterNotFound =
                "Không tìm thấy tài khoản người gửi đơn.";

            public const string SubmitterMustBePlayer =
                "Chỉ tài khoản Player mới có thể liên kết làm người gửi đơn.";

            public const string RepresentativeEmailMustMatch =
                "Email đại diện phải trùng email tài khoản đang đăng nhập.";

            public const string GpsLocationRequiredBeforeActivation =
                "Cần có vị trí GPS trước khi kích hoạt quán.";

            public const string WorkingHoursRequiredBeforeActivation =
                "Cần cấu hình giờ mở cửa trước khi kích hoạt quán.";

            public const string CafePermanentlyClosedBlocker =
                "Quán đã đóng vĩnh viễn (INACTIVE).";

            public const string CafeBannedBlocker =
                "Quán đã bị quản trị viên cấm hoạt động.";

            public static string ActivationRequirementsNotMet(IReadOnlyCollection<string> blockers) =>
                "Chưa đủ điều kiện kích hoạt: " + string.Join("; ", blockers);
        }

        public static class Email
        {
            public const string BrevoApiKeyMissing =
                "Dịch vụ email chưa được cấu hình. Hãy đặt Brevo:ApiKey trên máy chủ.";

            public const string BrevoSenderMissing =
                "Email người gửi chưa được cấu hình. Hãy xác minh email người gửi trên Brevo.";

            public const string BrevoConnectionFailed =
                "Không thể kết nối API Brevo. Kiểm tra Brevo__ApiKey và quyền truy cập mạng.";

            public const string BrevoRequestTimedOut =
                "Yêu cầu email Brevo đã hết thời gian chờ. Vui lòng thử lại sau.";

            public static string BrevoApiFailed(int statusCode, string details) =>
                $"API Brevo từ chối yêu cầu email ({statusCode}). {details}";
        }

        public static class Rating
        {
            public static string CrossRatingTagsReason(IEnumerable<KarmaRatingTag> tags) =>
                $"Thẻ đánh giá chéo trong phòng: {string.Join(", ", tags)}";

            public static string LobbyNotFound(Guid lobbyId) =>
                $"Đánh giá karma thất bại. Không tìm thấy phòng '{lobbyId}'.";

            public static string NotLobbyMember(Guid lobbyId, Guid userId) =>
                $"Đánh giá karma thất bại. Người dùng '{userId}' không phải thành viên đang hoạt động của phòng '{lobbyId}'.";

            public static string LobbyNotOpenForRating(Guid lobbyId) =>
                $"Đánh giá karma thất bại. Phòng '{lobbyId}' chưa mở đánh giá chéo (có thể chưa hoàn tất thanh toán).";

            public static string CannotRateSelf(Guid lobbyId) =>
                $"Đánh giá karma thất bại. Bạn không thể tự đánh giá mình trong phòng '{lobbyId}'.";

            public static string TargetNotLobbyMember(Guid lobbyId, Guid targetUserId) =>
                $"Đánh giá karma thất bại. Người được đánh giá '{targetUserId}' không phải thành viên phòng '{lobbyId}'.";

            public static string DuplicateTargetInRequest(Guid targetUserId) =>
                $"Đánh giá karma thất bại. Người được đánh giá '{targetUserId}' xuất hiện nhiều lần trong yêu cầu.";

            public static string AlreadyRated(Guid lobbyId, Guid targetUserId) =>
                $"Đánh giá karma thất bại. Bạn đã đánh giá người dùng '{targetUserId}' trong phòng '{lobbyId}'.";

            public const string EmptyTagsForEntry =
                "Đánh giá karma thất bại. Mỗi mục đánh giá phải có ít nhất một thẻ.";

            public const string InvalidTagValue =
                "Đánh giá karma thất bại. Một hoặc nhiều thẻ đánh giá không được nhận diện.";

            public static string TargetProfileMissing(Guid targetUserId) =>
                $"Đánh giá karma thất bại. Người được đánh giá '{targetUserId}' chưa có hồ sơ để nhận cập nhật karma.";

            public static string LobbyAlreadyOpenForRating(Guid lobbyId) =>
                $"Cửa sổ đánh giá karma của phòng '{lobbyId}' đã được mở trước đó.";

            public static string LobbyCannotOpenRating(Guid lobbyId) =>
                $"Không thể mở đánh giá karma cho phòng '{lobbyId}' vì phiên chưa đủ điều kiện.";
        }

        public static class Match
        {
            public const string MatchResultsConflict =
                "Kết quả trận đấu không khớp. Tất cả người chơi phải nhập lại kết quả thống nhất (một Win, các Loss còn lại, hoặc tất cả Draw).";

            public static string LobbyNotFound(Guid lobbyId) =>
                $"Gửi kết quả trận đấu thất bại. Không tìm thấy phòng '{lobbyId}'.";

            public static string NotLobbyMember(Guid lobbyId, Guid userId) =>
                $"Gửi kết quả trận đấu thất bại. Người dùng '{userId}' không phải thành viên đang hoạt động của phòng '{lobbyId}'.";

            public static string LobbyNotEligible(Guid lobbyId) =>
                $"Gửi kết quả trận đấu thất bại. Phòng '{lobbyId}' chưa ở trạng thái nhận kết quả.";

            public static string GameNotCompetitive(Guid gameTemplateId) =>
                $"Gửi kết quả trận đấu thất bại. Game '{gameTemplateId}' chưa được cấu hình theo dõi Elo cạnh tranh.";

            public static string MatchAlreadyFinalized(Guid lobbyId) =>
                $"Gửi kết quả trận đấu thất bại. Kết quả phòng '{lobbyId}' đã được chốt.";

            public static string ProfileMissing(Guid userId) =>
                $"Gửi kết quả trận đấu thất bại. Người dùng '{userId}' chưa có hồ sơ để nhận cập nhật Elo.";

            public const string InvalidOutcomeValue =
                "Gửi kết quả trận đấu thất bại. Kết quả phải là Win, Loss hoặc Draw.";
        }

        public static class AdminModeration
        {
            public const string InvalidPunishmentAction =
                "Hành động xử phạt không hợp lệ. Dùng Warning, Suspend hoặc Ban.";

            public const string SuspendDurationRequired =
                "Đình chỉ yêu cầu duration_days từ 1 đến 365.";

            public const string KarmaAdjustmentZeroNotAllowed =
                "Số điểm điều chỉnh karma không được bằng 0.";

            public const string CannotPunishAdmin =
                "Không thể xử phạt tài khoản Admin qua endpoint này.";

            public static string ProfileNotFound(Guid userId) =>
                $"Không thể điều chỉnh karma vì người dùng '{userId}' chưa có hồ sơ.";

            public const string InvalidViolationCategoryFilter =
                "Giá trị lọc loại vi phạm không hợp lệ.";
        }

        public static class AdminCatalog
        {
            public static string CategoryNotFound(Guid id) =>
                $"Không tìm thấy thể loại '{id}'.";

            public const string CategoryNameRequired =
                "Tên thể loại là bắt buộc.";

            public const string CategorySlugRequired =
                "Slug thể loại là bắt buộc.";

            public static string CategorySlugTaken(string slug) =>
                $"Slug thể loại '{slug}' đã được sử dụng.";

            public static string GameTemplateNotFound(Guid id) =>
                $"Không tìm thấy game template '{id}'.";

            public static string ComponentNotFound(Guid gameTemplateId, Guid componentId) =>
                $"Không tìm thấy linh kiện '{componentId}' trên game '{gameTemplateId}'.";

            public static string ComponentInUse(Guid componentId) =>
                $"Không thể xóa linh kiện '{componentId}' vì đang được tham chiếu bởi phí kho quán.";

            public static string InvalidComponentKind(int kind) =>
                $"Giá trị loại linh kiện '{kind}' không hợp lệ.";

            public static string CategoriesNotFound(IReadOnlyCollection<Guid> missingIds) =>
                $"Một hoặc nhiều thể loại không tồn tại: {string.Join(", ", missingIds)}.";
        }

        public static class Jwt
        {
            public const string MissingUserIdentifier =
                "Access token thiếu mã định danh người dùng hợp lệ. Vui lòng đăng nhập lại.";

            public const string UserNoLongerExists =
                "Tài khoản không còn tồn tại. Vui lòng đăng nhập lại.";

            public const string TokenExpired =
                "Access token đã hết hạn. Dùng POST /api/auth/refresh-token hoặc đăng nhập lại.";

            public const string TokenInvalidSignature =
                "Chữ ký access token không hợp lệ. Vui lòng đăng nhập lại.";

            public const string TokenInvalid =
                "Access token không hợp lệ hoặc sai định dạng. Vui lòng đăng nhập lại.";

            public const string AuthenticationFailed =
                "Xác thực thất bại. Vui lòng đăng nhập lại.";

            public const string AuthorizationHeaderMissing =
                "Thiếu header Authorization. Hãy cung cấp Bearer access token.";

            public const string AccessDenied =
                "Từ chối truy cập. Tài khoản không có vai trò hoặc quyền cần thiết cho endpoint này.";
        }

        public static class AccountAccess
        {
            public const string ActionSignIn = "Đăng nhập";
            public const string ActionGoogleSignIn = "Đăng nhập Google";
            public const string ActionTokenRefresh = "Làm mới token";
            public const string ActionSendVerificationEmail = "Gửi email xác minh";
            public const string ActionEmailVerification = "Xác minh email";
            public const string ActionPasswordResetRequest = "Yêu cầu đặt lại mật khẩu";
            public const string ActionPasswordReset = "Đặt lại mật khẩu";
            public const string ActionPasswordChange = "Đổi mật khẩu";
            public const string ActionGoogleAccountLinking = "Liên kết tài khoản Google";

            public const string BannedPermanent =
                "Tài khoản của bạn đã bị cấm vĩnh viễn.";

            public const string AccountInactive =
                "Tài khoản của bạn đã bị vô hiệu hóa. Vui lòng liên hệ hỗ trợ để kích hoạt lại.";

            public static string BannedPermanentWithReason(string reason) =>
                $"Tài khoản của bạn đã bị cấm vĩnh viễn. Lý do: {reason}";

            public static string SuspendedUntil(DateTime lockoutEnd) =>
                $"Tài khoản của bạn bị đình chỉ đến {lockoutEnd:O}.";

            public static string SuspendedUntilWithReason(DateTime lockoutEnd, string reason) =>
                $"Tài khoản của bạn bị đình chỉ đến {lockoutEnd:O}. Lý do: {reason}";

            public const string SuspendedIndefinite =
                "Tài khoản của bạn đang bị đình chỉ.";

            public static string SuspendedIndefiniteWithReason(string reason) =>
                $"Tài khoản của bạn đang bị đình chỉ. Lý do: {reason}";

            public static string Restricted(string message) => message;

            public static string LoginDeniedBanned(string? reason = null) =>
                string.IsNullOrWhiteSpace(reason)
                    ? "Từ chối đăng nhập. Tài khoản của bạn đã bị cấm vĩnh viễn."
                    : $"Từ chối đăng nhập. Tài khoản của bạn đã bị cấm vĩnh viễn. Lý do: {reason}";

            public static string LoginDeniedSuspended(DateTime lockoutEnd, string? reason = null) =>
                string.IsNullOrWhiteSpace(reason)
                    ? $"Từ chối đăng nhập. Tài khoản bị đình chỉ đến {lockoutEnd:O}."
                    : $"Từ chối đăng nhập. Tài khoản bị đình chỉ đến {lockoutEnd:O}. Lý do: {reason}";
        }

        public static class Http
        {
            public static string Fallback(int statusCode, string path) => statusCode switch
            {
                400 => $"Yêu cầu tới '{path}' không hợp lệ. Kiểm tra tham số query/body.",
                401 => $"Cần xác thực để truy cập '{path}'.",
                403 => $"Bạn không có quyền truy cập '{path}'.",
                404 => $"Không tìm thấy route hoặc tài nguyên API khớp '{path}'.",
                409 => $"Yêu cầu tới '{path}' xung đột với dữ liệu hiện có.",
                429 => $"Quá nhiều yêu cầu tới '{path}'. Hãy chậm lại và thử lại sau.",
                500 => $"Đã xảy ra lỗi máy chủ không mong đợi khi xử lý '{path}'.",
                _ => $"Yêu cầu tới '{path}' thất bại với mã trạng thái {statusCode}."
            };
        }

        public static class Controller
        {
            public const string InvalidUserIdClaim =
                "Không xác định được người dùng đang đăng nhập. Access token thiếu claim user id hợp lệ.";

            public const string ChangePasswordInvalidUserId =
                "Không thể đổi mật khẩu. Access token thiếu mã định danh người dùng hợp lệ.";
        }

        public static class Validation
        {
            public const string RequestFailed = "Xác thực dữ liệu yêu cầu thất bại cho '{0}': {1}";
            public const string FieldRequired = "Trường {0} là bắt buộc.";
            public const string EmailRequired = "Email là bắt buộc.";
            public const string EmailInvalid = "Email không hợp lệ.";
            public const string EmailMaxLength = "Email không được vượt quá 256 ký tự.";
            public const string PasswordRequired = "Mật khẩu là bắt buộc.";
            public const string PasswordLength8To100 = "Mật khẩu phải từ 8 đến 100 ký tự.";
            public const string PasswordLength6To100 = "Mật khẩu phải từ 6 đến 100 ký tự.";
            public const string PasswordMin8 = "Mật khẩu phải có ít nhất 8 ký tự.";
            public const string UsernameRequired = "Tên đăng nhập là bắt buộc.";
            public const string UsernameLength3To100 = "Tên đăng nhập phải từ 3 đến 100 ký tự.";
            public const string UsernameMax100 = "Tên đăng nhập không được vượt quá 100 ký tự.";
            public const string UsernameOrEmailRequired = "Tên đăng nhập hoặc email là bắt buộc.";
            public const string UsernameOrEmailLength3To256 = "Tên đăng nhập hoặc email phải từ 3 đến 256 ký tự.";
            public const string PhoneInvalid = "Số điện thoại không hợp lệ.";
            public const string PhoneMax50 = "Số điện thoại không được vượt quá 50 ký tự.";
            public const string RoleRequired = "Vai trò là bắt buộc.";
            public const string RoleMax32 = "Vai trò không được vượt quá 32 ký tự.";
            public const string AccountStatusMax32 = "AccountStatus không được vượt quá 32 ký tự.";
            public const string SearchMax100 = "Từ khóa tìm kiếm không được vượt quá 100 ký tự.";
            public const string PageRange1To100 = "Trang phải từ 1 đến 100.";
            public const string PageSizeRange1To100 = "PageSize phải từ 1 đến 100.";
            public const string BioMax1000 = "Tiểu sử không được vượt quá 1000 ký tự.";
            public const string GlobalEloMinZero = "GlobalElo phải lớn hơn hoặc bằng 0.";
            public const string LevelMin1 = "Cấp độ phải ít nhất là 1.";
            public const string FirstNameMax100 = "Tên không được vượt quá 100 ký tự.";
            public const string LastNameMax100 = "Họ không được vượt quá 100 ký tự.";
            public const string AvatarUrlRequired = "URL avatar là bắt buộc.";
            public const string AvatarUrlInvalid = "URL avatar không hợp lệ.";
            public const string BlockReasonRequired = "Lý do khóa là bắt buộc.";
            public const string BlockReasonMax500 = "Lý do khóa không được vượt quá 500 ký tự.";
            public const string RejectionReasonRequired = "Lý do từ chối là bắt buộc.";
            public const string RejectionReasonMax1000 = "Lý do từ chối không được vượt quá 1000 ký tự.";
            public const string CafeNameMax200 = "Tên quán không được vượt quá 200 ký tự.";
            public const string AddressMax500 = "Địa chỉ không được vượt quá 500 ký tự.";
            public const string PhoneNumberMax50 = "Số điện thoại không được vượt quá 50 ký tự.";
            public const string DescriptionMax2000 = "Mô tả không được vượt quá 2000 ký tự.";
            public const string LatitudeRange = "Vĩ độ phải từ -90 đến 90.";
            public const string LongitudeRange = "Kinh độ phải từ -180 đến 180.";
            public const string GoogleIdTokenRequired = "Google idToken là bắt buộc.";
            public const string GoogleIdTokenLength = "Google idToken phải từ 10 đến 4000 ký tự.";
            public const string RefreshTokenRequired = "Refresh token là bắt buộc.";
            public const string RefreshTokenLength = "Refresh token phải từ 20 đến 500 ký tự.";
            public const string VerificationTokenRequired = "Mã xác minh là bắt buộc.";
            public const string VerificationTokenLength = "Mã xác minh phải từ 6 đến 10 ký tự.";
            public const string ResetTokenRequired = "Mã đặt lại mật khẩu là bắt buộc.";
            public const string ResetTokenLength = "Mã đặt lại mật khẩu phải từ 6 đến 10 ký tự.";
            public const string NewPasswordRequired = "Mật khẩu mới là bắt buộc.";
            public const string CurrentPasswordRequired = "Mật khẩu hiện tại là bắt buộc.";
            public const string ConfirmPasswordRequired = "Xác nhận mật khẩu mới là bắt buộc.";
            public const string ConfirmPasswordMismatch = "Xác nhận mật khẩu mới phải trùng mật khẩu mới.";
            public const string NameRequired = "Tên là bắt buộc.";
            public const string NameMax100 = "Tên không được vượt quá 100 ký tự.";
            public const string DateOfBirthFormat = "dateOfBirth phải là chuỗi ngày (yyyy-MM-dd).";
            public const string GameTemplateIdRequired = "GameTemplateId là bắt buộc.";
            public const string LobbyIdRequired = "LobbyId là bắt buộc.";
            public const string OutcomeRequired = "Kết quả trận đấu là bắt buộc.";
            public const string RatingsRequired = "Danh sách đánh giá là bắt buộc.";
            public const string TargetUserIdRequired = "TargetUserId là bắt buộc.";
            public const string TagsRequired = "Thẻ đánh giá là bắt buộc.";
            public const string BarcodeRequired = "Mã vạch là bắt buộc.";
            public const string BarcodeLength = "Mã vạch phải từ 3 đến 50 ký tự.";
            public const string TableIdRequired = "CafeTableId là bắt buộc.";
            public const string BoxQuantityRange = "Số hộp phải từ 1 đến 1000.";
            public const string ComponentIdRequired = "ComponentId là bắt buộc.";
            public const string PenaltyFeeRange = "Phí phạt phải từ 0 đến 999999999.";
            public const string CategoryNameRequired = "Tên thể loại là bắt buộc.";
            public const string CategoryNameLength = "Tên thể loại phải từ 2 đến 100 ký tự.";
            public const string CategorySlugLength = "Slug thể loại phải từ 2 đến 100 ký tự.";
            public const string CategoryDescriptionMax500 = "Mô tả thể loại không được vượt quá 500 ký tự.";
            public const string SortOrderRange = "Thứ tự sắp xếp phải từ 0 đến 9999.";
            public const string ComponentNameRequired = "Tên linh kiện là bắt buộc.";
            public const string ComponentNameLength = "Tên linh kiện phải từ 1 đến 200 ký tự.";
            public const string DefaultQuantityRange = "Số lượng mặc định phải từ 1 đến 9999.";
            public const string ComponentKindRequired = "Loại linh kiện là bắt buộc.";
            public const string ConfigKeyRequired = "ConfigKey là bắt buộc.";
            public const string ConfigKeyLength = "ConfigKey phải từ 2 đến 100 ký tự.";
            public const string ConfigValueRequired = "ConfigValue là bắt buộc.";
            public const string ConfigValueMax500 = "ConfigValue không được vượt quá 500 ký tự.";
            public const string PunishmentActionRequired = "Hành động xử phạt là bắt buộc.";
            public const string SuspendDurationRange = "Thời gian đình chỉ phải từ 1 đến 365 ngày.";
            public const string ReasonRequired = "Lý do là bắt buộc.";
            public const string ReasonLength5To1000 = "Lý do phải từ 5 đến 1000 ký tự.";
            public const string KarmaAdjustmentRange = "Điểm karma phải từ -100 đến 100.";
            public const string OperationalStatusRequired = "Trạng thái vận hành là bắt buộc.";
            public const string OperationalStatusMax32 = "Trạng thái vận hành không được vượt quá 32 ký tự.";
            public const string OperationalStatusReasonMax500 = "Lý do trạng thái không được vượt quá 500 ký tự.";
            public const string CafePartnerCafeNameRequired = "Tên quán là bắt buộc.";
            public const string CafePartnerCafeNameLength = "Tên quán phải từ 5 đến 100 ký tự.";
            public const string CafePartnerAddressRequired = "Địa chỉ là bắt buộc.";
            public const string CafePartnerAddressLength = "Địa chỉ phải từ 10 đến 500 ký tự.";
            public const string CafePartnerPhoneNumberRequired = "Số điện thoại là bắt buộc.";
            public const string CafePartnerPhoneNumberLength = "Số điện thoại phải từ 10 đến 11 ký tự.";
            public const string CafePartnerRepresentativeEmailRequired = "Email đại diện là bắt buộc.";
            public const string CafePartnerBusinessLicenseRequired = "Giấy phép kinh doanh là bắt buộc.";
            public const string CafePartnerBusinessLicenseLength = "Giấy phép kinh doanh phải từ 5 đến 50 ký tự.";
            public const string CafePartnerBusinessLicenseImageRequired = "Ảnh giấy phép kinh doanh là bắt buộc.";
            public const string WorkingHoursRequired = "Giờ làm việc là bắt buộc.";
            public const string PopularGamesListRequired = "Danh sách game phổ biến là bắt buộc.";
            public const string PopularGamesListLength = "Danh sách game phổ biến phải từ 3 đến 2000 ký tự.";
            public const string TableCountRange = "Số bàn phải từ 1 đến 10000.";
            public const string PrivateRoomCountRange = "Số phòng riêng phải từ 0 đến 1000.";
            public const string GamesOwnedRange = "Số game sở hữu phải từ 1 đến 100000.";
        }

        public static class Entity
        {
            public const string ExpiresAtMustBeFuture = "Thời gian hết hạn phải ở tương lai.";
            public const string MinPlayersAtLeastOne = "Số người chơi tối thiểu phải ít nhất 1.";
            public const string MaxPlayersAtLeastOne = "Số người chơi tối đa phải ít nhất 1.";
            public const string PlayTimeMustBePositive = "Thời gian chơi phải lớn hơn 0.";
            public const string DefaultQuantityMustBePositive = "Số lượng mặc định phải lớn hơn 0.";
            public const string BoxQuantityAtLeastOne = "Số hộp phải ít nhất 1.";
            public const string PenaltyFeeCannotBeNegative = "Phí phạt không được âm.";
        }
    }
}
