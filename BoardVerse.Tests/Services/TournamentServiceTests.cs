using BoardVerse.Core.Data;
using BoardVerse.Core.DTOs.Tournament;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Helpers;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho <see cref="TournamentService"/>.
/// Tập trung các luồng nghiệp vụ cốt lõi:
/// - Đăng ký (Karma gate, dup-check, capacity check)
/// - Rút lui (state guard)
/// - Check-in (state guard)
/// - No-show (Karma penalty + KarmaLog audit)
/// - Cancel (state guard + reason-required nếu có người đăng ký)
/// - Advance round (state guard + current round completed check)
/// </summary>
public class TournamentServiceTests
{
    private static readonly Guid TournamentId = Guid.Parse("aaaa1111-1111-1111-1111-111111111111");
    private static readonly Guid CafeId = Guid.Parse("aaaa2222-2222-2222-2222-222222222222");
    private static readonly Guid ManagerId = Guid.Parse("aaaa3333-3333-3333-3333-333333333333");
    private static readonly Guid UserId = Guid.Parse("aaaa4444-4444-4444-4444-444444444444");
    private static readonly Guid OtherUserId = Guid.Parse("aaaa5555-5555-5555-5555-555555555555");
    private static readonly Guid ParticipantId = Guid.Parse("aaaa6666-6666-6666-6666-666666666666");
    private static readonly Guid GameId = Guid.Parse("aaaa7777-7777-7777-7777-777777777777");
    private static readonly Guid SplendorId = Guid.Parse("aaaa8888-8888-8888-8888-888888888888");

    private static Mock<ITournamentRepository> BuildTournamentRepo() => new(MockBehavior.Strict);
    private static Mock<IGameTemplateRepository> BuildGameRepo() => new(MockBehavior.Strict);
    private static Mock<ICafePosRepository> BuildCafeRepo() => new(MockBehavior.Strict);
    private static Mock<IUserProfileRepository> BuildUserRepo() => new(MockBehavior.Strict);
    private static Mock<ISystemConfigurationProvider> BuildConfigRepo() => new(MockBehavior.Strict);
    private static Mock<IKarmaRatingRepository> BuildKarmaRepo() => new(MockBehavior.Strict);
    private static Mock<ILogger<TournamentService>> BuildLogger() => new(MockBehavior.Loose);

    private static TournamentService BuildService(
        Mock<ITournamentRepository> tournamentRepo,
        Mock<IGameTemplateRepository> gameRepo,
        Mock<ICafePosRepository> cafeRepo,
        Mock<IUserProfileRepository> userRepo,
        Mock<ISystemConfigurationProvider> configRepo,
        Mock<IKarmaRatingRepository> karmaRepo)
        => new(tournamentRepo.Object, gameRepo.Object, cafeRepo.Object,
               userRepo.Object, configRepo.Object, karmaRepo.Object, BuildLogger().Object);

    // ============================================
    // === Tournament lifecycle (Manager POS) ===
    // ============================================

    [Fact]
    public async Task CreateTournamentAsync_ManagerOwnsCafeAndSplendor_CreatesDraft()
    {
        // Arrange
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var splendor = new GameTemplate
        {
            Id = SplendorId,
            Name = "Splendor",
            IsActive = true,
            IsTournamentSupported = true,
            TournamentMaxScorePerPlayer = 15,
            TournamentMinPlayersPerTable = 2
        };

        gameRepo.Setup(r => r.GetByNameAsync("Splendor")).ReturnsAsync(splendor);
        gameRepo.Setup(r => r.GetByIdAsync(SplendorId)).ReturnsAsync(splendor);
        cafeRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        tournamentRepo.Setup(r => r.AddAsync(It.IsAny<Tournament>())).Returns(Task.CompletedTask);
        tournamentRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
        tournamentRepo.Setup(r => r.GetByIdWithDetailsAsync(It.IsAny<Guid>()))
            .ReturnsAsync(BuildRegistrationOpenTournament());

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        // Act
        var result = await svc.CreateTournamentAsync(ManagerId, CafeId, new CreateTournamentRequestDto
        {
            Title = "Splendor Cup Thủ Đức - August 2026",
            StartTime = DateTime.UtcNow.AddDays(7),
            RoundDurationMinutes = 45,
            MaxParticipants = 8,
            WinnerKarmaBonus = 50,
            FinalistKarmaBonus = 20,
            NoShowKarmaPenalty = -30
        });

        // Assert
        Assert.NotNull(result);
        tournamentRepo.Verify(r => r.AddAsync(It.Is<Tournament>(
            t => t.Status == TournamentStatus.Draft && t.MaxParticipants == 8)), Times.Once);
    }

    [Fact]
    public async Task CreateTournamentAsync_ManagerDoesNotOwnCafe_ThrowsForbidden()
    {
        // Arrange
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        cafeRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(false);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        // Act & Assert
        await Assert.ThrowsAsync<ForbiddenException>(
            () => svc.CreateTournamentAsync(ManagerId, CafeId, ValidCreateRequest()));
    }

    [Fact]
    public async Task CreateTournamentAsync_SplendorGameNotFound_ThrowsConfigurationMissing()
    {
        // Arrange
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        cafeRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        gameRepo.Setup(r => r.GetByNameAsync("Splendor")).ReturnsAsync((GameTemplate?)null);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        // Act & Assert
        await Assert.ThrowsAsync<ConfigurationMissingException>(
            () => svc.CreateTournamentAsync(ManagerId, CafeId, ValidCreateRequest()));
    }

    [Fact]
    public async Task CreateTournamentAsync_MaxParticipantsNotMultipleOf4_ThrowsBadRequest()
    {
        // Arrange
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        cafeRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        var splendor = new GameTemplate
        {
            Id = SplendorId,
            Name = "Splendor",
            IsActive = true,
            IsTournamentSupported = true,
            TournamentMaxScorePerPlayer = 15,
            TournamentMinPlayersPerTable = 2
        };
        gameRepo.Setup(r => r.GetByNameAsync("Splendor")).ReturnsAsync(splendor);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        // Act & Assert — MaxParticipants = 10
        var req = ValidCreateRequest();
        req.MaxParticipants = 10;

        await Assert.ThrowsAsync<BadRequestException>(
            () => svc.CreateTournamentAsync(ManagerId, CafeId, req));
    }

    // ============================================
    // === Player: Register / Withdraw ===
    // ============================================

    [Fact]
    public async Task RegisterAsync_RegistrationOpenAndKarmaSatisfied_CreatesParticipant()
    {
        // Arrange
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var tournament = BuildRegistrationOpenTournament();
        tournamentRepo.Setup(r => r.GetByIdAsync(TournamentId)).ReturnsAsync(tournament);
        tournamentRepo.Setup(r => r.GetParticipantAsync(TournamentId, UserId))
            .ReturnsAsync((TournamentParticipant?)null);
        tournamentRepo.Setup(r => r.CountActiveParticipantsAsync(TournamentId)).ReturnsAsync(2);
        userRepo.Setup(r => r.GetByIdWithProfileAsync(UserId))
            .ReturnsAsync(BuildUserWithProfile(UserId, karma: 100));
        tournamentRepo.Setup(r => r.AddParticipantAsync(It.IsAny<TournamentParticipant>()))
            .Returns(Task.CompletedTask);
        tournamentRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
        tournamentRepo.Setup(r => r.GetParticipantByIdAsync(It.IsAny<Guid>()))
            .ReturnsAsync((Guid id) => new TournamentParticipant
            {
                Id = id,
                TournamentId = TournamentId,
                UserId = UserId,
                User = new User { Id = UserId, Username = "alice", Email = "alice@test.com" },
                RegisteredAt = DateTime.UtcNow,
                KarmaAtRegistration = 100,
                Status = TournamentParticipantStatus.Registered,
                InitialElo = 1200,
                FinalElo = 1200,
                CreatedAt = DateTime.UtcNow
            });

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        // Act
        await svc.RegisterAsync(TournamentId, UserId);

        // Assert
        tournamentRepo.Verify(r => r.AddParticipantAsync(It.Is<TournamentParticipant>(
            p => p.UserId == UserId && p.Status == TournamentParticipantStatus.Registered)), Times.Once);
    }

