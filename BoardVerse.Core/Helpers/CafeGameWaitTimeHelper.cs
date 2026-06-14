namespace BoardVerse.Core.Helpers
{
    public static class CafeGameWaitTimeHelper
    {
        /// <summary>
        /// Shortest estimated wait (minutes) until a box frees up.
        /// Formula: default play time − elapsed session time (floor 0).
        /// </summary>
        public static int? CalculateEstimatedWaitMinutes(
            int defaultPlayTimeMinutes,
            IEnumerable<DateTime> activeSessionStartTimes,
            DateTime utcNow)
        {
            var waitCandidates = activeSessionStartTimes
                .Select(startedAt =>
                {
                    var elapsedMinutes = (utcNow - startedAt).TotalMinutes;
                    return (int)Math.Max(0, Math.Ceiling(defaultPlayTimeMinutes - elapsedMinutes));
                })
                .ToList();

            if (waitCandidates.Count == 0)
            {
                return defaultPlayTimeMinutes;
            }

            return waitCandidates.Min();
        }
    }
}
