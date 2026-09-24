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
    private readonly IActiveSessionRepository _activeSessionRepository;

    public BoardGameDiscoveryService(
        IGameTemplateRepository gameTemplateRepository,
        IPlayerBoardGameSaveRepository saveRepository,
        ICategoryRepository categoryRepository,
        ICafeService cafeService,
        ILobbyService lobbyService,
        IActiveSessionRepository activeSessionRepository)
    {
        _gameTemplateRepository = gameTemplateRepository;
        _saveRepository = saveRepository;
        _categoryRepository = categoryRepository;
        _cafeService = cafeService;
        _lobbyService = lobbyService;
        _activeSessionRepository = activeSessionRepository;
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

        // ── NEW: Extract user preferences from saved games ────────────
        UserGamePreference? userPref = null;
        if (userId.HasValue)
        {
            userPref = await ExtractPreferencesFromSavedGames(userId.Value, cancellationToken);
        }

        var query = BuildSurveyQuery(request, effectivePlayerCount);
        var result = await _gameTemplateRepository.GetBoardGamesPagedAsync(query, cancellationToken);
        var games = result.Data.ToList();

        // ── Post-filter by WeightRanges if multi-select ──────────────
        if (request.WeightRanges is { Count: > 0 })
        {
            games = games.Where(g => g.Weight.HasValue && MatchesAnyWeightRange(g.Weight.Value, request.WeightRanges))
                .ToList();
        }

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
                effectivePlayerCount,
                userPref);  // ← Pass user preference
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
                Weight = game.Weight,
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
                    new PaginationParams { PageNumber = 1, PageSize = 1 },
                    cancellationToken);

                var firstCafe = cafeResult.Cafes.Data?.FirstOrDefault();
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

    public async Task<GroupDiscoveryResponseDto> GroupDiscoveryAsync(
        GroupDiscoveryRequestDto request,
        double? latitude,
        double? longitude,
        CancellationToken cancellationToken = default)
    {
        // ── Validation ──────────────────────────────────────────────
        if (request.Members is not { Count: > 0 })
        {
            throw new BadRequestException(ApiErrorMessages.Discovery.GroupMembersRequired);
        }

        if (request.Members.Count > 4)
        {
            throw new BadRequestException(ApiErrorMessages.Discovery.GroupTooManySubGroups);
        }

        var totalPlayerCount = request.Members.Sum(m => m.PlayerCount);
        if (totalPlayerCount < 1 || totalPlayerCount > 20)
        {
            throw new BadRequestException(
                ApiErrorMessages.Discovery.GroupPlayerCountInvalid(totalPlayerCount));
        }

        // ── NEW: Extract preferences for each member with userId ───
        var memberPreferences = new Dictionary<int, UserGamePreference?>();

        for (int i = 0; i < request.Members.Count; i++)
        {
            var member = request.Members[i];

            if (member.UserId.HasValue)
            {
                memberPreferences[i] = await ExtractPreferencesFromSavedGames(
                    member.UserId.Value,
                    cancellationToken);
            }
            else
            {
                memberPreferences[i] = null;
            }
        }

        // ── Load candidate games ───────────────────────────────────
        var query = new GetMasterGamesQuery
        {
            PageSize = 20,
            PageNumber = 1
        };
        var result = await _gameTemplateRepository.GetBoardGamesPagedAsync(query, cancellationToken);
        var games = result.Data.ToList();

        if (games.Count == 0)
        {
            return new GroupDiscoveryResponseDto
            {
                Games = [],
                TotalPlayerCount = totalPlayerCount,
                SubGroupCount = request.Members.Count
            };
        }

        // ── AWM Scoring: calculate aggregate score per game ────────
        var scoredGames = games.Select(game =>
        {
            var subgroupScores = new List<SubGroupScoreDto>();

            foreach (var member in request.Members)
            {
                int memberIndex = request.Members.IndexOf(member);
                var userPref = memberPreferences.GetValueOrDefault(memberIndex);

                var (score, reason) = CalculateSubGroupScore(game, member, userPref);
                subgroupScores.Add(new SubGroupScoreDto
                {
                    SubGroupIndex = memberIndex,
                    PlayerCount = member.PlayerCount,
                    ExperienceLevel = member.ExperienceLevel,
                    Score = score,
                    Reason = reason
                });
            }

            // AWMS = weighted mean — nhân theo số người mỗi sub-group
            double totalWeight = request.Members.Sum(m => m.PlayerCount);
            double weightedSum = subgroupScores
                .Zip(request.Members, (score, member) => score.Score * member.PlayerCount)
                .Sum();
            double aggregateScore = totalWeight > 0 ? weightedSum / totalWeight : 0;

            int satisfiedCount = subgroupScores.Count(s => s.Score >= 50);

            // Top reason: lấy reason của sub-group có điểm cao nhất
            var topSubGroup = subgroupScores.OrderByDescending(s => s.Score).First();
            string? topReason = topSubGroup.Reason;

            return new
            {
                Game = game,
                AggregateScore = Math.Min(aggregateScore, 100),
                SatisfiedSubGroups = satisfiedCount,
                TotalSubGroups = request.Members.Count,
                SubGroupBreakdown = subgroupScores,
                TopMatchReason = topReason
            };
        })
        .OrderByDescending(x => x.AggregateScore)
        .ThenBy(x => x.Game.Name)
        .ToList();

        var responseGames = scoredGames.Select(x =>
        {
            var game = x.Game;
            return new GroupDiscoveryBoardGameDto
            {
                Id = game.Id,
                Name = game.Name,
                ThumbnailUrl = game.ThumbnailUrl,
                Description = game.Description,
                MinPlayers = game.MinPlayers,
                MaxPlayers = game.MaxPlayers,
                PlayTimeMinutes = game.PlayTime,
                Weight = game.Weight,
                Categories = game.Categories.Select(gc => gc.Category.Name).ToList(),
                AggregateScore = Math.Round(x.AggregateScore, 1),
                SatisfiedSubGroups = x.SatisfiedSubGroups,
                TotalSubGroups = x.TotalSubGroups,
                SubGroupBreakdown = x.SubGroupBreakdown,
                TopMatchReason = x.TopMatchReason,
                HasOpenLobby = false
            };
        }).ToList();

        // ── Nearest cafe for top game ─────────────────────────────
        NearbyCafeForGameDto? nearestCafe = null;
        if (latitude.HasValue && longitude.HasValue && responseGames.Count > 0)
        {
            var topGameId = responseGames.First().Id;
            var radiusKm = request.RadiusKm ?? 15;

            var cafeResult = await _cafeService.GetNearbyCafesAsync(
                latitude.Value,
                longitude.Value,
                radiusKm: radiusKm,
                gameTemplateId: topGameId,
                name: null,
                new PaginationParams { PageNumber = 1, PageSize = 1 },
                cancellationToken);

            var firstCafe = cafeResult.Cafes.Data?.FirstOrDefault();
            if (firstCafe != null)
            {
                nearestCafe = MapToNearestCafe(firstCafe);
            }
        }

        return new GroupDiscoveryResponseDto
        {
            Games = responseGames,
            TotalPlayerCount = totalPlayerCount,
            SubGroupCount = request.Members.Count,
            NearestCafe = nearestCafe
        };
    }

    /// <summary>
    /// Tính individual match score cho một game đối với một sub-group preference.
    /// Trả về (score, reason) — score 0-100, reason là string mô tả lý do.
    /// </summary>
    private (double Score, string? Reason) CalculateSubGroupScore(
        GameTemplate game,
        MemberPreferenceDto member,
        UserGamePreference? userPref = null)
    {
        double score = 0;
        var reasons = new List<string>();

        int effectivePlayerCount = member.PlayerCount == 5 ? 5 : member.PlayerCount;

        // ── Player Range Fit: +30 pts ────────────────────────────
        bool fitsExactly = game.MinPlayers <= effectivePlayerCount
            && game.MaxPlayers >= effectivePlayerCount;

        if (fitsExactly)
        {
            double span = game.MaxPlayers - game.MinPlayers;
            if (span <= 2)
            {
                score += 30;
                reasons.Add($"Vừa vặn {effectivePlayerCount} người (game hỗ trợ {game.MinPlayers}-{game.MaxPlayers})");
            }
            else if (span <= 4)
            {
                score += 20;
                reasons.Add($"Chơi được {effectivePlayerCount} người (game hỗ trợ {game.MinPlayers}-{game.MaxPlayers})");
            }
            else
            {
                score += 10;
                reasons.Add($"Game hỗ trợ {game.MinPlayers}-{game.MaxPlayers} người, nhóm {effectivePlayerCount}");
            }
        }
        else
        {
            reasons.Add($"Game chỉ hỗ trợ {game.MinPlayers}-{game.MaxPlayers} người, không phù hợp {effectivePlayerCount}");
        }

        // ── Category Match: +25 pts ──────────────────────────────
        if (member.CategoryIds is { Count: > 0 })
        {
            var gameCategoryIds = game.Categories
                .Select(gc => gc.CategoryId)
                .ToHashSet();

            int matchCount = member.CategoryIds.Count(id => gameCategoryIds.Contains(id));
            if (matchCount > 0)
            {
                double categoryScore = 25.0 * matchCount / member.CategoryIds.Count;
                score += categoryScore;
                var matchedCats = game.Categories
                    .Where(gc => member.CategoryIds.Contains(gc.CategoryId))
                    .Select(gc => gc.Category.Name)
                    .ToList();
                if (matchedCats.Count > 0)
                    reasons.Add($"Có thể loại: {string.Join(", ", matchedCats)}");
            }
        }

        // ── Duration Match: +20 pts ──────────────────────────────
        if (member.PreferredDurations is { Count: > 0 })
        {
            var ranges = member.PreferredDurations
                .Select(d => d.ToLowerInvariant() switch
                {
                    "under30" or "under_30" or "short" => PlayTimeRange.Under30,
                    "30to60" or "30_to_60" or "medium" => PlayTimeRange.ThirtyToSixty,
                    "over60" or "over_60" or "long" => PlayTimeRange.Over60,
                    _ => PlayTimeRange.Under30
                })
                .Distinct()
                .ToList();

            bool match = ranges.Any(r => r switch
            {
                PlayTimeRange.Under30 => game.PlayTime < 30,
                PlayTimeRange.ThirtyToSixty => game.PlayTime >= 30 && game.PlayTime <= 60,
                PlayTimeRange.Over60 => game.PlayTime > 60,
                _ => false
            });

            if (match)
            {
                score += 20;
                reasons.Add($"Thời gian {game.PlayTime} phút phù hợp");
            }
        }

        // ── Weight Range Match: +10 pts ──────────────────────────
        if (member.WeightRanges is { Count: > 0 } && game.Weight.HasValue)
        {
            bool matchesWeight = MatchesAnyWeightRange(game.Weight.Value, member.WeightRanges);
            if (matchesWeight)
            {
                score += 10;
                reasons.Add($"Weight {game.Weight:F1} phù hợp mức độ phức tạp mong muốn");
            }
        }

        // ── Experience Level + BGG Weight Bonus: +15 pts ──────────
        if (member.ExperienceLevel.HasValue)
        {
            var level = member.ExperienceLevel.Value;

            // Experience bonus (dùng lại logic hiện có)
            double expBonus = CalculateExperienceBonus(game, level);
            if (expBonus > 0)
            {
                score += expBonus;
                reasons.Add($"Phù hợp level {level}");
            }

            // BGG Weight bonus (dùng lại logic hiện có)
            if (game.Weight.HasValue)
            {
                double weightBonus = CalculateWeightBonus(game.Weight.Value, level);
                if (weightBonus > 0)
                {
                    score += weightBonus;
                    reasons.Add($"Weight {game.Weight:F1} phù hợp với {level}");
                }
            }
        }

        // ── NEW: User Preference Boost: +15 pts ──────────────────
        if (userPref != null)
        {
            double prefBoost = CalculatePreferenceBoost(game, userPref);
            if (prefBoost > 0)
            {
                score += prefBoost;
                reasons.Add($"Phù hợp với lịch sử chơi của bạn");
            }
        }

        // ── Build reason ──────────────────────────────────────────
        string? topReason = reasons.Count > 0
            ? reasons.OrderByDescending(r => r.Length).First()
            : "Game có thể chơi được";

        return (Math.Min(score, 100), topReason);
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
                    Weight = game.Weight,
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

    public async Task<BoardGameSaveResultDto> UnsaveGameAsync(
        Guid userId,
        Guid gameTemplateId,
        CancellationToken cancellationToken = default)
    {
        var exists = await _gameTemplateRepository.ExistsAsync(gameTemplateId, cancellationToken);
        if (!exists)
        {
            throw new BoardGameNotFoundException(
                ApiErrorMessages.BoardGame.MasterNotFound(gameTemplateId));
        }

        var existing = await _saveRepository.GetByUserAndGameAsync(
            userId, gameTemplateId, cancellationToken);
        if (existing == null)
        {
            throw new NotFoundException(
                ApiErrorMessages.Discovery.GameNotSaved(gameTemplateId));
        }

        await _saveRepository.DeleteAsync(userId, gameTemplateId, cancellationToken);
        await _saveRepository.SaveChangesAsync(cancellationToken);

        return new BoardGameSaveResultDto
        {
            GameTemplateId = gameTemplateId,
            IsSaved = false,
            SavedAt = null
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

        // ── Weight Range Filter: apply UNION of all selected ranges ──
        if (request.WeightRanges is { Count: > 0 })
        {
            // Note: GetMasterGamesQuery.WeightRange is single-value nullable.
            // For multi-select support, we need to modify the scoring logic
            // to handle multiple ranges client-side OR expand the query model.
            // Current workaround: we apply filtering in the scoring phase below.
            // Set the first range as hint (repository will still fetch broadly).
            query.WeightRange = request.WeightRanges.First();
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
        int effectivePlayerCount,
        UserGamePreference? userPref = null)
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

        // ── Weight Range Match: +10 pts ──────────────────────────────
        if (request.WeightRanges is { Count: > 0 } && game.Weight.HasValue)
        {
            bool matchesWeight = MatchesAnyWeightRange(game.Weight.Value, request.WeightRanges);
            if (matchesWeight)
            {
                score += 10;
            }
        }

        // ── Experience Level Bonus: up to +15 pts ───────────────────
        if (request.ExperienceLevel.HasValue)
        {
            score += CalculateExperienceBonus(game, request.ExperienceLevel.Value);
        }

        // ── BGG Weight Bonus: up to +15 pts ───────────────────────
        if (request.ExperienceLevel.HasValue && game.Weight.HasValue)
        {
            score += CalculateWeightBonus(game.Weight.Value, request.ExperienceLevel.Value);
        }

        // ── Saved Game Preference Boost: up to +15 pts ──────────────
        if (userPref != null)
        {
            score += CalculatePreferenceBoost(game, userPref);
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

    /// <summary>
    /// Tính BGG Weight bonus dựa trên độ khớp weight của game với ngưỡng lý tưởng của ExperienceLevel.
    /// +15 pts khi weight nằm trong khoảng lý tưởng (proximity-based).
    /// +7 pts khi weight nằm ở adjacent range.
    /// 0 pts khi weight quá xa.
    /// </summary>
    private double CalculateWeightBonus(double weight, PlayerExperienceLevel level)
    {
        // Map ExperienceLevel → ideal weight range (min, max)
        var (idealMin, idealMax) = level switch
        {
            PlayerExperienceLevel.Beginner => (1.0, 2.0),
            PlayerExperienceLevel.Casual   => (1.5, 2.5),
            PlayerExperienceLevel.Regular  => (2.0, 3.5),
            PlayerExperienceLevel.Expert   => (3.0, 5.0),
            _                             => (1.0, 5.0)
        };

        // Perfect match: weight nằm trong khoảng lý tưởng → proximity bonus
        if (weight >= idealMin && weight <= idealMax)
        {
            double midPoint = (idealMin + idealMax) / 2.0;
            double halfRange = (idealMax - idealMin) / 2.0;
            double proximity = 1.0 - (Math.Abs(weight - midPoint) / halfRange);
            return 15.0 * Math.Max(0.5, proximity); // tối thiểu 7.5 pts khi ở rìa
        }

        // Adjacent range (1 bước): +7 pts
        bool isAdjacent = level switch
        {
            PlayerExperienceLevel.Beginner => weight >= 2.0 && weight < 2.5,
            PlayerExperienceLevel.Casual   => weight >= 2.5 && weight < 3.0,
            PlayerExperienceLevel.Regular  => (weight >= 1.5 && weight < 2.0) || (weight >= 3.5 && weight < 4.0),
            PlayerExperienceLevel.Expert   => weight >= 2.5 && weight < 3.0,
            _                             => false
        };

        return isAdjacent ? 7.0 : 0.0;
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

    /// <summary>
    /// Kiểm tra weight của game có nằm trong ít nhất một trong các WeightRange đã chọn không.
    /// </summary>
    private static bool MatchesAnyWeightRange(double weight, List<WeightRange> ranges)
    {
        return ranges.Any(range => range switch
        {
            // Light: ≤2.0
            WeightRange.Light => weight <= 2.0,
            // Medium-Light: 2.01 – 3.0
            WeightRange.MediumLight => weight > 2.0 && weight <= 3.0,
            // Medium: 3.01 – 3.5
            WeightRange.Medium => weight > 3.0 && weight <= 3.5,
            // Medium-Heavy: 3.51 – 4.0
            WeightRange.MediumHeavy => weight > 3.5 && weight <= 4.0,
            // Heavy: >4.0
            WeightRange.Heavy => weight > 4.0,
            _ => false
        });
    }

    /// <summary>
    /// Extract user preferences from saved games for personalization.
    /// Returns null if user has less than 3 saved games.
    /// </summary>
    private async Task<UserGamePreference?> ExtractPreferencesFromSavedGames(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var saves = await _saveRepository.GetByUserAsync(userId, cancellationToken);

        if (saves.Count < 3)
            return null; // Not enough data to personalize

        var gameIds = saves.Select(s => s.GameTemplateId).ToList();
        var query = new GetMasterGamesQuery { PageSize = 100, PageNumber = 1 };
        var allGames = await _gameTemplateRepository.GetBoardGamesPagedAsync(query, cancellationToken);

        var savedGames = allGames.Data
            .Where(g => gameIds.Contains(g.Id))
            .ToList();

        if (savedGames.Count < 3)
            return null;

        // Extract top 3 categories
        var categoryFrequency = new Dictionary<Guid, int>();
        foreach (var game in savedGames)
        {
            foreach (var gc in game.Categories)
            {
                categoryFrequency[gc.CategoryId] = categoryFrequency.GetValueOrDefault(gc.CategoryId, 0) + 1;
            }
        }

        var topCategories = categoryFrequency
            .OrderByDescending(kv => kv.Value)
            .Take(3)
            .Select(kv => kv.Key)
            .ToList();

        // Calculate average weight
        var weights = savedGames
            .Where(g => g.Weight.HasValue)
            .Select(g => g.Weight!.Value)
            .ToList();

        double? avgWeight = weights.Count > 0 ? weights.Average() : null;

        // Calculate average duration
        double avgDuration = savedGames.Average(g => g.PlayTime);

        return new UserGamePreference
        {
            UserId = userId,
            TopCategoryIds = topCategories,
            AverageWeight = avgWeight,
            AverageDuration = avgDuration,
            SavedGameCount = saves.Count
        };
    }

    /// <summary>
    /// Calculate preference boost based on user's saved game patterns.
    /// Max +15 pts: +8 category, +4 weight, +3 duration.
    /// </summary>
    private double CalculatePreferenceBoost(
        GameTemplate game,
        UserGamePreference userPref)
    {
        double boost = 0;

        // Category affinity: +8 pts if game in user's top 3 categories
        var gameCategoryIds = game.Categories
            .Select(gc => gc.CategoryId)
            .ToHashSet();

        if (userPref.TopCategoryIds.Any(id => gameCategoryIds.Contains(id)))
        {
            boost += 8;
        }

        // Weight affinity: +4 pts if game weight close to user's average
        if (userPref.AverageWeight.HasValue && game.Weight.HasValue)
        {
            double weightDiff = Math.Abs(game.Weight.Value - userPref.AverageWeight.Value);

            if (weightDiff <= 0.5)
                boost += 4;
            else if (weightDiff <= 1.0)
                boost += 2;
        }

        // Duration affinity: +3 pts if game duration close to user's average
        double durationDiff = Math.Abs(game.PlayTime - userPref.AverageDuration);

        if (durationDiff <= 15)
            boost += 3;
        else if (durationDiff <= 30)
            boost += 1.5;

        return boost;
    }

    public async Task<SoloPersonalizedResponseDto> SoloPersonalizedDiscoveryAsync(
        Guid userId,
        SoloPersonalizedRequestDto request,
        double? latitude,
        double? longitude,
        CancellationToken cancellationToken = default)
    {
        // ── Step 1: Extract user preference profile ─────────────────
        var userPref = await ExtractPreferencesFromSavedGames(userId, cancellationToken);

        // ── Step 2: Build base query with filters ───────────────────
        var query = new GetMasterGamesQuery
        {
            PageSize = request.PageSize > 0 ? request.PageSize : 20,
            PageNumber = 1
        };

        if (request.PlayerCount.HasValue && request.PlayerCount.Value > 0)
        {
            int effectivePlayerCount = request.PlayerCount.Value == 5 ? 5 : request.PlayerCount.Value;
            query.PlayerCount = effectivePlayerCount;
        }

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

        if (request.WeightRanges is { Count: > 0 })
        {
            query.WeightRange = request.WeightRanges.First();
        }

        if (!string.IsNullOrWhiteSpace(request.SearchKeyword))
        {
            query.SearchTerm = request.SearchKeyword;
        }

        var result = await _gameTemplateRepository.GetBoardGamesPagedAsync(query, cancellationToken);
        var games = result.Data.ToList();

        // ── Step 3: Post-filter by WeightRanges (multi-select) ──────
        if (request.WeightRanges is { Count: > 0 })
        {
            games = games.Where(g => g.Weight.HasValue && MatchesAnyWeightRange(g.Weight.Value, request.WeightRanges))
                .ToList();
        }

        // ── Step 4: Get saved game IDs ───────────────────────────────
        var savedIds = (await _saveRepository.GetSavedGameTemplateIdsAsync(userId, cancellationToken)).ToHashSet();

        // ── Step 5: Exclude saved games if requested ─────────────────
        if (request.ExcludeSavedGames)
        {
            games = games.Where(g => !savedIds.Contains(g.Id)).ToList();
        }

        // ── Step 6: Get play history for penalty calculation ─────────
        var playHistory = await _activeSessionRepository.GetUserPlayHistoryAsync(
            userId, 
            daysBack: 30, 
            cancellationToken);
        
        var playHistoryDict = playHistory.ToDictionary(h => h.GameTemplateId, h => h.PlayCount);

        // ── Step 7: Calculate personalized scores ────────────────────
        var scoredGames = games.Select(game =>
        {
            double baseScore = CalculateSoloBaseScore(game, request);
            double personalizationBoost = userPref != null
                ? CalculatePreferenceBoost(game, userPref)
                : 0;

            // Cải tiến 3: Play history penalty
            double playHistoryPenalty = CalculatePlayHistoryPenalty(game.Id, playHistoryDict);

            double personalizedScore = Math.Min(baseScore + personalizationBoost + playHistoryPenalty, 100);

            string? matchReason = BuildSoloMatchReason(game, request, userPref, personalizationBoost);

            return new
            {
                Game = game,
                BaseScore = baseScore,
                PersonalizationBoost = personalizationBoost,
                PlayHistoryPenalty = playHistoryPenalty,
                PersonalizedScore = personalizedScore,
                MatchReason = matchReason
            };
        })
        .OrderByDescending(x => x.PersonalizedScore)
        .ThenBy(x => x.Game.Name)
        .ToList();

        // ── Step 8: Map to response DTOs ─────────────────────────────
        var responseGames = scoredGames.Select(x =>
        {
            var game = x.Game;
            return new PersonalizedBoardGameDto
            {
                Id = game.Id,
                Name = game.Name,
                ThumbnailUrl = game.ThumbnailUrl,
                Description = game.Description,
                MinPlayers = game.MinPlayers,
                MaxPlayers = game.MaxPlayers,
                PlayTimeMinutes = game.PlayTime,
                Weight = game.Weight,
                Categories = game.Categories.Select(gc => gc.Category.Name).ToList(),
                BaseScore = Math.Round(x.BaseScore, 1),
                PersonalizationBoost = Math.Round(x.PersonalizationBoost, 1),
                PlayHistoryPenalty = Math.Round(x.PlayHistoryPenalty, 1),
                PersonalizedScore = Math.Round(x.PersonalizedScore, 1),
                MatchReason = x.MatchReason,
                IsSaved = savedIds.Contains(game.Id),
                HasOpenLobby = false
            };
        }).ToList();

        // ── Step 8: Find nearest cafe for top game ──────────────────
        NearbyCafeForGameDto? nearestCafe = null;
        List<OpenLobbySummaryDto> openLobbies = [];

        if (responseGames.Count > 0 && latitude.HasValue && longitude.HasValue)
        {
            var topGameId = responseGames.First().Id;

            var cafeResult = await _cafeService.GetNearbyCafesAsync(
                latitude.Value,
                longitude.Value,
                radiusKm: 15,
                gameTemplateId: topGameId,
                name: null,
                new PaginationParams { PageNumber = 1, PageSize = 1 },
                cancellationToken);

            var firstCafe = cafeResult.Cafes.Data?.FirstOrDefault();
            if (firstCafe != null)
            {
                nearestCafe = MapToNearestCafe(firstCafe);
            }

            var lobbies = await _lobbyService.GetDiscoverableLobbiesAsync(
                topGameId,
                latitude,
                longitude,
                radiusKm: null,
                limit: 10,
                requestingUserId: userId,
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

            // Mark HasOpenLobby
            foreach (var g in responseGames)
            {
                if (openLobbies.Any(l => l.GameTemplateId == g.Id))
                {
                    g.HasOpenLobby = true;
                }
            }
        }

        return new SoloPersonalizedResponseDto
        {
            Games = responseGames,
            UserProfile = userPref,
            TotalCount = result.Meta.TotalItems,
            NearestCafe = nearestCafe,
            OpenLobbies = openLobbies
        };
    }

    /// <summary>
    /// Calculate base score for solo discovery without personalization.
    /// Max 85 pts (category 40 + duration 30 + player fit 15).
    /// </summary>
    private double CalculateSoloBaseScore(
        GameTemplate game,
        SoloPersonalizedRequestDto request)
    {
        double score = 0;

        // ── Category Match: +40 pts ──────────────────────────────────
        if (request.CategoryIds is { Count: > 0 })
        {
            var gameCategoryIds = game.Categories
                .Select(gc => gc.CategoryId)
                .ToHashSet();

            int matchCount = request.CategoryIds.Count(id => gameCategoryIds.Contains(id));
            if (matchCount > 0)
            {
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

        // ── Player Range Fit: +15 pts ────────────────────────────────
        if (request.PlayerCount.HasValue && request.PlayerCount.Value > 0)
        {
            int effectivePlayerCount = request.PlayerCount.Value == 5 ? 5 : request.PlayerCount.Value;
            bool fitsExactly = game.MinPlayers <= effectivePlayerCount
                && game.MaxPlayers >= effectivePlayerCount;

            if (fitsExactly)
            {
                double playerRangeSpan = game.MaxPlayers - game.MinPlayers;
                if (playerRangeSpan <= 2)
                    score += 15;
                else if (playerRangeSpan <= 4)
                    score += 10;
                else
                    score += 5;
            }
        }

        return Math.Min(score, 85);
    }

    /// <summary>
    /// Build match reason string for solo discovery.
    /// </summary>
    private string BuildSoloMatchReason(
        GameTemplate game,
        SoloPersonalizedRequestDto request,
        UserGamePreference? userPref,
        double personalizationBoost)
    {
        var reasons = new List<string>();

        // Personalization reason (highest priority if boost > 5)
        if (personalizationBoost > 5 && userPref != null)
        {
            var gameCategoryIds = game.Categories.Select(gc => gc.CategoryId).ToHashSet();
            if (userPref.TopCategoryIds.Any(id => gameCategoryIds.Contains(id)))
            {
                reasons.Add("Khớp với thể loại bạn thường chơi");
            }
            else if (userPref.AverageWeight.HasValue && game.Weight.HasValue)
            {
                double weightDiff = Math.Abs(game.Weight.Value - userPref.AverageWeight.Value);
                if (weightDiff <= 1.0)
                {
                    reasons.Add($"Độ phức tạp phù hợp với sở thích của bạn");
                }
            }
        }

        // Category match
        if (request.CategoryIds is { Count: > 0 })
        {
            var gameCategoryIds = game.Categories.Select(gc => gc.CategoryId).ToHashSet();
            var matchedCats = game.Categories
                .Where(gc => request.CategoryIds.Contains(gc.CategoryId))
                .Select(gc => gc.Category.Name)
                .ToList();

            if (matchedCats.Count > 0)
            {
                reasons.Add($"Thể loại: {string.Join(", ", matchedCats)}");
            }
        }

        // Duration match
        if (request.PreferredDurations is { Count: > 0 })
        {
            reasons.Add($"Thời gian {game.PlayTime} phút phù hợp");
        }

        // Player fit
        if (request.PlayerCount.HasValue && request.PlayerCount.Value > 0)
        {
            int effectivePlayerCount = request.PlayerCount.Value == 5 ? 5 : request.PlayerCount.Value;
            if (game.MinPlayers <= effectivePlayerCount && game.MaxPlayers >= effectivePlayerCount)
            {
                reasons.Add($"Chơi được {effectivePlayerCount} người");
            }
        }

        return reasons.Count > 0
            ? reasons.First()
            : "Game phù hợp với bạn";
    }

    /// <summary>
    /// Cải tiến 3 - Play History Influence: Tính penalty cho games đã chơi nhiều lần gần đây.
    /// Logic:
    /// - 1 lần chơi: -5 điểm
    /// - 2 lần chơi: -10 điểm
    /// - 3 lần chơi: -15 điểm
    /// - 4+ lần chơi: -20 điểm (max penalty)
    /// 
    /// Mục đích: Khuyến khích đa dạng hóa trải nghiệm, tránh lặp lại game quá nhiều.
    /// </summary>
    private double CalculatePlayHistoryPenalty(Guid gameTemplateId, Dictionary<Guid, int> playHistoryDict)
    {
        if (!playHistoryDict.TryGetValue(gameTemplateId, out int playCount))
        {
            return 0; // Chưa chơi game này → không penalty
        }

        // Progressive penalty
        return playCount switch
        {
            1 => -5.0,
            2 => -10.0,
            3 => -15.0,
            _ => -20.0  // 4+ times
        };
    }
}