    [Fact]
    public async Task RegisterAsync_KarmaBelowRequirement_ThrowsForbidden()
    {
        // Arrange
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var tournament = BuildRegistrationOpenTournament();
        tournament.MinKarmaRequirement = 80;
        tournamentRepo.Setup(r => r.GetByIdAsync(TournamentId)).ReturnsAsync(tournament);
        tournamentRepo.Setup(r => r.GetParticipantAsync(TournamentId, UserId))
            .ReturnsAsync((TournamentParticipant?)null);
        tournamentRepo.Setup(r => r.CountActiveParticipantsAsync(TournamentId)).ReturnsAsync(2);
        userRepo.Setup(r => r.GetByIdWithProfileAsync(UserId))
            .ReturnsAsync(BuildUserWithProfile(UserId, karma: 50)); // below 80

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        // Act & Assert
        await Assert.ThrowsAsync<ForbiddenException>(
            () => svc.RegisterAsync(TournamentId, UserId));
    }

    [Fact]
    public async Task RegisterAsync_AlreadyRegistered_ThrowsConflict()
    {
        // Arrange
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var tournament = BuildRegistrationOpenTournament();
        tournamentRepo.Setup(r => r.GetByIdAsync(TournamentId)).ReturnsAsync(tournament);
        tournamentRepo.Setup(r => r.GetParticipantAsync(TournamentId, UserId))
            .ReturnsAsync(new TournamentParticipant { UserId = UserId, Status = TournamentParticipantStatus.Registered });

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        // Act & Assert
        await Assert.ThrowsAsync<ConflictException>(
            () => svc.RegisterAsync(TournamentId, UserId));
    }

    [Fact]
    public async Task RegisterAsync_DeadlinePassed_ThrowsConflict()
    {
        // Arrange
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var tournament = BuildRegistrationOpenTournament();
        tournament.RegistrationDeadline = DateTime.UtcNow.AddHours(-1);
        tournamentRepo.Setup(r => r.GetByIdAsync(TournamentId)).ReturnsAsync(tournament);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        // Act & Assert
        await Assert.ThrowsAsync<ConflictException>(
            () => svc.RegisterAsync(TournamentId, UserId));
    }

    [Fact]
    public async Task WithdrawRegistrationAsync_AlreadyWithdrawn_ThrowsConflict()
    {
        // Arrange
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var tournament = BuildRegistrationOpenTournament();
        var participant = new TournamentParticipant
        {
            Id = ParticipantId,
            UserId = UserId,
            Status = TournamentParticipantStatus.Withdrawn
        };
        tournamentRepo.Setup(r => r.GetByIdAsync(TournamentId)).ReturnsAsync(tournament);
        tournamentRepo.Setup(r => r.GetParticipantAsync(TournamentId, UserId)).ReturnsAsync(participant);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        // Act & Assert
        await Assert.ThrowsAsync<ConflictException>(
            () => svc.WithdrawRegistrationAsync(TournamentId, UserId));
    }

    [Fact]
    public async Task WithdrawRegistrationAsync_TournamentAlreadyOnGoing_ThrowsConflict()
    {
        // Arrange
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var tournament = BuildOnGoingTournament();
        var participant = new TournamentParticipant
        {
            Id = ParticipantId,
            UserId = UserId,
            Status = TournamentParticipantStatus.Active
        };
        tournamentRepo.Setup(r => r.GetByIdAsync(TournamentId)).ReturnsAsync(tournament);
        tournamentRepo.Setup(r => r.GetParticipantAsync(TournamentId, UserId)).ReturnsAsync(participant);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        // Act & Assert
        await Assert.ThrowsAsync<ConflictException>(
            () => svc.WithdrawRegistrationAsync(TournamentId, UserId));
    }

    [Fact]
    public async Task WithdrawRegistrationAsync_CheckedIn_ThrowsConflict()
    {
        // Arrange — player đã check-in thì không được rút lui (tránh bỏ trống ghế)
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var tournament = BuildOnGoingTournament();
        var participant = new TournamentParticipant
        {
            Id = ParticipantId,
            UserId = UserId,
            Status = TournamentParticipantStatus.CheckedIn
        };
        tournamentRepo.Setup(r => r.GetByIdAsync(TournamentId)).ReturnsAsync(tournament);
        tournamentRepo.Setup(r => r.GetParticipantAsync(TournamentId, UserId)).ReturnsAsync(participant);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        // Act & Assert
        await Assert.ThrowsAsync<ConflictException>(
            () => svc.WithdrawRegistrationAsync(TournamentId, UserId));
    }

    [Fact]
    public async Task WithdrawRegistrationAsync_Finished_ThrowsConflict()
    {
        // Arrange — đã finish thì không rút lui
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var tournament = BuildOnGoingTournament();
        var participant = new TournamentParticipant
        {
            Id = ParticipantId,
            UserId = UserId,
            Status = TournamentParticipantStatus.Finished
        };
        tournamentRepo.Setup(r => r.GetByIdAsync(TournamentId)).ReturnsAsync(tournament);
        tournamentRepo.Setup(r => r.GetParticipantAsync(TournamentId, UserId)).ReturnsAsync(participant);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        // Act & Assert
        await Assert.ThrowsAsync<ConflictException>(
            () => svc.WithdrawRegistrationAsync(TournamentId, UserId));
    }

    [Fact]
    public async Task WithdrawRegistrationAsync_TournamentCancelled_ReturnsIdempotentNoOp()
    {
        // Arrange — C3: tournament đã Cancelled → idempotent return, không throw.
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var tournament = BuildRegistrationOpenTournament();
        tournament.Status = TournamentStatus.Cancelled;
        var participant = new TournamentParticipant
        {
            Id = ParticipantId,
            UserId = UserId,
            Status = TournamentParticipantStatus.Withdrawn
        };
        tournamentRepo.Setup(r => r.GetByIdAsync(TournamentId)).ReturnsAsync(tournament);
        tournamentRepo.Setup(r => r.GetParticipantAsync(TournamentId, UserId)).ReturnsAsync(participant);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        // Act — không throw, trả về DTO.
        var dto = await svc.WithdrawRegistrationAsync(TournamentId, UserId);

        // Assert
        Assert.NotNull(dto);
        Assert.Equal(participant.Id, dto.Id);
    }

    // ============================================
    // === POS: Record Match Result — score validation per game ===
    // ============================================

    [Fact]
    public async Task RecordMatchResultAsync_ScoreExceedsSplendorMax_ThrowsBadRequest()
    {
        // Arrange — F3: Splendor có TournamentMaxScorePerPlayer = 15. Score = 20 phải reject.
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var player = Guid.NewGuid();
        var opponent = Guid.NewGuid();

        var tournament = BuildOnGoingTournament();
        tournament.GameTemplate = new GameTemplate
        {
            Id = SplendorId,
            Name = "Splendor",
            IsActive = true,
            IsTournamentSupported = true,
            TournamentMaxScorePerPlayer = 15,
            TournamentMinPlayersPerTable = 2
        };

        var match = new TournamentMatchBracket
        {
            Id = Guid.NewGuid(),
            TournamentId = TournamentId,
            RoundNumber = 1,
            MatchNumber = 1,
            IsFinal = false,
            Player1Id = player,
            Player2Id = opponent,
            Status = TournamentMatchStatus.OnGoing
        };

        cafeRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        tournamentRepo.Setup(r => r.GetMatchByIdAsync(match.Id)).ReturnsAsync(match);
        tournamentRepo.Setup(r => r.GetByIdWithDetailsAsync(TournamentId)).ReturnsAsync(tournament);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        var request = new RecordMatchResultRequestDto
        {
            MatchId = match.Id,
            WinnerUserId = player,
            Results = new List<MatchPlayerResultDto>
            {
                new() { UserId = player, Score = 20, CardsBought = 5 },     // exceeds 15!
                new() { UserId = opponent, Score = 5, CardsBought = 3 }
            }
        };

        // Act & Assert
        await Assert.ThrowsAsync<BadRequestException>(
            () => svc.RecordMatchResultAsync(ManagerId, match.Id, request));
    }

