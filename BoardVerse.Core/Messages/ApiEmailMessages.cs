namespace BoardVerse.Core.Messages
{
    public static class ApiEmailMessages
    {
        public static class Auth
        {
            public const string VerificationSubject = "BoardVerse — Xác minh email";
            public static string VerificationBody(string token) =>
                $"Mã xác minh BoardVerse của bạn là: {token}\n\nMã có hiệu lực trong 5 phút.";

            public const string PasswordResetSubject = "BoardVerse — Đặt lại mật khẩu";
            public static string PasswordResetBody(string token) =>
                $"Mã đặt lại mật khẩu BoardVerse của bạn là: {token}\n\nMã có hiệu lực trong 5 phút.";
        }

        public static class CafePartner
        {
            public const string ApplicationReceivedSubject = "BoardVerse — Đã nhận đơn đăng ký đối tác quán";
            public static string ApplicationReceivedBody(string cafeName, Guid applicationId) =>
                $"Xin chào,\n\nChúng tôi đã nhận đơn đăng ký đối tác cho quán \"{cafeName}\".\n" +
                $"Mã tham chiếu: {applicationId}\nTrạng thái: PENDING_APPROVAL\n\n— Đội ngũ BoardVerse";

            public const string ManagerAccountCreatedSubject = "BoardVerse — Tài khoản quản lý quán đã được tạo";

            public const string ApplicationRejectedSubject = "BoardVerse — Cập nhật đơn đăng ký đối tác quán";
            public static string ApplicationRejectedBody(string reason) =>
                $"Xin chào,\n\nĐơn đăng ký của bạn chưa được phê duyệt.\nLý do: {reason}\n\n— Đội ngũ BoardVerse";

            public const string CafeActivatedSubject = "BoardVerse — Quán của bạn đã mở cửa";
            public static string CafeActivatedBody(string cafeName) =>
                $"Xin chào,\n\nQuán \"{cafeName}\" hiện đã ACTIVE trên BoardVerse.\n\n— Đội ngũ BoardVerse";

            public static string OnboardingApprovedBody(string email, string? temporaryPassword, bool keptExistingPassword)
            {
                var body = $"Xin chào,\n\nĐơn đăng ký đối tác quán của bạn đã được phê duyệt.\n\n" +
                           $"Email đăng nhập Web POS: {email}\n";

                if (keptExistingPassword)
                {
                    body += "\nĐăng nhập bằng mật khẩu hiện tại và hoàn thiện hồ sơ vận hành.\n";
                }
                else if (temporaryPassword != null)
                {
                    body += $"\nMật khẩu tạm: {temporaryPassword}\n\nVui lòng đổi mật khẩu sau lần đăng nhập đầu tiên.\n";
                }

                return body +
                       "\nHoàn thiện hạ tầng, danh mục game và sơ đồ bàn trước khi kích hoạt quán.\n\n— Đội ngũ BoardVerse";
            }
        }
    }
}
