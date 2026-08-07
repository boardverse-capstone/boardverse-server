using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.User
{
    /// <summary>
    /// K-05: DTO for updating player profile with new fields: cover photo, bio, favorite games.
    /// GamesPlayedCount and WinRate are computed from MatchHistory and are read-only.
    /// </summary>
    public class UpdatePlayerProfileDto
    {
        [StringLength(2000, ErrorMessage = "Bio must be at most 2000 characters.")]
        public string? Bio { get; set; }

        /// <summary>K-05: Cover photo URL for the profile page header.</summary>
        [Url(ErrorMessage = "Cover photo must be a valid URL.")]
        [StringLength(500, ErrorMessage = "Cover photo URL must be at most 500 characters.")]
        public string? CoverPhotoUrl { get; set; }

        /// <summary>
        /// K-05: List of favorite game template IDs.
        /// Stored as a comma-separated string in UserProfile.FavoriteGamesJson.
        /// </summary>
        public List<Guid>? FavoriteGameIds { get; set; }

        [StringLength(100, ErrorMessage = "First name must be at most 100 characters.")]
        public string? FirstName { get; set; }

        [StringLength(100, ErrorMessage = "Last name must be at most 100 characters.")]
        public string? LastName { get; set; }

        /// <summary>K-05: Preferred play mode (Solo = 0, Group = 1).</summary>
        public PlayerPlayMode? PreferredPlayMode { get; set; }
    }
}
