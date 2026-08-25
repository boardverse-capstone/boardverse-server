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
    "Tài khoản hoặc email đã được sử dụng. Bạn thử đăng nhập hoặc dùng email khác nhé!";

    /// <summary>BR-11: minimum age 13 to register.</summary>
    public const string RegisterUnderage =
    "Bạn phải đủ 13 tuổi trở lên để đăng ký tài khoản nhé!";

    public const string LoginTooManyAttempts =
    "Bạn nhập sai mật khẩu nhiều lần rồi. Thử lại sau 15 phút nhé!";

 public const string LoginInvalidCredentials =
 "Tên đăng nhập/email hoặc mật khẩu không đúng. Bạn kiểm tra lại nhé!";

 public const string GoogleTokenMissingEmail =
 "Không thể đăng nhập Google. Token không chứa email. Thử đăng nhập theo cách khác nhé!";

 public const string GoogleTokenValidationFailed =
 "Đăng nhập Google thất bại. Bạn thử lại hoặc dùng cách khác nhé!";

 public const string RefreshTokenInvalidOrExpired =
 "Phiên đăng nhập đã hết hạn. Bạn đăng nhập lại nhé!";

 public const string RefreshTokenUserMissing =
 "Không tìm thấy tài khoản liên kết. Bạn đăng nhập lại nhé!";

 public const string SendVerificationUserNotFound =
 "Không tìm thấy tài khoản với email này. Bạn kiểm tra lại email nhé!";

 public const string VerifyEmailInvalidToken =
 "Mã xác minh không hợp lệ. Bạn thử lại hoặc yêu cầu mã mới nhé!";

 public const string VerifyEmailTokenExpired =
 "Mã xác minh đã hết hạn. Bạn yêu cầu mã mới nhé!";

 public const string RequestPasswordResetUserNotFound =
 "Không tìm thấy tài khoản với email này. Bạn kiểm tra lại email nhé!";

 public const string RequestPasswordResetEmailNotVerified =
 "Email chưa được xác minh nên không thể đặt lại mật khẩu. Bạn xác minh email trước nhé!";

 public const string ResetPasswordInvalidToken =
 "Mã đặt lại mật khẩu không hợp lệ. Bạn thử lại hoặc yêu cầu mã mới nhé!";

 public const string ResetPasswordTokenExpired =
 "Mã đặt lại mật khẩu đã hết hạn. Bạn yêu cầu mã mới nhé!";

 public const string ChangePasswordUserNotFound =
 "Không tìm thấy tài khoản đang đăng nhập. Bạn đăng nhập lại nhé!";

 public const string ChangePasswordNoLocalPassword =
 "Tài khoản này chỉ đăng nhập bằng Google nên không có mật khẩu để đổi.";

 public const string ChangePasswordCurrentIncorrect =
 "Mật khẩu hiện tại không đúng. Bạn kiểm tra lại nhé!";

 public const string ChangePasswordSameAsCurrent =
 "Mật khẩu mới phải khác mật khẩu hiện tại nhé!";

 public const string LinkGoogleAccountNotFound =
 "Không tìm thấy tài khoản để liên kết Google. Bạn đăng nhập bằng tài khoản đã đăng ký trước nhé!";

 public const string ChangePasswordInvalidToken =
 "Không xác định được tài khoản. Bạn đăng nhập lại nhé!";

 public const string LogoutInvalidToken =
 "Phiên đăng nhập không hợp lệ.";

 public const string VerificationEmailSent = "Đã gửi email xác minh. Bạn kiểm tra hộp thư nhé!";
 public const string PasswordResetEmailSent = "Đã gửi email đặt lại mật khẩu. Bạn kiểm tra hộp thư nhé!";
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

 public const string ExpMustBePositive =
 "Exp phải lớn hơn 0.";
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

public const string SearchNameRequired =
"Tìm kiếm quán cần có tên. Vui lòng nhập tên quán muốn tìm.";

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

 public const string NameRequired =
 "Tên cafe không được để trống.";

 public const string AddressRequired =
 "Địa chỉ cafe không được để trống.";

 public const string ManagerIdInvalid =
 "ManagerId không hợp lệ.";

 public static string ManagerNotFound(Guid managerId) =>
 $"Manager '{managerId}' không tìm thấy.";

 public const string CoordinatesInvalid =
 "Tọa độ không hợp lệ.";

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

        /// <summary>
        /// GAP-Fix: Tên hiển thị cho khách vô danh (BR-13) phải hợp lệ 2-100 ký tự.
        /// Alias JSON "username" được merge vào "displayName" trước khi validate.
        /// </summary>
        public const string GuestSlotDisplayNameInvalid =
            "Tên hiển thị cho khách vô danh phải từ 2-100 ký tự. " +
            "Gửi kèm 'displayName' (hoặc alias 'username') trong body.";

        /// <summary>
        /// Phone của Guest_Slot (optional) khi gửi lên phải là số VN hợp lệ
        /// (10-11 chữ số, đầu 03/05/07/08/09). Bỏ trống nếu khách không cung cấp.
        /// </summary>
        public const string GuestSlotPhoneNumberInvalid =
            "Số điện thoại khách vô danh phải là số Việt Nam hợp lệ gồm 10–11 chữ số " +
            "(bắt đầu bằng 03, 05, 07, 08 hoặc 09).";

        // ===== Phase 5 / EC-11 — Manager override played time (BR-REFUND-07 §time-slot-fixed-end v3.0) =====
        public const string OnlyManagerCanOverride =
            "Chỉ Manager mới có thể override giờ chơi. Staff chỉ được mở dispute.";

        public static string NoDisputeBeforeOverride(Guid sessionId) =>
            $"Phiên chơi '{sessionId}' chưa có dispute audit (PlayedTimeDisputed). Manager cần review dispute trước khi override.";

        public static string CannotOverridePaidSession(Guid sessionId) =>
            $"Phiên chơi '{sessionId}' đã thanh toán, không thể override. Hãy refund và reopen session nếu cần chỉnh sửa.";

        public static string OverrideMinutesExceedsPolicy(int requested, int maxAllowed) =>
            $"Số phút chỉnh sửa ({requested}) vượt quá giới hạn cho phép ({maxAllowed} phút = 24 giờ).";

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

        // GAP-R3-03 Fix: BR-12 — không cho EndGame khi không còn member Playing/Guest nào (billing = 0 vô nghĩa).
        public const string NoPlayingMembersToEndGame =
            "Không còn thành viên nào đang chơi trong phiên. Hãy kiểm tra trước khi chuyển sang trạng thái kiểm kê linh kiện.";

 public const string SessionMustBeUnpaidForPayment =
 "Phiên chơi phải ở trạng thái chờ thanh toán (UNPAID) để thanh toán.";

 public const string SessionMustBeCheckingForResume =
 "Chỉ có thể khôi phục phiên đang ở trạng thái kiểm kê linh kiện (CHECKING).";

public const string SessionCannotResumeHasCheckedOutMembers =
"Phiên đã có thành viên thanh toán. Không thể khôi phục — hãy tiếp tục thanh toán các thành viên còn lại.";

        // GAP-R3-02: Bảo vệ audit trail BR-12 — staff phải xử lý missing components qua component-check
        // hoặc checkout penalty trước khi resume phiên.
        public const string CannotResumeWithMissingComponents =
            "Phiên đang được đánh dấu có linh kiện thiếu/hỏng. Hãy xử lý qua kiểm kê linh kiện hoặc thanh toán phí phạt trước khi khôi phục phiên về ACTIVE.";

 public const string GuestSlotNotAllowedAfterSessionEnded =
 "Phiên chơi đã kết thúc. Không thể thêm khách vô danh.";

 public const string GuestSlotCannotPartialCheckout =
 "Khách vô danh (BR-13) không thể tách nhóm thanh toán một phần. Vui lòng gộp vào hóa đơn của host hoặc thu tiền mặt tại quầy.";

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

 /// <summary>
 /// Trạng thái hộp không hợp lệ khi chuyển (VD: cố set InUse từ API, hoặc set status không có trong enum).
 /// </summary>
 public const string InvalidBoxStatus =
 "Trạng thái hộp game không hợp lệ. Vui lòng chọn một trong: Available, Maintenance, Damaged, Retired.";

 /// <summary>
 /// Chuyển sang <c>Available</c> chỉ hợp lệ khi hộp đang ở <c>Maintenance</c> hoặc <c>Damaged</c>.
 /// </summary>
 public static string BoxStatusTransitionNotAllowed(string current, string target) =>
 $"Không thể chuyển hộp game từ '{current}' sang '{target}'. " +
 $"Chỉ chuyển sang 'Available' khi hộp đang ở 'Maintenance' hoặc 'Damaged'.";

 /// <summary>
 /// Hộp game đang được sử dụng trong phiên chơi — không thể đổi trạng thái cho đến khi kết thúc phiên.
 /// </summary>
 public static string BoxInUseCannotChangeStatus(Guid boxId) =>
 $"Hộp game '{boxId}' đang được sử dụng trong phiên chơi. Vui lòng kết thúc phiên trước khi đổi trạng thái.";

 public static string SessionMustBeActiveForEnd(string current) =>
 $"Phiên chơi phải đang hoạt động để kết thúc. Trạng thái hiện tại: '{current}'.";

 public const string SessionAlreadySuspended =
 "Phiên chơi đã bị tạm dừng trước đó.";

