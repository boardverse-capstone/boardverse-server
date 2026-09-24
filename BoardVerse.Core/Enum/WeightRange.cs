namespace BoardVerse.Core.Enum;

/// <summary>
/// BGG Complexity Weight ranges dùng cho bộ lọc multi-select.
/// Dựa trên thang điểm 1.0 → 5.0 của BoardGameGeek.
/// </summary>
public enum WeightRange
{
    /// <summary>Light: weight 1.0 – 1.99 (game rất dễ học, ví dụ: Uno, Dixit).</summary>
    Light = 1,

    /// <summary>Medium-Light: weight 1.5 – 2.49 (game dễ, ví dụ: Codenames, Avalon).</summary>
    MediumLight = 2,

    /// <summary>Medium: weight 2.0 – 2.99 (game trung bình, ví dụ: Catan, Splendor).</summary>
    Medium = 3,

    /// <summary>Medium-Heavy: weight 2.5 – 3.49 (game khó vừa, ví dụ: Pandemic, Wingspan).</summary>
    MediumHeavy = 4,

    /// <summary>Heavy: weight 3.5 – 5.0 (game rất phức tạp, ví dụ: Brass, Spirit Island).</summary>
    Heavy = 5
}