    [Fact]
    public async Task RecordMatchResultAsync_ScoreWithinSplendorMax_Succeeds()
    {
        // Arrange — score = 15 (đúng max) cho Splendor → OK
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var player = Guid.NewGuid();
        var opponent = Guid.NewGuid();

        var tournament = BuildOnGoingTournament();
        tournament.GameTemplate = new GameTemplate
        {
            Id = SplendorId,
            Name = "Splendor",
            IsActive = true,
            IsTournamentSupported = true,
            TournamentMaxScorePerPlayer = 15,
            TournamentMinPlayersPerTable = 2
        };

        var match = new TournamentMatchBracket
        {
            Id = Guid.NewGuid(),
            TournamentId = TournamentId,
            RoundNumber = 1,
            MatchNumber = 1,
            IsFinal = false,
            Player1Id = player,
            Player2Id = opponent,
            Status = TournamentMatchStatus.OnGoing
        };

        cafeRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        tournamentRepo.Setup(r => r.GetMatchByIdAsync(match.Id)).ReturnsAsync(match);
        tournamentRepo.Setup(r => r.GetByIdWithDetailsAsync(TournamentId)).ReturnsAsync(tournament);
        configRepo.Setup(r => r.GetIntAsync(SystemConfigKeys.EloKFactor, 32)).ReturnsAsync(32);
        tournamentRepo.Setup(r => r.UpdateMatchAsync(It.IsAny<TournamentMatchBracket>())).Returns(Task.CompletedTask);
        tournamentRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        var request = new RecordMatchResultRequestDto
        {
            MatchId = match.Id,
            WinnerUserId = player,
            Results = new List<MatchPlayerResultDto>
            {
                new() { UserId = player, Score = 15, CardsBought = 5 },
                new() { UserId = opponent, Score = 8, CardsBought = 3 }
            }
        };

        // Act — không throw
        var result = await svc.RecordMatchResultAsync(ManagerId, match.Id, request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(player, result.WinnerPlayerId);
    }

    // ============================================
    // === POS: Mark NoShow — Karma + audit ===
    // ============================================

    [Fact]
    public async Task MarkNoShowAsync_AppliesKarmaPenaltyAndWritesAuditLog()
    {
        // Arrange
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var tournament = BuildOnGoingTournament();
        tournament.NoShowKarmaPenalty = -30;

        var participant = new TournamentParticipant
        {
            Id = ParticipantId,
            UserId = UserId,
            TournamentId = TournamentId,
            Status = TournamentParticipantStatus.CheckedIn
        };

        var profile = new UserProfile { UserId = UserId, KarmaPoints = 100 };

        cafeRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        tournamentRepo.Setup(r => r.GetByIdAsync(TournamentId)).ReturnsAsync(tournament);
        tournamentRepo.Setup(r => r.GetParticipantByIdAsync(ParticipantId)).ReturnsAsync(participant);
        userRepo.Setup(r => r.GetProfileByUserIdAsync(UserId)).ReturnsAsync(profile);
        karmaRepo.Setup(r => r.AddKarmaLogAsync(It.IsAny<KarmaLog>())).Returns(Task.CompletedTask);
        karmaRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
        tournamentRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        // Act
        await svc.MarkNoShowAsync(ManagerId, TournamentId, ParticipantId);

        // Assert: profile reduced by 30, audit log written
        Assert.Equal(70, profile.KarmaPoints);
        karmaRepo.Verify(r => r.AddKarmaLogAsync(It.Is<KarmaLog>(k =>
            k.UserId == UserId
            && k.Source == KarmaLogSource.TournamentReward
            && k.KarmaBefore == 100
            && k.KarmaAfter == 70
            && k.KarmaPointsChange == -30
            && k.ViolationCategory == KarmaViolationCategory.NoShow
        )), Times.Once);
    }

    [Fact]
    public async Task MarkNoShowAsync_ParticipantAlreadyFinished_ThrowsConflict()
    {
        // Arrange
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var participant = new TournamentParticipant
        {
            Id = ParticipantId,
            UserId = UserId,
            TournamentId = TournamentId,
            Status = TournamentParticipantStatus.Finished
        };

        cafeRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        tournamentRepo.Setup(r => r.GetByIdAsync(TournamentId)).ReturnsAsync(BuildOnGoingTournament());
        tournamentRepo.Setup(r => r.GetParticipantByIdAsync(ParticipantId)).ReturnsAsync(participant);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        // Act & Assert
        await Assert.ThrowsAsync<ConflictException>(
            () => svc.MarkNoShowAsync(ManagerId, TournamentId, ParticipantId));
    }

    // ============================================
    // === Cancel Tournament ===
    // ============================================

    [Fact]
    public async Task CancelTournamentAsync_HasRegisteredNoReason_ThrowsBadRequest()
    {
        // Arrange
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var tournament = BuildRegistrationOpenTournament();
        cafeRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        tournamentRepo.Setup(r => r.GetByIdWithDetailsAsync(TournamentId)).ReturnsAsync(tournament);
        tournamentRepo.Setup(r => r.CountActiveParticipantsAsync(TournamentId)).ReturnsAsync(3);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        // Act & Assert — cancel without reason when participants exist
        await Assert.ThrowsAsync<BadRequestException>(
            () => svc.CancelTournamentAsync(ManagerId, TournamentId, reason: null));
    }

    [Fact]
    public async Task CancelTournamentAsync_AlreadyCompleted_ThrowsConflict()
    {
        // Arrange
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var tournament = BuildOnGoingTournament();
        tournament.Status = TournamentStatus.Completed;

        cafeRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        tournamentRepo.Setup(r => r.GetByIdWithDetailsAsync(TournamentId)).ReturnsAsync(tournament);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        // Act & Assert
        await Assert.ThrowsAsync<ConflictException>(
            () => svc.CancelTournamentAsync(ManagerId, TournamentId, "test"));
    }

    [Fact]
    public async Task CancelTournamentAsync_WithParticipants_AutoMarksParticipantsWithdrawn()
    {
        // Arrange — B2: Cancel tournament tự động mark Registered/CheckedIn/Active thành Withdrawn
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var p1 = new TournamentParticipant { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Status = TournamentParticipantStatus.Registered };
        var p2 = new TournamentParticipant { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Status = TournamentParticipantStatus.CheckedIn };
        var p3 = new TournamentParticipant { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Status = TournamentParticipantStatus.Active };
        var p4 = new TournamentParticipant { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Status = TournamentParticipantStatus.Finished };
        var p5 = new TournamentParticipant { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Status = TournamentParticipantStatus.Withdrawn };

        var tournament = BuildRegistrationOpenTournament();
        tournament.Participants = new List<TournamentParticipant> { p1, p2, p3, p4, p5 };
        var m0 = new TournamentMatchBracket { Id = Guid.NewGuid(), RoundNumber = 1, Status = TournamentMatchStatus.Scheduled };
        var m1 = new TournamentMatchBracket { Id = Guid.NewGuid(), RoundNumber = 1, Status = TournamentMatchStatus.OnGoing };
        var m2 = new TournamentMatchBracket { Id = Guid.NewGuid(), RoundNumber = 1, Status = TournamentMatchStatus.Completed };
        tournament.Matches = new List<TournamentMatchBracket> { m0, m1, m2 };

        cafeRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        tournamentRepo.Setup(r => r.GetByIdWithDetailsAsync(TournamentId)).ReturnsAsync(tournament);
        tournamentRepo.Setup(r => r.CountActiveParticipantsAsync(TournamentId)).ReturnsAsync(3);
        tournamentRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
        gameRepo.Setup(r => r.GetByIdAsync(SplendorId)).ReturnsAsync(new GameTemplate { Id = SplendorId, Name = "Splendor" });

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        // Act
        await svc.CancelTournamentAsync(ManagerId, TournamentId, "Force majeure");

        // Assert — Registered, CheckedIn, Active đã thành Withdrawn. Finished/Withdrawn không đổi.
        Assert.Equal(TournamentParticipantStatus.Withdrawn, p1.Status);
        Assert.Equal(TournamentParticipantStatus.Withdrawn, p2.Status);
        Assert.Equal(TournamentParticipantStatus.Withdrawn, p3.Status);
        Assert.Equal(TournamentParticipantStatus.Finished, p4.Status); // không đổi
        Assert.Equal(TournamentParticipantStatus.Withdrawn, p5.Status); // không đổi
        Assert.Equal(TournamentStatus.Cancelled, tournament.Status);

        // Matches: Scheduled + OnGoing → Cancelled. Completed giữ nguyên.
        Assert.Equal(TournamentMatchStatus.Cancelled, m0.Status);
        Assert.Equal(TournamentMatchStatus.Cancelled, m1.Status);
        Assert.Equal(TournamentMatchStatus.Completed, m2.Status);
    }

    // ============================================
    // === Advance Round ===
    // ============================================

    [Fact]
    public async Task AdvanceRoundAsync_TournamentNotOnGoing_ThrowsConflict()
    {
        // Arrange
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var tournament = BuildRegistrationOpenTournament();
        cafeRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        tournamentRepo.Setup(r => r.GetByIdWithDetailsAsync(TournamentId)).ReturnsAsync(tournament);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        // Act & Assert
        await Assert.ThrowsAsync<ConflictException>(
            () => svc.AdvanceRoundAsync(ManagerId, TournamentId));
    }

    [Fact]
    public async Task AdvanceRoundAsync_CurrentRoundHasUnfinishedMatches_ThrowsConflict()
    {
        // Arrange
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var tournament = BuildOnGoingTournament();
        tournament.CurrentRound = 1;
        // Round 1 has 1 finished, 1 still scheduled
        tournament.Matches = new List<TournamentMatchBracket>
        {
            new() { Id = Guid.NewGuid(), RoundNumber = 1, Status = TournamentMatchStatus.Completed },
            new() { Id = Guid.NewGuid(), RoundNumber = 1, Status = TournamentMatchStatus.Scheduled }
        };

        cafeRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        tournamentRepo.Setup(r => r.GetByIdWithDetailsAsync(TournamentId)).ReturnsAsync(tournament);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        // Act & Assert
        await Assert.ThrowsAsync<ConflictException>(
            () => svc.AdvanceRoundAsync(ManagerId, TournamentId));
    }

    [Fact]
    public async Task AdvanceRoundAsync_Round1CompletedAnd4Active_BuildsRound2()
    {
        // Arrange
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var tournament = BuildOnGoingTournament();
        tournament.CurrentRound = 1;
        tournament.TotalRounds = 4;
        tournament.PreliminaryRounds = 3;
        // Round 1 done; Round 2 not built yet
        tournament.Matches = new List<TournamentMatchBracket>
        {
            new() { Id = Guid.NewGuid(), RoundNumber = 1, Status = TournamentMatchStatus.Completed }
        };
        // 4 active participants available
        tournament.Participants = Enumerable.Range(1, 4)
            .Select(_ => new TournamentParticipant
            {
                Id = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                Status = TournamentParticipantStatus.Active,
                CheckedInAt = DateTime.UtcNow.AddMinutes(-30),
                RegisteredAt = DateTime.UtcNow.AddMinutes(-60)
            }).ToList();

        cafeRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        tournamentRepo.Setup(r => r.GetByIdWithDetailsAsync(TournamentId)).ReturnsAsync(tournament);
        tournamentRepo.Setup(r => r.AddMatchesAsync(It.IsAny<IEnumerable<TournamentMatchBracket>>()))
            .Returns(Task.CompletedTask);
        tournamentRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
        gameRepo.Setup(r => r.GetByIdAsync(SplendorId)).ReturnsAsync(new GameTemplate { Id = SplendorId, Name = "Splendor" });

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        // Act
        await svc.AdvanceRoundAsync(ManagerId, TournamentId);

        // Assert: 1 Round 2 match được build từ 4 active participants
        tournamentRepo.Verify(r => r.AddMatchesAsync(It.Is<IEnumerable<TournamentMatchBracket>>(
            matches => matches.Count() == 1
                && matches.All(m => m.RoundNumber == 2 && !m.IsFinal)
        )), Times.Once);
    }

    // ============================================
    // === Auto-close expired registrations ===
    // ============================================

    [Fact]
    public async Task AutoCloseExpiredRegistrationsAsync_ClosesExpiredTournaments()
    {
        // Arrange
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var t1 = new Tournament { Id = Guid.NewGuid(), Status = TournamentStatus.RegistrationOpen };
        var t2 = new Tournament { Id = Guid.NewGuid(), Status = TournamentStatus.RegistrationOpen };
        // cần có ít nhất 1 participant mới được auto-close
        t1.Participants = new List<TournamentParticipant>
        {
            new() { UserId = Guid.NewGuid(), Status = TournamentParticipantStatus.Registered }
        };
        t2.Participants = new List<TournamentParticipant>
        {
            new() { UserId = Guid.NewGuid(), Status = TournamentParticipantStatus.CheckedIn }
        };

        tournamentRepo.Setup(r => r.GetUpcomingForClosingAsync(It.IsAny<DateTime>()))
            .ReturnsAsync(new List<Tournament> { t1, t2 });
        tournamentRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        // Act
        var count = await svc.AutoCloseExpiredRegistrationsAsync(DateTime.UtcNow);

        // Assert
        Assert.Equal(2, count);
        Assert.All(new[] { t1, t2 }, t => Assert.Equal(TournamentStatus.RegistrationClosed, t.Status));
        tournamentRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task AutoCloseExpiredRegistrationsAsync_SkipsTournamentsWithoutParticipants()
    {
        // Arrange — I6: tournament 0 người đăng ký + đã hết hạn KHÔNG auto-close
        // (tránh kẹt ở RegistrationOpen mãi mãi).
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var emptyT = new Tournament
        {
            Id = Guid.NewGuid(),
            Status = TournamentStatus.RegistrationOpen,
            Participants = new List<TournamentParticipant>() // empty
        };
        var fullT = new Tournament
        {
            Id = Guid.NewGuid(),
            Status = TournamentStatus.RegistrationOpen,
            Participants = new List<TournamentParticipant>
            {
                new() { UserId = Guid.NewGuid(), Status = TournamentParticipantStatus.Registered }
            }
        };

        tournamentRepo.Setup(r => r.GetUpcomingForClosingAsync(It.IsAny<DateTime>()))
            .ReturnsAsync(new List<Tournament> { emptyT, fullT });
        tournamentRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        // Act
        var count = await svc.AutoCloseExpiredRegistrationsAsync(DateTime.UtcNow);

        // Assert — chỉ close tournament có participants
        Assert.Equal(1, count);
        Assert.Equal(TournamentStatus.RegistrationOpen, emptyT.Status); // không đổi
        Assert.Equal(TournamentStatus.RegistrationClosed, fullT.Status);
    }

    // ============================================
    // === Player Queries: GetTournament / Participants / Matches ===
    // ============================================

    // ============================================
    // === POS: Walk-in participant (khách vãng lai) ===
    // ============================================

    [Fact]
    public async Task ManagerAddWalkInParticipantAsync_RegistrationOpen_CreatesWalkInRow()
    {
        // Arrange — tournament đang mở đăng ký, manager tạo walk-in.
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var tournament = BuildRegistrationOpenTournament();
        tournament.Participants = new List<TournamentParticipant>();

        cafeRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        tournamentRepo.Setup(r => r.GetByIdWithDetailsAsync(TournamentId)).ReturnsAsync(tournament);
        tournamentRepo.Setup(r => r.AddParticipantAsync(It.IsAny<TournamentParticipant>())).Returns(Task.CompletedTask);
        tournamentRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
        tournamentRepo.Setup(r => r.GetParticipantByIdAsync(It.IsAny<Guid>()))
            .ReturnsAsync((Guid id) => new TournamentParticipant
            {
                Id = id,
                TournamentId = TournamentId,
                IsWalkIn = true,
                WalkInDisplayName = "Khách vãng lai #1",
                Status = TournamentParticipantStatus.Registered,
                InitialElo = EloRatingHelper.DefaultRating,
                FinalElo = EloRatingHelper.DefaultRating
            });

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        // Act
        var result = await svc.ManagerAddWalkInParticipantAsync(
            ManagerId, TournamentId,
            new AddWalkInParticipantRequestDto { DisplayName = "Khách vãng lai #1" });

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsWalkIn);
        Assert.Null(result.UserId);
        Assert.Null(result.Username);
        Assert.Equal("Khách vãng lai #1", result.WalkInDisplayName);
        Assert.Equal(TournamentParticipantStatus.Registered, result.Status);
        tournamentRepo.Verify(r => r.AddParticipantAsync(It.Is<TournamentParticipant>(p =>
            p.IsWalkIn && p.UserId == null && p.WalkInDisplayName == "Khách vãng lai #1"
            && p.RegisteredByStaffId == ManagerId
            && p.JoinedRoundNumber == 1
        )), Times.Once);
    }

    [Fact]
    public async Task ManagerAddWalkInParticipantAsync_OnGoingRoundOneNotCompleted_AllowsJoinedRoundOne()
    {
        // Arrange — tournament đang OnGoing nhưng R1 chưa có match nào Completed
        // (R1 đang OnGoing hoặc còn Scheduled, không có Swiss score nào).
        // Walk-in vẫn được phép vào R1 theo option A (fairness với player gốc).
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var tournament = BuildOnGoingTournament();
        tournament.CurrentRound = 1;
        tournament.Participants = new List<TournamentParticipant>();
        // R1 đã có 1 match Scheduled nhưng chưa Completed → Swiss score = 0 cho mọi participant.
        tournament.Matches = new List<TournamentMatchBracket>
        {
            new()
            {
                Id = Guid.NewGuid(),
                TournamentId = TournamentId,
                RoundNumber = 1,
                MatchNumber = 1,
                IsFinal = false,
                Status = TournamentMatchStatus.Scheduled
            }
        };

        cafeRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        tournamentRepo.Setup(r => r.GetByIdWithDetailsAsync(TournamentId)).ReturnsAsync(tournament);
        tournamentRepo.Setup(r => r.AddParticipantAsync(It.IsAny<TournamentParticipant>())).Returns(Task.CompletedTask);
        tournamentRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
        tournamentRepo.Setup(r => r.GetParticipantByIdAsync(It.IsAny<Guid>()))
            .ReturnsAsync((Guid id) => new TournamentParticipant
            {
                Id = id,
                TournamentId = TournamentId,
                IsWalkIn = true,
                WalkInDisplayName = "Walk-in Bob",
                JoinedRoundNumber = 1,
                Status = TournamentParticipantStatus.Registered,
                InitialElo = EloRatingHelper.DefaultRating,
                FinalElo = EloRatingHelper.DefaultRating
            });

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        // Act
        var result = await svc.ManagerAddWalkInParticipantAsync(
            ManagerId, TournamentId,
            new AddWalkInParticipantRequestDto { DisplayName = "Walk-in Bob" }
        );

        // Assert — walk-in luôn JoinedRound = 1 (R1 chưa completed vẫn cho vào).
        Assert.Equal(1, result.JoinedRoundNumber);
        tournamentRepo.Verify(r => r.AddParticipantAsync(It.Is<TournamentParticipant>(p =>
            p.JoinedRoundNumber == 1
        )), Times.Once);
    }

    [Fact]
    public async Task ManagerAddWalkInParticipantAsync_RoundOneCompleted_ThrowsConflict()
    {
        // Arrange — R1 đã có 1 match Completed → Swiss score tồn tại → block walk-in.
        // (Thực tế BGC: khóa sau R1 để giữ fairness cho player đã đầu tư 1 round.)
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var tournament = BuildOnGoingTournament();
        tournament.CurrentRound = 2; // Đã advance sang R2.
        tournament.Participants = new List<TournamentParticipant>();
        tournament.Matches = new List<TournamentMatchBracket>
        {
            new()
            {
                Id = Guid.NewGuid(),
                TournamentId = TournamentId,
                RoundNumber = 1,
                MatchNumber = 1,
                IsFinal = false,
                Status = TournamentMatchStatus.Completed // R1 đã xong → Swiss score đã ghi.
            },
            new()
            {
                Id = Guid.NewGuid(),
                TournamentId = TournamentId,
                RoundNumber = 2,
                MatchNumber = 1,
                IsFinal = false,
                Status = TournamentMatchStatus.Scheduled
            }
        };

        cafeRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        tournamentRepo.Setup(r => r.GetByIdWithDetailsAsync(TournamentId)).ReturnsAsync(tournament);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            svc.ManagerAddWalkInParticipantAsync(
                ManagerId, TournamentId,
                new AddWalkInParticipantRequestDto { DisplayName = "Too Late" }));

        Assert.Contains("Vòng 1 của giải đã hoàn thành", ex.Message);
        // Verify walk-in không được insert.
        tournamentRepo.Verify(r => r.AddParticipantAsync(It.IsAny<TournamentParticipant>()), Times.Never);
    }

