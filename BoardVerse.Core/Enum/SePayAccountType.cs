namespace BoardVerse.Core.Enum
{
    /// <summary>
    /// Loại tài khoản SePay.
    /// Master: Tài khoản central của BoardVerse (dùng cho deposit payment).
    /// Cafe: Tài khoản riêng của từng quán cafe (dùng cho session payment).
    /// </summary>
    public enum SePayAccountType
    {
        /// <summary>Tài khoản central của BoardVerse (dùng cho deposit payment).</summary>
        Master = 0,

        /// <summary>Tài khoản riêng của từng quán cafe (dùng cho session payment).</summary>
        Cafe = 1
    }
}
