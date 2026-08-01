namespace BoardVerse.Core.Settings;

/// <summary>
/// Cấu hình Firebase Cloud Messaging cho push notification.
/// Service account JSON có thể paste trực tiếp vào <c>appsettings.json</c>
/// (key <c>Firebase:CredentialsJson</c>) HOẶC cung cấp qua env
/// <c>FIREBASE_CREDENTIALS_JSON</c> cho production (tránh commit credentials).
///
/// Enable flag: <c>Firebase:Enabled</c> — nếu false thì service không gửi push
/// (chỉ log), hữu ích cho local dev/test khi chưa setup Firebase project.
/// </summary>
public class FirebaseSettings
{
    public const string SectionName = "Firebase";

    /// <summary>Bật/tắt toàn bộ push notification pipeline.</summary>
    public bool Enabled { get; set; }

    /// <summary>Firebase project ID (vd: "boardverse-prod").</summary>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>
    /// Service account JSON content (private_key, client_email, project_id...).
    /// Backend dùng để khởi tạo FirebaseAdmin app.
    /// Có thể override qua env <c>FIREBASE_CREDENTIALS_JSON</c>.
    /// </summary>
    public string CredentialsJson { get; set; } = string.Empty;
}
