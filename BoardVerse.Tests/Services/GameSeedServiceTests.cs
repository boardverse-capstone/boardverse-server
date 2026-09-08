using BoardVerse.Core.Data;
using BoardVerse.Core.DTOs.Game;
using BoardVerse.Core.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho GameSeedService — seed game từ master catalog (BR-SEED-GAME).
/// - SeedGamesFromCatalogAsync: chèn / cập nhật theo Name.
/// - SeedSingleGameAsync: idempotent per slug.
/// - Min/Max swap nếu min &gt; max.
/// - Slug không có trong catalog → skip.
/// </summary>
public class GameSeedServiceTests
{
    private static BoardVerseDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<BoardVerseDbContext>()
            .UseInMemoryDatabase($"GameSeedServiceTests-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new BoardVerseDbContext(options);
    }

    [Fact]
    public async Task SeedSingleGameAsync_KnownSlug_InsertsGameTemplate()
    {
        // Catan is a known slug in GameCatalog
        var db = CreateInMemoryDb();
        var sut = new GameSeedService(db);

        await sut.SeedSingleGameAsync("catan");

        var inserted = await db.GameTemplates.FirstOrDefaultAsync(g => g.Name == "Catan");
        Assert.NotNull(inserted);
        Assert.True(inserted!.MinPlayers >= 1);
        Assert.True(inserted.MaxPlayers >= inserted.MinPlayers);
        Assert.True(inserted.PlayTime > 0);
    }

    [Fact]
    public async Task SeedSingleGameAsync_UnknownSlug_SilentlySkipped()
    {
        var db = CreateInMemoryDb();
        var sut = new GameSeedService(db);

        await sut.SeedSingleGameAsync("nonexistent-slug-xyz");

        Assert.Empty(await db.GameTemplates.ToListAsync());
    }

    [Fact]
    public async Task SeedSingleGameAsync_DuplicateInsert_UpdatesExisting()
    {
        var db = CreateInMemoryDb();
        var sut = new GameSeedService(db);

        await sut.SeedSingleGameAsync("catan");
        var firstCount = await db.GameTemplates.CountAsync();

        // Seed again — should NOT add a duplicate row
        await sut.SeedSingleGameAsync("catan");
        var secondCount = await db.GameTemplates.CountAsync();

        Assert.Equal(firstCount, secondCount);
        Assert.Equal(1, secondCount);
    }

    [Fact]
    public async Task SeedGamesFromCatalogAsync_NullSlugs_UsesPopularSlugs()
    {
        var db = CreateInMemoryDb();
        var sut = new GameSeedService(db);

        await sut.SeedGamesFromCatalogAsync(slugs: null);

        // PopularGameSlugs has at least one entry
        var count = await db.GameTemplates.CountAsync();
        Assert.True(count >= 1);
    }

    [Fact]
    public async Task SeedGamesFromCatalogAsync_CustomSlugs_SeedsOnlyRequested()
    {
        var db = CreateInMemoryDb();
        var sut = new GameSeedService(db);

        await sut.SeedGamesFromCatalogAsync(slugs: new List<string> { "catan" });

        var games = await db.GameTemplates.ToListAsync();
        Assert.Single(games);
        Assert.Equal("Catan", games[0].Name);
    }

    [Fact]
    public async Task SeedGamesFromCatalogAsync_UnknownSlugInList_SkippedButContinues()
    {
        var db = CreateInMemoryDb();
        var sut = new GameSeedService(db);

        await sut.SeedGamesFromCatalogAsync(slugs: new List<string> { "catan", "nonexistent-slug-xyz" });

        // Only Catan should be inserted; unknown skipped silently
        Assert.Single(await db.GameTemplates.ToListAsync());
    }

    [Fact]
    public async Task SeedGamesFromCatalogAsync_InsertedGameHasSearchKey()
    {
        var db = CreateInMemoryDb();
        var sut = new GameSeedService(db);

        await sut.SeedSingleGameAsync("catan");

        var game = await db.GameTemplates.FirstAsync();
        Assert.False(string.IsNullOrEmpty(game.NameSearchKey));
        // Search key should be normalized (lowercase, no diacritics)
        Assert.Equal(game.NameSearchKey, VietnameseTextNormalizer.ToSearchKey(game.Name));
    }

    [Fact]
    public async Task SeedGamesFromCatalogAsync_ExistingGameWithoutComponents_AddsComponents()
    {
        // Insert a stub game manually without components, then re-seed
        var db = CreateInMemoryDb();
        var existing = new BoardVerse.Core.Entities.GameTemplate
        {
            Id = Guid.NewGuid(),
            Name = "Catan",
            NameSearchKey = VietnameseTextNormalizer.ToSearchKey("Catan"),
            Description = "old",
            MinPlayers = 3,
            MaxPlayers = 4,
            PlayTime = 60,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Components = new List<BoardVerse.Core.Entities.GameComponentTemplate>()
        };
        await db.GameTemplates.AddAsync(existing);
        await db.SaveChangesAsync();

        var sut = new GameSeedService(db);
        await sut.SeedSingleGameAsync("catan");

        var reloaded = await db.GameTemplates
            .Include(g => g.Components)
            .FirstAsync(g => g.Id == existing.Id);

        Assert.NotEmpty(reloaded.Components);
    }
}
