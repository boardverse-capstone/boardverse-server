using BoardVerse.Core.Data;

namespace BoardVerse.Tests.Helpers;

public class BggCategoryMapperTests
{
    [Fact]
    public void ResolveCategorySlugs_CatanBggMetadata_MapsStrategyAndNegotiation()
    {
        var slugs = BggCategoryMapper.ResolveCategorySlugs(
            ["Strategy", "Negotiation"],
            ["Dice Rolling", "Trading"],
            "Catan");

        Assert.Contains("chien-thuat", slugs);
        Assert.Contains("doi-khang", slugs);
    }

    [Fact]
    public void ResolveCategorySlugs_PandemicBggMetadata_MapsCooperative()
    {
        var slugs = BggCategoryMapper.ResolveCategorySlugs(
            ["Medical", "Environmental"],
            ["Cooperative Game", "Hand Management"],
            "Pandemic");

        Assert.Contains("hop-tac", slugs);
    }

    [Fact]
    public void ResolveCategorySlugs_EmptyBgg_FallsBackToSeedMapByName()
    {
        var slugs = BggCategoryMapper.ResolveCategorySlugs([], [], "Catan");

        Assert.Contains("chien-thuat", slugs);
        Assert.Contains("doi-khang", slugs);
    }

    [Fact]
    public void ResolveCategorySlugs_WerewolfMechanics_MapsHiddenRole()
    {
        var slugs = BggCategoryMapper.ResolveCategorySlugs(
            ["Bluffing", "Party Game"],
            ["Hidden Roles", "Voting"],
            "One Night Ultimate Werewolf");

        Assert.Contains("an-vai", slugs);
        Assert.Contains("giai-tri", slugs);
    }

    [Fact]
    public void ResolveCategorySlugs_UnknownGame_ReturnsEmpty()
    {
        var slugs = BggCategoryMapper.ResolveCategorySlugs(
            ["Children's Game"],
            ["Roll / Spin and Move"],
            "Totally Unknown Game XYZ");

        Assert.Empty(slugs);
    }
}
