namespace BoardVerse.Core.Enum
{
    /// <summary>
    /// Loại xác thực webhook SePay.
    /// Theo https://developer.sepay.vn/en/sepay-webhooks/xac-thuc — SePay hỗ trợ 3 mode.
    /// </summary>
    public enum SePayWebhookAuthType
    {
        /// <summary>
        /// Không xác thực. CHỈ dùng cho local development/testing.
        /// Production BẮT BUỘC dùng ApiKey hoặc HmacSha256.
        /// </summary>
        None = 0,

        /// <summary>
        /// API Key mode: header <c>Authorization: Apikey &lt;WebhookToken&gt;</c>.
        /// So sánh trực tiếp với giá trị header (KHÔNG qua Base64).
        /// </summary>
        ApiKey = 1,

        /// <summary>
        /// HMAC-SHA256 mode (RECOMMENDED): header <c>X-SePay-Signature: sha256=&lt;hex&gt;</c>
        /// + header <c>X-SePay-Timestamp: &lt;unix_seconds&gt;</c>.
        /// Reconstruct: <c>sha256=HMAC-SHA256(secret_key, "{timestamp}.{rawBody}")</c>.
        /// Timestamp phải trong khoảng ±300s của server time (anti-replay).
        /// </summary>
        HmacSha256 = 2
    }
}