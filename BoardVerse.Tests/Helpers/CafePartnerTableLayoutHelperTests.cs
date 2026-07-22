using BoardVerse.Core.Helpers;

namespace BoardVerse.Tests.Helpers;

public class CafePartnerTableLayoutHelperTests
{
    [Fact]
    public void GenerateDefaultNames_Zero_ReturnsEmpty()
    {
        Assert.Empty(CafePartnerTableLayoutHelper.GenerateDefaultNames(0));
    }

    [Fact]
    public void GenerateDefaultNames_PositiveCount_ReturnsSequential()
    {
        Assert.Equal(new[] { "Bàn 1", "Bàn 2", "Bàn 3" }, CafePartnerTableLayoutHelper.GenerateDefaultNames(3));
    }

    [Fact]
    public void IsDefaultLayout_Empty_ReturnsFalse()
    {
        Assert.False(CafePartnerTableLayoutHelper.IsDefaultLayout(Array.Empty<string>()));
    }

    [Fact]
    public void IsDefaultLayout_SequentialBans_ReturnsTrue()
    {
        Assert.True(CafePartnerTableLayoutHelper.IsDefaultLayout(new[] { "Bàn 1", "Bàn 2", "Bàn 3" }));
    }

    [Fact]
    public void IsDefaultLayout_CustomNames_ReturnsFalse()
    {
        Assert.False(CafePartnerTableLayoutHelper.IsDefaultLayout(new[] { "VIP", "Standard" }));
    }

    [Fact]
    public void ResolveTableNames_WithRequestedNames_TrimsAndDropsBlanks()
    {
        var names = new[] { "  VIP  ", "", "  ", "Patio" };

        var result = CafePartnerTableLayoutHelper.ResolveTableNames(3, names, Array.Empty<string>());

        Assert.Equal(new[] { "VIP", "Patio", "Bàn 3" }, result);
    }

    [Fact]
    public void ResolveTableNames_NoRequestedButDefaultExisting_Regenerates()
    {
        var result = CafePartnerTableLayoutHelper.ResolveTableNames(4, null, new[] { "Bàn 1", "Bàn 2", "Bàn 3" });

        Assert.Equal(new[] { "Bàn 1", "Bàn 2", "Bàn 3", "Bàn 4" }, result);
    }

    [Fact]
    public void ResolveTableNames_NoRequestedNoExisting_GeneratesFresh()
    {
        var result = CafePartnerTableLayoutHelper.ResolveTableNames(2, null, Array.Empty<string>());

        Assert.Equal(new[] { "Bàn 1", "Bàn 2" }, result);
    }

    [Fact]
    public void ResolveTableNames_WithCustomExisting_PreservesAndFillsGap()
    {
        var result = CafePartnerTableLayoutHelper.ResolveTableNames(3, null, new[] { "VIP", "Standard" });

        Assert.Equal(new[] { "VIP", "Standard", "Bàn 3" }, result);
    }

    [Fact]
    public void ResolveTableNames_DecreasingCount_TruncatesRequested()
    {
        var result = CafePartnerTableLayoutHelper.ResolveTableNames(2, new[] { "A", "B", "C" }, Array.Empty<string>());

        Assert.Equal(new[] { "A", "B" }, result);
    }
}