    [Fact]
    public async Task ManagerAddWalkInParticipantAsync_DuplicateDisplayName_ThrowsConflict()
    {
        // Arrange
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var tournament = BuildRegistrationOpenTournament();
        tournament.Participants = new List<TournamentParticipant>
        {
            new()
            {
                Id = Guid.NewGuid(),
                IsWalkIn = true,
                WalkInDisplayName = "Walk-in Bob",
                Status = TournamentParticipantStatus.Registered
            }
        };

        cafeRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        tournamentRepo.Setup(r => r.GetByIdWithDetailsAsync(TournamentId)).ReturnsAsync(tournament);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        // Act & Assert
        await Assert.ThrowsAsync<ConflictException>(() =>
            svc.ManagerAddWalkInParticipantAsync(
                ManagerId, TournamentId,
                new AddWalkInParticipantRequestDto { DisplayName = "WALK-IN BOB" })); // case-insensitive
    }

    [Fact]
    public async Task ManagerAddWalkInParticipantAsync_FinalAlreadyBuilt_ThrowsConflict()
    {
        // Arrange — sau khi Final đã build, không cho add walk-in.
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var tournament = BuildOnGoingTournament();
        tournament.Participants = new List<TournamentParticipant>();
        tournament.Matches = new List<TournamentMatchBracket>
        {
            new()
            {
                Id = Guid.NewGuid(),
                TournamentId = TournamentId,
                RoundNumber = 4,
                MatchNumber = 1,
                IsFinal = true,
                Status = TournamentMatchStatus.Scheduled
            }
        };

        cafeRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        tournamentRepo.Setup(r => r.GetByIdWithDetailsAsync(TournamentId)).ReturnsAsync(tournament);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        // Act & Assert
        await Assert.ThrowsAsync<ConflictException>(() =>
            svc.ManagerAddWalkInParticipantAsync(
                ManagerId, TournamentId,
                new AddWalkInParticipantRequestDto { DisplayName = "Too Late" }));
    }