public static string SessionMustBeActiveForGameAssignment(string current) =>
    $"Chỉ gán game khi phiên đang hoạt động. Trạng thái hiện tại: '{current}'.";

    /// <summary>
    /// GAP-26 / Return-Game legacy: Endpoint <c>POST /sessions/{id}/return-game</c>
    /// đã deprecated từ 2026-08-10. Penalty giờ là single source of truth từ
    /// <c>ComponentCheckResult.ResponsibleMemberId</c> (submit lúc component-check).
    /// Endpoint vẫn trả 200 + log warning để back-compat POS client cũ; v2.0 sẽ đổi 410 Gone.
    /// </summary>
    public static string ReturnGameDeprecated =>
        "Endpoint /return-game đã ngừng phát triển và sẽ bị xóa trong v2.0. " +
        "Vui lòng dùng POST /sessions/component-check để ghi nhận linh kiện mất/hỏng trước khi checkout.";

    /// <summary>
    /// BR-03: Tiền cọc không được vượt 50% giá vé/giờ đầu của quán.
    /// </summary>
    public static string DepositExceedsHalfBasePrice(decimal deposit, decimal basePrice) =>
        $"Mức cọc {deposit:N0} VND vượt quá 50% giá giờ đầu ({basePrice:N0} VND). Vui lòng giảm mức cọc.";

    /// <summary>
    /// Cafe chưa cấu hình SePay merchant nên không thể tạo thanh toán.
    /// </summary>
    public static string SePayBankNotConfigured(string cafeName) =>
        $"Quán '{cafeName}' chưa cấu hình SePay (bank/merchant/returnUrl). Liên hệ admin.";

    public static string ComponentCheckConcurrentSubmit =>
        "Đã có người khác đang gửi checklist cho phiên này. Vui lòng đợi hoặc tải lại.";

    public static string ComponentPenaltyMemberInvalidForFullComponent(Guid memberId) =>
        $"Không thể gán phí phạt cho thành viên '{memberId}' khi chưa chọn linh kiện hỏng/mất cụ thể.";
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

 /// <summary>Backend không proxy được ảnh QR từ vietqr.app (timeout / network / 5xx).
 /// Không block top-up — response vẫn trả <c>QrUrl</c>, chỉ thiếu <c>QrImageBase64</c>.</summary>
 public const string QrImageProxyUnavailable =
 "Không thể tải ảnh QR từ VietQR. Vui lòng dùng URL QR được trả về để tải trực tiếp.";

 /// <summary>Không tìm thấy QR cho OrderId (chưa tạo / hết hạn / failed).</summary>
 public static string QrImageNotFoundForOrder(string orderId) =>
 $"Không tìm thấy ảnh QR cho đơn top-up '{orderId}'. Đơn có thể đã hết hạn hoặc chưa được tạo.";

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

 public static string TopUpIdempotencyKeyConflict(Guid key) =>
 $"Idempotency key '{key}' đã được dùng cho yêu cầu top-up khác với payload khác.";

 public static string HeldBalanceInsufficient(long required, long available) =>
 $"Số BVC đang giữ không đủ để thực hiện. Cần {required:N0} BVC nhưng chỉ có {available:N0} BVC.";

 public static string HeldBalanceInsufficientForCapture(long required, long available) =>
 $"Số BVC đang giữ không đủ để capture. Cần {required:N0} BVC nhưng chỉ có {available:N0} BVC.";

 public static string HeldBalanceInsufficientForForfeit(long required, long available) =>
 $"Số BVC đang giữ không đủ để forfeit. Cần {required:N0} BVC nhưng chỉ có {available:N0} BVC.";

 public static string AvailableBalanceInsufficient(long required, long available) =>
 $"Số dư BVC khả dụng không đủ. Cần {required:N0} BVC nhưng chỉ có {available:N0} BVC.";
 }

 public static class Payment
 {
public const string SePayMasterAccountNotFound =
    "Chưa cấu hình tài khoản SePay Master. Liên hệ admin.";

    public const string SePayMasterAccountInactive =
    "Tài khoản SePay Master đang tạm ngưng. Liên hệ admin để kích hoạt.";

 public const string SePayMerchantIdMissing =
 "Tài khoản SePay Master chưa có MerchantId. Cập nhật cấu hình.";

public const string SePayWebhookTokenMissing =
"Cấu hình thanh toán chưa hoàn tất. Vui lòng thử lại sau nhé.";

public const string SePayReturnSuccess =
"Thanh toán thành công! Bạn quay lại ứng dụng nhé.";

public const string SePayReturnFailed =
"Thanh toán thất bại hoặc bị hủy. Bạn thử lại nhé!";

public const string SePayMockEndpointBlocked =
"Tính năng này chỉ dùng được trong môi trường phát triển.";

public const string SePayWebhookProcessingFailed =
"Có lỗi khi xử lý thanh toán. Bạn thử lại sau một chút nhé!";

public const string SePayMockWebhookProcessingFailed =
"Có lỗi khi xử lý thanh toán thử nghiệm. Bạn thử lại nhé!";

public const string SePayMasterAccountNotCreated =
"Master account SePay chưa được tạo.";

public const string SePayCafeNotConfigured =
"Quán của bạn chưa được cấu hình thanh toán. Liên hệ quản lý quán nhé!";

public const string SePayOrderIdRequired =
"Thiếu mã đơn hàng. Bạn thử lại nhé!";

public const string SePayCafeIdRequired =
"Thiếu thông tin quán. Bạn thử lại nhé!";

public static string SePayCafeAccountExists(Guid cafeId) =>
$"Quán này đã có tài khoản thanh toán rồi.";

public const string SePayMasterAccountExists =
"Tài khoản thanh toán đã tồn tại.";

public static string SePayInvalidEnvironment(string environment) =>
$"Môi trường thanh toán không hợp lệ: '{environment}'. Bạn thử lại nhé!";

public static string SePayAccountNotFound(Guid id) =>
$"Không tìm thấy tài khoản thanh toán này.";

public static string DebugSePayCafeNotFound(Guid cafeId) =>
$"Quán này chưa có trong hệ thống. Bạn kiểm tra lại nhé!";

public static string DebugSePayCafeNotFoundShort(Guid cafeId) =>
$"Không tìm thấy quán này.";

public const string SePayResponseInvalid =
"Phản hồi từ cổng thanh toán không hợp lệ. Bạn thử lại nhé!";

public static string SePayCreatePaymentFailed(int statusCode, string details) =>
$"Không tạo được link thanh toán ({statusCode}). {details}. Bạn thử lại sau nhé!";

public static string SePayTransferFailed(int statusCode, string details) =>
$"Chuyển khoản thất bại ({statusCode}). {details}. Bạn thử lại sau nhé!";

public static string SePayTransferFailed(string code, string details) =>
$"Chuyển khoản thất bại ({code}). {details}. Bạn thử lại sau nhé!";

public const string SePayTransactionIdRequired =
"sePayTransactionId là bắt buộc.";

public static string SePayTransactionNotFound(string sePayTransactionId) =>
$"Không tìm thấy BookingDeposit với SePayTransactionId='{sePayTransactionId}'.";

public const string GatewayCannotCreatePayment =
"Không tạo được thanh toán. Bạn thử lại sau một chút nhé!";

public static string GatewayCannotCreatePaymentWithError(string errorMessage) =>
$"Không tạo được thanh toán: {errorMessage}. Bạn thử lại nhé!";

public const string GatewayQrUrlMissing =
"Không nhận được mã thanh toán. Bạn thử lại nhé!";

public static string QrRegenerateInvalidState(string currentStatus) =>
$"Chỉ tạo lại mã thanh toán khi đang chờ. Trạng thái hiện tại: '{currentStatus}'.";

public static string QrRegenerateRateLimited(int secondsRemaining) =>
$"Bạn tạo lại mã hơi nhanh. Chờ {secondsRemaining} giây rồi thử lại nhé!";

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
$"Không tìm thấy đơn cọc này.";

public static string ActiveSessionNotFound(Guid id) =>
$"Không tìm thấy phiên chơi này.";

// ===== ManualPaymentService specific =====
public static string InvalidPaymentType(string input) =>
$"Loại thanh toán không hợp lệ: '{input}'. Chỉ chấp nhận Cọc hoặc Phiên chơi.";

public static string InvalidPaymentMethod(string input) =>
$"Phương thức thanh toán không hợp lệ: '{input}'.";

public static string DepositNotPending(string current) =>
$"Đơn cọc đang ở trạng thái '{current}', không phải đang chờ.";

public static string SessionNotUnpaid(string current) =>
$"Phiên chơi đang ở trạng thái '{current}', cần ở trạng thái chờ thanh toán.";

// ===== ManualPaymentService authorization (C3) =====
public static string ManualConfirmNotAuthorizedForCafe(Guid cafeId) =>
$"Bạn không có quyền xác nhận thanh toán cho quán này.";

// ===== ManualPaymentService amount validation (H5) =====
public static string ManualConfirmAmountMismatch(decimal expected, decimal received) =>
$"Số tiền xác nhận ({received:N0} VNĐ) không khớp với đơn hàng ({expected:N0} VNĐ). Bạn kiểm tra lại nhé!";

public const string SessionPaymentAmountMustBePositive =
"Số tiền thanh toán phải lớn hơn 0.";

public static string PaymentCafeNotConfiguredSePay(string cafeName) =>
$"Quán '{cafeName}' chưa cấu hình thanh toán. Liên hệ quản lý quán nhé!";

public static string RefundInvalidDepositStatus(string currentStatus) =>
$"Không thể hoàn cọc. Trạng thái đơn: '{currentStatus}', cần: Đã thanh toán.";

public const string RefundReasonRequired =
"Bạn cần nhập lý do hoàn cọc để admin xem xét.";

// ===== SePayAccountService specific =====
public const string ManagerHasNoCafe =
"Bạn chưa quản lý quán nào.";

public const string CafeSePayAccountNotConfigured =
"Quán của bạn chưa cấu hình thanh toán.";

public static string CafePaymentAccountBankCodeRequired =>
"Cấu hình thanh toán thiếu thông tin ngân hàng (bankCode).";

public static string CafePaymentAccountAccountNumberRequired =>
"Cấu hình thanh toán thiếu số tài khoản (accountNumber).";

public static string CafePaymentAccountAccountHolderRequired =>
"Cấu hình thanh toán thiếu tên chủ tài khoản (accountHolder).";

public static string CafePaymentAccountAlreadyExists(Guid cafeId) =>
$"Quán đã có cấu hình thanh toán. Dùng chức năng cập nhật để sửa nhé!";

public const string SePayBankInfoIncomplete =
"Cấu hình thanh toán của quán chưa đầy đủ (thiếu thông tin ngân hàng). Bạn cập nhật lại nhé!";
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
 "Thời gian kết thúc phải sau thời gian bắt đầu. Bạn kiểm tra lại nhé!";

 public const string SeatCountMustBePositive =
 "Số lượng ghế phải từ 1 trở lên.";

 public const string NotCheckedInYet =
 "Bạn cần check-in trước khi vote vắng mặt thành viên nhé!";

 public const string AlreadyCheckedOut =
 "Phiên đã kết thúc rồi. Không thể vote vắng mặt sau khi thanh toán.";

 public const string VoteWindowClosed =
 "Đã hết thời gian vote vắng mặt. Bạn chỉ vote được trong 24 giờ sau khi check-in thôi nhé!";

 public const string VoterNotCheckedInMember =
 "Chỉ thành viên đã check-in mới vote được.";

 public const string CannotVoteSelfAbsent =
 "Bạn không thể vote vắng mặt cho chính mình nhé!";

 public const string RatingWindowClosed =
 "Đã hết thời gian đánh giá. Bạn chỉ đánh giá được trong 24 giờ sau khi check-out thôi nhé!";

 public const string CannotRateSelf =
 "Bạn không thể đánh giá chính mình nhé!";

 public const string DuplicateRatedUser =
 "Danh sách đánh giá bị trùng. Bạn kiểm tra lại nhé!";

 public const string AlreadySubmittedRatings =
 "Bạn đã gửi đánh giá rồi!";

 public static string LobbyNotFoundForBooking(Guid lobbyId) =>
 $"Không tìm thấy phòng này.";

 public const string OnlyLobbyHostCanCreateBooking =
 "Chỉ chủ phòng mới tạo được đặt chỗ.";

 public const string LobbyMustBeFullToCreateBooking =
 "Phòng phải đầy người mới tạo được đặt chỗ.";

 public const string LobbyAlreadyHasBooking =
 "Phòng này đã có đặt chỗ rồi!";

 public static string CafeNotFound(Guid cafeId) =>
 $"Không tìm thấy quán này.";

 public static string TableNotFound(Guid tableId) =>
 $"Không tìm thấy bàn này.";

 public const string TableNotInCafe =
 "Bàn không thuộc quán đã chọn.";

 public const string StartTimeInPast =
 "Thời gian bắt đầu phải ở tương lai. Bạn kiểm tra lại nhé!";

 public const string TableAlreadyBookedInTimeRange =
 "Bàn này đã có người đặt trong khoảng thời gian này rồi!";

 public static string NotFound(Guid bookingId) =>
 $"Không tìm thấy đặt chỗ này.";

 public const string NotBookingOwner =
 "Bạn không phải là người đặt chỗ này.";

 public const string CannotUpdateBookingInCurrentState =
 "Không thể cập nhật đặt chỗ ở trạng thái này.";

 public const string TableNotInBookingCafe =
 "Bàn không thuộc quán của đặt chỗ này.";

 public const string CannotCancelCheckedInBooking =
 "Đặt chỗ đã check-in rồi nên không thể hủy.";

 public static string OnlyPendingDepositCanConfirm(BookingStatus status) =>
 $"Chỉ đặt chỗ đang chờ cọc mới xác nhận được (trạng thái hiện tại: {status}).";

 public const string OnlyConfirmedOrPendingDepositCanNoShow =
 "Chỉ đặt chỗ đã xác nhận hoặc đang chờ cọc mới đánh dấu vắng mặt được.";

 public const string NotMemberOfBooking =
 "Bạn không phải thành viên của đặt chỗ này.";

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

 public static string PlayerQuantityExceedsTableSeats(int playerCount, string tableName, int seatCount) =>
 $"Số lượng người chơi ({playerCount}) vượt quá số ghế khả dụng của bàn '{tableName}' ({seatCount}).";

 public const string LegacyEndpointDisabled =
 "Endpoint legacy này đã được thay thế bằng Reservation flow. Vui lòng dùng POST /api/v1/reservations/quote.";

 public const string LegacyEndpointMigrationPath =
 "Hướng dẫn: dùng POST /api/v1/reservations/quote để lấy báo giá và POST /api/v1/reservations/confirm để giữ chỗ.";
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
 $"Thẻ đánh giá trong phòng: {string.Join(", ", tags)}";

 public static string LobbyNotFound(Guid lobbyId) =>
 $"Oops! Không tìm thấy phòng '{lobbyId}'. Phòng có thể đã bị hủy hoặc không tồn tại.";

 public static string NotLobbyMember(Guid lobbyId, Guid userId) =>
 $"Bạn không phải thành viên của phòng này nên không thể đánh giá.";

 public static string LobbyNotOpenForRating(Guid lobbyId) =>
 $"Phòng này chưa mở đánh giá. Có thể phiên chơi chưa kết thúc hoặc chưa thanh toán.";

 public static string CannotRateSelf(Guid lobbyId) =>
 $"Bạn không thể tự đánh giá mình trong phòng nhé!";

 public static string TargetNotLobbyMember(Guid lobbyId, Guid targetUserId) =>
 $"Người bạn muốn đánh giá không có trong phòng này.";

 public static string DuplicateTargetInRequest(Guid targetUserId) =>
 $"Bạn đánh giá người này nhiều lần trong một yêu cầu. Bạn kiểm tra lại nhé!";

 public static string AlreadyRated(Guid lobbyId, Guid targetUserId) =>
 $"Bạn đã đánh giá người này rồi. Mỗi người chỉ được đánh giá một lần thôi nhé!";

 public const string EmptyTagsForEntry =
 "Bạn cần chọn ít nhất một thẻ đánh giá cho mỗi người nhé.";

 public const string InvalidTagValue =
 "Một số thẻ đánh giá không hợp lệ. Bạn kiểm tra lại nhé!";

 public static string TargetProfileMissing(Guid targetUserId) =>
 $"Người được đánh giá chưa có hồ sơ đầy đủ nên chưa thể nhận đánh giá.";

 public static string LobbyAlreadyOpenForRating(Guid lobbyId) =>
 $"Cửa sổ đánh giá của phòng này đã được mở trước đó rồi.";

 public static string LobbyCannotOpenRating(Guid lobbyId) =>
 $"Phòng chưa đủ điều kiện để mở đánh giá. Phiên chơi cần hoàn tất trước nhé!";
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

    public static class WalkIn
    {
        public static string WalkInWindowNotFound(Guid windowId) =>
            $"Không tìm thấy khung giờ này.";

        public static string WalkInWindowNotAvailable(Guid windowId, string status) =>
            $"Khung giờ này đang không khả dụng (trạng thái: '{status}'). " +
            $"Bạn thử chọn khung giờ khác nhé!";

        public static string NotEnoughSeats(int requested, int available) =>
            $"Không đủ chỗ trống. Bạn cần {requested} ghế nhưng chỉ còn {available} ghế. Thử đặt ít người hơn hoặc chọn khung giờ khác nhé!";

        public const string ConcurrentBooking =
            "Khung giờ này đang được đặt bởi nhân viên khác. Bạn thử lại sau một chút nhé!";

        public static string WalkInBookingNotFound(Guid bookingId) =>
            $"Không tìm thấy đặt chỗ này.";

        public const string WindowTooShort =
            "Khung giờ walk-in cần tối thiểu 30 phút. Khung giờ quá ngắn không thể tạo.";

        public const string ReservationMissingScheduledEndTime =
            "Reservation thiếu thời gian kết thúc (ScheduledEndTime) nên không thể tạo khung giờ walk-in. Lỗi dữ liệu — liên hệ admin.";
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

        // ===== BR-NEW-10 §XI.2 — Cooling-off từ chối tạo lobby future =====
        public static string InCoolingOffCannotCreateFutureLobby(DateTime? expiresAt) =>
            $"Bạn đang trong thời gian cooling-off{(expiresAt.HasValue ? $" đến {expiresAt.Value:yyyy-MM-dd HH:mm}" : "")}. " +
            "Trong thời gian này bạn chỉ có thể tạo lobby có playDate trong ngày hôm nay.";

        public static string WalletNotFound(Guid userId) =>
            $"Không tìm thấy ví của người dùng '{userId}'.";

        public static string ProfileNotFound(Guid userId) =>
            $"Người dùng '{userId}' chưa có hồ sơ.";

        // ===== BR-KARMA-02/03/05 — Karma system errors =====
        public const string AlreadyRestricted =
            "Tài khoản đang trong thời gian giới hạn (chỉ cho đặt slot >= 4 giờ). " +
            "Vui lòng liên hệ hỗ trợ nếu cần xem xét lại.";

        public const string AppealAlreadySubmitted =
            "Bạn đã gửi khiếu nại cho violation này rồi. Vui lòng đợi admin phản hồi.";

        public const string AppealReasonRequired =
            "Vui lòng nhập lý do khiếu nại (tối thiểu 10 ký tự).";

        public const string KarmaRestrictionSlotTooShort =
            "Tài khoản đang bị giới hạn do vi phạm karma. " +
            "Bạn chỉ có thể đặt slot có thời lượng từ 4 giờ trở lên. " +
            "Vui lòng liên hệ hỗ trợ nếu cần xem xét lại.";

        // ===== R-01 (BR-RISK-02) — PlayerAlert errors =====
        public static string AlertNotFound(Guid alertId) =>
            $"Không tìm thấy cảnh báo '{alertId}'.";

        public static string AlertAlreadyProcessed(PlayerAlertStatus status) =>
            $"Cảnh báo đã được xử lý (trạng thái: {status}).";

        public const string AlertAlreadyResolved =
            "Cảnh báo này đã được resolve. Không thể xử lý lại.";

        public const string SystemSuspensionExpiredReason =
            "Tài khoản được tự động mở khóa sau khi hết hạn suspension.";

        public const string AlertStaleDismissedReason =
            "Cảnh báo tự đóng do quá 30 ngày không được admin xử lý.";

        public const string InvalidViolationCategoryFilter =
        "Giá trị lọc loại vi phạm không hợp lệ.";

        public static string ExtendAdditionalDaysRange(int maxDays) =>
        $"Số ngày gia hạn phải nằm trong khoảng 1 đến {maxDays}.";

        public const string ExtendReasonMinLength =
        "Lý do gia hạn phải có ít nhất 10 ký tự. Bạn bổ sung thêm nhé!";
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
 "Không xác định được tài khoản đang đăng nhập. Bạn vui lòng đăng nhập lại nhé.";

 public const string UserNoLongerExists =
 "Tài khoản không còn tồn tại. Bạn vui lòng đăng nhập lại nhé.";

 public const string TokenExpired =
 "Phiên đăng nhập đã hết hạn. Bạn vui lòng đăng nhập lại để tiếp tục nhé.";

 public const string TokenInvalidSignature =
 "Không thể xác thực tài khoản. Bạn vui lòng đăng nhập lại nhé.";

 public const string TokenInvalid =
 "Phiên đăng nhập không hợp lệ. Bạn vui lòng đăng nhập lại nhé.";

 public const string AuthenticationFailed =
 "Đăng nhập thất bại. Bạn kiểm tra lại thông tin đăng nhập nhé.";

 public const string AuthorizationHeaderMissing =
 "Bạn cần đăng nhập để thực hiện thao tác này. Vui lòng đăng nhập trước nhé.";

 public const string AccessDenied =
 "Bạn không có quyền thực hiện thao tác này. Liên hệ hỗ trợ nếu cần.";
 }

 public static class AccountAccess
 {
 public const string ActionSignIn = "Đăng nhập";
 public const string ActionGoogleSignIn = "Đăng nhập Google";
 public const string ActionTokenRefresh = "Làm mới phiên";
 public const string ActionSendVerificationEmail = "Gửi email xác minh";
 public const string ActionEmailVerification = "Xác minh email";
 public const string ActionPasswordResetRequest = "Yêu cầu đặt lại mật khẩu";
 public const string ActionPasswordReset = "Đặt lại mật khẩu";
 public const string ActionPasswordChange = "Đổi mật khẩu";
 public const string ActionGoogleAccountLinking = "Liên kết tài khoản Google";

 public const string BannedPermanent =
 "Tài khoản của bạn đã bị cấm vĩnh viễn. Nếu bạn nghĩ đây là nhầm lẫn, hãy liên hệ hỗ trợ nhé.";

 public const string AccountInactive =
 "Tài khoản của bạn đã bị tạm khóa. Liên hệ hỗ trợ để được kích hoạt lại nha.";

 public static string BannedPermanentWithReason(string reason) =>
 $"Tài khoản của bạn đã bị cấm vĩnh viễn. Lý do: {reason}. Nếu bạn nghĩ đây là nhầm lẫn, hãy liên hệ hỗ trợ nhé.";

 public static string SuspendedUntil(DateTime lockoutEnd) =>
 $"Tài khoản của bạn đang bị tạm khóa đến {lockoutEnd:dd/MM/yyyy HH:mm}. Bạn thử lại sau nhé!";

 public static string SuspendedUntilWithReason(DateTime lockoutEnd, string reason) =>
 $"Tài khoản của bạn đang bị tạm khóa đến {lockoutEnd:dd/MM/yyyy HH:mm}. Lý do: {reason}. Bạn thử lại sau nhé!";

 public const string SuspendedIndefinite =
 "Tài khoản của bạn đang bị tạm khóa. Liên hệ hỗ trợ để được giải quyết nhé.";

 public static string SuspendedIndefiniteWithReason(string reason) =>
 $"Tài khoản của bạn đang bị tạm khóa. Lý do: {reason}. Liên hệ hỗ trợ để được giải quyết nhé.";

 public static string Restricted(string message) => message;

 public static string LoginDeniedBanned(string? reason = null) =>
 string.IsNullOrWhiteSpace(reason)
 ? "Tài khoản của bạn đã bị cấm vĩnh viễn. Nếu bạn nghĩ đây là nhầm lẫn, hãy liên hệ hỗ trợ nhé."
 : $"Tài khoản của bạn đã bị cấm vĩnh viễn. Lý do: {reason}. Nếu bạn nghĩ đây là nhầm lẫn, hãy liên hệ hỗ trợ nhé.";

 public static string LoginDeniedSuspended(DateTime lockoutEnd, string? reason = null) =>
 string.IsNullOrWhiteSpace(reason)
 ? $"Tài khoản của bạn đang bị tạm khóa đến {lockoutEnd:dd/MM/yyyy HH:mm}. Bạn thử lại sau nhé!"
 : $"Tài khoản của bạn đang bị tạm khóa đến {lockoutEnd:dd/MM/yyyy HH:mm}. Lý do: {reason}. Bạn thử lại sau nhé!";
 }

 public static class Http
 {
 public static string Fallback(int statusCode, string path) => statusCode switch
 {
 400 => $"Dữ liệu gửi lên không hợp lệ. Vui lòng kiểm tra lại thông tin và thử lại nha.",
 401 => $"Phiên đăng nhập đã hết hạn. Bạn vui lòng đăng nhập lại để tiếp tục nhé.",
 403 => $"Bạn không có quyền thực hiện thao tác này. Liên hệ hỗ trợ nếu cần.",
 404 => $"Không tìm thấy thông tin bạn yêu cầu. Vui lòng kiểm tra lại đường dẫn.",
 409 => $"Thao tác bị xung đột với dữ liệu hiện tại. Bạn thử làm mới trang rồi thử lại nha.",
 429 => $"Bạn thao tác hơi nhanh đấy! Hãy chờ một chút rồi thử lại nhé.",
 500 => $"Hệ thống đang bận chút xíu. Bạn thử lại sau vài phút nha!",
 _ => $"Đã xảy ra lỗi không mong đợi. Bạn thử lại sau nhé."
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

 public const string InvalidRequestBody =
 "Dữ liệu gửi lên không hợp lệ. Vui lòng kiểm tra và thử lại.";
 }

 public static class Validation
 {
 public const string RequestFailed = "Dữ liệu gửi lên không hợp lệ cho '{0}': {1}";
 public const string FieldRequired = "Trường '{0}' là bắt buộc. Bạn vui lòng điền đầy đủ thông tin nhé.";

 /// <summary>
 /// Tạo thông điệp lỗi validation thân thiện tiếng Việt cho field bị lỗi khi ASP.NET Core auto-trả về 400 trước khi controller chạy.
 /// Phần message user-facing LUÔN tiếng Việt; chi tiết kỹ thuật gốc của ASP.NET được giữ riêng ở <c>data.fields</c> để FE/dev debug.
 /// </summary>
 /// <param name="fieldName">Tên field bị lỗi (vd: <c>PreferredEndTime</c>, <c>PreferredStartTime</c>). Để trống = không trích xuất được.</param>
 /// <param name="errorCount">Tổng số lỗi trong request. 1 = số ít, &gt;1 = số nhiều.</param>
 public static string FieldValidationFailed(string fieldName, int errorCount = 1)
 {
 var hasField = !string.IsNullOrEmpty(fieldName) && fieldName != "$";
 return errorCount <= 1
 ? (hasField
 ? $"Trường '{fieldName}' chưa hợp lệ. Bạn kiểm tra và thử lại nhé!"
 : "Dữ liệu gửi lên chưa hợp lệ. Bạn kiểm tra lại rồi thử lại nhé!")
 : (hasField
 ? $"Có {errorCount} trường chưa hợp lệ — trường đầu tiên: '{fieldName}'. Bạn kiểm tra và thử lại nhé!"
 : $"Có {errorCount} trường chưa hợp lệ. Bạn kiểm tra và thử lại nhé!");
 }

 /// <summary>Fallback khi ModelState invalid nhưng không trích xuất được field name.</summary>
 public const string GenericValidationFailed =
 "Dữ liệu gửi lên chưa hợp lệ. Bạn kiểm tra lại rồi thử lại nhé!";

 // ===== Reservation flow fields (BR §XXI-A.2..21A.3) =====
 // Friendly Vietnamese messages cho các field DTO của Reservation flow —
 // được dùng bởi InvalidModelStateResponseFactory khi ASP.NET Core auto
 // reject request trước khi controller chạy (do [Required] validation).
 // Message này hiển thị trực tiếp lên UI cho người dùng cuối, nên phải
 // gợi ý cách sửa cụ thể thay vì chỉ chung chung "field không hợp lệ".

 /// <summary>
 /// BR-RESV-02: <c>PreferredEndTime</c> không bắt buộc — server tự tính
 /// <c>scheduledEndTime</c> từ <c>timeSlot</c>. FE không cần (và không nên) gửi field này.
 /// Hiển thị hướng dẫn thân thiện thay vì bắt FE gửi field không tồn tại trong spec.
 /// </summary>
 public const string ReservationPreferredEndTimeNotNeeded =
 "Trường 'preferredEndTime' không cần gửi lên — hệ thống sẽ tự tính giờ kết thúc dựa trên khung giờ (timeSlot) bạn đã chọn. Bạn bỏ field này khỏi request rồi thử lại nhé!";

 /// <summary>
 /// BR-RESV-02: <c>PreferredStartTime</c> nên nằm trong khung giờ đã chọn.
 /// Nếu FE gửi giờ nằm ngoài khung, đây là message gợi ý.
 /// </summary>
 public const string ReservationPreferredStartTimeOutOfRange =
 "Giờ bắt đầu ưu tiên (preferredStartTime) phải nằm trong khung giờ bạn đã chọn (timeSlot). Ví dụ: khung 'evening' (17:00–23:00) → chỉ chọn giờ từ 17:00 đến 23:00.";

 /// <summary>
 /// <c>TimeSlot</c> enum không hợp lệ (giá trị ngoài 0–3, vd: do FE deserialize lỗi).
 /// </summary>
 public const string ReservationTimeSlotInvalid =
 "Khung giờ (timeSlot) không hợp lệ. Vui lòng chọn 1 trong 4 khung: morning (06:00–12:00), afternoon (12:00–17:00), evening (17:00–23:00), lateNight (23:00–06:00).";

 /// <summary>
 /// <c>PlayDate</c> nằm ngoài khoảng cho phép [today, today+7] (BR §VIII).
 /// Thường do FE dùng date cũ hoặc date quá xa.
 /// </summary>
 public const string ReservationPlayDateOutOfRange =
 "Ngày dự kiến chơi (playDate) phải nằm trong vòng 7 ngày tới. Bạn chọn lại ngày khác nhé!";

 /// <summary>
 /// <c>MinPlayers</c>/<c>MaxPlayers</c> ngoài Range(1, 30) hoặc min &gt; max.
 /// </summary>
 public const string ReservationPlayerCountInvalid =
 "Số người chơi không hợp lệ. Số tối thiểu (minPlayers) phải từ 1–30 và không được lớn hơn số tối đa (maxPlayers).";

 /// <summary>
 /// <c>IdempotencyKey</c> thiếu hoặc không đúng format (8–128 ký tự).
 /// </summary>
 public const string ReservationIdempotencyKeyInvalid =
 "Mã idempotency (idempotencyKey) phải dài từ 8 đến 128 ký tự. Bạn kiểm tra lại nhé!";

 /// <summary>
 /// Lookup message cụ thể cho các field của Reservation flow.
 /// Trả về <c>null</c> nếu field không thuộc domain này (để factory dùng generic message).
 /// </summary>
 /// <param name="fieldName">Tên field (PascalCase như trong DTO, vd: <c>PreferredEndTime</c>).</param>
 /// <param name="errorMessage">Error message gốc của ASP.NET (<c>ModelStateEntry.Errors[0].ErrorMessage</c>) — dùng để phân biệt "required" vs "range" vs "format".</param>
 public static string? GetReservationFieldMessage(string? fieldName, string? errorMessage = null)
 {
 if (string.IsNullOrEmpty(fieldName))
 {
 return null;
 }

 // Detect "required" từ message gốc (ASP.NET dùng "The X field is required.")
 var isRequired = !string.IsNullOrEmpty(errorMessage)
 && errorMessage.Contains("is required", StringComparison.OrdinalIgnoreCase);

 return fieldName switch
 {
 "PreferredEndTime" => ReservationPreferredEndTimeNotNeeded,
 "PreferredStartTime" => isRequired
 ? "Giờ bắt đầu ưu tiên (preferredStartTime) là bắt buộc. Bạn chọn giờ trong khung giờ (timeSlot) đã chọn nhé."
 : ReservationPreferredStartTimeOutOfRange,
 "TimeSlot" => ReservationTimeSlotInvalid,
 "PlayDate" => ReservationPlayDateOutOfRange,
 "MinPlayers" or "MaxPlayers" => ReservationPlayerCountInvalid,
 "IdempotencyKey" => ReservationIdempotencyKeyInvalid,
 "CafeId" => "Mã quán (cafeId) là bắt buộc. Bạn kiểm tra lại nhé!",
 "GameId" => "Mã game (gameId) là bắt buộc. Bạn kiểm tra lại nhé!",
 _ => null,
 };
 }

 public const string TimeSlotRequired = "Khung giờ (TimeSlot) là bắt buộc. Bạn vui lòng chọn khung giờ hợp lệ nhé.";
 public const string EmailRequired = "Email là bắt buộc. Bạn nhập email để tiếp tục nhé.";
 public const string EmailInvalid = "Email không đúng định dạng. Bạn kiểm tra lại email nhé.";
 public const string EmailMaxLength = "Email không được dài quá 256 ký tự.";
 public const string PasswordRequired = "Bạn chưa nhập mật khẩu. Nhập mật khẩu để tiếp tục nhé.";
 public const string PasswordLength8To100 = "Mật khẩu phải từ 8 đến 100 ký tự.";
 public const string PasswordLength6To100 = "Mật khẩu phải từ 6 đến 100 ký tự.";
 public const string PasswordMin8 = "Mật khẩu phải có ít nhất 8 ký tự.";
 public const string UsernameRequired = "Tên đăng nhập là bắt buộc. Bạn nhập tên đăng nhập nhé.";
 public const string UsernameLength3To100 = "Tên đăng nhập phải từ 3 đến 100 ký tự.";
 public const string UsernameMax100 = "Tên đăng nhập không được dài quá 100 ký tự.";
 public const string UsernameOrEmailRequired = "Bạn nhập tên đăng nhập hoặc email để tiếp tục nhé.";
 public const string UsernameOrEmailLength3To256 = "Tên đăng nhập hoặc email phải từ 3 đến 256 ký tự.";
 public const string PhoneInvalid = "Số điện thoại không đúng định dạng. Bạn kiểm tra lại số điện thoại nhé.";
 public const string PhoneMax50 = "Số điện thoại không được dài quá 50 ký tự.";
 public const string RoleRequired = "Vai trò là bắt buộc.";
 public const string RoleMax32 = "Tên vai trò không được dài quá 32 ký tự.";
 public const string AccountStatusMax32 = "Trạng thái tài khoản không được dài quá 32 ký tự.";
 public const string SearchMax100 = "Từ khóa tìm kiếm không được dài quá 100 ký tự.";
 public const string PageRange1To100 = "Số trang phải từ 1 đến 100.";
 public const string PageSizeRange1To100 = "Số mục mỗi trang phải từ 1 đến 100.";
 public const string BioMax1000 = "Tiểu sử không được dài quá 1000 ký tự.";
 public const string GlobalEloMinZero = "Điểm Elo không được âm.";
 public const string LevelMin1 = "Cấp độ phải ít nhất là 1.";
 public const string FirstNameMax100 = "Tên không được dài quá 100 ký tự.";
 public const string LastNameMax100 = "Họ không được dài quá 100 ký tự.";
 public const string AvatarUrlRequired = "Bạn chưa chọn ảnh đại diện. Vui lòng chọn ảnh nhé.";
 public const string AvatarUrlInvalid = "Link ảnh đại diện không hợp lệ. Bạn chọn ảnh khác nhé.";
 public const string BlockReasonRequired = "Bạn cần nhập lý do khóa tài khoản.";
 public const string BlockReasonMax500 = "Lý do khóa không được dài quá 500 ký tự.";
 public const string RejectionReasonRequired = "Bạn cần nhập lý do từ chối.";
 public const string RejectionReasonMax1000 = "Lý do từ chối không được dài quá 1000 ký tự.";
 public const string CafeNameMax200 = "Tên quán không được dài quá 200 ký tự.";
 public const string AddressMax500 = "Địa chỉ không được dài quá 500 ký tự.";
 public const string PhoneNumberMax50 = "Số điện thoại không được dài quá 50 ký tự.";
 public const string DescriptionMax2000 = "Mô tả không được dài quá 2000 ký tự.";
 public const string LatitudeRange = "Vĩ độ phải từ -90 đến 90.";
    public const string LongitudeRange = "Kinh độ phải từ -180 đến 180.";
    public const string RadiusRange = "Bán kính tìm kiếm phải từ 0 đến 500 km.";
    public const string InvalidCoordinatesOrRadius =
    "Tọa độ hoặc bán kính không hợp lệ. Vĩ độ: -90 đến 90, Kinh độ: -180 đến 180, Bán kính: 0 đến 500 km.";
    public const string GoogleIdTokenRequired = "Bạn cần đăng nhập Google để tiếp tục.";
 public const string GoogleIdTokenLength = "Token Google không hợp lệ. Bạn đăng nhập lại nhé.";
 public const string RefreshTokenRequired = "Bạn cần đăng nhập lại để tiếp tục.";
 public const string RefreshTokenLength = "Token đăng nhập không hợp lệ.";
 public const string VerificationTokenRequired = "Bạn cần nhập mã xác minh.";
 public const string VerificationTokenLength = "Mã xác minh phải từ 6 đến 10 ký tự.";
 public const string ResetTokenRequired = "Bạn cần nhập mã đặt lại mật khẩu.";
 public const string ResetTokenLength = "Mã đặt lại mật khẩu phải từ 6 đến 10 ký tự.";
 public const string NewPasswordRequired = "Bạn cần nhập mật khẩu mới.";
 public const string CurrentPasswordRequired = "Bạn cần nhập mật khẩu hiện tại.";
 public const string ConfirmPasswordRequired = "Bạn cần xác nhận mật khẩu mới.";
 public const string ConfirmPasswordMismatch = "Mật khẩu xác nhận không khớp. Bạn nhập lại cho đúng nhé.";
 public const string NameRequired = "Bạn cần nhập tên.";
 public const string NameMax100 = "Tên không được dài quá 100 ký tự.";
 public const string DateOfBirthFormat = "Ngày sinh phải theo định dạng yyyy-MM-dd. Ví dụ: 2000-01-15";
 public const string GameTemplateIdRequired = "Bạn cần chọn một game.";
 public const string LobbyIdRequired = "Bạn cần chọn một phòng chờ.";
 public const string OutcomeRequired = "Bạn cần chọn kết quả trận đấu.";
 public const string RatingsRequired = "Bạn cần chọn đánh giá.";
 public const string TargetUserIdRequired = "Bạn cần chọn người dùng.";
 public const string TagsRequired = "Bạn cần chọn ít nhất một thẻ đánh giá.";
 public const string BarcodeRequired = "Bạn cần quét hoặc nhập mã vạch.";
 public const string BarcodeLength = "Mã vạch phải từ 3 đến 50 ký tự.";
 public const string TableIdRequired = "Bạn cần chọn một bàn.";
 public const string BoxQuantityRange = "Số hộp phải từ 1 đến 1000.";
 public const string ComponentIdRequired = "Bạn cần chọn một linh kiện.";
 public const string PenaltyFeeRange = "Phí phạt phải từ 0 đến 999.999.999 VNĐ.";
 public const string CategoryNameRequired = "Bạn cần nhập tên thể loại.";
 public const string CategoryNameLength = "Tên thể loại phải từ 2 đến 100 ký tự.";
 public const string CategorySlugLength = "Slug thể loại phải từ 2 đến 100 ký tự.";
 public const string CategoryDescriptionMax500 = "Mô tả thể loại không được dài quá 500 ký tự.";
 public const string SortOrderRange = "Thứ tự sắp xếp phải từ 0 đến 9999.";
 public const string ComponentNameRequired = "Bạn cần nhập tên linh kiện.";
 public const string ComponentNameLength = "Tên linh kiện phải từ 1 đến 200 ký tự.";
 public const string DefaultQuantityRange = "Số lượng mặc định phải từ 1 đến 9999.";
 public const string ComponentKindRequired = "Bạn cần chọn loại linh kiện.";
 public const string ConfigKeyRequired = "Bạn cần nhập khóa cấu hình.";
 public const string ConfigKeyLength = "Khóa cấu hình phải từ 2 đến 100 ký tự.";
 public const string ConfigValueRequired = "Bạn cần nhập giá trị cấu hình.";
 public const string ConfigValueMax500 = "Giá trị cấu hình không được dài quá 500 ký tự.";
 public const string PunishmentActionRequired = "Bạn cần chọn hành động xử phạt.";
 public const string SuspendDurationRange = "Thời gian đình chỉ phải từ 1 đến 365 ngày.";
 public const string ReasonRequired = "Bạn cần nhập lý do.";
 public const string ReasonLength5To1000 = "Lý do phải từ 5 đến 1000 ký tự.";
 public const string KarmaAdjustmentRange = "Điểm karma phải từ -100 đến 100.";
 public const string OperationalStatusRequired = "Bạn cần chọn trạng thái vận hành.";
 public const string OperationalStatusMax32 = "Trạng thái vận hành không được dài quá 32 ký tự.";
 public const string OperationalStatusReasonMax500 = "Lý do trạng thái không được dài quá 500 ký tự.";
 public const string CafePartnerCafeNameRequired = "Bạn cần nhập tên quán.";
 public const string CafePartnerCafeNameLength = "Tên quán phải từ 5 đến 100 ký tự.";
 public const string CafePartnerAddressRequired = "Bạn cần nhập địa chỉ quán.";
 public const string CafePartnerAddressLength = "Địa chỉ phải từ 10 đến 500 ký tự.";
 public const string CafePartnerPhoneNumberRequired = "Bạn cần nhập số điện thoại quán.";
 public const string CafePartnerPhoneNumberLength = "Số điện thoại phải từ 10 đến 11 số.";
 public const string CafePartnerRepresentativeEmailRequired = "Bạn cần nhập email đại diện.";
 public const string CafePartnerBusinessLicenseRequired = "Bạn cần nhập số giấy phép kinh doanh.";
 public const string CafePartnerBusinessLicenseLength = "Số giấy phép kinh doanh phải từ 5 đến 50 ký tự.";
 public const string CafePartnerBusinessLicenseImageRequired = "Bạn cần tải lên ảnh giấy phép kinh doanh.";
 public const string WorkingHoursRequired = "Bạn cần cấu hình giờ mở cửa.";
 public const string PopularGamesListRequired = "Bạn cần chọn ít nhất một game phổ biến.";
 public const string PopularGamesListLength = "Danh sách game phổ biến phải từ 3 đến 2000 ký tự.";
 public const string TableCountRange = "Số bàn phải từ 1 đến 10000.";
 public const string PrivateRoomCountRange = "Số phòng riêng phải từ 0 đến 1000.";
 public const string GamesOwnedRange = "Số game sở hữu phải từ 1 đến 100000.";
 public const string BasePriceRange = "Giá cơ bản phải từ 0 đến 10.000.000 VNĐ.";
 public const string TieredBlockMinutesRange = "Thời gian block tính tiền phải từ 1 đến 1440 phút.";
 public const string TieredBlockRateRequired = "Với mô hình tính theo giờ, bạn cần nhập giá block lũy tiến.";
 public const string DepositPercentageRange = "Phần trăm cọc không được vượt quá 50% giá vé.";
 public const string SeatsPerTableRange = "Số ghế mỗi bàn phải từ 1 đến 50.";
 public const string TableNameLength = "Tên bàn phải từ 1 đến 100 ký tự.";
 public const string TableNoFieldsToUpdate = "Bạn cần gửi ít nhất một trường để cập nhật (Tên bàn, Số ghế, hoặc Thứ tự).";

 public const string LobbySearchLimitRange = "Số kết quả mỗi trang phải từ 1 đến 100.";
 public const string LobbySearchGeoRequired = "Bạn cần nhập đầy đủ thông tin vị trí (vĩ độ, kinh độ, bán kính) để tìm quán gần bạn.";
 public const string LobbySearchRadiusRange = "Bán kính tìm kiếm phải từ 0 đến 500 km.";

 public const string FriendSearchMinLength = "Từ khóa tìm kiếm phải có ít nhất 2 ký tự.";

 public static string FriendInvalidDirection(string validValues) =>
 $"Giá trị bạn chọn không hợp lệ. Chỉ chấp nhận: {validValues}.";

 public static string TimeSlotInvalid(string slot) =>
 $"Khung giờ '{slot}' không hợp lệ. Vui lòng chọn một trong: Morning, Afternoon, Evening, LateNight.";

 public static string TournamentScoreExceedsLimitSimple(Guid userId, int score, int maxScore) =>
        $"Người chơi '{userId}' có điểm {score} vượt quá giới hạn {maxScore} cho phép của giải đấu.";
 }

 public static class Friend
 {
 public static string UserNotFound(Guid userId) =>
 $"Không tìm thấy người dùng này.";

 public const string CannotSendToSelf =
 "Bạn không thể gửi lời mời kết bạn cho chính mình nhé!";

 public const string PendingRequestAlreadyExists =
 "Bạn đã gửi lời mời kết bạn với người này rồi. Hãy đợi họ phản hồi nhé!";

 public const string AlreadyFriends =
 "Hai bạn đã là bạn bè rồi!";

 public const string NotFriendRequestRecipient =
 "Bạn không phải người nhận lời mời kết bạn này.";

 public const string FriendRequestNotPending =
 "Lời mời kết bạn này đã được xử lý trước đó.";

 public static string FriendshipNotFound(Guid id) =>
 $"Không tìm thấy lời mời hoặc quan hệ bạn bè này.";

 public const string CannotRemoveAcceptedByOther =
 "Chỉ xóa được quan hệ bạn bè đang hoạt động.";

 public const string CannotCancelRequestNotRequester =
 "Chỉ người gửi mới có thể hủy lời mời kết bạn nhé!";

 public const string CannotCancelNonPendingRequest =
 "Chỉ hủy được lời mời đang chờ phản hồi.";

 public const string CannotViewRequestNotMember =
 "Bạn không có quyền xem lời mời này.";

 public const string CannotViewBlockedRequest =
 "Bạn không thể xem vì đã bị chặn bởi người này.";

 public const string BlockedByOtherParty =
 "Bạn đã bị người này chặn. Không thể gửi lời mời kết bạn.";

 public const string AlreadyBlockedOtherParty =
 "Bạn đã chặn người này trước rồi. Hãy bỏ chặn trước nhé!";

 public const string RequesterNotActive =
 "Tài khoản người gửi lời mời không còn hoạt động.";

 public const string AddresseeNotActive =
 "Tài khoản của bạn đang không hoạt động.";

 public const string RateLimitExceeded =
 "Bạn gửi lời mời hơi nhanh đấy! Hãy chờ một chút rồi thử lại nhé.";

 public const string FriendListPrivate =
 "Người này đã ẩn danh sách bạn bè.";

 public const string CannotBlockAdmin =
 "Không thể chặn tài khoản quản trị viên nhé!";

 public const string CannotBlockSelf =
 "Bạn không thể tự chặn chính mình nhé!";

 public const string CannotReportSelf =
 "Bạn không thể tự báo cáo chính mình nhé!";

 public const string CannotReportAdmin =
 "Không thể báo cáo tài khoản quản trị viên nhé!";

 public const string CannotReportNotFriend =
 "Bạn chỉ có thể báo cáo người đang là bạn bè hoặc đã từng kết bạn.";

 public static string ReportReasonRequired =>
 "Bạn cần nhập lý do báo cáo (từ 5 đến 1000 ký tự) để admin xem xét nhé.";

 public static string ReportAlreadyExists(Guid targetUserId) =>
 $"Bạn đã gửi báo cáo cho người này rồi. Admin đang xử lý, bạn đợi nhé!";

 public static string ReportNotFound(Guid id) =>
 $"Không tìm thấy báo cáo này.";

 public const string CannotSuggestToSelf =
 "Bạn không thể tự gợi ý kết bạn cho chính mình nhé!";

 public const string NoSuggestionsAvailable =
 "Hiện chưa có gợi ý kết bạn nào phù hợp với bạn. Thử cập nhật thêm thông tin hoặc mời bạn bè nhé!";

 public const string CannotViewOwnFriendList =
 "Bạn không thể tự xem danh sách bạn bè qua endpoint này. Hãy vào trang cá nhân của mình nhé!";

 public const string CannotViewOwnProfile =
 "Bạn không thể xem hồ sơ của chính mình qua endpoint này. Hãy dùng trang cá nhân nhé!";

 public const string CannotNoteSelf =
 "Bạn không thể tự tạo ghi chú cho chính mình nhé!";

 public static string NoteNotFound(Guid noteId) =>
 $"Không tìm thấy ghi chú này.";

 public static string NoteNotOwner(Guid noteId) =>
 $"Bạn không phải là người tạo ghi chú này.";

 public const string PrivacyRequestNotAccepting =
 "Người này đã tắt nhận lời mời kết bạn từ người lạ.";

 public const string CannotSendRequestToInactive =
 "Tài khoản người nhận đang không hoạt động nên không thể gửi lời mời.";

 public const string CannotRemoveFriendshipNotMember =
 "Bạn không có quyền xóa quan hệ bạn bè này.";

 public const string CannotBlockInactiveAccount =
 "Không thể chặn tài khoản không hoạt động.";

 public const string UnblockNotFound =
 "Không có ai đang bị bạn chặn cả.";

 public const string CannotUnblockNotBlocker =
 "Bạn không phải là người đã chặn người này.";

 public const string ProfileNotYetCreated =
 "Bạn cần hoàn tất hồ sơ trước khi sử dụng tính năng này nhé!";

 public const string BlockedCannotViewProfile =
 "Người này đã chặn bạn nên không thể xem hồ sơ.";
 }

 public static class LobbyInvite
 {
 public static string InviteNotFound(Guid id) =>
 $"Không tìm thấy lời mời này.";

 public const string CannotInviteSelf =
 "Bạn không thể mời chính mình vào phòng nhé!";

 public const string InviteeAlreadyMember =
 "Người này đã là thành viên của phòng rồi!";

 public const string PendingInviteAlreadyExists =
 "Bạn đã gửi lời mời cho người này rồi. Hãy đợi họ phản hồi nhé!";

 public const string InviterNotMember =
 "Bạn không phải thành viên của phòng này nên không thể mời người khác.";

 public const string InviteNotPending =
 "Lời mời này đã được xử lý trước đó.";

 public const string NotInviteRecipient =
 "Bạn không phải người nhận lời mời này.";

 public const string InviteExpired =
 "Lời mời đã hết hạn hoặc phòng không còn hoạt động.";

 public const string PrivateLobbyRequiresInvite =
 "Phòng riêng tư chỉ có thể tham gia qua lời mời hoặc mã chia sẻ.";

 public const string PrivateLobbyShareCodeRequiresFriendship =
 "Phòng riêng tư chỉ cho phép người là bạn với thành viên tham gia qua mã chia sẻ.";

 public static string ShareCodeInvalid =>
 "Mã chia sẻ không hợp lệ. Bạn kiểm tra lại mã nhé!";

    public const string InviteRateLimitExceeded =
    "Bạn gửi/nhận lời mời hơi nhiều rồi! Hãy nghỉ một chút rồi thử lại nhé.";

    // L-01: Share code brute-force protection
    public const string ShareCodeRateLimitExceeded =
    "Bạn thử mã chia sẻ hơi nhiều lần rồi! Vui lòng chờ 15 phút rồi thử lại nhé.";

    // ===== LobbyInviteService specific =====
    public const string LobbyClosedOrUnavailable =
    "Phòng đã đóng hoặc không còn hoạt động.";

    public const string PrivateLobbyInviterMustBeFriend =
    "Phòng riêng tư chỉ cho phép mời bạn bè đã chấp nhận lời mời.";

    public const string LobbyDisappeared =
    "Phòng không còn tồn tại.";

    public const string LobbyFullCannotAcceptInvite =
    "Phòng đã đầy người rồi! Bạn thử tìm phòng khác nhé.";

    public const string PrivateLobbyRequiresActiveFriendship =
    "Phòng riêng tư yêu cầu quan hệ bạn bè đang hoạt động.";

    public const string OnlyInviterCanCancel =
    "Chỉ người gửi lời mời mới có thể hủy lời mời nhé!";

    public const string OnlyLobbyMemberCanViewShareCode =
    "Chỉ thành viên của phòng mới xem được mã chia sẻ.";

    public static string InviteInvalidStatus(string status) =>
    $"Lời mời đang ở trạng thái không hợp lệ: '{status}'.";

 public const string AlreadyAccepted =
 "Lời mời đã được chấp nhận trước đó. Không thể thao tác lại.";
 }

 public static class Lobby
 {
 public static string NotFound(Guid lobbyId) =>
 $"Oops! Không tìm thấy phòng này. Phòng có thể đã bị hủy hoặc không tồn tại.";

 public const string LobbyNotFoundById =
 "Không tìm thấy phòng này.";

 public const string AlreadyMember =
 "Bạn đã là thành viên của phòng này rồi!";

 public const string NotMember =
 "Bạn không phải thành viên của phòng này.";

 public const string NotOpen =
 "Phòng này không còn mở tuyển người nữa.";

 public const string AlreadyClosed =
 "Phòng đã đóng rồi.";

 public const string SeatCountExceeded =
 "Số thành viên đã vượt quá số ghế cho phép.";

 public static string MaxMembersOutOfRange(int min, int max, int requested) =>
 $"Số người tối đa ({requested}) phải từ {min} đến {max}.";

 public static string MinPlayersInvalid(int currentCount, int minPlayers) =>
 $"Phòng cần ít nhất {minPlayers} người để khóa (hiện có {currentCount} người).";

 public const string HostCannotKickSelf =
 "Bạn không thể tự kick mình khỏi phòng. Hãy dùng chức năng rời phòng thay thế nhé!";

 public const string NotHost =
 "Chỉ chủ phòng mới thực hiện được thao tác này.";

 public const string AlreadyHost =
 "Bạn đã là chủ phòng rồi!";

 public const string CannotReportOwnLobby =
 "Bạn không thể báo cáo phòng mà mình là chủ phòng nhé!";

 public static string NotActiveMember(Guid lobbyId) =>
 $"Bạn không phải thành viên đang hoạt động của phòng này.";

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
 "Chỉ phòng ở trạng thái FULL hoặc WAITING_CHECK_IN mới chuyển sang IN_PROGRESS được.";

 public const string OnlyFullOrWaitingCheckInCanInProgress =
 "Chỉ phòng ở trạng thái FULL hoặc WAITING_CHECK_IN mới chuyển sang IN_PROGRESS được.";

 public const string OnlyInProgressCanClose =
 "Chỉ phòng đang chơi hoặc đang đánh giá mới đóng được.";

 public const string CannotSwitchHostWhenClosed =
 "Chỉ chuyển host được khi phòng đang mở hoặc đầy.";

 public const string CannotRegenerateShareCodeWhenClosed =
 "Chỉ tạo lại mã chia sẻ khi phòng đang mở hoặc đầy.";

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

// ===== BR-NEW-14: 4 proposals cho lobby at-risk =====

public const string TimeSlotSameAsCurrent =
"Khung giờ bạn chọn trùng với khung giờ hiện tại. Hãy chọn khung giờ khác.";

public static string BufferTooShortForTimeSlotChange(int bufferMinutes) =>
$"Khung giờ mới chỉ còn {bufferMinutes} phút buffer (cần ít nhất 60 phút). Hãy chọn khung giờ khác xa hơn.";

public const string LobbyBoostCooldown =
"Bạn chỉ có thể boost 1 lần mỗi 6 giờ. Vui lòng đợi một chút nhé!";

public const string LobbyBoostOnlyWhenOpen =
"Chỉ có thể boost khi phòng đang mở tuyển người.";
}

 // ===== BR-NEW-* § XXI-G Phase 2/3 =====
 public static class Reservation
    {
    public static string NotFound(Guid reservationId) =>
        $"Không tìm thấy đặt chỗ này. Có thể đã bị hủy hoặc không tồn tại.";

    // ===== POS QR check-in (BR §21A.7 — 2 chiều) =====
    public const string PosTokenExpired =
        "Mã QR đã hết hạn. Bạn nhờ nhân viên tạo mã mới nhé!";

    public const string PosTokenAlreadyUsed =
        "Mã QR đã được quét rồi. Bạn nhờ nhân viên tạo mã mới nhé!";

    public const string PosTokenRevoked =
        "Mã QR đã bị thu hồi. Bạn nhờ nhân viên tạo mã mới nhé!";

    // ===== BR-RES-07: Reservation bắt buộc có startTime + endTime =====
    public const string ReservationRequiresStartAndEnd =
        "Đặt chỗ bắt buộc phải có thời gian bắt đầu và thời gian kết thúc. " +
        "Vui lòng chọn đầy đủ preferredStartTime và preferredEndTime.";

    public const string PreferredTimesMustDiffer =
        "Thời gian kết thúc phải khác thời gian bắt đầu. Nếu chơi qua đêm, giờ kết thúc sẽ được hiểu là thuộc ngày hôm sau.";

    public static string PreferredStartBeforeOpen(TimeOnly openTime) =>
        $"Thời gian bắt đầu không được trước giờ mở cửa ({openTime:HH:mm}). Vui lòng chọn giờ bắt đầu khác.";

    public static string PreferredEndAfterClose(TimeOnly closeTime) =>
        $"Thời gian kết thúc không được sau giờ đóng cửa ({closeTime:HH:mm}). Vui lòng chọn giờ kết thúc khác.";

    // ===== BR-RES-08: endTime cùng ngày startTime hoặc ngày kế tiếp nếu qua đêm =====
    public const string ReservationEndTimeDifferentDay =
        "Thời gian kết thúc phải cùng ngày với thời gian bắt đầu, hoặc thuộc ngày hôm sau nếu giờ kết thúc nhỏ hơn giờ bắt đầu.";

    // ===== BR-RES-09: TimeSlot không hợp lệ =====
    public static string ReservationInvalidTimeSlot(TimeSlot slot) =>
        $"Khung giờ không hợp lệ ({(int)slot}). Chỉ chấp nhận: morning (06:00-12:00), afternoon (12:00-17:00), evening (17:00-23:00), lateNight (23:00-06:00).";

    public static string PosTokenNotFound(string token) =>
        $"Không tìm thấy mã QR '{token}' này. Vui lòng kiểm tra lại mã QR hoặc liên hệ nhân viên quán.";

    public static string PosTokenNotInCheckInWindow(Guid reservationId) =>
        $"Đặt chỗ của bạn chưa đến giờ check-in. Bạn đến đúng giờ hoặc nhờ nhân viên hỗ trợ nhé!";

    public static string NotReservationMember(Guid reservationId, Guid userId) =>
        $"Bạn không phải thành viên của đặt chỗ này nên không thể check-in.";

    public static string PosTokenReservationMissing =>
        "Mã QR này không liên kết với đặt chỗ nào. Bạn nhờ nhân viên tạo lại nhé!";

    public static string InvalidStatusForCheckIn(Guid reservationId, string currentStatus, string expectedStatus) =>
        $"Đặt chỗ của bạn chưa thể check-in (trạng thái: '{currentStatus}', cần: '{expectedStatus}').";

    // ===== Buffer validation (BR-LOBBY-01a/b/c) =====
    public static string BufferTooShort(int bufferMinutes, int minRequired) =>
 $"Oops! Thời gian để tuyển người chỉ còn {bufferMinutes} phút, cần ít nhất {minRequired} phút. Bạn chọn khung giờ khác xa hơn một chút nhé!";

 // ===== Play date range (BR § VIII: max 7 ngày trong tương lai) =====
 public static string PlayDateOutOfRange(int maxDaysAhead) =>
 $"Ngày dự kiến chơi phải nằm trong vòng {maxDaysAhead} ngày tới.";

 public const string CafeConfigMissing =
 "Quán chưa được cấu hình BVC. Không thể đặt cọc. Liên hệ quản lý quán.";

    public const string SeatInventoryNotConfigured =
    "Quán chưa được cấu hình số ghế cho ngày và khung giờ này. Liên hệ quản lý quán.";

    public const string CafeScheduleClosedForPlayDate =
    "Quán đóng cửa vào ngày bạn chọn. Vui lòng chọn ngày khác.";

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
        "Bạn đang là chủ phòng của một phòng khác rồi. Hãy hủy phòng đó hoặc đợi nó kết thúc rồi hãy tạo phòng mới nhé!";

    public const string ActiveLobbyMemberLimitReached =
        "Bạn đang tham gia một phòng khác rồi. Hãy rời phòng đó trước khi tạo phòng mới làm chủ phòng nhé!";

    public const string TotalLobbyLimitReached =
        "Bạn đã đạt giới hạn tối đa 2 phòng (1 host + 1 member). Hãy rời hoặc kết thúc một phòng trước nhé!";

    // ===== BR-USER-LIMIT-04/05: cross-role =====
    public const string MemberCannotCreateLobby =
        "Bạn đang tham gia một phòng khác nên chưa thể tạo phòng mới. Hãy rời phòng hiện tại trước nhé!";

    public const string HostCannotJoinLobby =
        "Bạn đang là chủ phòng của một phòng khác nên chưa thể tham gia phòng khác. Hãy hủy phòng của bạn trước nhé!";

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
 $"Số dư BVC khả dụng của bạn ({available} BVC) không đủ. Bạn cần {required} BVC để đặt cọc. Nhấn 'Nạp BVC' để nạp thêm nhé!";

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
 "Chỉ chủ phòng mới có thể hủy phòng.";

 public static string CancelWithinGraceFullRefund(string graceMinutes) =>
 $"Hủy trong vòng {graceMinutes} phút đầu (chưa có ai tham gia) — hoàn 100% BVC cho bạn!";

 public static string CancelRefundMatrix(double hoursToStart, long percent) =>
 $"Hủy cách giờ chơi {hoursToStart:F1} giờ — bạn được hoàn {percent}% BVC.";

 public const string CancelAfter24hFullRefund =
 "Hủy trước giờ chơi từ 24 giờ trở lên — hoàn 100% BVC!";

 public const string Cancel6To24hHalfRefund =
 "Hủy trong khoảng 6–24 giờ trước giờ chơi — bạn được hoàn 50% BVC.";

 public const string CancelUnder6hNoRefund =
 "Hủy dưới 6 giờ trước giờ chơi — rất tiếc bạn sẽ không được hoàn BVC.";

 public const string CancelMissingScheduledStartTime =
 "Không thể hủy reservation vì thiếu thời gian bắt đầu (ScheduledStartTime). Lỗi dữ liệu — liên hệ admin.";

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

    // ===== BR-REFUND-08 (walk-in-override-design §2.3): late cancel sau check-in =====
    public const string OnlyHostCanLateCancelAfterCheckin =
        "Chỉ host mới có thể hủy reservation sau khi đã check-in.";

    public static string MustBeCheckedInToLateCancel(Guid reservationId, object currentStatus) =>
        $"Reservation '{reservationId}' chưa thể hủy sau check-in vì chưa đến trạng thái CheckedIn (hiện tại: {currentStatus}). Hãy hoàn tất check-in tại quán trước.";

    public static string NotEnoughPlayedForLateCancelRefund(decimal playedRatio) =>
        $"Bạn đã chơi {playedRatio:P0} khung giờ (dưới 50%) nên không đủ điều kiện hoàn cọc theo BR-REFUND-08. " +
        "Khoản cọc sẽ thuộc về quán.";

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
        $"Reservation '{reservationId}' không thể hủy ở trạng thái hiện tại ({status}). Chỉ có thể hủy khi đang ở trạng thái Holding hoặc Confirmed (chưa check-in).";

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

    public const string CheckInOutsideAllowedWindow =
        "Chỉ được check-in trong khoảng 30 phút trước đến 30 phút sau giờ hẹn. " +
        "Vui lòng liên hệ nhân viên nếu cần hỗ trợ đặc biệt.";

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

 public const string AlreadyInWaitlist =
 "Bạn đã có trong waitlist của giải này.";

 public const string KickReasonRequired =
 "Lý do kick là bắt buộc.";

 public static string GameTemplateNotFound(Guid gameTemplateId) =>
 $"Game template '{gameTemplateId}' không tìm thấy.";

 public static string CafeNotFound(Guid cafeId) =>
 $"Cafe '{cafeId}' không tìm thấy.";

 public const string OnlyDeleteInSpecificStatus =
 "Chỉ xóa được tournament ở trạng thái Draft, Cancelled hoặc Completed.";

 public const string OnlyOpenRegistrationDraft =
 "Chỉ mở đăng ký cho tournament ở trạng thái Draft.";

 public const string RegistrationDeadlineMustBeFuture =
 "Thời hạn đăng ký phải trong tương lai.";

 public const string OnlyCloseRegistrationOpen =
 "Chỉ đóng đăng ký cho tournament đang mở đăng ký.";

 public const string OnlyStartRegistrationClosed =
 "Chỉ bắt đầu tournament ở trạng thái RegistrationClosed.";

 public static string NotEnoughParticipants(int required, int current) =>
 $"Không đủ người tham gia. Cần tối thiểu {required}, hiện có {current}.";

 public const string AlreadyEndedOrCancelled =
 "Tournament đã kết thúc hoặc bị hủy trước đó.";

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

    public static string CannotViewFutureRound(Guid tournamentId, int requestedRound, int currentRound) =>
    $"Không thể xem vòng {requestedRound} của giải đấu '{tournamentId}'. Hiện tại đang ở Round {currentRound}. Vui lòng ghi nhận kết quả các bàn đấu hiện tại trước khi chuyển vòng.";

    public static string CannotStartFutureRoundMatch(Guid matchId, int matchRound, int currentRound) =>
    $"Không thể bắt đầu bàn đấu vòng {matchRound} (ID: '{matchId}'). Giải đấu hiện đang ở Round {currentRound}. Hãy hoàn thành các bàn đấu hiện tại trước.";

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

 public static string CannotKickAfterCheckIn(TournamentParticipantStatus currentStatus)
 {
     var message = currentStatus switch
     {
         TournamentParticipantStatus.CheckedIn =>
             "Không thể kick khi participant đã check-in tại quán. Hãy set NoShow thay thế.",
         TournamentParticipantStatus.Active =>
             "Không thể kick khi giải đã bắt đầu.",
         TournamentParticipantStatus.Finished =>
             "Không thể kick khi giải đã kết thúc.",
         _ => $"Không thể kick ở trạng thái '{currentStatus}'."
     };
     return $"[{currentStatus}] {message}";
 }

 public static string CannotKickParticipantTerminal(TournamentStatus tournamentStatus) =>
     $"Không thể kick participant khi giải đang ở trạng thái '{tournamentStatus}'.";

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

 public static string RegistrationDeadlineInPast(DateTime deadline) =>
 "Háº¡n chÃ³t Ä‘Äƒng kÃ½ '{deadline:HH:mm dd/MM/yyyy}' khÃ´ng Ä‘Æ°á»£c náº±m trong quÃ¡ khá»©.";

 public const string MinEloGreaterThanMaxElo =
 "Elo tá»‘i thiá»ƒu pháº£i nhá» hÆ¡n hoáº·c báº±ng Elo tá»‘i Ä‘a.";

 public static string InvalidStatusFilter(string status, string allowedValues) =>
 "Bá»™ lá»c tráº¡ng thÃ¡i '{status}' khÃ´ng há»£p lá»‡. Chá»‰ cháº¥p nháº­n: {allowedValues}.";

 public const string ProfileRequiredForJoin =
 "Báº¡n cáº§n hoÃ n thiá»‡n há»“ sÆ¡ (profile) trÆ°á»›c khi Ä‘Äƒng kÃ½ giáº£i Ä‘áº¥u.";

 public static string EloOutOfRange(int currentElo, int? minElo, int? maxElo) =>
 "Elo hiá»‡n táº¡i ({currentElo}) náº±m ngoÃ i khoáº£ng cho phÃ©p ({minElo} - {maxElo}).";

 public static string CannotAddWalkInInStatus(object currentStatus) =>
 "KhÃ´ng thá»ƒ thÃªm walk-in khi giáº£i Ä‘áº¥u Ä‘ang á»Ÿ tráº¡ng thÃ¡i '{currentStatus}'.";

 public const string FinalMatchExists =
 "Tráº­n chung káº¿t Ä‘Ã£ tá»“n táº¡i. KhÃ´ng thá»ƒ táº¡o thÃªm.";

 public const string RoundInProgress =
 "VÃ²ng Ä‘áº¥u hiá»‡n Ä‘ang diá»…n ra. HÃ£y hoÃ n táº¥t trÆ°á»›c khi thao tÃ¡c tiáº¿p.";

 public static string WalkInDuplicateName(string trimmedName) =>
 "TÃªn walk-in '{trimmedName}' Ä‘Ã£ tá»“n táº¡i trong giáº£i Ä‘áº¥u nÃ y. Vui lÃ²ng chá»n tÃªn khÃ¡c.";

 public static string NoShowAfterRoundStarted(object status) =>
 "KhÃ´ng thá»ƒ Ä‘Ã¡nh dáº¥u no-show sau khi vÃ²ng Ä‘áº¥u Ä‘Ã£ báº¯t Ä‘áº§u (status: '{status}').";

 public static string NoShowInvalidStatus(object status) =>
 "No-show chá»‰ cÃ³ thá»ƒ Ä‘Ã¡nh dáº¥u khi tráº­n Ä‘áº¥u Ä‘ang á»Ÿ tráº¡ng thÃ¡i '{status}'.";

 public static string ResultsIncomplete(int slots, int results) =>
 "Káº¿t quáº£ vÃ²ng Ä‘áº¥u chÆ°a Ä‘Æ°á»£c cáº­p nháº­t Ä‘áº§y Ä‘á»§. CÃ³ {slots} slot nhÆ°ng má»›i nháº­p {results} káº¿t quáº£.";

 public static string ScoreExceedsLimit(Guid userId, int score, int maxScore, string gameName) =>
        $"Người chơi '{userId}' có điểm {score} vượt quá giới hạn {maxScore} của giải đấu '{gameName}'.";

 public static string MatchEditOnlyCompleted(object status) =>
 "Chá»‰ cÃ³ thá»ƒ chï¿½nh sá»­a káº¿t quáº£ khi tráº­n Ä‘áº¥u á»Ÿ tráº¡ng thÃ¡i Completed (hiá»‡n táº¡i: '{status}').";

 public const string FinalMatchCannotEdit =
 "KhÃ´ng thá»ƒ chá»‰nh sá»­a tráº­n chung káº¿t khi chÆ°a Ä‘áº¿n thá»i Ä‘iá»ƒm phÃ¹ há»£p.";

 public static string MatchEditRoundConflict(int nextRound, int matchRound) =>
 "VÃ²ng Ä‘áº¥u hiá»‡n khÃ´ng khá»›p Ä‘á»ƒ sá»­a tráº­n nÃ y (next={nextRound}, match={matchRound}).";

 public static string NotEnoughActiveForNextRound(int nextRound, int active) =>
        $"Không đủ người chơi hoạt động để bắt đầu vòng {nextRound} (hiện có {active}).";

 public static string FinalPairingsInvalid(int finalistCount) =>
 "Cáº·p Ä‘áº¥u chung káº¿t khÃ´ng há»£p lá»‡ vá»›i {finalistCount} finalist.";

 public static string CannotSwitchManualWithMatches(int currentRound) =>
        $"Không thể chuyển sang chế độ ghép đôi thủ công khi vòng đấu hiện tại (Vòng {currentRound}) đã có bàn đấu. " +
        $"Vui lòng hoàn thành vòng đấu hiện tại trước, sau đó chuyển sang Manual cho vòng tiếp theo.";

 public static string CannotSwitchManualWithActiveMatches =>
        "Không thể chuyển sang chế độ ghép đôi thủ công khi có bàn đấu đang diễn ra hoặc đã kết thúc. " +
        "Vui lòng hoàn thành hoặc hủy các bàn đấu hiện tại trước khi thay đổi chế độ ghép đôi.";

 public static string RoundHasMatches(int roundNumber) =>
 "VÃ²ng {roundNumber} Ä‘Ã£ cÃ³ tráº­n Ä‘áº¥u tá»“n táº¡i. KhÃ´ng thá»ƒ thá»±c hiá»‡n thao tÃ¡c nÃ y.";

 public static string RoundCannotResetPairings(int roundNumber) =>
 "KhÃ´ng thá»ƒ táº¡o láº¡i cáº·p Ä‘áº¥u cho vÃ²ng {roundNumber} khi Ä‘Ã£ cÃ³ káº¿t quáº£.";

public static string RoundNumberOutOfRange(int roundNumber, int totalRounds) =>
"Số vòng {roundNumber} nằm ngoài phạm vi cho phép (1 - {totalRounds}).";

    public static string InvalidPairingUserIds(string userIds) =>
        $"Danh sách userId ghép cặp không hợp lệ: [{userIds}].";

 public static string FinalPairingInvalidSingle(int finalistCount) =>
        $"Cặp chung kết chỉ chấp nhận đúng 2 người chơi (hiện có {finalistCount}).";

    public static string PairingSizeInvalid(int matchNumber, int playerCount) =>
        $"Số người chơi của cặp đấu thứ {matchNumber} ({playerCount}) không hợp lệ.";

 

public static string RoundHasNoMatches(int roundNumber) =>
$"Vòng {roundNumber} chưa có trận đấu nào.";

public static string PlayerNotInMatch(Guid playerId, int matchNumber) =>
$"Người chơi không có trong bàn {matchNumber}.";

public const string SwapOnlyAllowedWhenOnGoing =
"Không thể hoán đổi khi giải đấu không diễn ra.";

public const string SwapSameMatch =
"Hai người chơi đang ở cùng một bàn. Không cần hoán đổi.";

public const string SwapMatchAlreadyCompleted =
"Không thể hoán đổi vì bàn đấu đã hoàn thành.";

public const string SwapMatchOnGoing =
"Không thể hoán đổi vì bàn đấu đang diễn ra.";

public const string NoOfferToDecline =
"Bạn không có offer nào để từ chối.";

 public static class Spectator
 {
 public const string CannotSpectateUnpublished =
 "Không thể spectate tournament chưa được công bố.";

 public const string CannotSpectateAsParticipant =
 "Bạn là người chơi trong tournament này, không cần spectate.";

        public const string NotSpectating =
        "Bạn không spectate tournament này.";
    }

 public static class Waitlist
 {
 public const string OnlyJoinWhenRegistrationOpen =
 "Chỉ có thể tham gia waitlist khi giải đang mở đăng ký.";

 public const string RegistrationDeadlinePassed =
 "Đã quá hạn đăng ký, không thể tham gia waitlist.";

 public const string AlreadyRegistered =
 "Bạn đã là người chơi chính thức của giải này, không cần vào waitlist.";

 public const string NotInWaitlist =
 "Bạn không có trong danh sách chờ của giải này.";

 public const string NoOfferReceived =
 "Bạn chưa nhận được lời mời nào từ waitlist.";

 public const string OfferExpired =
 "Lời mời từ waitlist đã hết hạn.";

 public const string NoOfferToDecline =
 "Bạn không có lời mời nào để từ chối.";
 }
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
 public const string DeviceTokenTooTooLong = "Device token FCM không được vượt quá 512 ký tự.";
 public const string DeviceTokenNotFound = "Không tìm thấy device token để xóa.";
 public const string DeviceTokenNotOwner = "Bạn không có quyền xóa device token này.";
 public const string PlatformInvalid = "Giá trị platform không hợp lệ. Chỉ chấp nhận 'android', 'ios' hoặc 'web'.";
 }

public static class Settlement
    {
        public static string NotFound(Guid settlementId) =>
        $"Không tìm thấy settlement '{settlementId}'.";

        public const string AlreadyOverridden =
        "Settlement này đã được override trước đó.";
    }

    public static class ReservationExtension
    {
        public const string OnlyHostCanExtend =
        "Chỉ host của reservation mới có thể gia hạn thêm thời gian.";

        public static string OnlyConfirmedStatus(string currentStatus) =>
        $"Chỉ có thể gia hạn reservation ở trạng thái Confirmed. Trạng thái hiện tại: {currentStatus}.";

        public static string MaxExtensionCountReached(int maxCount) =>
        $"Đã gia hạn tối đa {maxCount} lần. Không thể gia hạn thêm nữa.";

        public static string RemainingMinutesInsufficient(int remainingMinutes, int requestedMinutes) =>
        $"Chỉ còn {remainingMinutes} phút có thể gia hạn. Bạn yêu cầu {requestedMinutes} phút.";

        public const string CannotExtendPastMidnight =
        "Không thể gia hạn qua ngày. Bạn tạo reservation mới cho ngày mai nhé!";

        public const string WalkInWindowOverlap =
        "Đang có khung giờ walk-in hoạt động trong thời gian bạn muốn gia hạn. Bạn chọn khung giờ khác nhé!";

        public static string CheckInMissingScheduledTime(Guid reservationId) =>
        $"Không thể check-in reservation '{reservationId}' vì thiếu thời gian dự kiến. Liên hệ hỗ trợ.";

        public static string CheckInMissingScheduledEndTime(Guid reservationId) =>
        $"Không thể check-in reservation '{reservationId}' vì thiếu thời gian kết thúc. Liên hệ hỗ trợ.";

        public static string ExtendMissingScheduledEndTime(Guid reservationId) =>
        $"Không thể gia hạn reservation '{reservationId}' vì thiếu thời gian kết thúc. Liên hệ hỗ trợ.";
    }

    public static class Receipt
    {
        public static string OnlyForPaidSession(string currentStatus) =>
        $"Receipt chỉ có thể tạo cho phiên đã thanh toán. Trạng thái hiện tại: {currentStatus}.";
    }

    public static class FriendReport
    {
        public const string AdminNoteRequired =
        "Bạn cần nhập ghi chú admin khi xử lý report.";

        public const string InvalidResolveStatus =
        "Trạng thái xử lý không hợp lệ. Chỉ chấp nhận Reviewed hoặc Dismissed.";

        public static string NotFound(Guid reportId) =>
        $"Không tìm thấy báo cáo '{reportId}'.";

        public static string AlreadyProcessed(Guid reportId, string currentStatus) =>
        $"Báo cáo '{reportId}' đã được xử lý trước đó (trạng thái: {currentStatus}).";
    }

    // ===== Infrastructure / dev-facing errors (NOT user-visible; dùng cho cấu hình & SDK nội bộ) =====
    public static class System
    {
        public const string ConfigurationMissing =
        "Thiếu cấu hình bắt buộc của hệ thống. Liên hệ admin.";

        public static string ConfigurationKeyMissing(string key) =>
        $"Thiếu cấu hình '{key}'. Liên hệ admin.";

        public const string FirebaseCredentialsMissing =
        "Firebase:CredentialsJson chưa được cấu hình. Set trong appsettings.json hoặc env FIREBASE_CREDENTIALS_JSON.";

        public const string BggInvalidResponse =
        "Phản hồi t� BGG không hợp lệ. Bạn thử lại sau nhé!";

        public const string SessionGamesNotLoaded =
        "Dữ liệu games của phiên chơi chưa được nạp. Lỗi nội bộ — liên hệ admin.";

        public const string PosCheckInTokenGenerationFailed =
        "Không thể tạo mã QR check-in do xung đột ngẫu nhiên. Bạn thử lại nhé!";

        public static string NoAvailableTablesForAutoCheckIn(string cafeName) =>
        $"Quán '{cafeName}' hiện không còn bàn trống để check-in tự động. Bạn liên hệ nhân viên nhé!";

        public const string ReservationScheduledStartTimeMissing =
        "Reservation thiếu thời gian bắt đầu (ScheduledStartTime). Lỗi dữ liệu — liên hệ admin.";

        public const string ReservationScheduledEndTimeMissing =
        "Reservation thiếu thời gian kết thúc (ScheduledEndTime). Lỗi dữ liệu — liên hệ admin.";

        public static string SevereDataDuplication =>
        "Mã số thuế hoặc Địa chỉ này đã được đăng ký trên hệ thống. Vui lòng kiểm tra lại.";

        public const string InvalidUserContext =
        "Không xác định được người dùng. Bạn đăng nhập lại nhé!";

        public static string CafeRetrieveFailed(Guid cafeId) =>
        $"Không thể lấy thông tin quán '{cafeId}' sau khi lưu. Lỗi nội bộ.";

        public static string TournamentRetrieveFailed(Guid tournamentId) =>
        $"Không thể lấy thông tin giải đấu '{tournamentId}' sau khi lưu. Lỗi nội bộ.";

        public static string BvcCaptureRetryExhausted(Guid lobbyId, int maxRetries) =>
            $"Đã hết số lần thử ({maxRetries}) để capture BVC cho lobby '{lobbyId}'. Vui lòng liên hệ hỗ trợ.";

        public static string IdempotencyKeyParamsMismatch(string key, string paramsList) =>
            $"Idempotency key '{key}' đã được dùng cho request khác với params khác: [{paramsList}].";

        public const string VietQrAccountNumberRequired =
            "Số tài khoản (VietQR) chưa được cấu hình cho quán. Liên hệ admin.";

        public const string VietQrBankCodeRequired =
            "Mã ngân hàng (VietQR) chưa được cấu hình cho quán. Liên hệ admin.";

        public const string VietQrAmountMustBePositive =
            "Số tiền thanh toán VietQR phải lớn hơn 0.";

        public const string QrAlreadyUsed =
            "Mã QR đã được sử dụng trước đó. Vui lòng dùng mã mới.";

        public static string BoxNotInSession(string barcode) =>
            $"Hộp game '{barcode}' không nằm trong phiên chơi nào đang hoạt động.";

        public static string SessionNotFoundInCafe(Guid cafeId, Guid sessionId) =>
            $"Không tìm thấy phiên chơi '{sessionId}' trong quán '{cafeId}'.";

        public static string SessionNotInCafe(Guid sessionId, Guid cafeId) =>
            $"Phiên chơi '{sessionId}' không thuộc quán '{cafeId}'.";

        public static string LobbyNotFoundForCapture(Guid lobbyId) =>
            $"Không tìm thấy lobby '{lobbyId}' để capture deposit.";

        public const string LobbyNotInProgressForCapture =
            "Lobby phải ở trạng thái InProgress để capture deposit BVC.";

        public static string BrevoInvalidApiBaseUrl(string url) =>
            $"Brevo:ApiBaseUrl '{url}' không hợp lệ. Phải bắt đầu bằng http:// hoặc https://.";

        public static string FriendReportInvalidStatusFilter(string status) =>
            $"Bộ lọc trạng thái báo cáo bạn bè '{status}' không hợp lệ. Chỉ chấp nhận: Open, Reviewed, Dismissed.";

        public static string CafeScheduleEffectiveRangeInvalid(DateOnly? from, DateOnly? to) =>
            $"Khoảng hiệu lực của cafe schedule không hợp lệ: từ '{from?.ToString("yyyy-MM-dd")}' đến '{to?.ToString("yyyy-MM-dd")}'.";

        public static string CancelRetryExhausted(Guid reservationId, int maxRetries) =>
            $"Đã hết số lần thử ({maxRetries}) để hủy reservation '{reservationId}'. Vui lòng liên hệ hỗ trợ.";

        public static string CancelAfterCheckinRetryExhausted(Guid reservationId, int maxRetries) =>
            $"Đã hết số lần thử ({maxRetries}) để hủy reservation '{reservationId}' sau check-in. Vui lòng liên hệ hỗ trợ.";

        public static string CafeApprovalRetryExhausted(Guid reservationId, int maxRetries) =>
            $"Đã hết số lần thử ({maxRetries}) để duyệt reservation '{reservationId}' bởi cafe. Vui lòng liên hệ hỗ trợ.";

        public static string CheckInRetryExhausted(Guid reservationId, int maxRetries) =>
            $"Đã hết số lần thử ({maxRetries}) để check-in reservation '{reservationId}'. Vui lòng liên hệ hỗ trợ.";

        public static string ReservationByLobbyNotFoundAfterRetry(Guid lobbyId, int maxRetries) =>
            $"Đã hết số lần thử ({maxRetries}) nhưng không tìm thấy reservation cho lobby '{lobbyId}'.";

        public static string DateRangeInvalid(DateOnly from, DateOnly to) =>
            $"Khoảng ngày không hợp lệ: từ '{from:yyyy-MM-dd}' đến '{to:yyyy-MM-dd}'. Ngày bắt đầu phải trước hoặc bằng ngày kết thúc.";

        public const string ReservationInvalidRequest =
            "Yêu cầu không hợp lệ. Vui lòng kiểm tra lại các trường và thử lại.";

        public const string ReservationOnlyOneOfTableNamesOrTables =
            "Chỉ được gửi một trong hai: tableNames (legacy) hoặc tables (cấu hình mới). Không gửi cả hai.";

        public static string ReservationInvalidTableConfig(string reason) =>
            $"Cấu hình bàn không hợp lệ: {reason}.";

        public static string ReservationAccessDenied(Guid reservationId) =>
            $"Không tìm thấy reservation '{reservationId}' hoặc bạn không có quyền truy cập.";

        public static string InvalidGranularity(int invalidValue) =>
            $"Giá trị granularity '{invalidValue}' không hợp lệ. Vui lòng chọn day/week/month.";

        public static string DateRangeExceeded(DateOnly from, DateOnly to) =>
            $"Khoảng ngày từ '{from:yyyy-MM-dd}' đến '{to:yyyy-MM-dd}' vượt quá giới hạn cho phép (tối đa 92 ngày).";

        public const string SubtotalNegative =
            "Tổng phụ (subtotal) bị âm. Dữ liệu không hợp lệ — liên hệ admin.";

        public static string ReservationCheckInFailed(string code, string reason) =>
            $"Check-in reservation '{code}' thất bại: {reason}";

        public static string ReservationGameMismatch(Guid reservationId, Guid gameTemplateId) =>
            $"Reservation '{reservationId}' không dành cho game '{gameTemplateId}'. Vui lòng chọn game đúng trên app.";

        public const string ChecklistViewRequiresChecking =
            "Chỉ có thể xem checklist khi phiên chơi đang ở trạng thái Checking.";

        public const string ChecklistSubmitRequiresChecking =
            "Chỉ có thể gửi checklist khi phiên chơi đang ở trạng thái Checking.";

        public const string ChecklistResetRequiresChecking =
            "Chỉ có thể reset checklist khi phiên chơi đang ở trạng thái Checking.";

        public static string ChecklistDuplicateComponentIds(Guid duplicateId) =>
            $"Mã linh kiện '{duplicateId}' bị trùng trong checklist. Vui lòng kiểm tra lại.";

        public const string ChecklistMissingComponents =
            "Thiếu một hoặc nhiều linh kiện bắt buộc trong checklist. Vui lòng kiểm tra lại.";

        public const string ChecklistNegativeQuantity =
            "Số lượng linh kiện không được âm. Vui lòng kiểm tra lại.";

        public static string LobbyNotEnoughMembersToLock(int minPlayers, int currentCount) =>
            $"Phòng cần ít nhất {minPlayers} thành viên để khóa. Hiện tại chỉ có {currentCount} người.";

        public const string LobbyBoostRequiresOpen =
            "Chỉ có thể boost khi phòng đang ở trạng thái mở tuyển người (Open).";

        public static string LobbyBoostCooldown(int cooldownHours, int remainingMinutes) =>
            $"Bạn vừa boost cách đây chưa lâu. Vui lòng đợi {remainingMinutes} phút nữa (cooldown {cooldownHours} giờ).";

        public static string LobbyInviteFriendStatusInvalid(string fieldName, string allowedValues) =>
            $"Giá trị '{fieldName}' không hợp lệ. Chỉ chấp nhận: {allowedValues}.";

        public static string FriendLimitReached(string username, int limit) =>
            $"Người dùng '{username}' đã đạt giới hạn bạn bè ({limit}). Không thể gửi lời mời kết bạn mới.";

        public static string GameInventoryMissingForReservation(Guid cafeId, DateOnly playDate, string timeSlot) =>
            $"Không tìm thấy tồn kho game cho quán '{cafeId}' ngày '{playDate:yyyy-MM-dd}' khung giờ '{timeSlot}'.";

        public static string SeatInventoryMissingForReservation(Guid cafeId, DateOnly playDate, string timeSlot) =>
            $"Không tìm thấy tồn kho ghế cho quán '{cafeId}' ngày '{playDate:yyyy-MM-dd}' khung giờ '{timeSlot}'.";

        public static string ReservationMissingCheckedInAt(Guid reservationId) =>
            $"Reservation '{reservationId}' thiếu thời gian check-in. Dữ liệu không hợp lệ — liên hệ admin.";

        public static string ReservationLobbyMissingOnIdempotent(Guid reservationId) =>
            $"Reservation '{reservationId}' thiếu liên kết lobby khi xử lý idempotent. Dữ liệu không hợp lệ — liên hệ admin.";

        public static string RefundAmountExceedsDeposit(decimal refundAmount, decimal depositAmount) =>
            $"Số tiền hoàn ({refundAmount:N0}) vượt quá tiền cọc ({depositAmount:N0}).";

        public static string OverrideRefundInvalidStatus(string currentStatus, string requiredStatus) =>
            $"Không thể override refund khi reservation ở trạng thái '{currentStatus}'. Chỉ chấp nhận '{requiredStatus}'.";

        public static string WalkInCancelInvalidStatus(object currentStatus) =>
            $"Không thể hủy walk-in ở trạng thái '{currentStatus}'.";

        public static string LateNightMustEndNextDay(string startTime, string endTime) =>
            $"Khung LateNight phải kết thúc vào ngày hôm sau (start='{startTime}', end='{endTime}').";

        public const string TimeSlotOverrideInvalidTimeRange =
            "Khoảng giờ override không hợp lệ: Giờ bắt đầu và giờ kết thúc không được bằng nhau (trừ khi IsClosed = true).";

        public const string TimeSlotOverrideInvalidEffectiveRange =
            "Khoảng hiệu lực của override không hợp lệ.";

        public static string TimeSlotOverrideOverrideAlreadyExists(Guid cafeId, string timeSlot) =>
            $"Quán '{cafeId}' đã có override cho TimeSlot '{timeSlot}' trong khoảng thời gian này. Hãy cập nhật thay vì tạo mới.";

        public static string TimeSlotOverrideOverrideNotFound(Guid cafeId, string timeSlot) =>
            $"Không tìm thấy override đang hoạt động cho quán '{cafeId}' và TimeSlot '{timeSlot}'.";

        public const string TimeSlotOverrideNoFieldsToUpdate =
            "Bạn cần gửi ít nhất một trường để cập nhật override.";

        public static string TimeSlotOverrideNotFoundForCafe(Guid cafeId) =>
            $"Không tìm thấy override cho quán '{cafeId}'.";

        public static string TimeSlotOverrideNotManagerForCafe(Guid cafeId) =>
            $"Bạn không phải quản lý của quán '{cafeId}' nên không thể chỉnh sửa override.";

        public static string IPv4ResolutionFailed(string host) =>
            $"Không phân giải được địa chỉ IPv4 cho '{host}'.";
    }

        // ===== Player Session APIs =====
        public static class Session
        {
            public const string PlayerNoActiveSession =
                "Bạn không có phiên chơi nào đang hoạt động.";

            public const string PlayerNotInSession =
                "Bạn không tham gia phiên chơi này.";

            // GAP-6 Fix: Thêm method này vào Session class để tránh shadow với root-level method
            public static string SessionNotFoundById(Guid id) =>
                $"Không tìm thấy phiên chơi '{id}'.";

            public const string InvalidExtensionMinutes =
                "Số phút gia hạn phải lớn hơn 0.";

            public const string ExtensionTooLong =
                "Số phút gia hạn không được vượt quá 240 phút.";

            public static string CannotExtendSessionStatus(string currentStatus) =>
                "Không thể gia hạn khi phiên đang ở trạng thái: " + currentStatus + ". Chỉ có thể gia hạn khi phiên đang hoạt động.";

            public const string AlreadyPaid =
                "Bạn đã thanh toán cho phiên chơi này rồi.";

            // GAP-2 Fix: SuspendedMutation members cannot pay directly
            public const string CannotPayWhileSuspendedMutation =
                "Thành viên đang chờ kiểm kê linh kiện. Nhân viên sẽ xử lý thanh toán tại quầy.";

            public const string InsufficientBvcBalance =
                "Số dư BVC không đủ để thanh toán.";

            // ===== Extension Request APIs (GAP-NEW-1) =====
            public const string ExtensionRequestNotFound =
                "Không tìm thấy yêu cầu gia hạn.";

            public const string ExtensionRequestAlreadyProcessed =
                "Yêu cầu gia hạn này đã được xử lý.";

            public const string ExtensionRequestExpired =
                "Yêu cầu gia hạn đã hết hạn.";

            public const string CannotApproveExtensionSessionNotActive =
                "Không thể duyệt gia hạn khi phiên không còn ở trạng thái Active.";

            // GAP-R3-05: Refactor từ hardcoded VN string trong ExtendSessionAsync
            public const string AlreadyPaidCannotExtend =
                "Phiên chơi đã thanh toán. Không thể gia hạn.";

            public const string UnpaidCannotExtend =
                "Phiên chơi đang chờ thanh toán. Vui lòng thanh toán trước khi gia hạn.";

            // GAP-15 Fix: Guest slots cannot pay via app — staff handles at POS
            public const string GuestCannotPayViaApp =
                "Khách vô danh không thể thanh toán qua ứng dụng. Vui lòng thanh toán tại quầy.";

            // GAP-16 Fix: POS-side extension approval validation
            public const string ApprovedMinutesTooLong =
                "Số phút duyệt không được vượt quá 480 phút (8 giờ).";

            public const string RejectionReasonTooShort =
                "Lý do từ chối phải có ít nhất 10 ký tự.";

            public const string ExtensionSessionNotExtendable =
                "Phiên chơi không còn ở trạng thái có thể gia hạn (trạng thái hiện tại: {0}).";

            // GAP-5 Fix: Insufficient balance warning for extension request
            public const string InsufficientBvcForExtension =
                "Số dư BVC của bạn có thể không đủ để thanh toán phần gia hạn này. Vui lòng nạp thêm BVC trước khi staff duyệt yêu cầu.";

            // GAP-12 Fix: Cafe closed/paused
            public const string CafeClosedCannotExtend =
                "Quán đã đóng cửa, không thể gia hạn thêm thời gian chơi.";

            public const string SessionPausedCannotExtend =
                "Phiên chơi đang tạm dừng, vui lòng liên hệ nhân viên để tiếp tục trước khi gia hạn.";
        }
    }
}