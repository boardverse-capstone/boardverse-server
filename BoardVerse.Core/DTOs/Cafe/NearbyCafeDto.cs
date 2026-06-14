using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Cafe
{
    public class NearbyCafeDto : CafeDto
    {
        public double DistanceMeters { get; set; }
        public int AvailableGameCount { get; set; }
        public int TotalGameBoxCount { get; set; }
        public int AvailableTableCount { get; set; }
        public int TotalTableCount { get; set; }
        public NearbyCafeGameAvailabilityStatus? SelectedGameAvailabilityStatus { get; set; }
        public int? EstimatedWaitMinutes { get; set; }
    }
}
