namespace BoardVerse.Core.Enum
{
    /// <summary>
    /// BR-01/BR-16: Mô hình tính tiền của quán đối tác.
    /// </summary>
    public enum CafePartnerBillingModel
    {
        /// <summary>Tính theo phút/thực tế sử dụng.</summary>
        ByHour = 0,

        /// <summary>Tính theo phút/thực tế sử dụng (alias).</summary>
        TimeBased = 0,

        /// <summary>Vào cổng trọn gói; các block sau = 0 VNĐ.</summary>
        FlatEntry = 1
    }
}