    [Fact]
    public async Task ManagerAddWalkInParticipantAsync_RoundOnGoing_ThrowsConflict()
    {
        // Arrange — round hiện tại đang OnGoing → reject (manager chờ round kết thúc).
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var tournament = BuildOnGoingTournament();
        tournament.CurrentRound = 2;
        tournament.Participants = new List<TournamentParticipant>();
        tournament.Matches = new List<TournamentMatchBracket>
        {
            new()
            {
                Id = Guid.NewGuid(),
                TournamentId = TournamentId,
                RoundNumber = 2,
                MatchNumber = 1,
                IsFinal = false,
                Status = TournamentMatchStatus.OnGoing
            }
        };

        cafeRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        tournamentRepo.Setup(r => r.GetByIdWithDetailsAsync(TournamentId)).ReturnsAsync(tournament);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        // Act & Assert
        await Assert.ThrowsAsync<ConflictException>(() =>
            svc.ManagerAddWalkInParticipantAsync(
                ManagerId, TournamentId,
                new AddWalkInParticipantRequestDto { DisplayName = "Mid-Round Walker" }));
    }

    [Fact]
    public async Task GetTournamentAsync_TournamentExists_ReturnsDto()
    {
        // Arrange
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var tournament = BuildRegistrationOpenTournament();
        tournament.Participants = new List<TournamentParticipant>
        {
            new() { UserId = UserId, Status = TournamentParticipantStatus.Registered }
        };

        tournamentRepo.Setup(r => r.GetByIdAsync(TournamentId)).ReturnsAsync(tournament);
        gameRepo.Setup(r => r.GetByIdAsync(SplendorId)).ReturnsAsync(new GameTemplate { Id = SplendorId, Name = "Splendor" });

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        // Act
        var result = await svc.GetTournamentAsync(TournamentId, UserId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(TournamentId, result.Id);
        Assert.Equal(1, result.RegisteredCount);
        Assert.True(result.CurrentUserRegistered);
        Assert.Equal(TournamentParticipantStatus.Registered, result.CurrentUserParticipantStatus);
    }

    [Fact]
    public async Task GetTournamentAsync_TournamentNotFound_ThrowsNotFound()
    {
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        tournamentRepo.Setup(r => r.GetByIdAsync(TournamentId)).ReturnsAsync((Tournament?)null);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        await Assert.ThrowsAsync<NotFoundException>(
            () => svc.GetTournamentAsync(TournamentId, UserId));
    }

    [Fact]
    public async Task GetParticipantsAsync_ReturnsMappedParticipants()
    {
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var participants = new List<TournamentParticipant>
        {
            new()
            {
                Id = ParticipantId,
                TournamentId = TournamentId,
                UserId = UserId,
                User = new User { Id = UserId, Username = "alice", Email = "alice@test.com" },
                RegisteredAt = DateTime.UtcNow.AddHours(-1),
                KarmaAtRegistration = 90,
                Status = TournamentParticipantStatus.Registered,
                InitialElo = 1500,
                FinalElo = 1500
            }
        };
        tournamentRepo.Setup(r => r.GetParticipantsAsync(TournamentId)).ReturnsAsync(participants);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        var result = await svc.GetParticipantsAsync(TournamentId);

        Assert.Single(result);
        Assert.Equal("alice", result[0].Username);
        Assert.Equal(90, result[0].KarmaAtRegistration);
    }

    [Fact]
    public async Task GetMyRegistrationsAsync_InvalidStatus_ThrowsBadRequest()
    {
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        await Assert.ThrowsAsync<BadRequestException>(
            () => svc.GetMyRegistrationsAsync(UserId, "INVALID_STATUS"));
    }

    [Fact]
    public async Task GetMyRegistrationsAsync_NoParticipants_ReturnsEmpty()
    {
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        tournamentRepo.Setup(r => r.GetParticipantsByUserAsync(UserId))
            .ReturnsAsync(new List<TournamentParticipant>());

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        var result = await svc.GetMyRegistrationsAsync(UserId);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetEloHistoryAsync_UserNotFound_ThrowsNotFound()
    {
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        userRepo.Setup(r => r.GetByIdWithProfileAsync(UserId)).ReturnsAsync((User?)null);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        await Assert.ThrowsAsync<NotFoundException>(
            () => svc.GetEloHistoryAsync(UserId));
    }

    [Fact]
    public async Task GetLeaderboardAsync_TopCountOutOfRange_NormalizedToDefault()
    {
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        // topCount = 999 → normalized to 100 internally
        tournamentRepo.Setup(r => r.GetTopEloProfilesAsync(100, null))
            .ReturnsAsync(new List<UserProfile>());
        tournamentRepo.Setup(r => r.GetAggregatedTournamentStatsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), null))
            .ReturnsAsync((IReadOnlyDictionary<Guid, (int, int)>)new Dictionary<Guid, (int, int)>());

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        var result = await svc.GetLeaderboardAsync(999);

        Assert.Equal(0, result.TotalPlayers);
        tournamentRepo.Verify(r => r.GetTopEloProfilesAsync(100, null), Times.Once);
    }

