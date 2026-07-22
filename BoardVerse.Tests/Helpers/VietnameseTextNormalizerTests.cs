using BoardVerse.Core.Helpers;

namespace BoardVerse.Tests.Helpers;

public class VietnameseTextNormalizerTests
{
    [Fact]
    public void ToSearchKey_StripsVietnameseDiacritics()
    {
        var result = VietnameseTextNormalizer.ToSearchKey("Cát An");

        Assert.Equal("cat an", result);
    }

    [Fact]
    public void ToSearchKey_ReplacesDWithD()
    {
        Assert.Equal("do", VietnameseTextNormalizer.ToSearchKey("đo"));
        Assert.Equal("do", VietnameseTextNormalizer.ToSearchKey("Đo"));
        Assert.Equal("don dep", VietnameseTextNormalizer.ToSearchKey("Dọn Dẹp"));
    }

    [Fact]
    public void ToSearchKey_HandlesEmptyAndWhitespace()
    {
        Assert.Equal(string.Empty, VietnameseTextNormalizer.ToSearchKey(null));
        Assert.Equal(string.Empty, VietnameseTextNormalizer.ToSearchKey(""));
        Assert.Equal(string.Empty, VietnameseTextNormalizer.ToSearchKey("   "));
    }

    [Fact]
    public void ToSearchKey_LowercasesAndTrims()
    {
        Assert.Equal("hello", VietnameseTextNormalizer.ToSearchKey("  Hello  "));
    }

    [Fact]
    public void ToSlug_ReplacesSpacesAndUnderscoresWithHyphens()
    {
        Assert.Equal("catan-board-game", VietnameseTextNormalizer.ToSlug("Catan Board Game"));
        Assert.Equal("catan-board-game", VietnameseTextNormalizer.ToSlug("catan_board_game"));
    }

    [Fact]
    public void ToSlug_StripsDiacriticsBeforeSplitting()
    {
        Assert.Equal("tro-choi-viet-nam", VietnameseTextNormalizer.ToSlug("Trò Chơi Việt Nam"));
    }

    [Fact]
    public void ToSlug_ReturnsEmptyForNull()
    {
        Assert.Equal(string.Empty, VietnameseTextNormalizer.ToSlug(null));
    }
}