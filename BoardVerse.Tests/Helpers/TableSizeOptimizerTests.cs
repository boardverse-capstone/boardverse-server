using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Helpers;
using Xunit;

namespace BoardVerse.Tests.Helpers;

/// <summary>
/// Tests cho TableSizeOptimizer - tự tính cách chia bàn tối ưu cho N người.
///
/// Splendor rules:
///   - Tối đa 4 người/bàn.
///   - Tối thiểu 2 người/bàn (không bàn 1 người).
///
/// Verify:
///   - Không bao giờ trả về size = 1 (sẽ được caller handle riêng).
///   - Tất cả size nằm trong [2, 4].
///   - Sum size = playerCount.
///   - Optimal cases: chia đều nhất có thể.
/// </summary>
public class TableSizeOptimizerTests
{
    [Theory]
    [InlineData(2, new[] { 2 })]         // 2 người → 1 bàn 2
    [InlineData(3, new[] { 3 })]         // 3 người → 1 bàn 3
    [InlineData(4, new[] { 4 })]         // 4 người → 1 bàn 4
    [InlineData(5, new[] { 3, 2 })]      // 5 người → [3, 2]
    [InlineData(6, new[] { 3, 3 })]      // 6 người → [3, 3]
    [InlineData(7, new[] { 4, 3 })]      // 7 người → [4, 3]
    [InlineData(8, new[] { 4, 4 })]      // 8 người → [4, 4]
    [InlineData(9, new[] { 3, 3, 3 })]   // 9 người → [3, 3, 3] (chia đều thắng)
    [InlineData(10, new[] { 4, 3, 3 })]  // 10 người → [4, 3, 3]
    [InlineData(11, new[] { 4, 4, 3 })]  // 11 người → [4, 4, 3] (3 bàn ít overhead hơn 4 bàn)
    [InlineData(12, new[] { 4, 4, 4 })]  // 12 người → [4, 4, 4] (3 bàn full thắng 4 bàn 3)
    [InlineData(13, new[] { 4, 3, 3, 3 })] // 13 người → [4, 3, 3, 3]
    [InlineData(15, new[] { 4, 4, 4, 3 })] // 15 người → [4, 4, 4, 3]
    [InlineData(16, new[] { 4, 4, 4, 4 })] // 16 người → [4, 4, 4, 4]
    [InlineData(17, new[] { 4, 4, 3, 3, 3 })] // 17 người → chia đều
    [InlineData(20, new[] { 4, 4, 4, 4, 4 })] // 20 người → 5 bàn 4
    [InlineData(32, new[] { 4, 4, 4, 4, 4, 4, 4, 4 })] // 32 người → 8 bàn 4
    public void CalculateOptimalTableSizes_ReturnsExpected(int playerCount, int[] expected)
    {
        var result = TableSizeOptimizer.CalculateOptimalTableSizes(playerCount);

        Assert.NotNull(result);
        Assert.Equal(expected, result);

        // Verify constraints
        Assert.All(result, size => Assert.InRange(size, TableSizeOptimizer.MinTableSize, TableSizeOptimizer.MaxTableSize));
        Assert.Equal(playerCount, result.Sum());

        // Verify không có size = 1
        Assert.DoesNotContain(result, size => size == 1);
    }

    [Fact]
    public void CalculateOptimalTableSizes_OnePlayer_ReturnsNull()
    {
        var result = TableSizeOptimizer.CalculateOptimalTableSizes(1);

        Assert.Null(result); // caller phải handle riêng
    }

    [Fact]
    public void CalculateOptimalTableSizes_ZeroPlayers_ReturnsNull()
    {
        var result = TableSizeOptimizer.CalculateOptimalTableSizes(0);

        Assert.Null(result);
    }

    [Theory]
    [InlineData(5)]      // Edge case shortage
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(9)]      // Chia đều 3 bàn 3
    [InlineData(13)]     // Chia 4 bàn không đều
    [InlineData(17)]
    public void CalculateOptimalTableSizes_NeverProducesSolos(int playerCount)
    {
        var result = TableSizeOptimizer.CalculateOptimalTableSizes(playerCount);

        Assert.NotNull(result);
        Assert.DoesNotContain(1, result!);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(13)]
    [InlineData(17)]
    [InlineData(25)]
    public void CalculateOptimalTableSizes_SumEqualsPlayerCount(int playerCount)
    {
        var result = TableSizeOptimizer.CalculateOptimalTableSizes(playerCount);

        Assert.NotNull(result);
        Assert.Equal(playerCount, result!.Sum());
    }
}

