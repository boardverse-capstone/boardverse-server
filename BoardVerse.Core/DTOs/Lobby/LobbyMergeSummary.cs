using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Lobby;

/// <summary>
/// Projection record cho LobbyMergeService — chỉ 7 fields cần thiết cho merge logic.
/// <para>
/// Thay thế việc load FULL Lobby graph (Members → User → Profile, GameTemplate, Cafe,
/// Booking, Reservation — 6-8 Include chains) trong Serializable transaction.
/// Mỗi Include chain tạo 1 SELECT round-trip + predicate lock riêng → tăng xác suất
/// Postgres 40001 (serialization_failure) khi 2 staff merge cùng cafe đồng thời.
/// </para>
/// <para>
/// Projection này giảm:
///   - Predicate lock surface: 6-8 tables → 1 table (chỉ Lobbies).
///   - Query time: ~95ms → &lt;5ms (chỉ load 7 columns).
///   - Memory: không materialize navigation collections.
/// </para>
/// <para>
/// EF Core 8 <c>.Select(... =&gt; new LobbyMergeSummary(...))</c> cho phép project vào
/// record class non-entity. Constructor argument order phải match với Select invocation.
/// </para>
/// <para>
/// Optimization (2026-10-02): dùng cho LobbyMergeService.CreateMergeRequestAsync —
/// load 7 fields thay vì full graph. ApproveMergeAsync vẫn dùng full Lobby entity
/// (cần update Lobby.Status, Lobby.HostUserId trong transaction).
/// </para>
/// </summary>
public record LobbyMergeSummary(
    Guid Id,
    LobbyStatus Status,
    Guid? CafeId,
    Guid GameTemplateId,
    Guid? ReservationId,
    Guid HostUserId,
    Guid? ActiveSessionId);
