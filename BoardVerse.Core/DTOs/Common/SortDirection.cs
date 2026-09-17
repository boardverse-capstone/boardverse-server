namespace BoardVerse.Core.DTOs.Common;

/// <summary>
/// Thứ tự sort cho các list endpoint. Default <see cref="Asc"/>.
/// </summary>
public enum SortDirection
{
    /// <summary>Tăng dần (A → Z, 0 → 9, cũ → mới).</summary>
    Asc = 0,

    /// <summary>Giảm dần (Z → A, 9 → 0, mới → cũ).</summary>
    Desc = 1
}
