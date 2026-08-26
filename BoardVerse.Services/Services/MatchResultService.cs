using BoardVerse.Core.Data;
using BoardVerse.Core.DTOs.Match;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Helpers;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services
{
    public class MatchResultService : IMatchResultService
    {
        private static readonly IReadOnlyList<MatchOutcomeOptionDto> OutcomeOptions =
        [
            new() { Outcome = MatchOutcome.Win, Label = "Thắng" },
            new() { Outcome = MatchOutcome.Loss, Label = "Thua" },
            new() { Outcome = MatchOutcome.Draw, Label = "Hòa" }
        ];

        private readonly IMatchResultRepository _matchResultRepository;
        private readonly ISystemConfigurationProvider _systemConfigurationProvider;
        private readonly BoardVerseDbContext _db;
        private readonly ILogger<MatchResultService> _logger;

        public MatchResultService(
            IMatchResultRepository matchResultRepository,
            ISystemConfigurationProvider systemConfigurationProvider,
            BoardVerseDbContext db,
            ILogger<MatchResultService> logger)
        {
            _matchResultRepository = matchResultRepository;
            _systemConfigurationProvider = systemConfigurationProvider;
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Backward-compatible constructor dùng cho unit tests cũ (không pass BoardVerseDbContext).
        /// </summary>
        public MatchResultService(
            IMatchResultRepository matchResultRepository,
            ISystemConfigurationProvider systemConfigurationProvider,
            ILogger<MatchResultService> logger)
            : this(matchResultRepository, systemConfigurationProvider, db: null!, logger: logger)
        {
        }

        /// <summary>
        /// Backward-compatible constructor dùng cho unit tests cũ (không pass logger).
        /// </summary>
        public MatchResultService(
            IMatchResultRepository matchResultRepository,
            ISystemConfigurationProvider systemConfigurationProvider)
            : this(matchResultRepository, systemConfigurationProvider, db: null!, logger: null!)
        {
        }

        public async Task<MatchResultStatusDto> GetMatchResultStatusAsync(Guid userId, Guid lobbyId, CancellationToken cancellationToken = default)
        {
            var lobby = await RequireEligibleLobbyAsync(userId, lobbyId);
            var supportsMatch = await _matchResultRepository.GameSupportsMatchResultsAsync(lobby.GameTemplateId);
            var finalized = await _matchResultRepository.GetFinalizedHistoryAsync(lobbyId);
            var submissions = await _matchResultRepository.GetSubmissionsAsync(lobbyId);
            var submissionLookup = submissions.ToDictionary(s => s.UserId, s => s.Outcome);
            var requiredCount = lobby.Members.Count(m => m.IsActive);

            if (finalized != null)
            {
                return BuildStatusDto(
                    lobby,
                    supportsMatch,
                    MatchConsensusStatus.Finalized,
                    submissions.Count,
                    requiredCount,
                    null,
                    finalized.Id,
                    finalized.WinnerUserId,
                    finalized.IsDraw,
                    userId,
                    submissionLookup);
            }

            var evaluation = MatchConsensusHelper.Evaluate(
                submissions.Select(s => (s.UserId, s.Outcome)).ToList(),
                requiredCount);

            return BuildStatusDto(
                lobby,
                supportsMatch,
                evaluation.Status,
                submissions.Count,
                requiredCount,
                evaluation.ConflictReason,
                null,
                evaluation.WinnerUserId,
                evaluation.IsDraw ? true : null,
                userId,
                submissionLookup);
        }

        public async Task<SubmitMatchResultResponseDto> SubmitMatchResultAsync(
            Guid userId,
            SubmitMatchResultRequestDto request, CancellationToken cancellationToken = default)
        {
            if (!System.Enum.IsDefined(typeof(MatchOutcome), request.Outcome))
            {
                throw new BadRequestException(ApiErrorMessages.Match.InvalidOutcomeValue);
            }

            var lobby = await RequireEligibleLobbyAsync(userId, request.LobbyId);

            if (!await _matchResultRepository.GameSupportsMatchResultsAsync(lobby.GameTemplateId))
            {
                throw new BadRequestException(
                    ApiErrorMessages.Match.GameNotCompetitive(lobby.GameTemplateId));
            }

            if (await _matchResultRepository.GetFinalizedHistoryAsync(request.LobbyId) != null)
            {
                throw new ConflictException(ApiErrorMessages.Match.MatchAlreadyFinalized(request.LobbyId));
            }

            var now = DateTime.UtcNow;
            var existing = await _matchResultRepository.GetSubmissionAsync(request.LobbyId, userId);
            if (existing == null)
            {
                await _matchResultRepository.AddSubmissionAsync(new MatchResult
                {
                    Id = Guid.NewGuid(),
                    LobbyId = request.LobbyId,
                    UserId = userId,
                    Outcome = request.Outcome,
                    SubmittedAt = now,
                    UpdatedAt = now
                });
            }
            else
            {
                existing.Outcome = request.Outcome;
                existing.UpdatedAt = now;
            }

            await _matchResultRepository.SaveChangesAsync();

            var submissions = await _matchResultRepository.GetSubmissionsAsync(request.LobbyId);
            var requiredCount = lobby.Members.Count(m => m.IsActive);
            var evaluation = MatchConsensusHelper.Evaluate(
                submissions.Select(s => (s.UserId, s.Outcome)).ToList(),
                requiredCount);

            if (evaluation.Status != MatchConsensusStatus.Finalized)
            {
                return new SubmitMatchResultResponseDto
                {
                    LobbyId = request.LobbyId,
                    ConsensusStatus = evaluation.Status,
                    SubmittedCount = submissions.Count,
                    RequiredCount = requiredCount,
                    ConflictReason = evaluation.ConflictReason
                };
            }

            return await FinalizeMatchAsync(lobby, submissions, evaluation);
        }

    private async Task<SubmitMatchResultResponseDto> FinalizeMatchAsync(
        Lobby lobby,
        IReadOnlyList<MatchResult> submissions,
        MatchConsensusEvaluation evaluation)
    {
        // GAP-R6-TW Fix: wrap entire finalize flow trong Serializable transaction.
        // Trước đây: 2 user submit cùng lúc đều thấy "Finalized == null", cả 2 đều vào FinalizeMatchAsync,
        // cả 2 tính Elo delta + cộng vào profile.GlobalElo → Elo bị apply 2 lần (double-elo bug).
        // Fix: BeginTransactionAsync Serializable + SaveChanges duy nhất 1 lần cuối + atomic re-check.
        await using var tx = await _db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable);

        // Re-check guard SAU khi acquire transaction — chặn 2nd finalizer.
        var existingHistory = await _matchResultRepository.GetFinalizedHistoryAsync(lobby.Id);
        if (existingHistory != null)
        {
            _logger.LogInformation(
                "FinalizeMatchAsync: lobby {LobbyId} already finalized (id={HistoryId}) — replay",
                lobby.Id, existingHistory.Id);
            await tx.RollbackAsync();

            // Reuse existing finalization — không tính lại Elo.
            return BuildReplayDto(lobby, submissions, existingHistory);
        }

        var memberIds = lobby.Members.Where(m => m.IsActive).Select(m => m.UserId).ToList();
        var ratings = new Dictionary<Guid, int>();
        var profiles = new Dictionary<Guid, UserProfile>();

        foreach (var memberId in memberIds)
        {
            var profile = await _matchResultRepository.GetProfileForUpdateAsync(memberId);
            if (profile == null)
            {
                throw new NotFoundException(ApiErrorMessages.Match.ProfileMissing(memberId));
            }

            profiles[memberId] = profile;
            ratings[memberId] = profile.GlobalElo <= 0 ? EloRatingHelper.DefaultRating : profile.GlobalElo;
        }

        var configuredK = await _systemConfigurationProvider.GetIntAsync(
            SystemConfigKeys.EloKFactor,
            32);

        var eloDeltas = EloRatingHelper.CalculateRatingChanges(
            ratings,
            evaluation.WinnerUserId,
            evaluation.IsDraw,
            configuredK);

        var historyId = Guid.NewGuid();
        var history = new MatchHistory
        {
            Id = historyId,
            LobbyId = lobby.Id,
            GameTemplateId = lobby.GameTemplateId,
            Status = MatchConsensusStatus.Finalized,
            WinnerUserId = evaluation.WinnerUserId,
            IsDraw = evaluation.IsDraw,
            FinalizedAt = DateTime.UtcNow
        };

        var eloUpdates = new List<MatchEloUpdateDto>();

        foreach (var submission in submissions)
        {
            var profile = profiles[submission.UserId];
            var eloBefore = ratings[submission.UserId];
            var delta = eloDeltas[submission.UserId];
            var eloAfter = Math.Max(EloRatingHelper.MinimumRating, eloBefore + delta);

            profile.GlobalElo = eloAfter;
            profile.UpdatedAt = DateTime.UtcNow;

            history.Participants.Add(new MatchHistoryParticipant
            {
                Id = Guid.NewGuid(),
                MatchHistoryId = historyId,
                UserId = submission.UserId,
                ReportedOutcome = submission.Outcome,
                EloBefore = eloBefore,
                EloAfter = eloAfter,
                EloDelta = delta
            });

            eloUpdates.Add(new MatchEloUpdateDto
            {
                UserId = submission.UserId,
                ReportedOutcome = submission.Outcome,
                EloBefore = eloBefore,
                EloAfter = eloAfter,
                EloDelta = delta
            });
        }

        await _matchResultRepository.AddMatchHistoryAsync(history);

        // GAP-R6-TW Fix: SaveChangesAsync với CT default trước CommitAsync.
        // Structural integrity: chỉ 1 SaveChangesAsync duy nhất, commit, return.
        // (Trước đây SaveChangesAsync() không truyền CT + thiếu try/catch wrap.)
        try
        {
            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.LogInformation(
                "Finalized match. LobbyId={LobbyId}, HistoryId={HistoryId}, Winner={Winner}, Draw={IsDraw}",
                lobby.Id, historyId, evaluation.WinnerUserId, evaluation.IsDraw);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        return new SubmitMatchResultResponseDto
        {
            LobbyId = lobby.Id,
            ConsensusStatus = MatchConsensusStatus.Finalized,
            SubmittedCount = submissions.Count,
            RequiredCount = memberIds.Count,
            MatchHistoryId = historyId,
            EloUpdates = eloUpdates
        };
    }

    /// <summary>
    /// GAP-R6-TW Fix: helper để build replay DTO khi lobby đã finalized (2nd finalizer race).
    /// Trả về DTO giống FinalizeMatchAsync success path nhưng với EloUpdates empty.
    /// </summary>
    private static SubmitMatchResultResponseDto BuildReplayDto(
        Lobby lobby,
        IReadOnlyList<MatchResult> submissions,
        MatchHistory existing)
    {
        return new SubmitMatchResultResponseDto
        {
            LobbyId = lobby.Id,
            ConsensusStatus = MatchConsensusStatus.Finalized,
            SubmittedCount = submissions.Count,
            RequiredCount = lobby.Members.Count(m => m.IsActive),
            MatchHistoryId = existing.Id,
            EloUpdates = [] // replay → không có delta mới
        };
    }

        private async Task<Lobby> RequireEligibleLobbyAsync(Guid userId, Guid lobbyId)
        {
            var lobby = await _matchResultRepository.GetLobbyForMatchAsync(lobbyId);
            if (lobby == null)
            {
                throw new NotFoundException(ApiErrorMessages.Match.LobbyNotFound(lobbyId));
            }

            if (!lobby.Members.Any(m => m.IsActive && m.UserId == userId))
            {
                throw new ForbiddenException(ApiErrorMessages.Match.NotLobbyMember(lobbyId, userId));
            }

            if (lobby.Status is LobbyStatus.Open or LobbyStatus.Full)
            {
                throw new BadRequestException(ApiErrorMessages.Match.LobbyNotEligible(lobbyId));
            }

            return lobby;
        }

        private static MatchResultStatusDto BuildStatusDto(
            Lobby lobby,
            bool supportsMatch,
            MatchConsensusStatus status,
            int submittedCount,
            int requiredCount,
            string? conflictReason,
            Guid? matchHistoryId,
            Guid? winnerUserId,
            bool? isDraw,
            Guid currentUserId,
            IReadOnlyDictionary<Guid, MatchOutcome> submissionLookup)
        {
            return new MatchResultStatusDto
            {
                LobbyId = lobby.Id,
                GameTemplateId = lobby.GameTemplateId,
                GameName = lobby.GameTemplate.Name,
                SupportsMatchResults = supportsMatch,
                ConsensusStatus = status,
                SubmittedCount = submittedCount,
                RequiredCount = requiredCount,
                ConflictReason = conflictReason,
                MatchHistoryId = matchHistoryId,
                WinnerUserId = winnerUserId,
                IsDraw = isDraw,
                AvailableOutcomes = supportsMatch ? OutcomeOptions : [],
                Submissions = lobby.Members
                    .Where(m => m.IsActive)
                    .Select(m => new MatchMemberSubmissionDto
                    {
                        UserId = m.UserId,
                        Username = m.User.Username,
                        Outcome = submissionLookup.TryGetValue(m.UserId, out var outcome) ? outcome : null,
                        IsCurrentUser = m.UserId == currentUserId
                    })
                    .OrderBy(m => m.Username)
                    .ToList()
            };
        }
    }
}
