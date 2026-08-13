namespace BoardVerse.Core.DTOs.Admin;

/// <summary>
/// R-01: Payload cho POST /admin/alerts/{alertId}/resolve hoặc /dismiss.
/// </summary>
public class AlertResolveRequestDto
{
    /// <summary>Ghi chú lý do resolve/dismiss (audit trail).</summary>
    public string Note { get; set; } = string.Empty;
}
