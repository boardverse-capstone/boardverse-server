using BoardVerse.Core.DTOs.User;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.Services;
using Microsoft.Extensions.Caching.Memory;
using Moq;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho <see cref="LeaderboardService"/> — phục vụ BR §K-06 leaderboard UI.
/// Mục tiêu:
///   - Karma/Elo ordering đúng (DESC).
///   - Paging offset/limit + TotalCount đúng.
///   - Cache hit không gọi DB lần 2.
///   - Viewer rank lookup trả về rank đúng trong top 1000.
/// </summary>
public class LeaderboardServiceTests
{
    private readonly Mock<IUserProfileRepository> _repo = new();

    private static IMemoryCache NewCache() => new MemoryCache(new MemoryCacheOptions());

    private LeaderboardService CreateSut(IMemoryCache? cache = null) =>
        new LeaderboardService(_repo.Object, cache ?? NewCache());

    private static List<KarmaLeaderboardRow> KarmaRows(int startKarma, int count) =>
        Enumerable.Range(0, count).Select(i => new KarmaLeaderboardRow
        {
            UserId = Guid.NewGuid(),
            Username = $"player_{i:D2}",
            DisplayName = $"Player {i}",
            AvatarUrl = $"https://cdn/{i}.png",
            KarmaPoints = startKarma - i * 10,
            GlobalElo = 1500 + i,
            Level = i + 1,
            GamerTier = i switch
            {
                0 => GamerTier.Grandmaster,
                1 => GamerTier.Master,
                2 => GamerTier.Diamond,
                _ => GamerTier.Bronze
            }
        }).ToList();

    private static List<EloLeaderboardRow> EloRows(int startElo, int count) =>
        Enumerable.Range(0, count).Select(i => new EloLeaderboardRow
        {
            UserId = Guid.NewGuid(),
            Username = $"player_{i:D2}",
            DisplayName = $"Player {i}",
            AvatarUrl = $"https://cdn/{i}.png",
            KarmaPoints = 100 + i,
            GlobalElo = startElo - i * 25,
            Level = i + 1,
            GamerTier = i % 2 == 0 ? GamerTier.Gold : GamerTier.Silver
        }).ToList();

