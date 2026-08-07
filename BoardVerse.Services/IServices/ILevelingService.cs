namespace BoardVerse.Services.IServices;

/// <summary>
/// K-04: Level/Exp computation service.
/// </summary>
public interface ILevelingService
{
    /// <summary>Tính level từ totalExp.</summary>
    int CalculateLevel(long totalExp);

    /// <summary>Exp cần để đạt level tiếp theo.</summary>
    long GetExpForNextLevel(int currentLevel);

    /// <summary>Exp còn thiếu để lên level tiếp theo.</summary>
    long GetExpToNextLevel(long currentExp, int currentLevel);

    /// <summary>Cập nhật level cho user profile.</summary>
    Task UpdateUserLevelAsync(Guid userId, long expToAdd);
}
