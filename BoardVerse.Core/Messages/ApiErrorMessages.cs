using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Messages
{
 public static class ApiErrorMessages
 {
 public static string AccountBlocked(string action, string? reason = null) =>
 string.IsNullOrWhiteSpace(reason)
 ? $"{action} bị từ chối. Tài khoản của bạn đã bị khóa. Liên hệ hỗ trợ."
 : $"{action} bị từ chối. Tài khoản của bạn đã bị khóa. Lý do: {reason}";

public static class Auth
    {
    public const string RegisterDuplicate =
    "Tên đăng nhập hoặc email đã được sử dụng.";

    /// <summary>BR-11: minimum age 13 to register.</summary>
    public const string RegisterUnderage =
    "Bạn phải đủ 13 tuổi trở lên để đăng ký tài khoản (BR-11).";

    public const string LoginTooManyAttempts =
 "Sai mật khẩu quá nhiều lần. Thử lại sau 15 phút.";

 public const string LoginInvalidCredentials =
 "Tên đăng nhập/email hoặc mật khẩu không đúng.";

 public const string GoogleTokenMissingEmail =
 "Token Google không chứa địa chỉ email.";

 public const string GoogleTokenValidationFailed =
 "Không thể xác thực token Google.";

 public const string RefreshTokenInvalidOrExpired =
 "Refresh token không hợp lệ hoặc đã hết hạn. Đăng nhập lại.";

 public const string RefreshTokenUserMissing =
 "Refresh token hợp lệ nhưng tài khoản liên kết không còn tồn tại.";

 public const string SendVerificationUserNotFound =
 "Không thể gửi email xác minh. Không tìm thấy tài khoản với email này.";

 public const string VerifyEmailInvalidToken =
 "Xác minh email thất bại. Mã xác minh không hợp lệ.";

 public const string VerifyEmailTokenExpired =
 "Mã xác minh đã hết hạn. Yêu cầu mã mới.";

 public const string RequestPasswordResetUserNotFound =
 "Không thể đặt lại mật khẩu. Không tìm thấy tài khoản với email này.";

 public const string RequestPasswordResetEmailNotVerified =
 "Không thể đặt lại mật khẩu cho đến khi email đã được xác minh.";

 public const string ResetPasswordInvalidToken =
 "Đặt lại mật khẩu thất bại. Mã đặt lại không hợp lệ.";

 public const string ResetPasswordTokenExpired =
 "Mã đặt lại mật khẩu đã hết hạn. Yêu cầu mã mới.";

 public const string ChangePasswordUserNotFound =
 "Không tìm thấy tài khoản đang đăng nhập.";

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
 "Refresh token không hợp lệ.";

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
 "Không tìm thấy tài khoản người dùng.";

 public const string UserNotFoundUpdate =
 "Không tìm thấy tài khoản người dùng.";

 public const string UserNotFoundUpdateProgress =
 "Không tìm thấy tài khoản người dùng.";

 public const string UserNotFoundUpdateAvatar =
 "Không tìm thấy tài khoản người dùng.";

 public const string UserNotFoundKarma =
 "Không tìm thấy tài khoản người dùng.";

 public const string UserNotFoundCreateOrGet =
 "Không tìm thấy tài khoản người dùng.";

 public const string UserNotFoundUpdateLocation =
 "Không tìm thấy tài khoản người dùng.";

 public const string UserNotFoundGetLocation =
 "Không tìm thấy tài khoản người dùng.";

 public const string InvalidLatitudeForLocationUpdate =
 "Cập nhật vị trí thất bại. Vĩ độ phải nằm trong khoảng -90 đến 90.";

 public const string InvalidLongitudeForLocationUpdate =
 "Cập nhật vị trí thất bại. Kinh độ phải nằm trong khoảng -180 đến 180.";

 public const string ProfileNotFoundClearLocation =
 "Không tìm thấy hồ sơ người dùng.";

 public const string NoSavedLocationToClear =
 "Hồ sơ chưa lưu vị trí nào.";

 public static string AcceptFriendRequestsFromInvalid(string allowedList) =>
 $"AcceptFriendRequestsFrom chỉ nhận 1 trong các giá trị: {allowedList}.";
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
 "Không tìm thấy người dùng được chỉ định.";

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

 public const string PartialTiersRequired =
 "Bạn cần cung cấp ít nhất 1 bậc hoàn cọc cho chính sách hoàn một phần.";

 public const string PartialTiersMaxFive =
 "Chính sách hoàn một phần chỉ cho phép tối đa 5 bậc.";

 public const string RefundPercentOutOfRange =
 "Phần trăm hoàn phải nằm trong khoảng 0 đến 100.";

 public const string PartialTiersDuplicateMinHours =
 "Các bậc hoàn không được trùng mốc giờ.";

    public const string PricingLockedWhileOpen =
        "Quán đang trong khung giờ hoạt động. Bạn chỉ có thể chỉnh biểu phí khi quán đóng cửa.";
    }

    public static class CafeShift
    {
        public static string ShiftNotFound(Guid id) =>
            $"Không tìm thấy ca làm việc '{id}'.";

        public static string ShiftAlreadyOpen(Guid existingShiftId) =>
            $"Quán đang có ca làm việc đang mở (ID: '{existingShiftId}'). Đóng ca hiện tại trước khi mở ca mới.";

        public static string ShiftAlreadyClosed(Guid shiftId) =>
            $"Ca làm việc '{shiftId}' đã được đóng.";
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
 "Đã có mục kho đang hoạt động cho game này.";

 public static string ComponentNotInGame(Guid componentId) =>
 $"Linh kiện '{componentId}' không thuộc game đã chọn.";

 public static string ComponentsInvalidForGame() =>
 "Một hoặc nhiều mã linh kiện không thuộc game đã chọn.";
 }

 public static class Pos
 {
public static string AccessForbidden(Guid cafeId) =>
            $"Từ chối truy cập POS. Bạn không có quyền vận hành quán '{cafeId}'.";

        /// <summary>
        /// Lỗi khi payload PUT /pos/tables chứa nhiều bàn có cùng <c>SortOrder</c>.
        /// Trùng SortOrder trong payload khiến Phase 2 không match được bàn cũ theo thứ tự
        /// và sẽ gây nhầm lẫn — phải reject ngay với 400 để FE sửa.
        /// </summary>
        public static string DuplicateSortOrderInPayload(string duplicates) =>
            $"SortOrder không được trùng lặp trong payload: [{duplicates}]. " +
            $"Vui lòng đánh số thứ tự không trùng nhau (0, 1, 2, ...).";

 public const string BarcodeRequired =
 "Mã vạch không được để trống.";

 public static string BoxNotFound(Guid cafeId, string barcode) =>
 $"Không tìm thấy hộp game với mã vạch '{barcode}' trong quán '{cafeId}'.";

 public static string TableNotFound(Guid cafeId, Guid tableId) =>
 $"Không tìm thấy bàn '{tableId}' trong quán '{cafeId}'.";

 public static string TableInUse(Guid tableId) =>
 $"Bàn đang có phiên chơi. Kết thúc phiên trước khi cập nhật.";

 public static string TableNameAlreadyExists(Guid cafeId, string name) =>
 $"Tên bàn '{name}' đã được sử dụng cho bàn khác trong quán '{cafeId}'.";

 public static string TableNotAvailableForGame(Guid tableId) =>
 $"Bàn '{tableId}' đang được giữ hoặc trong sự kiện và không thể nhận game.";

 public static string BoxNotAvailable(string barcode, string status) =>
 $"Hộp game '{barcode}' không khả dụng (trạng thái hiện tại: {status}).";

 public static string BoxAlreadyInSession(string barcode) =>
 $"Hộp game '{barcode}' đang được gán cho một phiên chơi đang hoạt động.";

 public static string SessionNotFound(Guid cafeId, Guid sessionId) =>
 $"Không tìm thấy phiên chơi '{sessionId}' trong quán '{cafeId}'.";

 // BR-12: Component Checklist errors
 public static string SessionGameNotFound(Guid sessionGameId) =>
 $"Không tìm thấy game trong phiên '{sessionGameId}'.";

 public static string ComponentCheckAlreadyDone(Guid sessionGameId) =>
 $"Game trong phiên '{sessionGameId}' đã được kiểm tra linh kiện.";

 public static string ComponentNotBelongToGame(Guid componentId, Guid gameTemplateId) =>
 $"Linh kiện '{componentId}' không thuộc game template '{gameTemplateId}'.";

 public static string InventoryChecklistNotRequired =>
 "Không cần kiểm tra linh kiện. Phiên chơi chưa bắt đầu kiểm kê.";

 // BR-12: Strict checklist enforcement
 public const string ChecklistNotComplete =
 "Hoàn tất kiểm kê linh kiện cho tất cả game trước khi xuất hóa đơn.";

 public static string ChecklistNotCompleteForGames(int remaining) =>
 $"Còn {remaining} game chưa được kiểm kê linh kiện. Kiểm tra đủ trước khi thanh toán.";

 public static string DepositMissingForSettlement =>
 "Phiên chơi này không có deposit để giải ngân.";

 public static string DepositNotPaid =>
 "Deposit chưa ở trạng thái PAID nên không thể chuyển.";

 public static string MasterAccountNotConfigured =>
 "Chưa cấu hình master account để giải ngân.";

 // P1 Fix #1: Lobby state machine
 public const string LobbyCannotLeaveDuringSession =
 "Không thể rời phòng khi phiên chơi đang diễn ra hoặc đã kết thúc.";

 // P1 Fix #2: ActiveSession merge validation
 public static string SessionSourceNotValidForMerge =>
 "Phiên chơi nguồn phải đang hoạt động hoặc đang kiểm tra để có thể ghép thành viên.";

 public static string SessionPaymentWebhookInvalidState(Guid sessionId, string currentStatus) =>
 $"Webhook thanh toán phiên '{sessionId}' không hợp lệ. Trạng thái hiện tại: '{currentStatus}', yêu cầu: UNPAID.";

 public static string BookingAlreadyCheckedIn =>
 "Đơn đặt chỗ này đã được check-in trước đó. Không thể check-in lại.";

 public static string BookingDepositNotPaid =>
 "Đơn đặt chỗ chưa được thanh toán deposit. Liên hệ khách hàng.";

 public static string DepositAlreadyProcessed =>
 "Đơn cọc đã được xử lý thanh toán trước đó.";

 public static string DepositQrRegenerateOnlyPending =>
 "Chỉ có thể tạo lại QR cho đơn cọc đang PENDING.";

 public static string SessionPaymentInvalidState =>
 "Phiên chơi phải ở trạng thái UNPAID để tạo thanh toán.";

 public static string ActiveSessionNotFound(Guid sessionId) =>
 $"Không tìm thấy phiên chơi với ID: {sessionId}";

 // ===== ActiveSession state validation =====
 public const string SessionMustBeCheckingForCheckout =
 "Phiên chơi phải ở trạng thái kiểm kê linh kiện (đã bấm 'Trả game') trước khi thanh toán.";

 public const string SessionMustBeCheckingForPartialCheckout =
 "Phiên chơi phải ở trạng thái kiểm kê linh kiện (đã bấm 'Trả game') trước khi thanh toán một phần.";

 public const string SessionMustBeActiveForEndGame =
 "Phiên chơi phải đang hoạt động để bấm 'Trả game'.";

 public const string SessionNoGamesForEndGame =
 "Phiên chơi chưa có game nào. Gán game trước khi bấm 'Trả game'.";

 public const string SessionMustBeUnpaidForPayment =
 "Phiên chơi phải ở trạng thái chờ thanh toán (UNPAID) để thanh toán.";

 public const string SessionMustBeCheckingForResume =
 "Chỉ có thể khôi phục phiên đang ở trạng thái kiểm kê linh kiện (CHECKING).";

 public const string SessionCannotResumeHasCheckedOutMembers =
 "Phiên đã có thành viên thanh toán. Không thể khôi phục — hãy tiếp tục thanh toán các thành viên còn lại.";

 public const string GuestSlotNotAllowedAfterSessionEnded =
 "Phiên chơi đã kết thúc. Không thể thêm khách vô danh.";

 public const string PartialCheckoutRequiresAtLeastOneMember =
 "Cần chọn ít nhất 1 thành viên để thanh toán một phần.";

 public const string AddMemberRequiresAtLeastOneUser =
 "Cần chọn ít nhất 1 thành viên để thêm vào phiên.";

 public const string OnlyActiveSessionCanAddMembers =
 "Chỉ phiên đang hoạt động mới thêm được thành viên.";

 public static string PartialCheckoutInvalidMemberStatuses(string statuses) =>
 $"Chỉ thành viên đang chơi mới có thể thanh toán một phần. Trạng thái không hợp lệ: {statuses}.";

 public const string PenaltyCannotAssignToGuestSlot =
 "Không thể gán phí phạt cho khách vô danh. Gán vào hóa đơn của người khởi tạo hoặc thu tiền mặt trực tiếp từ người về sớm.";

 public static string ComponentPenaltyMemberNotInSession(Guid componentId, Guid memberId) =>
 $"Không thể gán phí phạt cho linh kiện '{componentId}' vào thành viên '{memberId}': thành viên không thuộc phiên chơi này.";

 public const string GameAlreadyAttachedToSession =
 "Hộp game này đã được gán vào phiên chơi.";

 public const string ChecklistOnlyDuringChecking =
 "Chỉ kiểm kê linh kiện khi phiên đang ở trạng thái kiểm kê (CHECKING).";

 public const string GameDoesNotBelongToSession =
 "Hộp game không thuộc phiên chơi này.";

 public static string SessionCafeMismatch(Guid sessionId, Guid cafeId) =>
 $"Phiên chơi '{sessionId}' không thuộc quán '{cafeId}'.";

        public static string ResumeInvalidStatus(GroupSessionStatus current) =>
 $"Chỉ có thể khôi phục phiên đang ở trạng thái kiểm kê linh kiện. Trạng thái hiện tại: {current}.";

        public const string SessionNotPaused =
            "Phiên chơi không ở trạng thái bị tạm dừng.";

 public static string MergeTargetMustBeActive =>
 "Phiên chơi đích phải đang hoạt động (ACTIVE) để ghép thành viên vào.";

 public const string MergeCannotCrossCafes =
 "Không thể ghép thành viên sang phiên chơi của quán khác.";

 public const string MemberNotInSourceSession =
 "Thành viên không thuộc phiên chơi nguồn.";

 public const string MemberMustBeSuspendedMutationToMerge =
 "Thành viên phải ở trạng thái chờ ghép nhóm (SUSPENDED_MUTATION) để ghép vào nhóm mới.";

 public static string SessionGameNotFoundInSession(Guid sessionGameId) =>
 $"Không tìm thấy game '{sessionGameId}' trong phiên chơi.";

 public static string BoxNotFoundByBarcodeInSession(string barcode) =>
 $"Không tìm thấy hộp game với mã vạch '{barcode}'.";

 public static string BoxAlreadyInUseInOtherSession(string barcode) =>
 $"Hộp game '{barcode}' đang được sử dụng bởi phiên chơi khác.";

 public static string MemberNotFound(Guid memberId) =>
 $"Không tìm thấy thành viên '{memberId}'.";

 public const string BookingNotInThisCafe =
 "Đơn đặt chỗ này không thuộc quán bạn quản lý.";

 public const string NoGameBoxInSession =
 "Không tìm thấy hộp game trong phiên chơi.";

 public const string DepositAmountMustBePositive =
 "Số tiền cọc phải lớn hơn 0.";

 public const string SessionMustBePaidForDepositSettlement =
 "Chỉ giải ngân cọc sau khi phiên chơi đã thanh toán xong.";

 public static string BookingNotFoundByCode(string code) =>
 $"Không tìm thấy đơn đặt chỗ với mã '{code}'.";

 public static string ReservationNotFoundByCode(string code) =>
 $"Không tìm thấy reservation với mã '{code}'.";

 public static string SessionNotFoundById(Guid id) =>
 $"Không tìm thấy phiên chơi '{id}'.";

 public static string BoxNotFoundById(Guid id) =>
 $"Không tìm thấy hộp game '{id}'.";

 public static string BoxCafeMismatch(Guid boxId, Guid cafeId) =>
 $"Hộp game '{boxId}' không thuộc quán '{cafeId}'.";

 public static string SessionMustBeActiveForEnd(string current) =>
 $"Phiên chơi phải đang hoạt động để kết thúc. Trạng thái hiện tại: '{current}'.";

 public static string SessionMustBeActiveForGameAssignment(string current) =>
 $"Chỉ gán game khi phiên đang hoạt động. Trạng thái hiện tại: '{current}'.";
 }

 public static class Wallet
 {
 /// <summary>Top-up amount dưới ngưỡng tối thiểu (BR § II.2).</summary>
 public const string TopUpBelowMinimum =
 "Số tiền nạp tối thiểu là 10.000 VND (= 10 BVC).";

 /// <summary>Top-up amount không chia hết cho 1.000 (BR § II.2).</summary>
 public const string TopUpInvalidMultiple =
 "Số tiền nạp phải là bội số của 1.000 VND.";

 /// <summary>Không tạo được SePay master payment cho top-up.</summary>
 public const string TopUpGatewayFailed =
 "Không tạo được đơn nạp qua cổng thanh toán. Thử lại hoặc dùng mã QR dự phòng.";

 /// <summary>User bị suspended/banned cố top-up (BR-RISK-04).</summary>
 public const string TopUpBlockedAccount =
 "Tài khoản của bạn đang bị tạm khóa, không thể nạp BVC.";

 /// <summary>Không tìm thấy user khi tạo wallet tự động — lỗi hiếm gặp.</summary>
 public const string WalletAutoCreateUserNotFound =
 "Không thể tự tạo ví. Tài khoản không tồn tại hoặc đã bị xóa.";

 /// <summary>Lock row Wallet để atomic trừ/cộng BVC thất bại.</summary>
 public const string WalletLockFailed =
 "Thử lại sau.";

 public static string NotFound(Guid userId) =>
 $"Ví BVC của user '{userId}' chưa được khởi tạo.";

 public static string NotFoundForUser(Guid userId) =>
 $"Ví BVC của user '{userId}' không tồn tại.";

 public static string NotFoundForTargetUser(Guid targetUserId) =>
 $"Không tìm thấy ví BVC của user '{targetUserId}'.";

 public const string SePayCheckoutUrlMissing =
 "Không nhận được URL thanh toán từ SePay master.";

 public const string AmountMustBePositive =
 "Số BVC phải lớn hơn 0.";

 public const string AdjustmentReasonRequired =
 "Lý do điều chỉnh là bắt buộc (audit).";

 public const string IdempotencyKeyRequired =
 "Idempotency key là bắt buộc.";

 /// <summary>Không tìm thấy top-up request theo id (cho cancel/update).</summary>
 public static string TopUpNotFound(Guid topUpId) =>
 $"Không tìm thấy đơn top-up BVC '{topUpId}'.";

 /// <summary>Chỉ đơn đang Pending mới có thể hủy.</summary>
 public const string TopUpNotCancellable =
 "Chỉ có thể hủy đơn top-up đang ở trạng thái chờ thanh toán (Pending).";

 /// <summary>Chỉ đơn đang Pending mới có thể đổi số tiền.</summary>
 public const string TopUpNotUpdateable =
 "Chỉ có thể đổi số tiền cho đơn top-up đang ở trạng thái chờ thanh toán (Pending).";

 /// <summary>Player cố hủy/update đơn top-up của user khác.</summary>
 public const string TopUpNotOwned =
 "Bạn không có quyền thao tác trên đơn top-up này.";

 public const string TopUpIdInvalid =
 "Id đơn top-up không hợp lệ.";

 // ===== BVC Refund Request (player → admin review) =====
 public static string RefundRequestNotFound(Guid id) =>
 $"Không tìm thấy yêu cầu hoàn BVC '{id}'.";

 public const string RefundRequestNotPending =
 "Chỉ có thể thao tác trên yêu cầu hoàn BVC đang chờ xử lý (Pending).";

 public const string RefundRequestNotOwned =
 "Bạn không có quyền thao tác trên yêu cầu hoàn BVC này.";

 public const string RefundRequestInvalidAmount =
 "Số BVC yêu cầu hoàn phải lớn hơn 0.";

 public const string RefundRequestLedgerEntryNotFound =
 "Không tìm thấy ledger entry được tham chiếu. Kiểm tra lại id từ /api/v1/wallet/transactions.";

 public const string RefundRequestLedgerEntryNotOwned =
 "Ledger entry này không thuộc tài khoản của bạn.";

 public const string RefundRequestReasonTooShort =
 "Lý do hoàn cọc phải có ít nhất 20 ký tự để admin có đủ thông tin xem xét.";

 public const string RefundRequestAdminNoteRequired =
 "Admin phải ghi chú lý do duyệt hoặc từ chối (audit).";

 public static string RefundRequestApproveAmountInvalid =>
 "Số BVC duyệt hoàn phải lớn hơn 0 khi Decision = Approve.";

 // ===== BookingDepositService specific =====
 public static string DepositMarkAsPaidInvalidStatus(string current) =>
 $"Đơn cọc đã được xử lý hoặc không ở trạng thái chờ. Trạng thái hiện tại: '{current}'.";

 public static string DepositRefundInvalidStatus(string current) =>
 $"Chỉ hoàn cọc khi đơn đang ở trạng thái đã thanh toán. Trạng thái hiện tại: '{current}'.";

 public static string DepositForfeitInvalidStatus(string current) =>
 $"Chỉ tịch thu cọc khi đơn đang ở trạng thái đã thanh toán. Trạng thái hiện tại: '{current}'.";

 public static string DepositForfeitInvalidPolicy(string current) =>
 $"Chỉ tịch thu cọc khi chính sách hoàn tiền là 'không hoàn'. Chính sách hiện tại: '{current}'.";

 public static string DepositNotFound(Guid id) =>
 $"Không tìm thấy đơn cọc '{id}'.";

 public static string ActiveSessionNotFound(Guid id) =>
 $"Không tìm thấy phiên chơi '{id}'.";

 // ===== ManualPaymentService specific =====
 public static string InvalidPaymentType(string input) =>
 $"PaymentType không hợp lệ '{input}'. Chỉ chấp nhận Deposit hoặc Session.";

 public static string InvalidPaymentMethod(string input) =>
 $"PaymentMethod không hợp lệ '{input}'.";

 public static string DepositNotPending(string current) =>
 $"Đơn cọc không ở trạng thái chờ. Trạng thái hiện tại: '{current}'.";

 public static string SessionNotUnpaid(string current) =>
 $"Phiên chơi không ở trạng thái chờ thanh toán. Trạng thái hiện tại: '{current}'.";

 public const string DepositForbidden =
 "Bạn không có quyền xem đơn cọc này.";

 public static string DepositNotFoundById(Guid id) =>
 $"Không tìm thấy đơn cọc với ID: {id}.";

 public static string DepositNotFoundByOrderId(string orderId) =>
 $"Không tìm thấy đơn cọc với mã đặt chỗ: {orderId}.";
 }

 public static class Payment
 {
 public const string SePayMasterAccountNotFound =
 "Chưa cấu hình tài khoản SePay Master. Liên hệ admin.";

 public const string SePayMerchantIdMissing =
 "Tài khoản SePay Master chưa có MerchantId. Cập nhật cấu hình.";

public const string SePayWebhookTokenMissing =
"Dịch vụ thanh toán chưa được cấu hình. Hãy đặt SePay:WebhookToken.";

public const string SePayReturnSuccess =
"Thanh toán thành công! Vui lòng quay lại ứng dụng.";

public const string SePayReturnFailed =
"Thanh toán thất bại hoặc bị hủy.";

public const string SePayMockEndpointBlocked =
"Mock endpoint chỉ khả dụng trong môi trường Development.";

public const string SePayWebhookProcessingFailed =
"Đã xảy ra lỗi khi xử lý webhook SePay.";

public const string SePayMockWebhookProcessingFailed =
"Đã xảy ra lỗi khi xử lý mock webhook.";

public const string SePayMasterAccountNotCreated =
"Master account SePay chưa được tạo.";

public const string SePayCafeNotConfigured =
"Cafe của bạn chưa được cấu hình SePay.";

public const string SePayOrderIdRequired =
"orderId là bắt buộc.";

public const string SePayCafeIdRequired =
"CafeId là bắt buộc cho loại tài khoản Cafe.";

public static string SePayCafeAccountExists(Guid cafeId) =>
$"Cafe '{cafeId}' đã có tài khoản SePay.";

public const string SePayMasterAccountExists =
"Tài khoản Master SePay đã tồn tại.";

public static string SePayInvalidEnvironment(string environment) =>
$"Môi trường SePay không hợp lệ '{environment}'. Chỉ chấp nhận 'Test' hoặc 'Production'.";

public static string SePayAccountNotFound(Guid id) =>
$"Không tìm thấy SePay account '{id}'.";

public static string DebugSePayCafeNotFound(Guid cafeId) =>
$"Không tìm thấy cafe '{cafeId}'. Chạy seeder trước.";

public static string DebugSePayCafeNotFoundShort(Guid cafeId) =>
$"Không tìm thấy cafe '{cafeId}'.";

public const string SePayResponseInvalid =
"SePay trả về dữ liệu thanh toán không hợp lệ.";

public static string SePayCreatePaymentFailed(int statusCode, string details) =>
$"Tạo link thanh toán SePay thất bại ({statusCode}). {details}";

public static string SePayTransferFailed(int statusCode, string details) =>
$"Chuyển khoản SePay thất bại ({statusCode}). {details}";

public static string SePayTransferFailed(string code, string details) =>
$"Chuyển khoản SePay thất bại ({code}). {details}";

public const string GatewayCannotCreatePayment =
"Thử lại sau.";

public static string GatewayCannotCreatePaymentWithError(string errorMessage) =>
$"Không thể tạo thanh toán: {errorMessage}";

public const string GatewayQrUrlMissing =
"Không nhận được QR URL từ gateway.";

public static string QrRegenerateInvalidState(string currentStatus) =>
$"Chỉ tạo lại QR cho đơn cọc đang chờ thanh toán. Trạng thái hiện tại: '{currentStatus}'.";

public static string QrRegenerateRateLimited(int secondsRemaining) =>
$"QR vừa tạo lại. Chờ {secondsRemaining} giây rồi thử lại.";

// ===== C1/C3/C4/C5/C6/C7: Cross-tenant IDOR guard =====
// Tái sử dụng message này cho các guard deposit/booking/operator ownership
// (avoid leaking "exists vs. not exists" tới caller khác tenant).
public const string NotAuthorizedToViewDeposit =
"Bạn không có quyền thao tác trên đơn cọc này.";

// ===== C1: Deposit ownership guard =====
public const string DepositForbidden =
"Bạn không có quyền thao tác trên đơn cọc này.";

public static string DepositNotFoundByOrderId(string orderId) =>
$"Không tìm thấy đơn cọc với mã đặt chỗ '{orderId}'.";

public static string DepositNotFoundById(Guid depositId) =>
$"Không tìm thấy đơn cọc với mã định danh '{depositId}'.";

// ===== BookingDepositService specific (C3, was in Wallet) =====
public static string DepositMarkAsPaidInvalidStatus(string current) =>
$"Đơn cọc đã được xử lý hoặc không ở trạng thái chờ. Trạng thái hiện tại: '{current}'.";

public static string DepositRefundInvalidStatus(string current) =>
$"Chỉ hoàn cọc khi đơn đang ở trạng thái đã thanh toán. Trạng thái hiện tại: '{current}'.";

public static string DepositForfeitInvalidStatus(string current) =>
$"Chỉ tịch thu cọc khi đơn đang ở trạng thái đã thanh toán. Trạng thái hiện tại: '{current}'.";

public static string DepositForfeitInvalidPolicy(string current) =>
$"Chỉ tịch thu cọc khi chính sách hoàn tiền là 'không hoàn'. Chính sách hiện tại: '{current}'.";

public static string DepositNotFound(Guid id) =>
$"Không tìm thấy đơn cọc '{id}'.";

public static string ActiveSessionNotFound(Guid id) =>
$"Không tìm thấy phiên chơi '{id}'.";

// ===== ManualPaymentService specific =====
public static string InvalidPaymentType(string input) =>
$"PaymentType không hợp lệ '{input}'. Chỉ chấp nhận Deposit hoặc Session.";

public static string InvalidPaymentMethod(string input) =>
$"PaymentMethod không hợp lệ '{input}'.";

public static string DepositNotPending(string current) =>
$"Đơn cọc không ở trạng thái chờ. Trạng thái hiện tại: '{current}'.";

public static string SessionNotUnpaid(string current) =>
    $"Phiên chơi không ở trạng thái chờ thanh toán. Trạng thái hiện tại: '{current}'.";

    // ===== ManualPaymentService authorization (C3) =====
    public static string ManualConfirmNotAuthorizedForCafe(Guid cafeId) =>
    $"Bạn không có quyền xác nhận thanh toán cho quán '{cafeId}'.";

    // ===== ManualPaymentService amount validation (H5) =====
    public static string ManualConfirmAmountMismatch(decimal expected, decimal received) =>
    $"Số tiền xác nhận ({received:N0} VND) không khớp với đơn hàng ({expected:N0} VND).";

    public const string SessionPaymentAmountMustBePositive =
    "Số tiền thanh toán phải lớn hơn 0.";

 public static string PaymentCafeNotConfiguredSePay(string cafeName) =>
 $"Cafe '{cafeName}' chưa được cấu hình SePay account.";

 public static string RefundInvalidDepositStatus(string currentStatus) =>
 $"Không thể hoàn cọc. Trạng thái hiện tại: '{currentStatus}', yêu cầu: Paid.";

 public const string RefundReasonRequired =
 "Lý do hoàn cọc là bắt buộc để phục vụ audit.";

 // ===== SePayAccountService specific =====
 public const string ManagerHasNoCafe =
 "Bạn không quản lý cafe nào.";

 public const string CafeSePayAccountNotConfigured =
 "Cafe của bạn chưa được cấu hình SePay.";

 public static string CafePaymentAccountBankCodeRequired =>
 "Cấu hình payment account cho cafe thiếu 'bankCode'.";

 public static string CafePaymentAccountAccountNumberRequired =>
 "Cấu hình payment account cho cafe thiếu 'accountNumber'.";

 public static string CafePaymentAccountAccountHolderRequired =>
 "Cấu hình payment account cho cafe thiếu 'accountHolder'.";

 public static string CafePaymentAccountAlreadyExists(Guid cafeId) =>
 $"Cafe '{cafeId}' đã có payment account. Dùng PUT /api/sepay-accounts/my-cafe để cập nhật.";

 public const string SePayBankInfoIncomplete =
 "Payment account của cafe thiếu thông tin ngân hàng (bankCode/accountNumber/accountHolder). Cập nhật lại trước khi test.";
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

 public static class Booking
 {
 public const string InvalidTimeRange =
 "Khoảng thời gian không hợp lệ. Thời gian kết thúc phải sau thời gian bắt đầu.";

 public const string SeatCountMustBePositive =
 "Số lượng ghế yêu cầu phải lớn hơn hoặc bằng 1.";

 public const string NotCheckedInYet =
 "Booking phải ở trạng thái CheckedIn mới có thể vote vắng mặt.";

 public const string AlreadyCheckedOut =
 "Booking đã kết thúc — không thể vote vắng mặt sau khi check-out.";

 public const string VoteWindowClosed =
 "Đã quá thời hạn vote vắng mặt (chỉ được vote trong 24h sau check-in).";

 public const string VoterNotCheckedInMember =
 "Chỉ thành viên đã check-in mới có thể vote vắng mặt.";

 public const string CannotVoteSelfAbsent =
 "Bạn không thể vote chính mình vắng mặt.";

 public const string RatingWindowClosed =
 "Đã quá thời hạn chấm điểm (chỉ được chấm trong 24h sau check-out).";

 public const string CannotRateSelf =
 "Bạn không thể tự chấm điểm cho mình.";

 public const string DuplicateRatedUser =
 "Danh sách chấm điểm chứa thành viên bị trùng lặp.";

 public const string AlreadySubmittedRatings =
 "Bạn đã gửi lượt chấm điểm cho booking này rồi.";

 public static string LobbyNotFoundForBooking(Guid lobbyId) =>
 $"Không tìm thấy phòng chờ '{lobbyId}'.";

 public const string OnlyLobbyHostCanCreateBooking =
 "Chỉ Host của phòng chờ mới có thể tạo booking.";

 public const string LobbyMustBeFullToCreateBooking =
 "Phòng chờ phải ở trạng thái Full (đã khóa) mới có thể tạo booking.";

 public const string LobbyAlreadyHasBooking =
 "Phòng chờ này đã có booking được tạo trước đó.";

 public static string CafeNotFound(Guid cafeId) =>
 $"Không tìm thấy quán cafe '{cafeId}'.";

 public static string TableNotFound(Guid tableId) =>
 $"Không tìm thấy bàn '{tableId}'.";

 public const string TableNotInCafe =
 "Bàn không thuộc quán đã chọn.";

 public const string StartTimeInPast =
 "Thời gian bắt đầu không được là thời điểm trong quá khứ.";

 public const string TableAlreadyBookedInTimeRange =
 "Bàn đã có booking khác trong khoảng thời gian này.";

 public static string NotFound(Guid bookingId) =>
 $"Không tìm thấy booking '{bookingId}'.";

 public const string NotBookingOwner =
 "Bạn không có quyền cập nhật booking này.";

 public const string CannotUpdateBookingInCurrentState =
 "Không thể cập nhật booking ở trạng thái này.";

 public const string TableNotInBookingCafe =
 "Bàn không thuộc quán của booking này.";

 public const string CannotCancelCheckedInBooking =
 "Không thể hủy booking đã check-in.";

 public static string OnlyPendingDepositCanConfirm(BookingStatus status) =>
 $"Chỉ booking ở trạng thái PendingDeposit mới có thể xác nhận (hiện tại: {status}).";

 public const string OnlyConfirmedOrPendingDepositCanNoShow =
 "Chỉ booking ở trạng thái Confirmed hoặc PendingDeposit mới có thể NoShow.";

 public const string NotMemberOfBooking =
 "Bạn không phải thành viên của booking này.";

 // ===== BookingRatingService specific =====
 public static string BookingIdMismatch =>
 "BookingId trong URL và body không khớp.";

 public static string BookingNotFoundById(Guid bookingId) =>
 $"Không tìm thấy booking '{bookingId}'.";

 public static string LobbyNotFoundById =>
 "Không tìm thấy phòng chờ liên kết với booking.";

 public const string WalkInBookingNoShowVoteNotSupported =
 "Booking walk-in chưa hỗ trợ vote no-show.";

 public const string WalkInBookingRatingNotSupported =
 "Booking walk-in chưa hỗ trợ chấm điểm chéo.";

 public const string OnlyLobbyMemberCanRate =
 "Chỉ thành viên phòng chờ mới có thể chấm điểm.";

 public const string OnlyLobbyMemberCanViewRating =
 "Chỉ thành viên phòng chờ mới có thể xem trạng thái chấm điểm.";

 public const string WalkInBookingHasNoRating =
 "Booking walk-in không có chấm điểm chéo.";

 public const string RatingScoreOutOfRange =
 "Điểm attitude/sportsmanship/punctuality phải nằm trong khoảng 1 đến 5.";

 public const string RatingCommentTooLong =
 "Bình luận không được vượt quá 500 ký tự.";

 public const string BookingNotYetEligibleForRating =
 "Booking chưa thể chấm điểm. Cần check-in trước.";

 public static string VoteOpensAtTime(DateTime opensAt) =>
 $"Bạn chỉ có thể vote sau khi booking đã check-in được 30 phút (mở lúc {opensAt:o}).";

 public static string NotLobbyMemberIdsJoin(IEnumerable<Guid> ids) =>
 $"Các UserId không thuộc phòng chờ: {string.Join(", ", ids)}.";

 public static string CannotAggregateBookingStatus(BookingStatus status) =>
 $"Không thể tổng hợp kết quả booking ở trạng thái '{status}'.";
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
 "Thiếu bản ghi quán liên kết.";

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
 "Yêu cầu email Brevo đã hết thời gian chờ. Thử lại sau.";

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
 $"Phiên chưa đủ điều kiện mở cửa sổ đánh giá.";
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

        public const string KarmaAdjustmentRange =
        "Số điểm điều chỉnh karma phải từ -100 đến 100.";

        public const string CannotPunishAdmin =
        "Không thể xử phạt tài khoản Admin qua endpoint này.";

        public const string UserNotInCoolingOff =
        "Người dùng này không đang trong trạng thái cooling-off.";

        public static string WalletNotFound(Guid userId) =>
        $"Không tìm thấy ví của người dùng '{userId}'.";

        public static string ProfileNotFound(Guid userId) =>
        $"Người dùng '{userId}' chưa có hồ sơ.";

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
 $"Đang được tham chiếu bởi phí kho quán.";

 public static string InvalidComponentKind(int kind) =>
 $"Giá trị loại linh kiện '{kind}' không hợp lệ.";

 public static string CategoriesNotFound(IReadOnlyCollection<Guid> missingIds) =>
 $"Một hoặc nhiều thể loại không tồn tại: {string.Join(", ", missingIds)}.";
 }

 public static class Jwt
 {
 public const string MissingUserIdentifier =
 "Access token thiếu mã định danh người dùng hợp lệ. Đăng nhập lại.";

 public const string UserNoLongerExists =
 "Tài khoản không còn tồn tại. Đăng nhập lại.";

 public const string TokenExpired =
 "Access token đã hết hạn. Dùng POST /api/auth/refresh-token hoặc đăng nhập lại.";

 public const string TokenInvalidSignature =
 "Chữ ký access token không hợp lệ. Đăng nhập lại.";

 public const string TokenInvalid =
 "Access token không hợp lệ hoặc sai định dạng. Đăng nhập lại.";

 public const string AuthenticationFailed =
 "Xác thực thất bại. Đăng nhập lại.";

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
 "Tài khoản của bạn đã bị vô hiệu hóa. Liên hệ hỗ trợ để kích hoạt lại.";

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

 public static string InvalidQueryParameter(string name, string allowedValues)
 => $"Giá trị tham số '{name}' không hợp lệ. Cho phép: {allowedValues}.";
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
 public const string BasePriceRange = "Giá cơ bản phải từ 0 đến 10000000.";
 public const string TieredBlockMinutesRange = "Thời gian block tính tiền phải từ 1 đến 1440 phút.";
 public const string TieredBlockRateRequired = "Với mô hình TIME_BASED, giá block lũy tiến là bắt buộc.";
 public const string DepositPercentageRange = "Phần trăm cọc không được vượt quá 50%.";
 public const string SeatsPerTableRange = "Số ghế mỗi bàn phải từ 1 đến 50.";
 public const string TableNameLength = "Tên bàn phải từ 1 đến 100 ký tự.";
 public const string TableNoFieldsToUpdate = "Cần gửi ít nhất một trường để cập nhật (Name, SeatCount hoặc SortOrder).";

 public const string LobbySearchLimitRange = "Limit phải nằm trong khoảng 1 đến 100.";
 public const string LobbySearchGeoRequired = "latitude, longitude, radiusKm phải truyền đồng thời nếu muốn filter theo khu vực.";
 public const string LobbySearchRadiusRange = "radiusKm phải nằm trong khoảng (0, 500] km.";

public const string FriendSearchMinLength = "Từ khóa tìm kiếm phải có ít nhất 2 ký tự.";

public static string FriendInvalidDirection(string validValues) =>
$"Direction không hợp lệ. Giá trị hợp lệ: {validValues}";
}

 public static class Friend
 {
 public static string UserNotFound(Guid userId) =>
 $"Không tìm thấy người dùng '{userId}'.";

 public const string CannotSendToSelf =
 "Không thể gửi lời mời kết bạn cho chính mình.";

 public const string PendingRequestAlreadyExists =
 "Đã có lời mời kết bạn đang chờ với người dùng này.";

 public const string AlreadyFriends =
 "Bạn và người dùng này đã là bạn bè.";

 public const string NotFriendRequestRecipient =
 "Bạn không phải người nhận của lời mời kết bạn này.";

 public const string FriendRequestNotPending =
 "Lời mời kết bạn này không ở trạng thái chờ phản hồi.";

 public static string FriendshipNotFound(Guid id) =>
 $"Không tìm thấy quan hệ bạn bè '{id}'.";

 public const string CannotRemoveAcceptedByOther =
 "Chỉ có thể xóa quan hệ bạn bè khi nó đang ở trạng thái Accepted.";

 public const string CannotCancelRequestNotRequester =
 "Chỉ người gửi lời mời mới có thể hủy lời mời kết bạn này.";

 public const string CannotCancelNonPendingRequest =
 "Chỉ có thể hủy lời mời kết bạn đang ở trạng thái chờ phản hồi (Pending).";

 public const string CannotViewRequestNotMember =
 "Bạn không có quyền xem lời mời kết bạn này vì bạn không phải một bên của quan hệ.";

 public const string CannotViewBlockedRequest =
 "Không thể xem chi tiết quan hệ đang bị chặn bởi người dùng khác.";

 public const string BlockedByOtherParty =
 "Bạn đã bị người dùng này chặn.";

 public const string AlreadyBlockedOtherParty =
 "Bạn đã chặn người dùng này. Hãy bỏ chặn trước khi gửi lời mời.";

 public const string RequesterNotActive =
 "Tài khoản người gửi không còn hoạt động.";

 public const string AddresseeNotActive =
 "Tài khoản của bạn không ở trạng thái hoạt động.";

 public const string RateLimitExceeded =
 "Bạn đã gửi quá nhiều lời mời kết bạn. Chờ vài phút rồi thử lại.";

 public const string FriendListPrivate =
 "Người dùng này đã ẩn danh sách bạn bè.";

 public const string CannotBlockAdmin =
 "Không thể chặn tài khoản quản trị viên.";

 public const string CannotBlockSelf =
 "Không thể chặn chính mình.";

 public const string CannotReportSelf =
 "Không thể báo cáo chính mình.";

 public const string CannotReportAdmin =
 "Không thể báo cáo tài khoản quản trị viên.";

 public const string CannotReportNotFriend =
 "Chỉ có thể báo cáo người dùng đang là bạn bè hoặc đã từng kết bạn.";

 public static string ReportReasonRequired =>
 "Lý do báo cáo là bắt buộc và phải từ 5 đến 1000 ký tự.";

 public static string ReportAlreadyExists(Guid targetUserId) =>
 $"Bạn đã gửi báo cáo cho người dùng '{targetUserId}' và đang được xử lý.";

 public static string ReportNotFound(Guid id) =>
 $"Không tìm thấy báo cáo '{id}'.";

 public const string CannotSuggestToSelf =
 "Không thể lấy gợi ý kết bạn cho chính mình.";

 public const string NoSuggestionsAvailable =
 "Hiện chưa có gợi ý kết bạn phù hợp với bạn.";

 public const string CannotViewOwnFriendList =
 "Không thể truy vấn chính mình qua endpoint này. Hãy dùng GET /api/v1/friends.";

 public const string CannotViewOwnProfile =
 "Không thể truy vấn profile của chính mình qua endpoint này. Hãy dùng GET /api/v1/players/me.";

 public const string CannotNoteSelf =
 "Không thể tạo ghi chú cho chính mình.";

 public static string NoteNotFound(Guid noteId) =>
 $"Không tìm thấy ghi chú '{noteId}'.";

 public static string NoteNotOwner(Guid noteId) =>
 $"Bạn không phải chủ sở hữu của ghi chú '{noteId}'.";

 public const string PrivacyRequestNotAccepting =
 "Người dùng này đã tắt nhận lời mời kết bạn từ người lạ.";

 public const string CannotSendRequestToInactive =
 "Tài khoản người nhận không hoạt động nên không thể gửi lời mời.";

 public const string CannotRemoveFriendshipNotMember =
 "Bạn không có quyền xóa quan hệ bạn bè này.";

 public const string CannotBlockInactiveAccount =
 "Không thể chặn tài khoản không hoạt động.";

 public const string UnblockNotFound =
 "Không có quan hệ chặn nào giữa bạn và người dùng này để bỏ chặn.";

 public const string CannotUnblockNotBlocker =
 "Bạn không phải người đã chặn người dùng này.";

 public const string ProfileNotYetCreated =
 "Hồ sơ người dùng chưa được tạo. Hoàn tất hồ sơ trước khi sử dụng tính năng này.";

 public const string BlockedCannotViewProfile =
 "Phía bên kia đã chặn bạn nên không thể xem hồ sơ.";
 }

 public static class LobbyInvite
 {
 public static string InviteNotFound(Guid id) =>
 $"Không tìm thấy lời mời '{id}'.";

 public const string CannotInviteSelf =
 "Không thể mời chính mình vào phòng chờ.";

 public const string InviteeAlreadyMember =
 "Người được mời đã là thành viên của phòng chờ.";

 public const string PendingInviteAlreadyExists =
 "Đã có lời mời đang chờ với người dùng này cho lobby này.";

 public const string InviterNotMember =
 "Chỉ thành viên của phòng chờ mới có thể gửi lời mời.";

 public const string InviteNotPending =
 "Lời mời này không ở trạng thái chờ phản hồi.";

 public const string NotInviteRecipient =
 "Bạn không phải người nhận của lời mời này.";

 public const string InviteExpired =
 "Lời mời đã hết hạn hoặc lobby không còn khả dụng.";

 public const string PrivateLobbyRequiresInvite =
 "Phòng chờ riêng tư chỉ có thể tham gia qua lời mời hoặc share code.";

 public const string PrivateLobbyShareCodeRequiresFriendship =
 "Phòng chờ riêng tư chỉ cho phép người có quan hệ bạn bè với thành viên tham gia bằng share code.";

 public static string ShareCodeInvalid =>
 "Mã chia sẻ không hợp lệ hoặc không tồn tại.";

    public const string InviteRateLimitExceeded =
    "Bạn đã gửi/nhận quá nhiều lời mời trong ngày. Thử lại sau.";

    // L-01: Share code brute-force protection
    public const string ShareCodeRateLimitExceeded =
    "Bạn đã thử quá nhiều mã chia sẻ. Vui lòng chờ 15 phút rồi thử lại.";

 // ===== LobbyInviteService specific =====
 public const string LobbyClosedOrUnavailable =
 "Phòng chờ đã đóng hoặc không còn khả dụng.";

 public const string PrivateLobbyInviterMustBeFriend =
 "Phòng chờ riêng tư chỉ cho phép mời bạn bè đã chấp nhận.";

 public const string LobbyDisappeared =
 "Phòng chờ không còn tồn tại.";

 public const string LobbyFullCannotAcceptInvite =
 "Phòng chờ đã đủ người. Không thể chấp nhận lời mời này.";

 public const string PrivateLobbyRequiresActiveFriendship =
 "Phòng chờ riêng tư yêu cầu quan hệ bạn bè đang hoạt động.";

 public const string OnlyInviterCanCancel =
 "Chỉ người gửi lời mời mới có thể hủy lời mời.";

 public const string OnlyLobbyMemberCanViewShareCode =
 "Chỉ thành viên phòng chờ mới có thể xem mã chia sẻ.";

 public static string InviteInvalidStatus(string status) =>
 $"Trạng thái lời mời không hợp lệ: '{status}'.";
 }

 public static class Lobby
 {
 public static string NotFound(Guid lobbyId) =>
 $"Không tìm thấy phòng chờ '{lobbyId}'.";

 public const string LobbyNotFoundById =
 "Không tìm thấy phòng chờ yêu cầu.";

 public const string AlreadyMember =
 "Bạn đã là thành viên của phòng này.";

 public const string NotMember =
 "Bạn không phải là thành viên của phòng này.";

 public const string NotOpen =
 "Phòng chờ này không còn mở.";

 public const string AlreadyClosed =
 "Phòng chờ đã đóng.";

 public const string SeatCountExceeded =
 "Số thành viên đã vượt quá số ghế cho phép.";

 public static string MaxMembersOutOfRange(int min, int max, int requested) =>
 $"Số người tối đa ({requested}) phải nằm trong khoảng [{min}, {max}].";

 public static string MinPlayersInvalid(int currentCount, int minPlayers) =>
 $"Phòng chờ cần ít nhất {minPlayers} người để khóa (hiện có {currentCount}).";

 public const string HostCannotKickSelf =
 "Host không thể tự kick mình. Hãy dùng Leave thay thế.";

 public const string NotHost =
 "Chỉ Host mới có thể thực hiện thao tác này.";

 public const string AlreadyHost =
 "Bạn đã là Host rồi.";

 public const string CannotReportOwnLobby =
 "Bạn không thể báo cáo phòng chờ mà bạn là Host.";

 public static string NotActiveMember(Guid lobbyId) =>
 $"Bạn không phải thành viên đang hoạt động của phòng '{lobbyId}'.";

 // ===== LobbyMessage domain =====
 public static class Message
 {
 public const string ContentLength =
 "Tin nhắn phải có từ 1 đến 1000 ký tự.";

 public const string NotLobbyMember =
 "Bạn không phải thành viên của phòng chờ này.";
 }

 public static string LockerIdMessage_NotFound(string id) =>
 $"Không tìm thấy phòng chờ '{id}'.";

            public static string VenueCapacityFull(int availableSeats, int requestedSeats) =>
                $"Quán không đủ ghế. Còn {availableSeats} ghế trống, cần thêm {requestedSeats}.";

            // P1 Fix #1: Prevent leaving during in-progress or closed states
            public const string CannotLeaveLobbyDuringSession =
                "Không thể rời phòng khi phiên chơi đang diễn ra hoặc đã kết thúc.";

            public static string UserNotFoundInLobbyContext(Guid userId) =>
                $"Không tìm thấy người dùng '{userId}'.";

 // ===== Lobby create / update validation =====
 public const string ScheduledStartTimeTooEarly =
 "Thời gian bắt đầu dự kiến phải cách hiện tại ít nhất 5 phút.";

 public const string MinPlayersOutOfRangeForCreate =
 "Số người tối thiểu phải từ 2 đến MaxMembers.";

 public const string SeatCountInvalidForLobby =
 "SeatCount không hợp lệ so với MaxMembers.";

 public const string CafeDoesNotHaveGame =
 "Quán đã chọn không có sẵn game này trong kho.";

 public const string BookingNotFound =
 "Không tìm thấy đơn đặt chỗ đi kèm lobby.";

 public const string NotBookingOwner =
 "Bạn không phải chủ sở hữu đơn đặt chỗ này.";

            public const string BookingNotPaid =
                "Đơn đặt chỗ chưa được xác nhận thanh toán.";

            public const string MemberAlreadyInLobby =
 "Bạn đã là thành viên của phòng này.";

 public const string KarmaRequirementNotMetForLobby =
 "Điểm uy tín của bạn không đạt yêu cầu tối thiểu để tham gia phòng chờ này.";

 public const string PrivateLobbyRequiresLogin =
 "Phòng chờ riêng tư. Cần đăng nhập để xem.";

 public const string PrivateLobbyNoAccess =
 "Bạn không có quyền xem phòng chờ riêng tư này.";

 public const string OnlyHostCanClose =
 "Chỉ Host mới có thể đóng phòng chờ.";

 public const string LobbyAlreadyClosed =
 "Phòng chờ đã đóng.";

 public const string OnlyHostCanLock =
 "Chỉ Host mới có thể khóa phòng chờ.";

 public const string OnlyHostCanDissolve =
 "Chỉ Host mới có thể giải tán phòng chờ.";

 public const string LobbyNotOpenForLock =
 "Phòng chờ không ở trạng thái mở.";

 public const string OnlyHostCanOpenRating =
 "Chỉ Host mới có thể mở cửa sổ đánh giá.";

 public const string OnlyFullLobbyCanInProgress =
 "Chỉ phòng ở trạng thái FULL mới chuyển sang IN_PROGRESS được.";

 public const string OnlyInProgressCanClose =
 "Chỉ phòng đang chơi hoặc đang đánh giá mới đóng được.";

 public const string CannotSwitchHostWhenClosed =
 "Chỉ chuyển host được khi phòng đang mở hoặc đầy.";

 public const string NotCurrentHost =
 "Bạn không phải Host hiện tại của phòng này.";

 public const string TargetMemberNotInLobby =
 "Thành viên được chọn không còn trong phòng.";

 public const string CannotKickWhenClosed =
 "Không thể kick thành viên khi phòng đã đóng.";

 public const string OnlyHostCanKick =
 "Chỉ Host mới có thể kick thành viên.";

 public const string OnlyHostCanUpdate =
 "Chỉ Host mới có thể cập nhật phòng chờ.";

 public const string LobbyUpdateNotAllowedWhenClosed =
 "Không thể cập nhật phòng chờ đã đóng hoặc đang chơi.";

 public const string CannotReduceMaxMembersWhenFull =
 "Không thể giảm MaxMembers khi phòng đã đầy.";

 public const string GameTemplateNotFound =
 "Không tìm thấy thông tin game.";

 public static string MaxMembersExceedsGameRange(int requested, int min, int max) =>
 $"Số người tối đa ({requested}) phải nằm trong khoảng [{min}, {max}] của game.";

 public const string CannotReduceMaxMembersBelowCurrent =
 "Không thể giảm MaxMembers xuống dưới số thành viên hiện tại.";

 public static string MinPlayersOutOfRange(int min, int max) =>
 $"MinPlayers phải từ {min} đến {max}.";

 public static string CancellationLeadTimeOutOfRange(int min, int max) =>
 $"CancellationLeadTimeMinutes phải từ {min} đến {max}.";

 public const string OnlyFullLobbyCanReady =
 "Chỉ có thể bấm Ready khi phòng đã đầy.";

 public const string MemberNotReadyBecauseLeftOrKicked =
 "Không thể Ready khi đã rời/bị kick.";

 public const string LobbyNotReadyForReady =
 "Phòng chờ đã đóng hoặc không thể Ready ở trạng thái hiện tại.";

 public const string LobbyReadyTimeoutReason =
 "Phòng chờ đầy nhưng không có thành viên nào bấm Ready trong vòng 20 phút.";

 public const string SeatInventoryFull =
 "Số thành viên đã vượt quá số ghế cho phép.";

 public static string DissolveInvalidState(object currentStatus) =>
 $"Không thể giải tán lobby ở trạng thái '{currentStatus}'. Phòng đã đóng hoặc đang trong phiên chơi.";
 }

 // ===== BR-NEW-* § XXI-G Phase 2/3 =====
public static class Reservation
    {
    // ===== POS QR check-in (BR §21A.7 — 2 chiều) =====
    public const string PosTokenExpired =
        "Mã QR mời quét đã hết hạn. Yêu cầu nhân viên tạo mã mới.";

    public const string PosTokenAlreadyUsed =
        "Mã QR mời quét đã được sử dụng. Yêu cầu nhân viên tạo mã mới.";

    public const string PosTokenRevoked =
        "Mã QR mời quét đã bị thu hồi. Yêu cầu nhân viên tạo mã mới.";

    public static string PosTokenNotFound(string token) =>
        $"Không tìm thấy mã QR mời quét '{token}'.";

    public static string PosTokenNotInCheckInWindow(Guid reservationId) =>
        $"Reservation '{reservationId}' không nằm trong khung giờ cho phép check-in. Vui lòng đến đúng giờ hoặc liên hệ nhân viên.";

    public static string NotReservationMember(Guid reservationId, Guid userId) =>
        $"Người dùng '{userId}' không phải thành viên của reservation '{reservationId}'. Không thể check-in.";

    public static string PosTokenReservationMissing =>
        "Mã QR POS này không liên kết với reservation nào. Liên hệ nhân viên để tạo lại.";

    public static string InvalidStatusForCheckIn(Guid reservationId, string currentStatus, string expectedStatus) =>
        $"Reservation '{reservationId}' không thể check-in. Trạng thái hiện tại: '{currentStatus}', yêu cầu: '{expectedStatus}'.";

    // ===== Buffer validation (BR-LOBBY-01a/b/c) =====
 public static string BufferTooShort(int bufferMinutes, int minRequired) =>
 $"Thời gian đệm đến hạn tuyển người quá ngắn ({bufferMinutes} phút). Cần tối thiểu {minRequired} phút — vui lòng chọn khung giờ khác.";

 // ===== Play date range (BR § VIII: max 7 ngày trong tương lai) =====
 public static string PlayDateOutOfRange(int maxDaysAhead) =>
 $"Ngày dự kiến chơi phải nằm trong vòng {maxDaysAhead} ngày tới.";

 public const string CafeConfigMissing =
 "Quán chưa được cấu hình BVC. Không thể đặt cọc. Liên hệ quản lý quán.";

 public const string SeatInventoryNotConfigured =
 "Quán chưa được cấu hình số ghế cho ngày và khung giờ này. Liên hệ quản lý quán.";

 public const string GameNotInCafeInventory =
 "Quán chưa nhập game này vào kho. Bạn chọn game khác đi nha.";

 // ===== BR-RESERVATION-01: maxPlayers > capacity =====
 public static string MaxPlayersExceedsCafeCapacity(int maxPlayers, int cafeCapacity) =>
 $"Số người tối đa ({maxPlayers}) vượt quá công suất quán ({cafeCapacity}).";

 // ===== BR-NEW-01: maxPlayers theo khoảng cách playDate =====
 public static string MaxPlayersExceedsDistanceLimit(int requested, int maxAllowed, int daysAhead) =>
 $"Số người tối đa ({requested}) vượt quá giới hạn cho phép ({maxAllowed}) khi chơi cách {daysAhead} ngày.";

    // ===== BR-USER-LIMIT-01: 1 host lobby + 1 member lobby =====
    public const string ActiveLobbyHostLimitReached =
        "Bạn đang là chủ phòng của 1 lobby khác. Hãy hủy lobby đó hoặc đợi nó kết thúc rồi hãy tạo phòng mới.";

    public const string ActiveLobbyMemberLimitReached =
        "Bạn đang là thành viên của 1 lobby khác. Hãy rời lobby đó trước khi tạo phòng mới làm chủ phòng.";

    // ===== BR-USER-LIMIT-04/05: cross-role =====
    public const string MemberCannotCreateLobby =
        "Bạn đang là thành viên của 1 lobby đang tuyển người, nên chưa thể tạo lobby mới làm chủ phòng. Hãy rời lobby hiện tại trước.";

    public const string HostCannotJoinLobby =
        "Bạn đang là chủ phòng của 1 lobby khác, nên chưa thể tham gia lobby người khác làm thành viên. Hãy hủy lobby của bạn trước.";

 // ===== BR-NEW-08: 1 lobby / cafe / playDate+timeSlot / user =====
 public static string SameCafeSlotLobbyAlreadyActive =>
 "Bạn đã có 1 lobby đang hoạt động ở cùng quán, cùng khung giờ, cùng ngày.";

 // ===== BR-USER-LIMIT-02: overlap +30p buffer =====
 public static string OverlappingLobbyExists(DateTime otherDeadline, DateTime otherStart) =>
 $"Lịch của bạn bị trùng với lobby khác (deadline {otherDeadline:HH:mm dd/MM}, bắt đầu {otherStart:HH:mm dd/MM}).";

 // ===== BR-USER-LIMIT-03: cap heldBalance =====
 public static string HeldDepositCapExceeded(long currentHeld, long cap, string userType) =>
 $"Tổng cọc đang giữ ({currentHeld} BVC) vượt quá giới hạn cho user {userType} ({cap} BVC).";

 // ===== BR-NEW-05: 5 lần tạo+hủy / playDate =====
 public static string HostCreatesCancelsLimitReached(int limit) =>
 $"Bạn đã tạo/hủy lobby {limit} lần cho cùng ngày. Chọn ngày khác.";

 // ===== BR-RISK-04: suspended/banned =====
 public const string BannedCannotCreateLobby =
 "Tài khoản của bạn đã bị cấm vĩnh viễn nên không thể tạo lobby.";

 public const string SuspendedCannotCreateLobby =
 "Tài khoản của bạn đang bị tạm khóa nên không thể tạo lobby. Liên hệ hỗ trợ.";

 public const string RestrictedCannotCreateLobby =
 "Tài khoản của bạn đang ở trạng thái hạn chế nên không thể tạo lobby.";

 // ===== BR-NEW-10: cooling-off =====
 public static string CoolingOffCannotCreateDistantLobby(DateTime expiresAt) =>
 $"Bạn đang trong thời gian giới hạn đến {expiresAt:dd/MM/yyyy HH:mm}. Chỉ có thể tạo lobby có playDate trong ngày.";

 // ===== BR-DEPOSIT-03: rate per person 1.100 =====
 public const string InvalidDepositRate =
"Mức cọc mỗi người phải nằm trong khoảng 1 đến 100 BVC.";

 public const string MinPlayersAtLeastTwo =
"Số người tối thiểu phải ít nhất 1. Solo play (1 người) được phép.";

 public static string MinGreaterThanMax(int min, int max) =>
 $"Số người tối thiểu ({min}) không được lớn hơn tối đa ({max}).";

 // ===== BVC hold =====
 public static string InsufficientAvailableBalance(long available, long required) =>
 $"Số dư BVC khả dụng ({available}) không đủ để giữ cọc ({required}).";

 // ===== Reservation lifecycle =====
 public static string ReservationNotFound(Guid id) =>
 $"Không tìm thấy reservation '{id}'.";

 public static string ReservationNotFoundByLobby(Guid lobbyId) =>
 $"Không tìm thấy reservation cho lobby '{lobbyId}'.";

 public static string ReservationNotHolding(Guid id) =>
 $"Reservation '{id}' không ở trạng thái Holding. Không thể thao tác.";

 public const string IdempotencyKeyConflict =
 "Mã idempotency đã được dùng cho reservation khác. Tạo mã mới.";

 // ===== Seat/Game inventory =====
 public static string SeatsNotAvailable(int available, int requested) =>
 $"Quán không đủ ghế trống ({available}/{requested}).";

 public static string GameCopyNotAvailable(int available) =>
 available <= 0
 ? "Quán đang không có game này để bạn chơi. Bạn chọn game khác hoặc khung giờ khác nha."
 : $"Quán chỉ còn {available} bản game này, không đủ cho nhóm bạn. Thử đổi sang game khác xem sao.";

 // ===== Cancel =====
 public static string CancelOnlyByHost =
 "Chỉ host mới có thể hủy lobby.";

 public static string CancelWithinGraceFullRefund(string graceMinutes) =>
 $"Hủy trong vòng {graceMinutes} phút đầu chưa có thành viên — hoàn 100% BVC.";

 public static string CancelRefundMatrix(double hoursToStart, long percent) =>
 $"Hủy cách giờ chơi {hoursToStart:F1} giờ — hoàn {percent}% BVC.";

 public const string CancelAfter24hFullRefund =
 "Hủy trước giờ chơi từ 24 giờ trở lên — hoàn 100% BVC.";

 public const string Cancel6To24hHalfRefund =
 "Hủy trong khoảng 6–24 giờ trước giờ chơi — hoàn 50% BVC.";

 public const string CancelUnder6hNoRefund =
 "Hủy dưới 6 giờ trước giờ chơi — không hoàn BVC.";

 // ===== BR-NEW-11 cafe approval =====
 public const string ApproveOnlyByCafeOwner =
 "Chỉ quản lý quán mới có thể duyệt lobby.";

 public static string CafeRejectionReasonRequired =>
 "Lý do từ chối là bắt buộc khi cafe từ chối duyệt lobby.";

 // ===== BR-USER-LIMIT-* chi tiết cho member join =====
 public const string MemberAlreadyInLobby =
 "Bạn đã là thành viên của phòng chờ khác. Rời phòng trước khi tham gia phòng mới.";

 public const string LobbyExpired =
 "Phòng chờ đã hết hạn tuyển thành viên.";

 public const string LobbyFull =
 "Phòng chờ đã đủ số người tối đa.";

 public const string LobbyNotOpen =
 "Phòng chờ không ở trạng thái mở (có thể đã đóng, đang chờ cafe duyệt, hoặc đang chơi).";

 // ===== BR-10: Karma filter =====
 public static string KarmaRequirementNotMet(int required, int current) =>
 $"Điểm uy tín của bạn ({current}) chưa đạt yêu cầu tối thiểu ({required}) để tham gia phòng chờ này.";

 // ===== Validation message chuẩn cho Reservation flow =====
 public const string OnlyHostCanCancel =
 "Chỉ host mới có thể hủy reservation.";

 public const string CafeNotActive =
 "Cafe này hiện không hoạt động.";

 public const string GameInventoryNotFound =
 "Quán đang không có game này vào khung giờ bạn chọn để bạn chơi. Bạn thử đổi sang khung giờ khác hoặc chọn game khác nha.";

 public const string PreferredStartTimeOutOfRange =
 "Giờ dự kiến không nằm trong khung giờ đã chọn.";

 public const string SeatInventoryMissing =
 "Không tìm thấy dữ liệu ghế cho cafe và khung giờ này.";

 public const string ReservationNotFoundByCode =
 "Không tìm thấy reservation với mã đã cung cấp.";

 public static string CafeMismatchOnCheckIn(Guid reservationCafeId, Guid requestCafeId) =>
 $"Reservation thuộc cafe '{reservationCafeId}' không khớp với cafe hiện tại '{requestCafeId}'.";

 public static string OnlyConfirmedCanCheckIn(Guid reservationId, object currentStatus) =>
 $"Reservation '{reservationId}' không ở trạng thái Confirmed (hiện tại: {currentStatus}). " +
 "Chỉ reservation đã đạt minPlayers mới có thể check-in.";

 public static string CheckInTimeWindowInvalid(
 Guid reservationId, DateTime scheduledTime, DateTime windowStart, DateTime windowEnd) =>
 $"Reservation '{reservationId}' ngoài khung giờ cho phép check-in. " +
 $"Giờ chơi dự kiến: {scheduledTime:HH:mm dd/MM/yyyy}. " +
 $"Cho phép check-in từ {windowStart:HH:mm dd/MM/yyyy} đến {windowEnd:HH:mm dd/MM/yyyy}.";

 public static string CheckInTimeWindowLate(
 Guid reservationId, DateTime slotEndTime, DateTime windowEnd) =>
 $"Reservation '{reservationId}' đã quá giờ chơi. " +
 $"Giờ chơi kết thúc: {slotEndTime:HH:mm dd/MM/yyyy}. " +
 $"Deadline check-in: {windowEnd:HH:mm dd/MM/yyyy}.";

 public static string LobbyStatusInvalidForCancel(Guid lobbyId, object status) =>
 $"Lobby '{lobbyId}' đã ở trạng thái '{status}', không thể hủy.";

 public static string ReservationStatusInvalidForCancel(Guid reservationId, object status) =>
 $"Reservation '{reservationId}' không ở trạng thái Holding (hiện tại: {status}).";

 public static string LobbyNotPendingCafeApproval(Guid reservationId, object status) =>
 $"Lobby của reservation '{reservationId}' không ở trạng thái chờ cafe duyệt (hiện tại: {status}).";

 public static string NoManagerForCafe(Guid cafeId) =>
 $"Bạn không phải chủ quán '{cafeId}' nên không có quyền duyệt lobby.";

 public const string RejectReasonRequiredForCafe =
 "Lý do từ chối là bắt buộc khi cafe từ chối duyệt lobby.";

 public static string InvalidPlayDateForReservation(DateOnly playDate, int maxDaysAhead) =>
 $"playDate '{playDate:yyyy-MM-dd}' phải nằm trong [{DateOnly.FromDateTime(DateTime.UtcNow.Date):yyyy-MM-dd}, {DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(maxDaysAhead):yyyy-MM-dd}].";

 public static string InvalidTimeSlot(object timeSlot) =>
 $"timeSlot '{timeSlot}' không hợp lệ.";

    public static string MinPlayersLessThanTwo =>
"Số người chơi tối thiểu phải từ 1 trở lên. Solo play được phép.";

 public static string MinGreaterThanMaxPlayers(int min, int max) =>
 $"Số người chơi tối thiểu ({min}) phải nhỏ hơn hoặc bằng số người tối đa ({max}).";

 public static string BufferTooShortForLobbyCreate(int bufferMinutes, int minBufferMinutes) =>
 $"Thời gian đệm đến recruitmentDeadline chỉ còn {bufferMinutes} phút. " +
 $"Yêu cầu tối thiểu {minBufferMinutes} phút (BR-LOBBY-01b). " +
 "Chọn khung giờ xa hơn.";

 public static string FinalDepositMismatch(long serverAmount, long clientAmount) =>
 $"Số tiền cọc đã thay đổi (server: {serverAmount} BVC, client: {clientAmount} BVC). " +
 "Tạo lại quote và thử lại.";

 public static string CompleteCaptureInvalidStatus(Guid reservationId, object status) =>
 $"Reservation '{reservationId}' status = {status} (mong đợi CheckedIn). " +
 "Không thể capture BVC cho reservation chưa check-in thành công.";

 public static string SeatInventoryStateInvalid(int held, int required) =>
 $"Số ghế đang giữ ({held}) nhỏ hơn số người tối đa của đặt chỗ ({required}).";

 public const string GameInventoryStateInvalid =
 "Số bản game đang giữ nhỏ hơn 1.";

 public static string ReservationMissingLobby(Guid reservationId) =>
 $"Reservation '{reservationId}' thiếu lobby liên kết.";

 public static string SeatInventoryStateInvalidOnCapture(int inUse, int required) =>
 $"Số ghế đang dùng ({inUse}) nhỏ hơn số người tối đa của đặt chỗ ({required}).";

 public const string GameInventoryStateInvalidOnCapture =
 "Số bản game đang dùng nhỏ hơn 1.";

 public const string CoolingOffBlockDistantLobby =
 "Bạn đang trong thời gian giới hạn. Chỉ có thể tạo lobby có ngày chơi trong hôm nay.";

 public static string CafeScheduleSlotClosed(string slotName, Guid cafeId) =>
 $"Quán '{cafeId}' đã đóng khung giờ '{slotName}' cho ngày đã chọn. Chọn khung giờ khác.";

 public static string CafeScheduleInvalidTimeRange =>
 "Giờ bắt đầu phải khác giờ kết thúc khi đóng slot. Nếu muốn đóng slot, vui lòng dùng IsClosed = true.";

 public static string CafeScheduleOverlapInvalid =>
 "Khung giờ override không hợp lệ: giờ bắt đầu và kết thúc không được bằng nhau (trừ khi IsClosed = true).";

 public const string ConfirmRetryExhausted =
 "Quán đang bận hoặc có lỗi hệ thống. Bạn thử lại sau ít phút nha.";
 }

 public static class Tournament
 {
 public static string NotFound(Guid tournamentId) =>
 $"Không tìm thấy giải đấu '{tournamentId}'.";

 public static string NotFoundForManager(Guid tournamentId, Guid cafeId) =>
 $"Không tìm thấy giải đấu '{tournamentId}' thuộc quán '{cafeId}'.";

 public static string ManagerForbidden(Guid cafeId) =>
 $"Bạn không phải quản lý của quán '{cafeId}' nên không thể thao tác tournament.";

 public const string SplendorGameNotFound =
 "Không tìm thấy game Splendor trong hệ thống. Hãy import Splendor trước khi tạo tournament.";

 public const string SplendorRequired =
 "Tựa game '{0}' chưa được bật hỗ trợ giải đấu. Hiện hệ thống chỉ hỗ trợ Splendor. Liên hệ admin để kích hoạt thêm.";

 public const string TitleRequired =
 "Tên giải đấu là bắt buộc và phải từ 5 đến 200 ký tự.";

 public const string StartTimeRequired =
 "Thời gian bắt đầu giải đấu là bắt buộc.";

 public const string StartTimeMustBeFuture =
 "Thời gian bắt đầu giải đấu phải ở tương lai.";

 public const string RegistrationDeadlineAfterStartTime =
 "Hạn chót đăng ký phải trước thời gian bắt đầu giải.";

 public const string RegistrationDeadlinePassed =
 "Đã quá hạn đăng ký giải đấu.";

 public const string OnlyOnGoingCompletable =
 "Chỉ có thể hoàn thành giải đang diễn ra.";

 public const string FinalMatchNotCompleted =
 "Bàn chung kết chưa hoàn thành. Hãy ghi nhận kết quả Final trước.";

 public static string ParticipantNotRegistered(Guid tournamentId) =>
 $"Bạn chưa đăng ký giải đấu '{tournamentId}'.";

 public const string ParticipantNotFound =
 "Không tìm thấy người chơi.";

 public const string ParticipantNotInTournament =
 "Người chơi không thuộc giải đấu này.";

 public const string WalkInDisplayNameRequired =
 "Nhập tên hiển thị cho khách vãng lai.";

 public const string GameTemplateIdRequired =
 "Mã tựa game là bắt buộc.";

 public const string MatchAlreadyStartedOrFinished =
 "Bàn đấu đã bắt đầu hoặc đã kết thúc.";

 public const string CancelMatchReasonRequired =
 "Nhập lý do hủy ván đấu.";

 public const string CorrectionReasonRequired =
 "Nhập lý do sửa kết quả (lưu vết kiểm toán).";

 public static string MatchNotFoundById =>
 "Không tìm thấy ván đấu.";

 public const string MatchNumbersMustBeUnique =
 "MatchNumber không được trùng giữa các bàn.";

 public const string PlayerCannotAppearInMultipleTables =
 "Cùng 1 người chơi không thể xuất hiện ở 2 bàn khác nhau.";

 public const string MatchIdBodyMismatch =
 "MatchId trong body phải trùng với URL.";

 public const string MaxParticipantsMustBeMultipleOf4 =
 "Số người tối đa phải là bội số của 4 (4, 8, 12, 16, 20, 24, 28, 32) để chia đều các bàn.";

 public static string OnlyDraftEditable(Guid tournamentId) =>
 $"Chỉ có thể chỉnh sửa giải đấu '{tournamentId}' khi đang ở trạng thái Draft.";

 public static string CannotOpenRegistration(Guid tournamentId) =>
 $"Giải đấu không ở trạng thái chờ duyệt hoặc đã quá hạn.";

 public static string RegistrationNotOpen(Guid tournamentId) =>
 $"Giải đấu '{tournamentId}' chưa mở đăng ký hoặc đã đóng.";

 public static string AlreadyRegistered(Guid tournamentId) =>
 $"Bạn đã đăng ký giải đấu '{tournamentId}' rồi.";

 public static string TournamentFull(Guid tournamentId) =>
 $"Giải đấu '{tournamentId}' đã đủ người tối đa.";

 public static string KarmaRequirementNotMet(int required, int current) =>
 $"Bạn cần đạt tối thiểu {required} điểm Karma để đăng ký. Hiện tại của bạn là {current}.";

 public static string CannotStartNotEnoughParticipants(int required, int current) =>
 $"Không thể bắt đầu giải: cần tối thiểu {required} người đã check-in, hiện tại mới có {current}.";

 public static string CannotStartRegistrationOpen(Guid tournamentId) =>
 $"Giải '{tournamentId}' chưa đóng đăng ký. Hãy đóng đăng ký trước khi bắt đầu.";

 public static string NotCheckInStatus(Guid tournamentId) =>
 $"Bạn chưa check-in tại giải '{tournamentId}'.";

 public const string AlreadyCheckedIn =
 "Bạn đã check-in giải đấu này rồi.";

 public const string NotCheckedIn =
 "Người chơi chưa check-in tại quán.";

 public static string PlayerNotInMatch(Guid matchId, Guid userId) =>
 $"Người chơi '{userId}' không tham gia bàn đấu '{matchId}'.";

 public static string WinnerMustBePlayer(Guid matchId) =>
 $"Người thắng phải là 1 trong 4 người chơi của bàn đấu '{matchId}'.";

 public static string MatchNotFound(Guid matchId) =>
 $"Không tìm thấy bàn đấu '{matchId}'.";

 public static string MatchNotOnGoing(Guid matchId) =>
 $"Bàn đấu '{matchId}' không ở trạng thái đang diễn ra.";

 public static string AlreadyWithdrawn(Guid tournamentId) =>
 $"Bạn đã rút lui khỏi giải '{tournamentId}' rồi.";

 public static string CannotCancelNotDraft(Guid tournamentId) =>
 $"Chỉ có thể hủy giải đấu '{tournamentId}' khi chưa bắt đầu.";

 public static string CannotCancelCompleted(Guid tournamentId) =>
 $"Giải đấu '{tournamentId}' đã hoàn thành và không thể hủy.";

 public static string AlreadyCancelled(Guid tournamentId) =>
 $"Giải đấu '{tournamentId}' đã được hủy trước đó.";

 public static string AlreadyCompleted(Guid tournamentId) =>
 $"Giải đấu '{tournamentId}' đã được hoàn thành trước đó.";

 public static string CancellationReasonRequired =>
 "Lý do hủy là bắt buộc khi hủy giải đấu đã có người đăng ký.";

 public static string CannotReopenRegistration(Guid tournamentId) =>
 $"Không thể mở lại đăng ký: giải đấu phải ở trạng thái RegistrationClosed.";

 public static string CannotRecordNoScores(Guid matchId) =>
 $"Không thể ghi nhận kết quả cho bàn '{matchId}' nếu chưa nhập điểm các người chơi.";

 public static string InvalidAutoShortenMode(string mode) =>
 $"AutoShortenMode '{mode}' không hợp lệ. Chỉ chấp nhận 'Auto' hoặc 'Manual'.";

 public static string InvalidReducedRounds(int rounds) =>
 $"ReducedRounds '{rounds}' không hợp lệ. Phải nằm trong khoảng 1-5.";

 public static string RegistrationAutoExtended(
 int currentExtensionCount, int maxExtensions, int minutesPerExtension,
 int currentCheckedIn, int required) =>
 $"Đã tự động gia hạn đăng ký thêm {minutesPerExtension} phút (lần {currentExtensionCount}/{maxExtensions}). Hiện có {currentCheckedIn}/{required} người. chờ thêm người đăng ký hoặc thử lại.";

 public static string CannotExtendRegistrationNotOpen(Guid tournamentId) =>
 $"Đăng ký không còn mở.";

 public static string CannotExtendRegistrationMaxReached(int maxExtensions, int minutesPerExtension) =>
 $"Đã đạt giới hạn gia hạn ({maxExtensions} lần × {minutesPerExtension} phút = {maxExtensions * minutesPerExtension} phút). Hãy hủy giải hoặc dùng force-start.";

 public static string CannotAdvanceRoundNotOnGoing(Guid tournamentId) =>
 $"Chỉ có thể chuyển vòng cho giải đấu '{tournamentId}' đang ở trạng thái OnGoing.";

 public static string CannotAdvanceRoundAlreadyCompleted(Guid tournamentId) =>
 $"Giải đấu '{tournamentId}' đã hoàn thành toàn bộ vòng đấu.";

 public static string CannotAdvanceRoundCurrentNotFinished(int currentRound) =>
 $"Vòng hiện tại (Round {currentRound}) chưa kết thúc toàn bộ các bàn đấu. Hãy ghi nhận kết quả các bàn trước khi chuyển vòng.";

 public static string CannotAdvanceRoundFinalAlreadyBuilt(Guid tournamentId) =>
 $"Giải đấu '{tournamentId}' đã có bàn chung kết. Không thể chuyển sang vòng khác.";

 public static string CannotWithdrawAfterCheckIn(TournamentParticipantStatus currentStatus)
 {
 var message = currentStatus switch
 {
 TournamentParticipantStatus.CheckedIn =>
 "Không thể rút lui sau khi đã check-in tại quán. Hãy liên hệ nhân viên quán để được hỗ trợ.",
 TournamentParticipantStatus.Active =>
 "Không thể rút lui khi giải đã bắt đầu.",
 TournamentParticipantStatus.Finished =>
 "Không thể rút lui khi đã hoàn thành giải đấu.",
 _ => "Không thể rút lui ở trạng thái hiện tại."
 };
 return $"[{currentStatus}] {message}";
 }

 public static string WalkInClosedAfterRoundOne =>
 "Vòng 1 của giải đã hoàn thành. Không thể thêm khách vãng lai. Hãy đăng ký giải tuần sau hoặc liên hệ nhân viên để biết thêm.";

 public static string FinalRequiresFourActiveParticipants(int current, int required) =>
 $"Bàn chung kết cần đủ {required} người chơi Active, hiện chỉ có {current}. Hãy thêm người chơi hoặc tăng shortage tolerance.";

 public static string UserProfileNotFoundById(Guid userId) =>
 $"Không tìm thấy hồ sơ của user '{userId}'.";

 // === Notification Messages ===
 public static string Reminder30Minutes(string tournamentTitle, DateTime startTime, string cafeName) =>
 $"Giải đấu '{tournamentTitle}' bắt đầu sau 30 phút ({startTime:HH:mm}) tại {cafeName}. Hãy check-in sớm!";

 public static string Reminder15Minutes(string tournamentTitle, DateTime startTime, string cafeName) =>
 $"Nhắc nhở: Giải đấu '{tournamentTitle}' bắt đầu sau 15 phút ({startTime:HH:mm}) tại {cafeName}. Hãy có mặt ngay!";

 public static string Reminder5Minutes(string tournamentTitle, DateTime startTime, string cafeName) =>
 $"Cảnh báo: Giải đấu '{tournamentTitle}' bắt đầu sau 5 phút ({startTime:HH:mm}) tại {cafeName}. Hãy check-in ngay!";

 public static string NoShowMarked(string tournamentTitle, int karmaPenalty) =>
 $"Bạn đã không có mặt tại giải đấu '{tournamentTitle}' và bị đánh dấu no-show. Điểm uy tín bị trừ {karmaPenalty} điểm.";

 public static string RegistrationExtended(string tournamentTitle, DateTime newDeadline, int minutes) =>
 $"Giải đấu '{tournamentTitle}' đã được gia hạn thêm {minutes} phút. Hạn chót mới: {newDeadline:HH:mm}.";
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

 public static class Notification
 {
 public const string DeviceTokenRequired = "Device token FCM là bắt buộc.";
 public const string DeviceTokenTooLong = "Device token FCM không được vượt quá 512 ký tự.";
 public const string DeviceTokenNotFound = "Không tìm thấy device token để xóa.";
 public const string DeviceTokenNotOwner = "Bạn không có quyền xóa device token này.";
 public const string PlatformInvalid = "Giá trị platform không hợp lệ. Chỉ chấp nhận 'android', 'ios' hoặc 'web'.";
 }
 }
}