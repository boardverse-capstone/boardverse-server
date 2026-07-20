using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Tournament;

/// <summary>
/// Request đổi pairing mode cho tournament.
/// </summary>
public class SetPairingModeRequestDto
{
    /// <summary>PairingMode: Auto (0) hoặc Manual (1).</summary>
    [Required]
    public TournamentPairingMode Mode { get; set; }
}