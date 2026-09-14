using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Data.Repositories;
using BoardVerse.Tests.Helpers;
using Xunit;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho <see cref="LobbyRepository.GetMyLobbiesAsync"/> — endpoint
/// <c>GET /api/v1/lobbies/my</c> (lobby của user hiện tại: host + member, chỉ active).
///
/// Tập trung vào:
/// - Sort priority: Open (0) → InProgress (1) → PendingActivation (2) → ... (2026-09-14 user feedback).
/// - statuses filter (union các LobbyStatus enum).
/// - Chỉ trả lobby ACTIVE (lọc theo <c>ActiveLobbyStatuses</c>).
/// </summary>
public class LobbyRepositoryGetMyLobbiesTests : IDisposable
{
    private readonly FakeDbContext _db;
    private readonly LobbyRepository _repo;

    private readonly Guid _hostId;
    private readonly Guid _memberId;
    private readonly Guid _otherUserId;
    private readonly Guid _cafeId;
    private readonly Guid _gameId;

    public LobbyRepositoryGetMyLobbiesTests()
    {
        _db = new FakeDbContext();
        _repo = new LobbyRepository(_db);

        _hostId = Guid.NewGuid();
        _memberId = Guid.NewGuid();
        _otherUserId = Guid.NewGuid();

        _cafeId = Guid.NewGuid();
        var cafe = new Cafe
        {
            Id = _cafeId,
            Name = "Test Cafe",
            Address = "1 Test Street",
            TotalSeats = 20,
            IsActive = true,
            PartnerOperationalStatus = CafePartnerOperationalStatus.Active,
            ManagerId = _hostId
        };
        _db.Cafes.Add(cafe);

        _gameId = Guid.NewGuid();
        _db.GameTemplates.Add(new GameTemplate
        {
            Id = _gameId,
            Name = "Test Catan",
            MinPlayers = 2,
            MaxPlayers = 4,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });

        // Seed host + member + other user.
        foreach (var id in new[] { _hostId, _memberId, _otherUserId })
        {
            var email = $"user-{id:N}@test.com";
            _db.Users.Add(new User
            {
                Id = id,
                Username = email,
                Email = email,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        }
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    /// <summary>
    /// Tạo 1 lobby với status + host + 1 host member + (optional) member user.
    /// </summary>
    private Lobby SeedLobby(
        Guid hostId,
        LobbyStatus status,
        DateTime? scheduledStart = null,
        DateTime? createdAt = null,
        Guid? memberUserId = null,
        bool memberIsActive = true)
    {
        var lobbyId = Guid.NewGuid();
        var shareCode = $"SL{lobbyId:N}".Substring(0, 8).ToUpper();
        var scheduledStartTime = scheduledStart ?? DateTime.UtcNow.AddHours(3);

        var lobby = new Lobby
        {
            Id = lobbyId,
            HostUserId = hostId,
            GameTemplateId = _gameId,
            CafeId = _cafeId,
            PlayDate = DateOnly.FromDateTime(scheduledStartTime),
            PreferredStartTime = new TimeOnly(19, 0),
            PreferredEndTime = new TimeOnly(21, 0),
            ScheduledStartTime = scheduledStartTime,
            ScheduledEndTime = scheduledStartTime.AddHours(2),
            RecruitmentDeadline = scheduledStartTime.AddHours(-2),
            MaxMembers = 4,
            MinPlayers = 2,
            ShareCode = shareCode,
            Status = status,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            UpdatedAt = createdAt ?? DateTime.UtcNow,
            Members = new List<LobbyMember>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    UserId = hostId,
                    IsHost = true,
                    IsActive = true,
                    JoinedAt = DateTime.UtcNow
                }
            }
        };

        if (memberUserId.HasValue)
        {
            lobby.Members.Add(new LobbyMember
            {
                Id = Guid.NewGuid(),
                UserId = memberUserId.Value,
                IsHost = false,
                IsActive = memberIsActive,
                JoinedAt = DateTime.UtcNow
            });
        }

        _db.Lobbies.Add(lobby);
        _db.SaveChanges(); // 2026-09-14: SeedLobby tự save để lobby persist xuống testing DB (Npgsql thật).
        return lobby;
    }

    // ====================================================================
    // Sort priority — Open > InProgress > PendingActivation > ...
    // (user feedback 2026-09-14: ưu tiên Open & InProgress trước)
    // ====================================================================

    /// <summary>
    /// Open (priority 0) phải đứng trước InProgress (priority 1)
    /// — cả 2 đều active và user cần theo dõi trực tiếp.
    /// </summary>
    [Fact]
    public async Task GetMyLobbiesAsync_OpenBeforeInProgress()
    {
        SeedLobby(_hostId, LobbyStatus.InProgress, createdAt: DateTime.UtcNow.AddMinutes(-10));
        SeedLobby(_hostId, LobbyStatus.Open, createdAt: DateTime.UtcNow.AddMinutes(-5));

        var result = await _repo.GetMyLobbiesAsync(_hostId);

        Assert.Equal(2, result.Count);
        Assert.Equal(LobbyStatus.Open, result[0].Status);
        Assert.Equal(LobbyStatus.InProgress, result[1].Status);
    }

    /// <summary>
    /// InProgress phải đứng trên PendingActivation / PendingCafeApproval /
    /// Viable / Full / WaitingCheckIn (priority 2-6).
    /// </summary>
    [Fact]
    public async Task GetMyLobbiesAsync_InProgressBeforeIntermediateStatuses()
    {
        SeedLobby(_hostId, LobbyStatus.WaitingCheckIn);
        SeedLobby(_hostId, LobbyStatus.Full);
        SeedLobby(_hostId, LobbyStatus.Viable);
        SeedLobby(_hostId, LobbyStatus.PendingCafeApproval);
        SeedLobby(_hostId, LobbyStatus.PendingActivation);
        SeedLobby(_hostId, LobbyStatus.InProgress);

        var result = await _repo.GetMyLobbiesAsync(_hostId);

        Assert.Equal(6, result.Count);
        Assert.Equal(LobbyStatus.InProgress, result[0].Status);
        Assert.Equal(LobbyStatus.PendingActivation, result[1].Status);
        Assert.Equal(LobbyStatus.PendingCafeApproval, result[2].Status);
        Assert.Equal(LobbyStatus.Viable, result[3].Status);
        Assert.Equal(LobbyStatus.Full, result[4].Status);
        Assert.Equal(LobbyStatus.WaitingCheckIn, result[5].Status);
    }

    /// <summary>
    /// Trong cùng status, sắp theo ScheduledStartTime desc (gần nhất trước).
    /// </summary>
    [Fact]
    public async Task GetMyLobbiesAsync_WithinSameStatus_SortByStartTimeDesc()
    {
        var earlier = SeedLobby(_hostId, LobbyStatus.Open, scheduledStart: DateTime.UtcNow.AddHours(1));
        var later = SeedLobby(_hostId, LobbyStatus.Open, scheduledStart: DateTime.UtcNow.AddHours(5));
        var middle = SeedLobby(_hostId, LobbyStatus.Open, scheduledStart: DateTime.UtcNow.AddHours(3));

        var result = await _repo.GetMyLobbiesAsync(_hostId);

        Assert.Equal(3, result.Count);
        Assert.Equal(later.Id, result[0].Id);     // 5h
        Assert.Equal(middle.Id, result[1].Id);    // 3h
        Assert.Equal(earlier.Id, result[2].Id);   // 1h
    }

    // ====================================================================
    // Filter — statuses (union set LobbyStatus enum)
    // ====================================================================

    /// <summary>
    /// Filter statuses=[Open, InProgress] phải trả đúng 2 lobby đó và bỏ qua các status khác.
    /// </summary>
    [Fact]
    public async Task GetMyLobbiesAsync_WithStatusesFilter_OnlyReturnsMatchingStatuses()
    {
        SeedLobby(_hostId, LobbyStatus.Open);
        SeedLobby(_hostId, LobbyStatus.InProgress);
        SeedLobby(_hostId, LobbyStatus.PendingActivation);
        SeedLobby(_hostId, LobbyStatus.Viable);

        var result = await _repo.GetMyLobbiesAsync(
            _hostId,
            statuses: new[] { LobbyStatus.Open, LobbyStatus.InProgress });

        Assert.Equal(2, result.Count);
        Assert.Contains(result, l => l.Status == LobbyStatus.Open);
        Assert.Contains(result, l => l.Status == LobbyStatus.InProgress);
    }

    /// <summary>
    /// Filter statuses rỗng = trả tất cả active lobbies (null = all).
    /// </summary>
    [Fact]
    public async Task GetMyLobbiesAsync_WithEmptyStatuses_ReturnsAllActive()
    {
        SeedLobby(_hostId, LobbyStatus.Open);
        SeedLobby(_hostId, LobbyStatus.InProgress);
        SeedLobby(_hostId, LobbyStatus.PendingActivation);

        var resultNull = await _repo.GetMyLobbiesAsync(_hostId, statuses: null);
        var resultEmpty = await _repo.GetMyLobbiesAsync(_hostId, statuses: Array.Empty<LobbyStatus>());

        Assert.Equal(3, resultNull.Count);
        Assert.Equal(3, resultEmpty.Count);
    }

    // ====================================================================
    // Active filter — bỏ qua terminal statuses
    // ====================================================================

    /// <summary>
    /// Chỉ trả ACTIVE lobbies (Open/InProgress/Pending*/Viable/Full/WaitingCheckIn).
    /// Closed/TimeoutFailed/HostCancelled/Dissolved/RejectedByCafe/ExpiredByCafe bị loại.
    /// </summary>
    [Fact]
    public async Task GetMyLobbiesAsync_ExcludesTerminalStatuses()
    {
        SeedLobby(_hostId, LobbyStatus.Open);
        SeedLobby(_hostId, LobbyStatus.InProgress);
        SeedLobby(_hostId, LobbyStatus.Closed);
        SeedLobby(_hostId, LobbyStatus.TimeoutFailed);
        SeedLobby(_hostId, LobbyStatus.HostCancelled);
        SeedLobby(_hostId, LobbyStatus.Dissolved);

        var result = await _repo.GetMyLobbiesAsync(_hostId);

        Assert.Equal(2, result.Count);
        Assert.All(result, l => Assert.Contains(l.Status, new[]
        {
            LobbyStatus.Open, LobbyStatus.InProgress
        }));
    }

    // ====================================================================
    // Member role — user join lobby của host khác cũng xuất hiện trong /my
    // ====================================================================

    /// <summary>
    /// Member không host cũng thấy lobby mình đang tham gia trong /my.
    /// </summary>
    [Fact]
    public async Task GetMyLobbiesAsync_MemberRole_ReturnsLobbiesUserJoined()
    {
        SeedLobby(_hostId, LobbyStatus.Open, memberUserId: _memberId);

        var resultAsHost = await _repo.GetMyLobbiesAsync(_hostId);
        var resultAsMember = await _repo.GetMyLobbiesAsync(_memberId);
        var resultAsOther = await _repo.GetMyLobbiesAsync(_otherUserId);

        Assert.Single(resultAsHost);
        Assert.Single(resultAsMember);
        Assert.Empty(resultAsOther);
    }

    /// <summary>
    /// Member với IsActive=false (đã rời lobby) không còn xuất hiện trong /my.
    /// </summary>
    [Fact]
    public async Task GetMyLobbiesAsync_InactiveMember_NotReturned()
    {
        SeedLobby(_hostId, LobbyStatus.Open, memberUserId: _memberId, memberIsActive: false);

        var result = await _repo.GetMyLobbiesAsync(_memberId);

        Assert.Empty(result);
    }
}
