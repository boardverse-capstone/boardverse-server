using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Services.IServices;

namespace BoardVerse.Services.Services
{
    /// <summary>
    /// Computes player level and experience progression.
    /// Each level requires an increasing amount of exp:
    /// - Level 1: 0-99 exp (needs 100)
    /// - Level 2: 100-299 exp (needs 200)
    /// - Level 3: 300-599 exp (needs 300)
    /// - Level N: needs BaseExpPerLevel + (N-1) * ExpIncrementPerLevel
    /// </summary>
    public class LevelingService : ILevelingService
    {
        private const int BaseExpPerLevel = 100;
        private const int ExpIncrementPerLevel = 100;

        public int CalculateLevel(long totalExp)
        {
            if (totalExp < BaseExpPerLevel) return 1;

            var level = 1;
            var remaining = totalExp;
            var expNeeded = BaseExpPerLevel;

            while (remaining >= expNeeded)
            {
                remaining -= expNeeded;
                level++;
                expNeeded = BaseExpPerLevel + (level - 1) * ExpIncrementPerLevel;
            }

            return level;
        }

        public long GetExpForNextLevel(int currentLevel)
        {
            if (currentLevel < 1) currentLevel = 1;
            return BaseExpPerLevel + (currentLevel - 1) * ExpIncrementPerLevel;
        }

        public long GetExpToNextLevel(long currentExp, int currentLevel)
        {
            var expForNext = GetExpForNextLevel(currentLevel);
            return Math.Max(0, expForNext - currentExp);
        }

        public (int Level, long RemainingExp) GetLevelAndRemainingExp(long totalExp)
        {
            var level = CalculateLevel(totalExp);
            var expForCurrentLevelStart = GetExpStartForLevel(level);
            var withinLevelExp = totalExp - expForCurrentLevelStart;
            var expForNext = GetExpForNextLevel(level);
            var remainingToNext = expForNext - withinLevelExp;
            return (level, remainingToNext);
        }

        private long GetExpStartForLevel(int level)
        {
            if (level <= 1) return 0;
            var n = level - 1;
            return 50L * n * (n + 1);
        }

        /// <summary>
        /// Stub - actual implementation is in UserProfileService.AddExpAndUpdateLevelAsync
        /// </summary>
        public Task UpdateUserLevelAsync(Guid userId, long expToAdd, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