    // ============================================
    // === Manual Pairing: Set / Preview / Clear ===
    // ============================================

    [Fact]
    public async Task PreviewPairingsAsync_NoManualSet_ReturnsAutoSuggested()
    {
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var tournament = BuildRegistrationOpenTournament();
        tournament.Participants = Enumerable.Range(1, 8).Select(_ => new TournamentParticipant
        {
            Id = Guid.NewGuid(),
            TournamentId = TournamentId,
            UserId = Guid.NewGuid(),
            Status = TournamentParticipantStatus.CheckedIn,
            CheckedInAt = DateTime.UtcNow,
            RegisteredAt = DateTime.UtcNow
        }).ToList();

        cafeRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        tournamentRepo.Setup(r => r.GetByIdWithDetailsAsync(TournamentId)).ReturnsAsync(tournament);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        var result = await svc.PreviewPairingsAsync(ManagerId, TournamentId, 1);

        Assert.Equal(2, result.Pairings.Count);
        Assert.All(result.Pairings, p => Assert.Equal(4, p.PlayerIds.Count));
        Assert.Equal("Auto (suggested)", result.Source);
    }

    [Fact]
    public async Task PreviewPairingsAsync_ManualSet_ReturnsExistingManual()
    {
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var tournament = BuildRegistrationOpenTournament();
        tournament.Round1PairingsJson = System.Text.Json.JsonSerializer.Serialize(new[]
        {
            new ManualPairingDto { MatchNumber = 1, PlayerIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() } }
        });

        cafeRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        tournamentRepo.Setup(r => r.GetByIdWithDetailsAsync(TournamentId)).ReturnsAsync(tournament);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        var result = await svc.PreviewPairingsAsync(ManagerId, TournamentId, 1);