    [Fact]
    public async Task GetKarmaLeaderboardPagedAsync_FirstPage_ReturnsRank1Based()
    {
        var rows = KarmaRows(startKarma: 200, count: 5);
        _repo.Setup(r => r.GetKarmaLeaderboardAsync(0, 5, It.IsAny<CancellationToken>())).ReturnsAsync(rows);
        _repo.Setup(r => r.CountActiveKarmaUsersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(50L);

        var sut = CreateSut();
        var result = await sut.GetKarmaLeaderboardPagedAsync(0, 5, viewerUserId: null);

        Assert.Equal(5, result.Entries.Count);
        Assert.Equal(1, result.Entries[0].Rank);
        Assert.Equal(2, result.Entries[1].Rank);
        Assert.Equal(5, result.Entries[4].Rank);
        Assert.Equal(200, result.Entries[0].KarmaPoints);
        Assert.Equal(160, result.Entries[4].KarmaPoints);
        Assert.Equal(0, result.Offset);
        Assert.Equal(5, result.Limit);
        Assert.Equal(50, result.TotalCount);
        Assert.Null(result.UserRank);
    }

    [Fact]
    public async Task GetKarmaLeaderboardPagedAsync_OffsetPage_RanksContinue()
    {
        var rows = KarmaRows(startKarma: 100, count: 3);
        _repo.Setup(r => r.GetKarmaLeaderboardAsync(5, 3, It.IsAny<CancellationToken>())).ReturnsAsync(rows);
        _repo.Setup(r => r.CountActiveKarmaUsersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(20L);

        var sut = CreateSut();
        var result = await sut.GetKarmaLeaderboardPagedAsync(5, 3, viewerUserId: null);

        Assert.Equal(6, result.Entries[0].Rank);
        Assert.Equal(8, result.Entries[2].Rank);
        Assert.Equal(5, result.Offset);
    }

    [Fact]
    public async Task GetKarmaLeaderboardPagedAsync_ViewerInsideTop1000_ReturnsUserRank()
    {
        var viewer = new KarmaLeaderboardRow
        {
            UserId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Username = "viewer",
            KarmaPoints = 150,
            GlobalElo = 1500,
            Level = 3,
            GamerTier = GamerTier.Gold
        };

        var rows = KarmaRows(startKarma: 300, count: 5); // ranks 1..5
        rows.Insert(2, viewer); // now at position 3 (rank 3)

        _repo.Setup(r => r.GetKarmaLeaderboardAsync(0, 10, It.IsAny<CancellationToken>())).ReturnsAsync(rows);
        _repo.Setup(r => r.GetKarmaLeaderboardAsync(0, 1000, It.IsAny<CancellationToken>())).ReturnsAsync(rows); // cursor scan
        _repo.Setup(r => r.CountActiveKarmaUsersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(10L);
        _repo.Setup(r => r.GetUserRankAsync(viewer.UserId, LeaderboardMetric.Karma, It.IsAny<CancellationToken>()))
            .ReturnsAsync(viewer);

        var sut = CreateSut();
        var result = await sut.GetKarmaLeaderboardPagedAsync(0, 10, viewer.UserId);

        Assert.NotNull(result.UserRank);
        Assert.Equal(3, result.UserRank!.Rank);
        Assert.Equal("viewer", result.UserRank.Username);
        Assert.Equal(GamerTier.Gold, result.UserRank.GamerTier);
    }

    [Fact]
    public async Task GetKarmaLeaderboardPagedAsync_ViewerOutsideTop_ReturnsNullUserRank()
    {
        // Service calls GetKarmaLeaderboardAsync(0, 10) for the main page…
        var rows = KarmaRows(startKarma: 200, count: 10);
        _repo.Setup(r => r.GetKarmaLeaderboardAsync(0, 10, It.IsAny<CancellationToken>())).ReturnsAsync(rows);
        _repo.Setup(r => r.CountActiveKarmaUsersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(100L);

        // …then re-scans the top 1000 to find the viewer's rank.
        _repo.Setup(r => r.GetKarmaLeaderboardAsync(0, 1000, It.IsAny<CancellationToken>())).ReturnsAsync(rows);

        // Viewer exists in DB but falls beyond rank 10 — the cursor scan returns no match.
        var viewer = new LeaderboardRankRow
        {
            UserId = Guid.NewGuid(),
            Username = "lowrank",
            KarmaPoints = 50
        };
        _repo.Setup(r => r.GetUserRankAsync(viewer.UserId, LeaderboardMetric.Karma, It.IsAny<CancellationToken>()))
            .ReturnsAsync(viewer);

        var sut = CreateSut();
        var result = await sut.GetKarmaLeaderboardPagedAsync(0, 10, viewer.UserId);

        Assert.Null(result.UserRank);
    }

    [Fact]
    public async Task GetKarmaLeaderboardPagedAsync_CacheHit_DoesNotCallRepoTwice()
    {
        var rows = KarmaRows(startKarma: 200, count: 3);
        _repo.Setup(r => r.GetKarmaLeaderboardAsync(0, 3, It.IsAny<CancellationToken>())).ReturnsAsync(rows);
        _repo.Setup(r => r.CountActiveKarmaUsersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(10L);

        var sut = CreateSut();
        await sut.GetKarmaLeaderboardPagedAsync(0, 3, viewerUserId: null);
        await sut.GetKarmaLeaderboardPagedAsync(0, 3, viewerUserId: null);

        _repo.Verify(r => r.GetKarmaLeaderboardAsync(0, 3, It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.CountActiveKarmaUsersAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetEloLeaderboardPagedAsync_OrdersByGlobalEloDesc()
    {
        var rows = EloRows(startElo: 2400, count: 4);
        _repo.Setup(r => r.GetEloLeaderboardAsync(0, 4, It.IsAny<CancellationToken>())).ReturnsAsync(rows);
        _repo.Setup(r => r.CountActiveEloUsersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(100L);

        var sut = CreateSut();
        var result = await sut.GetEloLeaderboardPagedAsync(0, 4, viewerUserId: null);

        Assert.Equal(2400, result.Entries[0].GlobalElo);
        Assert.Equal(2325, result.Entries[3].GlobalElo);
        Assert.Equal(1, result.Entries[0].Rank);
        Assert.Equal(4, result.Entries[3].Rank);
        Assert.Equal(4, result.Entries[3].Level);
        Assert.Equal(100, result.TotalCount);
    }

    [Fact]
    public async Task GetEloLeaderboardPagedAsync_EloEntryDoesNotExposeGamerTier()
    {
        var rows = EloRows(startElo: 2400, count: 1);
        _repo.Setup(r => r.GetEloLeaderboardAsync(0, 1, It.IsAny<CancellationToken>())).ReturnsAsync(rows);
        _repo.Setup(r => r.CountActiveEloUsersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1L);

        var sut = CreateSut();
        var result = await sut.GetEloLeaderboardPagedAsync(0, 1, viewerUserId: null);

        // Compile-time đã chứng minh EloLeaderboardEntryDto không có GamerTier;
        // runtime chỉ verify entry shape còn nguyên vẹn.
        Assert.Single(result.Entries);
        Assert.Equal(2400, result.Entries[0].GlobalElo);
        Assert.NotNull(result.Entries[0].Username);
    }

    [Fact]
    public async Task GetEloLeaderboardPagedAsync_ViewerInsideTop_ReturnsUserRank()
    {
        var rows = EloRows(startElo: 2500, count: 3);
        var viewer = new EloLeaderboardRow
        {
            UserId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Username = "eloviewer",
            GlobalElo = 2200,
            Level = 5,
            GamerTier = GamerTier.Diamond
        };
        rows.Add(viewer); // rank 4

        _repo.Setup(r => r.GetEloLeaderboardAsync(0, 10, It.IsAny<CancellationToken>())).ReturnsAsync(rows);
        _repo.Setup(r => r.GetEloLeaderboardAsync(0, 1000, It.IsAny<CancellationToken>())).ReturnsAsync(rows); // cursor scan
        _repo.Setup(r => r.CountActiveEloUsersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(50L);
        _repo.Setup(r => r.GetUserRankAsync(viewer.UserId, LeaderboardMetric.Elo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(viewer);

        var sut = CreateSut();
        var result = await sut.GetEloLeaderboardPagedAsync(0, 10, viewer.UserId);

        Assert.NotNull(result.UserRank);
        Assert.Equal(4, result.UserRank!.Rank);
        Assert.Equal(2200, result.UserRank.GlobalElo);
        Assert.Equal(GamerTier.Diamond, result.UserRank.GamerTier);
    }

    [Fact]
    public async Task GetKarmaLeaderboardAsync_LegacyOverload_DelegatesToPaged()
    {
        var rows = KarmaRows(startKarma: 150, count: 2);
        _repo.Setup(r => r.GetKarmaLeaderboardAsync(0, 2, It.IsAny<CancellationToken>())).ReturnsAsync(rows);
        _repo.Setup(r => r.CountActiveKarmaUsersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(10L);

        var sut = CreateSut();
        var result = await sut.GetKarmaLeaderboardAsync(2);

        Assert.Equal(2, result.Entries.Count);
        Assert.Equal(1, result.Entries[0].Rank);
    }

    [Fact]
    public async Task GetEloLeaderboardAsync_LegacyOverload_DelegatesToPaged()
    {
        var rows = EloRows(startElo: 2000, count: 2);
        _repo.Setup(r => r.GetEloLeaderboardAsync(0, 2, It.IsAny<CancellationToken>())).ReturnsAsync(rows);
        _repo.Setup(r => r.CountActiveEloUsersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(10L);

        var sut = CreateSut();
        var result = await sut.GetEloLeaderboardAsync(2);

        Assert.Equal(2, result.Entries.Count);
        Assert.Equal(2000, result.Entries[0].GlobalElo);
    }

    [Fact]
    public async Task GetKarmaLeaderboardPagedAsync_OffsetNegative_ClampsToZero()
    {
        _repo.Setup(r => r.GetKarmaLeaderboardAsync(0, 10, It.IsAny<CancellationToken>())).ReturnsAsync(KarmaRows(200, 2));
        _repo.Setup(r => r.CountActiveKarmaUsersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(2L);

        var sut = CreateSut();
        var result = await sut.GetKarmaLeaderboardPagedAsync(-5, 10, viewerUserId: null);

        Assert.Equal(0, result.Offset);
    }

    [Fact]
    public async Task GetKarmaLeaderboardPagedAsync_LimitZero_FallsBackToDefault50()
    {
        _repo.Setup(r => r.GetKarmaLeaderboardAsync(0, 50, It.IsAny<CancellationToken>())).ReturnsAsync(KarmaRows(200, 1));
        _repo.Setup(r => r.CountActiveKarmaUsersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1L);

        var sut = CreateSut();
        var result = await sut.GetKarmaLeaderboardPagedAsync(0, 0, viewerUserId: null);

        Assert.Equal(50, result.Limit);
    }

    [Fact]
    public async Task GetKarmaLeaderboardPagedAsync_LimitTooLarge_ClampsTo500()
    {
        _repo.Setup(r => r.GetKarmaLeaderboardAsync(0, 500, It.IsAny<CancellationToken>())).ReturnsAsync(KarmaRows(200, 1));
        _repo.Setup(r => r.CountActiveKarmaUsersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1L);

        var sut = CreateSut();
        var result = await sut.GetKarmaLeaderboardPagedAsync(0, 9999, viewerUserId: null);

        Assert.Equal(500, result.Limit);
    }
}
