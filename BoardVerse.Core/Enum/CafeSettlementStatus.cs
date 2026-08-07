namespace BoardVerse.Core.Enum;

public enum CafeSettlementStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
    Retrying = 3,
    /// <summary>W-06: Admin manually override after retry exhaustion.</summary>
    Overridden = 4
}
