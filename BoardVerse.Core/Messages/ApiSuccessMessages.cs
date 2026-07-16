namespace BoardVerse.Core.Messages
{
    public static class ApiSuccessMessages
    {
        public static class Auth
        {
            public const string RegistrationSuccessful = "Đăng ký thành công.";
            public const string LoginSuccessful = "Đăng nhập thành công.";
            public const string GoogleLoginSuccessful = "Đăng nhập Google thành công.";
            public const string TokenRefreshed = "Làm mới token thành công.";
            public const string LoggedOut = "Đăng xuất thành công.";
            public const string EmailVerified = "Xác minh email thành công.";
            public const string PasswordReset = "Đặt lại mật khẩu thành công.";
            public const string PasswordChanged = "Đổi mật khẩu thành công.";
            public const string GoogleAccountLinked = "Liên kết tài khoản Google thành công.";
        }

        public static class Profile
        {
            public const string Retrieved = "Lấy hồ sơ thành công.";
            public const string Created = "Tạo hồ sơ thành công.";
            public const string Updated = "Cập nhật hồ sơ thành công.";
            public const string ProgressUpdated = "Cập nhật tiến trình thành công.";
            public const string AvatarUpdated = "Cập nhật avatar thành công.";
            public const string LocationRetrieved = "Lấy vị trí hiện tại thành công.";
            public const string LocationUpdated = "Cập nhật vị trí hiện tại thành công.";
            public const string LocationCleared = "Xóa vị trí đã lưu thành công.";
            public const string KarmaStateRetrieved = "Lấy trạng thái karma thành công.";
            public const string Deleted = "Xóa hồ sơ thành công.";
        }

        public static class Cafe
        {
            public const string NearbyRetrieved = "Lấy danh sách quán gần bạn thành công.";
            public const string Retrieved = "Lấy thông tin quán thành công.";
            public const string Updated = "Cập nhật quán thành công.";
            public const string StaffAdded = "Thêm nhân viên thành công.";
            public const string StaffPromoted = "Thăng cấp nhân viên quán thành công.";
            public const string StaffListRetrieved = "Lấy danh sách nhân viên thành công.";
            public const string StaffRemoved = "Xóa nhân viên thành công.";
            public const string ListRetrieved = "Lấy danh sách quán thành công.";
            public const string OperationalStatusUpdated = "Cập nhật trạng thái vận hành quán thành công.";
        }

        public static class Inventory
        {
            public const string GameAdded = "Thêm game vào kho thành công.";
            public const string Retrieved = "Lấy kho game thành công.";
            public const string DeletedRetrieved = "Lấy danh sách game đã xóa thành công.";
            public const string ItemRetrieved = "Lấy mục kho thành công.";
            public const string Updated = "Cập nhật kho thành công.";
            public const string Restored = "Khôi phục mục kho thành công.";
            public const string PenaltiesSynced = "Đồng bộ phí linh kiện thành công.";
            public const string BoxesSynced = "Đồng bộ hộp game vật lý thành công.";
            public const string GameRemoved = "Xóa game khỏi kho thành công.";
        }

        public static class Pos
        {
            public const string TablesRetrieved = "Lấy danh sách bàn thành công.";
            public const string BoxesRetrieved = "Lấy danh sách hộp game thành công.";
            public const string BoxRetrieved = "Lấy thông tin hộp game thành công.";
            public const string SessionsRetrieved = "Lấy phiên chơi đang hoạt động thành công.";
            public const string SessionStarted = "Bắt đầu phiên chơi thành công.";
            public const string SessionEnded = "Kết thúc phiên chơi thành công.";
        }

        public static class BoardGame
        {
            public const string CategoriesRetrieved = "Lấy danh sách thể loại thành công.";
            public const string ListRetrieved = "Lấy danh sách board game thành công.";
            public const string Retrieved = "Lấy board game thành công.";
            public const string DetailsRetrieved = "Lấy chi tiết board game thành công.";
            public const string PlayConfigurationRetrieved = "Lấy cấu hình chơi thành công.";
            public const string PlayNavigationResolved = "Phân giải điều hướng chơi thành công.";
        }

        public static class MasterGame
        {
            public const string ListRetrieved = "Lấy danh sách game master thành công.";
            public const string Retrieved = "Lấy game master thành công.";
        }

        public static class Bgg
        {
            public const string ComponentCatalogRetrieved = "Lấy danh mục linh kiện thành công.";
            public const string SearchCompleted = "Tìm kiếm BGG thành công.";
            public const string PreviewRetrieved = "Lấy xem trước game BGG thành công.";
            public const string GameImported = "Import game từ BGG thành công.";
            public const string GameUpdated = "Cập nhật game từ BGG thành công.";
        }

        public static class CafePartner
        {
            public const string ApplicationSubmitted = "Gửi đơn đăng ký đối tác thành công.";
            public const string ApplicationRetrieved = "Lấy đơn đăng ký thành công.";
            public const string ApplicationsRetrieved = "Lấy danh sách đơn đăng ký thành công.";
            public const string ApplicationApproved = "Phê duyệt đơn và tạo tài khoản quản lý thành công.";
            public const string ApplicationRejected = "Từ chối đơn đăng ký thành công.";
            public const string ProfileRetrieved = "Lấy hồ sơ đối tác thành công.";
            public const string OperationalProfileUpdated = "Cập nhật hồ sơ vận hành thành công.";
            public const string CafeActivated = "Kích hoạt quán thành công.";
            public const string CafeReopened = "Mở lại quán thành công.";
            public const string CafePaused = "Tạm dừng quán thành công.";
            public const string CafeClosedPermanently = "Đóng quán vĩnh viễn thành công.";
        }

        public static class AdminUsers
        {
            public const string ListRetrieved = "Lấy danh sách người dùng thành công.";
            public const string Retrieved = "Lấy thông tin người dùng thành công.";
            public const string Created = "Tạo người dùng thành công.";
            public const string Updated = "Cập nhật người dùng thành công.";
            public const string Disabled = "Vô hiệu hóa người dùng thành công.";
            public const string Blocked = "Khóa người dùng thành công.";
            public const string Unblocked = "Mở khóa người dùng thành công.";
            public const string RoleUpdated = "Cập nhật vai trò người dùng thành công.";
        }

        public static class AdminModeration
        {
            public const string KarmaLogsRetrieved = "Lấy nhật ký karma thành công.";
            public const string KarmaAlertsRetrieved = "Lấy cảnh báo karma thành công.";
            public const string PunishmentApplied = "Áp dụng hình phạt thành công.";
            public const string KarmaAdjusted = "Điều chỉnh karma thành công.";
        }

        public static class AdminCatalog
        {
            public const string CategoriesRetrieved = "Lấy danh sách thể loại thành công.";
            public const string CategoryCreated = "Tạo thể loại thành công.";
            public const string CategoryUpdated = "Cập nhật thể loại thành công.";
            public const string CategoryDeactivated = "Vô hiệu hóa thể loại thành công.";
            public const string ComponentsRetrieved = "Lấy danh sách linh kiện thành công.";
            public const string ComponentCreated = "Tạo linh kiện thành công.";
            public const string ComponentUpdated = "Cập nhật linh kiện thành công.";
            public const string ComponentDeleted = "Xóa linh kiện thành công.";
            public const string GameCategoriesRetrieved = "Lấy thể loại của game thành công.";
            public const string GameCategoriesUpdated = "Cập nhật thể loại game thành công.";
            public const string BoardGameUpdated = "Cập nhật board game thành công.";
            public const string ThumbnailUpdated = "Cập nhật ảnh thumbnail thành công.";
        }

        public static class AdminConfig
        {
            public const string Retrieved = "Lấy cấu hình hệ thống thành công.";
            public const string Updated = "Cập nhật cấu hình hệ thống thành công.";
        }

        public static class Lobby
        {
            public const string KarmaRatingWindowOpened = "Mở cửa sổ đánh giá karma thành công.";
        }

        public static class Rating
        {
            public const string ContextRetrieved = "Lấy ngữ cảnh đánh giá karma thành công.";
            public const string Submitted = "Gửi đánh giá karma thành công.";
        }

        public static class Match
        {
            public const string StatusRetrieved = "Lấy trạng thái kết quả trận đấu thành công.";
            public const string ResultSubmitted = "Gửi kết quả trận đấu thành công.";
        }

        public static class Staff
        {
            public const string WorkplacesRetrieved = "Lấy danh sách nơi làm việc thành công.";
        }

        public static class Health
        {
            public const string ApiOperational = "API đang hoạt động.";
            public const string DatabaseConnected = "Kết nối cơ sở dữ liệu thành công.";
            public const string Pong = "pong";
        }

        public static class Protected
        {
            public const string EndpointAccessed = "Truy cập endpoint bảo vệ thành công.";
        }
    }
}