/// <summary>
/// Tests cho SwissPairingHelper.DistributeIntoTablesWithRemainder với auto-sizing.
///
/// Verify:
///   - 5 người → 1 bàn 3 + 1 bàn 2 (không bàn 1).
///   - 6 người → 2 bàn 3 (chia đều).
///   - 9 người → 3 bàn 3.
///   - Snake draft preserved (top player ở table 1, bottom ở table cuối).
/// </summary>
public class SwissPairingAutoSizeTests
{
    private static List<TournamentParticipant> MakeParticipants(int count)
    {
        var list = new List<TournamentParticipant>();
        for (int i = 0; i < count; i++)
        {
            list.Add(new TournamentParticipant
            {
                Id = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                FinalElo = 2000 - (i * 10), // Elo giảm dần: top player có Elo cao nhất
                TotalPrestigePoints = 0,
                CheckedInAt = DateTime.UtcNow.AddMinutes(-i),
                Status = TournamentParticipantStatus.Active,
            });
        }
        return list;
    }

    [Fact]
    public void DistributeIntoTablesWithRemainder_FivePlayers_NoSoloTable()
    {
        // Arrange
        var participants = MakeParticipants(5);

        // Act
        var tables = SwissPairingHelper.DistributeIntoTablesWithRemainder(participants);

        // Assert
        Assert.Equal(2, tables.Count); // [3, 2]

        // Verify no solo tables
        Assert.All(tables, t => Assert.True(t.Count >= 2, "Không được có bàn 1 người"));

        // Verify total = 5
        Assert.Equal(5, tables.Sum(t => t.Count));

        // Verify sizes theo TableSizeOptimizer (chia đều nhất)
        var tableSizes = tables.Select(t => t.Count).ToList();
        Assert.Equal(new[] { 3, 2 }, tableSizes);
    }

    [Fact]
    public void DistributeIntoTablesWithRemainder_SixPlayers_TwoBalancedTables()
    {
        var participants = MakeParticipants(6);

        var tables = SwissPairingHelper.DistributeIntoTablesWithRemainder(participants);

        Assert.Equal(2, tables.Count);
        Assert.Equal(new[] { 3, 3 }, tables.Select(t => t.Count).ToArray());
    }

    [Fact]
    public void DistributeIntoTablesWithRemainder_NinePlayers_ThreeBalancedTables()
    {
        var participants = MakeParticipants(9);

        var tables = SwissPairingHelper.DistributeIntoTablesWithRemainder(participants);

        Assert.Equal(3, tables.Count);
        Assert.Equal(new[] { 3, 3, 3 }, tables.Select(t => t.Count).ToArray());
    }

    [Fact]
    public void DistributeIntoTablesWithRemainder_TopPlayerAlwaysFirstTable()
    {
        // Snake draft: player cao Elo nhất (top) phải ở table 1
        var participants = MakeParticipants(7);

        var tables = SwissPairingHelper.DistributeIntoTablesWithRemainder(participants);

        // Player 0 có Elo cao nhất (2000)
        var topPlayer = participants[0];
        Assert.Contains(topPlayer, tables[0]);
    }

    [Fact]
    public void DistributeIntoTablesWithRemainder_EloBalancedAcrossTables()
    {
        // 8 players với Elo giảm đều
        var participants = MakeParticipants(8);
        // Elo: [2000, 1990, 1980, ..., 1930]
        var avgElo = participants.Average(p => (double)p.FinalElo);

        var tables = SwissPairingHelper.DistributeIntoTablesWithRemainder(participants);

        Assert.Equal(2, tables.Count); // [4, 4]

        // Mỗi bàn có avg Elo gần với avg tổng
        foreach (var table in tables)
        {
            var tableAvg = table.Average(p => (double)p.FinalElo);
            var diff = Math.Abs(tableAvg - avgElo);
            Assert.True(diff < 50, $"Bàn lệch Elo {diff} so với tổng (quá 50).");
        }
    }
}