        Assert.Single(result.Pairings);
        Assert.Equal("Manual", result.Source);
    }

    [Fact]
    public async Task PreviewPairingsAsync_NotDivisibleBy4_AddsWarning()
    {
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var tournament = BuildRegistrationOpenTournament();
        // 5 người - không chia hết cho 4
        tournament.Participants = Enumerable.Range(1, 5).Select(_ => new TournamentParticipant
        {
            Id = Guid.NewGuid(),
            TournamentId = TournamentId,
            UserId = Guid.NewGuid(),
            Status = TournamentParticipantStatus.CheckedIn
        }).ToList();

        cafeRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        tournamentRepo.Setup(r => r.GetByIdWithDetailsAsync(TournamentId)).ReturnsAsync(tournament);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        var result = await svc.PreviewPairingsAsync(ManagerId, TournamentId, 1);

        Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Warnings, w => w.Contains("không chia hết cho 4"));
    }

    [Fact]
    public async Task SetRoundPairingsAsync_ValidPairings_PersistsAndSetsManualMode()
    {
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();
        var user3 = Guid.NewGuid();
        var user4 = Guid.NewGuid();

        var tournament = BuildRegistrationOpenTournament();
        tournament.Participants = new List<TournamentParticipant>
        {
            new() { Id = Guid.NewGuid(), TournamentId = TournamentId, UserId = user1, Status = TournamentParticipantStatus.CheckedIn },
            new() { Id = Guid.NewGuid(), TournamentId = TournamentId, UserId = user2, Status = TournamentParticipantStatus.CheckedIn },
            new() { Id = Guid.NewGuid(), TournamentId = TournamentId, UserId = user3, Status = TournamentParticipantStatus.CheckedIn },
            new() { Id = Guid.NewGuid(), TournamentId = TournamentId, UserId = user4, Status = TournamentParticipantStatus.CheckedIn }
        };

        cafeRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        tournamentRepo.Setup(r => r.GetByIdWithDetailsAsync(TournamentId)).ReturnsAsync(tournament);
        tournamentRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        var request = new SetRoundPairingsRequestDto
        {
            RoundNumber = 1,
            Pairings = new List<ManualPairingDto>
            {
                new() { MatchNumber = 1, PlayerIds = new List<Guid> { user1, user2, user3, user4 } }
            }
        };

        var result = await svc.SetRoundPairingsAsync(ManagerId, TournamentId, request);

        Assert.NotNull(result);
        Assert.Equal("Manual", result.Source);
        Assert.Equal(TournamentPairingMode.Manual, tournament.PairingMode);
        Assert.False(string.IsNullOrWhiteSpace(tournament.Round1PairingsJson));
    }

    [Fact]
    public async Task SetRoundPairingsAsync_DuplicatePlayerInDifferentMatches_ThrowsBadRequest()
    {
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();

        var tournament = BuildRegistrationOpenTournament();
        tournament.Participants = new List<TournamentParticipant>
        {
            new() { Id = Guid.NewGuid(), TournamentId = TournamentId, UserId = user1, Status = TournamentParticipantStatus.CheckedIn },
            new() { Id = Guid.NewGuid(), TournamentId = TournamentId, UserId = user2, Status = TournamentParticipantStatus.CheckedIn }
        };

        cafeRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        tournamentRepo.Setup(r => r.GetByIdWithDetailsAsync(TournamentId)).ReturnsAsync(tournament);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        var request = new SetRoundPairingsRequestDto
        {
            RoundNumber = 1,
            Pairings = new List<ManualPairingDto>
            {
                new() { MatchNumber = 1, PlayerIds = new List<Guid> { user1, user2 } },
                new() { MatchNumber = 2, PlayerIds = new List<Guid> { user1 } } // duplicate user1
            }
        };

        await Assert.ThrowsAsync<BadRequestException>(
            () => svc.SetRoundPairingsAsync(ManagerId, TournamentId, request));
    }

    [Fact]
    public async Task SetRoundPairingsAsync_UserNotInTournament_ThrowsBadRequest()
    {
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var registeredUser = Guid.NewGuid();
        var outsider = Guid.NewGuid();

        var tournament = BuildRegistrationOpenTournament();
        tournament.Participants = new List<TournamentParticipant>
        {
            new() { Id = Guid.NewGuid(), TournamentId = TournamentId, UserId = registeredUser, Status = TournamentParticipantStatus.CheckedIn }
        };

        cafeRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        tournamentRepo.Setup(r => r.GetByIdWithDetailsAsync(TournamentId)).ReturnsAsync(tournament);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        var request = new SetRoundPairingsRequestDto
        {
            RoundNumber = 1,
            Pairings = new List<ManualPairingDto>
            {
                new() { MatchNumber = 1, PlayerIds = new List<Guid> { registeredUser, outsider } } // outsider không phải participant
            }
        };

        var ex = await Assert.ThrowsAsync<BadRequestException>(
            () => svc.SetRoundPairingsAsync(ManagerId, TournamentId, request));

        Assert.Contains(outsider.ToString(), ex.Message);
    }

    [Fact]
    public async Task SetRoundPairingsAsync_FinalRoundNotExactly4Players_ThrowsBadRequest()
    {
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var tournament = BuildRegistrationOpenTournament();

        cafeRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        tournamentRepo.Setup(r => r.GetByIdWithDetailsAsync(TournamentId)).ReturnsAsync(tournament);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        var request = new SetRoundPairingsRequestDto
        {
            RoundNumber = 4, // Final
            Pairings = new List<ManualPairingDto>
            {
                new() { MatchNumber = 1, PlayerIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() } } // 2 người, không phải 4
            }
        };

        await Assert.ThrowsAsync<BadRequestException>(
            () => svc.SetRoundPairingsAsync(ManagerId, TournamentId, request));
    }

    [Fact]
    public async Task SetRoundPairingsAsync_RoundAlreadyHasMatches_ThrowsConflict()
    {
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var tournament = BuildRegistrationOpenTournament();
        tournament.Matches = new List<TournamentMatchBracket>
        {
            new() { Id = Guid.NewGuid(), TournamentId = TournamentId, RoundNumber = 1, MatchNumber = 1 }
        };

        cafeRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        tournamentRepo.Setup(r => r.GetByIdWithDetailsAsync(TournamentId)).ReturnsAsync(tournament);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        var request = new SetRoundPairingsRequestDto
        {
            RoundNumber = 1,
            Pairings = new List<ManualPairingDto>
            {
                new() { MatchNumber = 1, PlayerIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() } }
            }
        };

        await Assert.ThrowsAsync<ConflictException>(
            () => svc.SetRoundPairingsAsync(ManagerId, TournamentId, request));
    }

    [Fact]
    public async Task SetRoundPairingsAsync_BadMatchNumber_ThrowsBadRequest()
    {
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var tournament = BuildRegistrationOpenTournament();

        cafeRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        tournamentRepo.Setup(r => r.GetByIdWithDetailsAsync(TournamentId)).ReturnsAsync(tournament);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        var request = new SetRoundPairingsRequestDto
        {
            RoundNumber = 99, // Invalid
            Pairings = new List<ManualPairingDto>
            {
                new() { MatchNumber = 1, PlayerIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() } }
            }
        };

        await Assert.ThrowsAsync<BadRequestException>(
            () => svc.SetRoundPairingsAsync(ManagerId, TournamentId, request));
    }

    [Fact]
    public async Task SetPairingModeAsync_DraftToManual_Allowed()
    {
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var tournament = BuildRegistrationOpenTournament();
        tournament.Status = TournamentStatus.Draft;

        cafeRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        tournamentRepo.Setup(r => r.GetByIdAsync(TournamentId)).ReturnsAsync(tournament);
        tournamentRepo.Setup(r => r.GetByIdWithDetailsAsync(TournamentId)).ReturnsAsync(tournament);
        tournamentRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
        gameRepo.Setup(r => r.GetByIdAsync(SplendorId)).ReturnsAsync(new GameTemplate { Id = SplendorId, Name = "Splendor" });

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        await svc.SetPairingModeAsync(ManagerId, TournamentId, TournamentPairingMode.Manual);

        Assert.Equal(TournamentPairingMode.Manual, tournament.PairingMode);
    }

    [Fact]
    public async Task SetPairingModeAsync_OnGoingToManual_ThrowsConflict()
    {
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var tournament = BuildOnGoingTournament();
        // F15: Set matches đã có ở round hiện tại → throw ConflictException
        tournament.Matches = new List<TournamentMatchBracket>
        {
            new() { Id = Guid.NewGuid(), TournamentId = TournamentId, RoundNumber = 1, MatchNumber = 1 }
        };

        cafeRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        tournamentRepo.Setup(r => r.GetByIdWithDetailsAsync(TournamentId)).ReturnsAsync(tournament);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        await Assert.ThrowsAsync<ConflictException>(
            () => svc.SetPairingModeAsync(ManagerId, TournamentId, TournamentPairingMode.Manual));
    }

    [Fact]
    public async Task ClearRoundPairingsAsync_NoMatchesYet_ClearsJson()
    {
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var tournament = BuildRegistrationOpenTournament();
        tournament.Round1PairingsJson = "[{\"MatchNumber\":1,\"PlayerIds\":[]}]";

        cafeRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        tournamentRepo.Setup(r => r.GetByIdAsync(TournamentId)).ReturnsAsync(tournament);
        tournamentRepo.Setup(r => r.GetByIdWithDetailsAsync(TournamentId)).ReturnsAsync(tournament);
        tournamentRepo.Setup(r => r.GetMatchesByTournamentAsync(TournamentId))
            .ReturnsAsync(new List<TournamentMatchBracket>());
        tournamentRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        var result = await svc.ClearRoundPairingsAsync(ManagerId, TournamentId, 1);

        Assert.Null(tournament.Round1PairingsJson);
    }

    // ============================================
    // === Helpers ===
    // ============================================

    private static CreateTournamentRequestDto ValidCreateRequest() => new()
    {
        Title = "Splendor Cup August 2026",
        StartTime = DateTime.UtcNow.AddDays(7),
        RoundDurationMinutes = 45,
        MaxParticipants = 8,
        WinnerKarmaBonus = 50,
        FinalistKarmaBonus = 20,
        NoShowKarmaPenalty = -30
    };

    private static Tournament BuildRegistrationOpenTournament()
    {
        var startTime = DateTime.UtcNow.AddDays(7);
        return new Tournament
        {
            Id = TournamentId,
            CafeId = CafeId,
            CreatedByManagerId = ManagerId,
            Title = "Splendor Cup Thủ Đức",
            GameTemplateId = SplendorId,
            StartTime = startTime,
            RegistrationDeadline = startTime.AddHours(-24),
            RoundDurationMinutes = 45,
            MinParticipants = 4,
            MaxParticipants = 8,
            PreliminaryRounds = 3,
            TotalRounds = 4,
            FinalistCount = 4,
            CurrentRound = 0,
            MinKarmaRequirement = 0,
            Status = TournamentStatus.RegistrationOpen,
            CreatedAt = DateTime.UtcNow,
            Participants = new List<TournamentParticipant>(),
            Matches = new List<TournamentMatchBracket>()
        };
    }

    private static Tournament BuildOnGoingTournament()
    {
        var t = BuildRegistrationOpenTournament();
        t.Status = TournamentStatus.OnGoing;
        t.CurrentRound = 1;
        return t;
    }

    private static User BuildUserWithProfile(Guid userId, int karma)
    {
        var user = new User { Id = userId, Username = $"user_{userId.ToString()[..8]}", Email = $"{userId}@test.com" };
        user.Profile = new UserProfile { UserId = userId, KarmaPoints = karma, GlobalElo = 1200 };
        return user;
    }

    // ============================================
    // === F14: MinParticipants from GameTemplate ===
    // ============================================

    [Fact]
    public async Task CreateTournamentAsync_MinParticipantsFromGameTemplate_Applied()
    {
        // Arrange
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        // F14: GameTemplate TournamentMinPlayersPerTable = 2 (vd Splendor Duel)
        var splendor = new GameTemplate
        {
            Id = SplendorId,
            Name = "Splendor Duel",
            IsActive = true,
            IsTournamentSupported = true,
            TournamentMaxScorePerPlayer = 15,
            TournamentMinPlayersPerTable = 2
        };

        gameRepo.Setup(r => r.GetByNameAsync("Splendor")).ReturnsAsync(splendor);
        gameRepo.Setup(r => r.GetByIdAsync(SplendorId)).ReturnsAsync(splendor);
        cafeRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        tournamentRepo.Setup(r => r.AddAsync(It.IsAny<Tournament>())).Returns(Task.CompletedTask);
        tournamentRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
        tournamentRepo.Setup(r => r.GetByIdWithDetailsAsync(It.IsAny<Guid>()))
            .ReturnsAsync(BuildRegistrationOpenTournament());

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        // Act: không set request.MinParticipants → lấy từ GameTemplate = 2
        await svc.CreateTournamentAsync(ManagerId, CafeId, new CreateTournamentRequestDto
        {
            Title = "Splendor Cup August 2026",
            StartTime = DateTime.UtcNow.AddDays(7),
            MaxParticipants = 8
        });

        // Assert: MinParticipants = 2 (từ GameTemplate), không phải hardcoded 4
        tournamentRepo.Verify(r => r.AddAsync(It.Is<Tournament>(
            t => t.MinParticipants == 2)), Times.Once);
    }

    [Fact]
    public async Task CreateTournamentAsync_ManagerOverrideHigherThanTemplate_Allowed()
    {
        // Arrange
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var splendor = new GameTemplate
        {
            Id = SplendorId,
            Name = "Splendor",
            IsTournamentSupported = true,
            TournamentMinPlayersPerTable = 2
        };

        gameRepo.Setup(r => r.GetByNameAsync("Splendor")).ReturnsAsync(splendor);
        gameRepo.Setup(r => r.GetByIdAsync(SplendorId)).ReturnsAsync(splendor);
        cafeRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        tournamentRepo.Setup(r => r.AddAsync(It.IsAny<Tournament>())).Returns(Task.CompletedTask);
        tournamentRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
        tournamentRepo.Setup(r => r.GetByIdWithDetailsAsync(It.IsAny<Guid>()))
            .ReturnsAsync(BuildRegistrationOpenTournament());

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        // Act: override MinParticipants = 8 (cao hơn template 2)
        await svc.CreateTournamentAsync(ManagerId, CafeId, new CreateTournamentRequestDto
        {
            Title = "Splendor Cup August 2026",
            StartTime = DateTime.UtcNow.AddDays(7),
            MaxParticipants = 16,
            MinParticipants = 8
        });

        // Assert: MinParticipants = max(template, override) = 8
        tournamentRepo.Verify(r => r.AddAsync(It.Is<Tournament>(
            t => t.MinParticipants == 8)), Times.Once);
    }

    [Fact]
    public async Task CreateTournamentAsync_ManagerOverrideLowerThanTemplate_ClampedToTemplate()
    {
        // Arrange
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var splendor = new GameTemplate
        {
            Id = SplendorId,
            Name = "Splendor",
            IsTournamentSupported = true,
            TournamentMinPlayersPerTable = 4
        };

        gameRepo.Setup(r => r.GetByNameAsync("Splendor")).ReturnsAsync(splendor);
        gameRepo.Setup(r => r.GetByIdAsync(SplendorId)).ReturnsAsync(splendor);
        cafeRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        tournamentRepo.Setup(r => r.AddAsync(It.IsAny<Tournament>())).Returns(Task.CompletedTask);
        tournamentRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
        tournamentRepo.Setup(r => r.GetByIdWithDetailsAsync(It.IsAny<Guid>()))
            .ReturnsAsync(BuildRegistrationOpenTournament());

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        // Act: cố override = 2 (thấp hơn template = 4)
        await svc.CreateTournamentAsync(ManagerId, CafeId, new CreateTournamentRequestDto
        {
            Title = "Splendor Cup August 2026",
            StartTime = DateTime.UtcNow.AddDays(7),
            MaxParticipants = 8,
            MinParticipants = 2
        });

        // Assert: clamped lên 4 (template config thắng)
        tournamentRepo.Verify(r => r.AddAsync(It.Is<Tournament>(
            t => t.MinParticipants == 4)), Times.Once);
    }

    // ============================================
    // === F15: SetPairingMode OnGoing + round chưa build ===
    // ============================================

    [Fact]
    public async Task SetPairingModeAsync_OnGoingButCurrentRoundHasNoMatches_Allowed()
    {
        // F15: Cho phép Auto → Manual khi đã OnGoing nhưng round hiện tại chưa build matches.
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var tournament = BuildOnGoingTournament();
        tournament.CurrentRound = 1;
        tournament.Matches = new List<TournamentMatchBracket>(); // chưa build matches

        cafeRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        tournamentRepo.Setup(r => r.GetByIdWithDetailsAsync(TournamentId)).ReturnsAsync(tournament);
        tournamentRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
        // BuildResponseAsync gọi GetByIdAsync để lấy GameTemplate
        gameRepo.Setup(r => r.GetByIdAsync(SplendorId))
            .ReturnsAsync(new GameTemplate { Id = SplendorId, Name = "Splendor" });

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        await svc.SetPairingModeAsync(ManagerId, TournamentId, TournamentPairingMode.Manual);

        Assert.Equal(TournamentPairingMode.Manual, tournament.PairingMode);
    }

    [Fact]
    public async Task SetPairingModeAsync_OnGoingAndCurrentRoundHasMatches_ThrowsConflict()
    {
        // F15: Không cho phép khi round hiện tại đã build matches.
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var tournament = BuildOnGoingTournament();
        tournament.CurrentRound = 1;
        tournament.Matches = new List<TournamentMatchBracket>
        {
            new() { Id = Guid.NewGuid(), TournamentId = TournamentId, RoundNumber = 1, MatchNumber = 1 }
        };

        cafeRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        tournamentRepo.Setup(r => r.GetByIdWithDetailsAsync(TournamentId)).ReturnsAsync(tournament);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        await Assert.ThrowsAsync<ConflictException>(
            () => svc.SetPairingModeAsync(ManagerId, TournamentId, TournamentPairingMode.Manual));
    }

    // ============================================
    // === F16: Walk-in auto-CheckedIn khi RegistrationClosed ===
    // ============================================

    [Fact]
    public async Task ManagerAddWalkInParticipantAsync_TournamentRegistrationOpen_StatusIsRegistered()
    {
        // F16: RegistrationOpen → walk-in là Registered (chưa đến quán)
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var tournament = BuildRegistrationOpenTournament();

        cafeRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        tournamentRepo.Setup(r => r.GetByIdWithDetailsAsync(TournamentId)).ReturnsAsync(tournament);
        TournamentParticipant? added = null;
        tournamentRepo.Setup(r => r.AddParticipantAsync(It.IsAny<TournamentParticipant>()))
            .Callback<TournamentParticipant>(p => added = p)
            .Returns(Task.CompletedTask);
        tournamentRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
        tournamentRepo.Setup(r => r.GetParticipantByIdAsync(It.IsAny<Guid>()))
            .ReturnsAsync(() => added);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        await svc.ManagerAddWalkInParticipantAsync(ManagerId, TournamentId,
            new AddWalkInParticipantRequestDto { DisplayName = "Walk-in A" });

        Assert.NotNull(added);
        Assert.Equal(TournamentParticipantStatus.Registered, added!.Status);
        Assert.Null(added.CheckedInAt);
    }

    [Fact]
    public async Task ManagerAddWalkInParticipantAsync_TournamentRegistrationClosed_StatusIsCheckedIn()
    {
        // F16: RegistrationClosed (chưa Start) → walk-in auto CheckedIn
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var tournament = BuildRegistrationOpenTournament();
        tournament.Status = TournamentStatus.RegistrationClosed;

        cafeRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        tournamentRepo.Setup(r => r.GetByIdWithDetailsAsync(TournamentId)).ReturnsAsync(tournament);
        TournamentParticipant? added = null;
        tournamentRepo.Setup(r => r.AddParticipantAsync(It.IsAny<TournamentParticipant>()))
            .Callback<TournamentParticipant>(p => added = p)
            .Returns(Task.CompletedTask);
        tournamentRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
        tournamentRepo.Setup(r => r.GetParticipantByIdAsync(It.IsAny<Guid>()))
            .ReturnsAsync(() => added);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        await svc.ManagerAddWalkInParticipantAsync(ManagerId, TournamentId,
            new AddWalkInParticipantRequestDto { DisplayName = "Walk-in B" });

        Assert.NotNull(added);
        Assert.Equal(TournamentParticipantStatus.CheckedIn, added!.Status);
        Assert.NotNull(added.CheckedInAt);
        Assert.Equal(ManagerId, added.CheckedInByStaffId);
    }

    // ============================================
    // === F12: AutoCloseExpiredRegistrationsAsync skip 0-participant ===
    // ============================================

    [Fact]
    public async Task AutoCloseExpiredRegistrationsAsync_NoParticipants_SkipsTournament()
    {
        // F12: Tournament không có participant nào + đã hết hạn → KHÔNG chuyển Closed.
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var tournament = BuildRegistrationOpenTournament();
        tournament.Participants = new List<TournamentParticipant>(); // 0 participants

        tournamentRepo.Setup(r => r.GetUpcomingForClosingAsync(It.IsAny<DateTime>()))
            .ReturnsAsync(new List<Tournament> { tournament });

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        var count = await svc.AutoCloseExpiredRegistrationsAsync(DateTime.UtcNow);

        Assert.Equal(0, count);
        Assert.Equal(TournamentStatus.RegistrationOpen, tournament.Status); // vẫn Open
    }

    [Fact]
    public async Task AutoCloseExpiredRegistrationsAsync_WithParticipants_ClosesRegistration()
    {
        var tournamentRepo = BuildTournamentRepo();
        var gameRepo = BuildGameRepo();
        var cafeRepo = BuildCafeRepo();
        var userRepo = BuildUserRepo();
        var configRepo = BuildConfigRepo();
        var karmaRepo = BuildKarmaRepo();

        var tournament = BuildRegistrationOpenTournament();
        tournament.Participants = new List<TournamentParticipant>
        {
            new() { Status = TournamentParticipantStatus.Registered }
        };

        tournamentRepo.Setup(r => r.GetUpcomingForClosingAsync(It.IsAny<DateTime>()))
            .ReturnsAsync(new List<Tournament> { tournament });
        tournamentRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        var svc = BuildService(tournamentRepo, gameRepo, cafeRepo, userRepo, configRepo, karmaRepo);

        var count = await svc.AutoCloseExpiredRegistrationsAsync(DateTime.UtcNow);

        Assert.Equal(1, count);
        Assert.Equal(TournamentStatus.RegistrationClosed, tournament.Status);
    }
}
