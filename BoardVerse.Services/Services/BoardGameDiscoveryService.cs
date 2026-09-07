using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Cafe;
using BoardVerse.Core.DTOs.Discovery;
using BoardVerse.Core.DTOs.Game;
using BoardVerse.Core.DTOs.Lobby;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Services.Services;

public class BoardGameDiscoveryService : IBoardGameDiscoveryService
{
    private readonly IGameTemplateRepository _gameTemplateRepository;
    private readonly IPlayerBoardGameSaveRepository _saveRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly ICafeService _cafeService;
    private readonly ILobbyService _lobbyService;

    public BoardGameDiscoveryService(
        IGameTemplateRepository gameTemplateRepository,
        IPlayerBoardGameSaveRepository saveRepository,
        ICategoryRepository categoryRepository,
        ICafeService cafeService,
        ILobbyService lobbyService)
    {
        _gameTemplateRepository = gameTemplateRepository;
        _saveRepository = saveRepository;
        _categoryRepository = categoryRepository;
        _cafeService = cafeService;
        _lobbyService = lobbyService;
    }

    public async Task<BoardGameSurveyResponseDto> RunSurveyAsync(
        BoardGameSurveyRequestDto request,
        Guid? userId,
        double? latitude,
        double? longitude,
        CancellationToken cancellationToken = default)
    {
        if (request.PlayerCount < 1 || request.PlayerCount > 5)
        {
            throw new BadRequestException(ApiErrorMessages.Discovery.PlayerCountInvalid);
        }

        var effectivePlayerCount = request.PlayerCount == 5 ? 5 : request.PlayerCount;

        var query = BuildSurveyQuery(request, effectivePlayerCount);
        var result = await _gameTemplateRepository.GetBoardGamesPagedAsync(query, cancellationToken);
        var games = result.Data.ToList();

        var savedIds = userId.HasValue
            ? (await _saveRepository.GetSavedGameTemplateIdsAsync(userId.Value, cancellationToken)).ToHashSet()
            : new HashSet<Guid>();

        IReadOnlyList<Guid>? topGameIds = games.Count > 0
            ? games.Select(g => g.Id).ToList()
            : null;

        var categoryNames = await GetCategoryNamesAsync(
            request.CategoryIds, cancellationToken);

        var scoredGames = games.Select(game =>
        {
            var score = CalculateMatchScore(
                game,
                request,
                effectivePlayerCount);
            return new
            {
                Game = game,
                Score = score
            };
        })
        .OrderByDescending(x => x.Score)
        .ThenBy(x => x.Game.Name)
        .ToList();

        var responseGames = scoredGames.Select(x =>
        {
            var game = x.Game;
            return new DiscoveryBoardGameDto
            {
                Id = game.Id,
                Name = game.Name,
                ThumbnailUrl = game.ThumbnailUrl,
                Description = game.Description,
                MinPlayers = game.MinPlayers,
                MaxPlayers = game.MaxPlayers,
                PlayTimeMinutes = game.PlayTime,
                Categories = game.Categories.Select(gc => gc.Category.Name).ToList(),
                MatchScore = x.Score,
                IsSaved = userId.HasValue && savedIds.Contains(game.Id),
                HasOpenLobby = false
            };
        }).ToList();

        NearbyCafeForGameDto? nearestCafe = null;
        List<OpenLobbySummaryDto> openLobbies = [];

        if (topGameIds != null && topGameIds.Count > 0)
        {
            var topGameId = topGameIds.First();

            if (latitude.HasValue && longitude.HasValue)
            {
                var cafeResult = await _cafeService.GetNearbyCafesAsync(
                    latitude.Value,
                    longitude.Value,
                    radiusKm: 15,
                    gameTemplateId: topGameId,
                    name: null,
                    new PaginationParams(1, 1),
                    cancellationToken);

                var firstCafe = cafeResult.Data?.FirstOrDefault();
                if (firstCafe != null)
                {
                    nearestCafe = MapToNearestCafe(firstCafe);
                }
            }

            var lobbies = await _lobbyService.GetDiscoverableLobbiesAsync(
                topGameId,
                latitude,
                longitude,
                radiusKm: null,
                limit: 10,
                requestingUserId: null,
                cancellationToken: cancellationToken);

            openLobbies = lobbies
                .Take(5)
                .Select(l => new OpenLobbySummaryDto
                {
                    LobbyId = l.Id,
                    GameTemplateId = l.GameTemplateId,
                    GameName = l.GameName ?? string.Empty,
                    CurrentMembers = l.Members?.Count ?? 0,
                    MaxMembers = l.MaxMembers,
                    PlayDate = l.ScheduledStartTime?.Date,
                    StartTime = l.ScheduledStartTime.HasValue
                        ? TimeOnly.FromDateTime(l.ScheduledStartTime.Value)
                        : null,
                    CafeId = l.CafeId,
                    CafeName = l.CafeName,
                    IsOpen = l.Status is LobbyStatus.Open or LobbyStatus.Viable
                        or LobbyStatus.Full or LobbyStatus.WaitingCheckIn
                })
                .ToList();
        }

        foreach (var g in responseGames)
        {
            if (topGameIds != null && topGameIds.Contains(g.Id) && openLobbies.Count > 0)
            {
                g.HasOpenLobby = openLobbies.Any(l => l.GameTemplateId == g.Id);
            }
        }

        return new BoardGameSurveyResponseDto
        {
            Games = responseGames,
            TotalCount = result.Meta.TotalItems,
            SurveyedPlayerCount = request.PlayerCount,
            AppliedFilters = categoryNames,
            NearestCafe = nearestCafe,
            OpenLobbies = openLobbies
        };
    }

