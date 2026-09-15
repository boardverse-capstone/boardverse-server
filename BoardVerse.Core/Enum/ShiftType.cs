namespace BoardVerse.Core.Enum;

/// <summary>
/// Loại ca làm việc của staff — phân biệt ca thường, tăng ca, trực, hay Game Master.
/// </summary>
public enum ShiftType
{
    /// <summary>Ca làm việc bình thường.</summary>
    Regular = 0,

    /// <summary>Ca tăng ca (OT) — được trả lương cao hơn.</summary>
    Overtime = 1,

    /// <summary>Ca trực — staff chỉ cần có mặt khi quán có sự cố.</summary>
    OnCall = 2,

    /// <summary>Ca Game Master — staff hỗ trợ khách chơi game.</summary>
    GameMaster = 3
}