    public async Task<List<SavedBoardGameDto>> GetSavedGamesAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var saves = await _saveRepository.GetByUserAsync(userId, cancellationToken);
        if (saves.Count == 0) return [];

        var gameIds = saves.Select(s => s.GameTemplateId).ToList();

        var query = new GetMasterGamesQuery { PageSize = 100, PageNumber = 1 };
        var allGames = await _gameTemplateRepository.GetBoardGamesPagedAsync(query, cancellationToken);

        var savedGames = allGames.Data
            .Where(g => gameIds.Contains(g.Id))
            .ToList();

        var gameCategories = new Dictionary<Guid, List<string>>();
        foreach (var game in savedGames)
        {
            var cats = game.Categories.Select(gc => gc.Category.Name).ToList();
            gameCategories[game.Id] = cats;
        }

        var saveMap = saves.ToDictionary(s => s.GameTemplateId);

        var hasLobbyFlags = new Dictionary<Guid, bool>();
        foreach (var game in savedGames)
        {
            var lobbies = await _lobbyService.GetDiscoverableLobbiesAsync(
                game.Id, null, null, null, 1, null, cancellationToken);
            hasLobbyFlags[game.Id] = lobbies.Count > 0;
        }

        return savedGames
            .Where(g => saveMap.ContainsKey(g.Id))
            .Select(game =>
            {
                saveMap.TryGetValue(game.Id, out var save);
                return new SavedBoardGameDto
                {
                    Id = save!.Id,
                    GameTemplateId = game.Id,
                    GameName = game.Name,
                    ThumbnailUrl = game.ThumbnailUrl,
                    Description = game.Description,
                    MinPlayers = game.MinPlayers,
                    MaxPlayers = game.MaxPlayers,
                    PlayTimeMinutes = game.PlayTime,
                    Categories = gameCategories.GetValueOrDefault(game.Id, []),
                    SavedAt = save.SavedAt,
                    HasOpenLobby = hasLobbyFlags.GetValueOrDefault(game.Id, false)
                };
            })
            .OrderByDescending(x => x.SavedAt)
            .ToList();
    }

    public async Task<BoardGameSaveResultDto> ToggleSaveAsync(
        Guid userId,
        Guid gameTemplateId,
        CancellationToken cancellationToken = default)
    {
        var exists = await _gameTemplateRepository.ExistsAsync(gameTemplateId, cancellationToken);
        if (!exists)
        {
            throw new BoardGameNotFoundException(
                ApiErrorMessages.Discovery.GameNotSaved(gameTemplateId));
        }

        var existing = await _saveRepository.GetByUserAndGameAsync(
            userId, gameTemplateId, cancellationToken);

        if (existing != null)
        {
            await _saveRepository.DeleteAsync(userId, gameTemplateId, cancellationToken);
            await _saveRepository.SaveChangesAsync(cancellationToken);
            return new BoardGameSaveResultDto
            {
                GameTemplateId = gameTemplateId,
                IsSaved = false,
                SavedAt = null
            };
        }

        var save = new PlayerBoardGameSave
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            GameTemplateId = gameTemplateId,
            SavedAt = DateTime.UtcNow
        };

        await _saveRepository.AddAsync(save, cancellationToken);
        await _saveRepository.SaveChangesAsync(cancellationToken);

        return new BoardGameSaveResultDto
        {
            GameTemplateId = gameTemplateId,
            IsSaved = true,
            SavedAt = save.SavedAt
        };
    }

    // ===== Private helper methods =====

    private GetMasterGamesQuery BuildSurveyQuery(
        BoardGameSurveyRequestDto request,
        int effectivePlayerCount)
    {
        var query = new GetMasterGamesQuery
        {
            PageSize = 20,
            PageNumber = 1,
            PlayerCount = effectivePlayerCount
        };

        if (request.CategoryIds is { Count: > 0 })
        {
            query.CategoryIds = request.CategoryIds;
        }

        if (request.PreferredDurations is { Count: > 0 })
        {
            var ranges = request.PreferredDurations
                .Select(d => d.ToLowerInvariant() switch
                {
                    "under30" or "under_30" or "short" => PlayTimeRange.Under30,
                    "30to60" or "30_to_60" or "medium" => PlayTimeRange.ThirtyToSixty,
                    "over60" or "over_60" or "long" => PlayTimeRange.Over60,
                    _ => throw new BadRequestException($"Giá trị PreferredDurations không hợp lệ: '{d}'. "
                        + "Chỉ chấp nhận: under30, 30to60, over60.")
                })
                .Distinct()
                .ToList();

            query.PlayTimeRanges = ranges;
        }

        if (!string.IsNullOrWhiteSpace(request.SearchKeyword))
        {
            query.SearchTerm = request.SearchKeyword;
        }

        return query;
    }

    private double CalculateMatchScore(
        GameTemplate game,
        BoardGameSurveyRequestDto request,
        int effectivePlayerCount)
    {
        double score = 0;

        // ── Category Match: +40 pts ──────────────────────────────────
        if (request.CategoryIds is { Count: > 0 })
        {
            var gameCategoryIds = game.Categories
                .Select(gc => gc.CategoryId)
                .ToHashSet();

            if (request.CategoryIds.Any(id => gameCategoryIds.Contains(id)))
            {
                int matchCount = request.CategoryIds.Count(id => gameCategoryIds.Contains(id));
                score += 40.0 * matchCount / request.CategoryIds.Count;
            }
        }

        // ── Duration Match: +30 pts ──────────────────────────────────
        if (request.PreferredDurations is { Count: > 0 })
        {
            var ranges = request.PreferredDurations
                .Select(d => d.ToLowerInvariant() switch
                {
                    "under30" or "under_30" or "short" => PlayTimeRange.Under30,
                    "30to60" or "30_to_60" or "medium" => PlayTimeRange.ThirtyToSixty,
                    "over60" or "over_60" or "long" => PlayTimeRange.Over60,
                    _ => PlayTimeRange.Under30
                })
                .Distinct()
                .ToList();

            bool durationMatch = ranges.Any(r => r switch
            {
                PlayTimeRange.Under30 => game.PlayTime < 30,
                PlayTimeRange.ThirtyToSixty => game.PlayTime >= 30 && game.PlayTime <= 60,
                PlayTimeRange.Over60 => game.PlayTime > 60,
                _ => false
            });

            if (durationMatch) score += 30;
        }

        // ── Player Range Fit: +30 pts ────────────────────────────────
        bool fitsExactly = game.MinPlayers <= effectivePlayerCount
            && game.MaxPlayers >= effectivePlayerCount;

        if (fitsExactly)
        {
            double playerRangeSpan = game.MaxPlayers - game.MinPlayers;
            if (playerRangeSpan <= 2)
                score += 30; // tight fit (e.g. 3-4 players)
            else if (playerRangeSpan <= 4)
                score += 20;
            else
                score += 10;
        }

        // ── Experience Level Bonus: up to +25 pts ───────────────────
        if (request.ExperienceLevel.HasValue)
        {
            score += CalculateExperienceBonus(game, request.ExperienceLevel.Value);
        }

        // ── Normalize: cap at 100 ──────────────────────────────────
        return Math.Min(score, 100);
    }

    private double CalculateExperienceBonus(
        GameTemplate game,
        PlayerExperienceLevel level)
    {
        return level switch
        {
            // Beginner: reward short & easy games
            PlayerExperienceLevel.Beginner => CalculateBeginnerBonus(game),

            // Casual: balanced, no heavy punishment
            PlayerExperienceLevel.Casual => CalculateCasualBonus(game),

            // Regular: reward variety and strategic depth
            PlayerExperienceLevel.Regular => CalculateRegularBonus(game),

            // Expert: reward depth, complexity, competition
            PlayerExperienceLevel.Expert => CalculateExpertBonus(game),

            _ => 0
        };
    }

    private double CalculateBeginnerBonus(GameTemplate game)
    {
        double bonus = 0;

        if (game.PlayTime <= 60)
            bonus += 15;
        else if (game.PlayTime <= 90)
            bonus += 8;

        if (game.MinPlayers >= 2 && game.MaxPlayers <= 6)
            bonus += 10;
        else if (game.MinPlayers >= 2 && game.MaxPlayers <= 8)
            bonus += 5;

        var categorySlugs = game.Categories
            .Select(gc => gc.Category.Slug)
            .ToHashSet();

        if (categorySlugs.Contains("giai-tri") || categorySlugs.Contains("hop-tac"))
            bonus += 10;

        if (categorySlugs.Contains("doi-khang"))
            bonus -= 5;

        return Math.Max(0, bonus);
    }

    private double CalculateCasualBonus(GameTemplate game)
    {
        double bonus = 0;

        if (game.PlayTime <= 90)
            bonus += 12;
        else if (game.PlayTime <= 120)
            bonus += 6;

        if (game.MinPlayers >= 2 && game.MaxPlayers <= 8)
            bonus += 10;

        var categorySlugs = game.Categories
            .Select(gc => gc.Category.Slug)
            .ToHashSet();

        if (categorySlugs.Contains("giai-tri"))
            bonus += 8;
        if (categorySlugs.Contains("an-vai"))
            bonus += 5;
        if (game.IsTournamentSupported)
            bonus += 5;

        return Math.Max(0, bonus);
    }

    private double CalculateRegularBonus(GameTemplate game)
    {
        double bonus = 0;

        if (game.PlayTime >= 30 && game.PlayTime <= 120)
            bonus += 12;
        else if (game.PlayTime > 120)
            bonus += 6;

        var categorySlugs = game.Categories
            .Select(gc => gc.Category.Slug)
            .ToHashSet();

        if (categorySlugs.Contains("chien-thuat"))
            bonus += 12;
        if (categorySlugs.Contains("doi-khang"))
            bonus += 10;
        if (categorySlugs.Contains("phieu-luu"))
            bonus += 8;
        if (game.IsTournamentSupported)
            bonus += 10;

        return Math.Max(0, bonus);
    }

    private double CalculateExpertBonus(GameTemplate game)
    {
        double bonus = 0;

        if (game.IsTournamentSupported)
            bonus += 20;

        if (game.PlayTime > 60)
            bonus += 12;
        else if (game.PlayTime > 30)
            bonus += 6;

        if (game.MaxPlayers >= 4)
            bonus += 10;

        var categorySlugs = game.Categories
            .Select(gc => gc.Category.Slug)
            .ToHashSet();

        if (categorySlugs.Contains("doi-khang"))
            bonus += 15;
        if (categorySlugs.Contains("phieu-luu"))
            bonus += 10;
        if (categorySlugs.Contains("an-vai"))
            bonus += 5;

        return Math.Max(0, bonus);
    }

    private async Task<List<string>> GetCategoryNamesAsync(
        List<Guid>? categoryIds,
        CancellationToken cancellationToken)
    {
        if (categoryIds is not { Count: > 0 })
            return [];

        var categories = await _categoryRepository.GetAllActiveAsync(cancellationToken);
        return categories
            .Where(c => categoryIds.Contains(c.Id))
            .Select(c => c.Name)
            .ToList();
    }

    private static NearbyCafeForGameDto MapToNearestCafe(NearbyCafeDto cafe)
    {
        return new NearbyCafeForGameDto
        {
            CafeId = cafe.Id,
            CafeName = cafe.Name,
            CafeAddress = cafe.Address,
            DistanceKm = cafe.DistanceMeters / 1000.0,
            Latitude = cafe.Latitude,
            Longitude = cafe.Longitude,
            AvailableGamesCount = cafe.AvailableGameCount,
            HasOpenLobby = false
        };
    }
}
