using BoardVerse.Core.DTOs.Session;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using Moq;

namespace BoardVerse.Tests.Services;

public class ActiveSessionServiceTests
{
    [Fact]
    public async Task CheckoutAsync_WithVerifiedComponents_ReturnsUnpaidSession()
    {
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Checking, // BR-12: Must be CHECKING (after EndGameSession)
            IsCheckingInventory = true,
            HasMissingComponents = false,
            Members = new List<ActiveSessionMember>(),
            GameTemplate = new GameTemplate
            {
                Id = Guid.NewGuid(),
                Name = "Catan",
                PlayTime = 60
            },
            CafeTable = new CafeTable
            {
                Id = Guid.NewGuid(),
                Name = "Table 1"
            },
            CafeInventoryBox = new CafeInventoryBox
            {
                Id = Guid.NewGuid(),
                Barcode = "BV-001"
            }
        };

        var repo = new Mock<IActiveSessionRepository>();
        repo.Setup(r => r.GetByIdAsync(sessionId)).ReturnsAsync(session);

        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId)).ReturnsAsync(new Cafe
        {
            Id = cafeId,
            Name = "Test Cafe",
            Address = "123 Test St",
            IsActive = true,
            PartnerOperationalStatus = CafePartnerOperationalStatus.Active
        });

        var posRepo = new Mock<ICafePosRepository>();
        // BR-12: Mock IsSessionFullyCheckedAsync to return true
        posRepo.Setup(r => r.IsSessionFullyCheckedAsync(sessionId)).ReturnsAsync(true);
        posRepo.Setup(r => r.GetSessionGamesAsync(sessionId)).ReturnsAsync(new List<ActiveSessionGame>());

        var depositRepo = new Mock<IBookingDepositRepository>();
        var lobbyRepo = new Mock<ILobbyRepository>();
        var settlementService = new Mock<ISettlementService>();

        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, lobbyRepo.Object, settlementService.Object);

        var request = new CheckoutRequestDto
        {
            ComponentsVerified = true,
            Components = new List<ComponentCheckoutItemDto>()
        };

        var result = await service.CheckoutAsync(cafeId, sessionId, request);

        Assert.Equal(GroupSessionStatus.Unpaid, result.Status);
        Assert.False(result.IsCheckingInventory);
    }

    /// <summary>
    /// BR-12: Checkout from ACTIVE status should be BLOCKED
    /// Staff must EndGameSession first to transition to CHECKING, then checkout.
    /// </summary>
    [Fact]
    public async Task CheckoutAsync_FromActiveStatus_ThrowsConflictException()
    {
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Active, // NOT Checking
            IsCheckingInventory = false,
            Members = new List<ActiveSessionMember>()
        };

        var repo = new Mock<IActiveSessionRepository>();
        repo.Setup(r => r.GetByIdAsync(sessionId)).ReturnsAsync(session);

        var posRepo = new Mock<ICafePosRepository>();
        var cafeRepo = new Mock<ICafeRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var lobbyRepo = new Mock<ILobbyRepository>();
        var settlementService = new Mock<ISettlementService>();

        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, lobbyRepo.Object, settlementService.Object);

        var request = new CheckoutRequestDto
        {
            ComponentsVerified = true,
            Components = new List<ComponentCheckoutItemDto>()
        };

        // BR-12: Must be CHECKING status
        await Assert.ThrowsAsync<ConflictException>(() =>
            service.CheckoutAsync(cafeId, sessionId, request));
    }

    /// <summary>
    /// BR-12: Checkout should be BLOCKED if session games are not fully checked.
    /// </summary>
    [Fact]
    public async Task CheckoutAsync_GamesNotFullyChecked_ThrowsBadRequestException()
    {
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var gameId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Checking,
            IsCheckingInventory = true,
            Members = new List<ActiveSessionMember>()
        };

        var repo = new Mock<IActiveSessionRepository>();
        repo.Setup(r => r.GetByIdAsync(sessionId)).ReturnsAsync(session);

        var posRepo = new Mock<ICafePosRepository>();
        // BR-12: Mock IsSessionFullyCheckedAsync to return false
        posRepo.Setup(r => r.IsSessionFullyCheckedAsync(sessionId)).ReturnsAsync(false);
        posRepo.Setup(r => r.GetSessionGamesAsync(sessionId)).ReturnsAsync(new List<ActiveSessionGame>
        {
            new ActiveSessionGame { Id = gameId, CheckStatus = ComponentCheckStatus.NotChecked }
        });

        var cafeRepo = new Mock<ICafeRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var lobbyRepo = new Mock<ILobbyRepository>();
        var settlementService = new Mock<ISettlementService>();

        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, lobbyRepo.Object, settlementService.Object);

        var request = new CheckoutRequestDto
        {
            ComponentsVerified = true,
            Components = new List<ComponentCheckoutItemDto>()
        };

        // BR-12: Must complete checklist for ALL games
        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            service.CheckoutAsync(cafeId, sessionId, request));

        Assert.Contains("BR-12", ex.Message);
    }

    [Fact]
    public async Task AddGuestSlotAsync_WithActiveSession_AddsGuestMember()
    {
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Active,
            Members = new List<ActiveSessionMember>(),
            GameTemplate = new GameTemplate
            {
                Id = Guid.NewGuid(),
                Name = "Catan",
                PlayTime = 60
            },
            CafeTable = new CafeTable
            {
                Id = Guid.NewGuid(),
                Name = "Table 1"
            },
            CafeInventoryBox = new CafeInventoryBox
            {
                Id = Guid.NewGuid(),
                Barcode = "BV-001"
            }
        };

        var repo = new Mock<IActiveSessionRepository>();
        repo.Setup(r => r.GetByIdAsync(sessionId)).ReturnsAsync(session);

        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId)).ReturnsAsync(new Cafe
        {
            Id = cafeId,
            Name = "Test Cafe",
            Address = "123 Test St",
            IsActive = true,
            PartnerOperationalStatus = CafePartnerOperationalStatus.Active
        });

        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var lobbyRepo = new Mock<ILobbyRepository>();
        var settlementService = new Mock<ISettlementService>();

        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, lobbyRepo.Object, settlementService.Object);

        var request = new AddGuestSlotRequestDto { DisplayName = "Guest 1" };
        var result = await service.AddGuestSlotAsync(cafeId, sessionId, request);

        Assert.Equal(GroupSessionStatus.Active, result.Status);
    }

    #region CheckoutAsync

    [Fact]
    public async Task CheckoutAsync_SessionNotActive_ThrowsConflictException()
    {
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Paid,
            Members = new List<ActiveSessionMember>(),
            Games = new List<ActiveSessionGame>()
        };

        var repo = new Mock<IActiveSessionRepository>();
        repo.Setup(r => r.GetByIdAsync(sessionId)).ReturnsAsync(session);

        var cafeRepo = new Mock<ICafeRepository>();
        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var lobbyRepo = new Mock<ILobbyRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, lobbyRepo.Object, settlementService.Object);

        var request = new CheckoutRequestDto { ComponentsVerified = true };

        await Assert.ThrowsAsync<ConflictException>(
            () => service.CheckoutAsync(cafeId, sessionId, request));
    }

    #endregion

    #region PartialCheckoutAsync

    [Fact]
    public async Task PartialCheckoutAsync_SessionNotActive_ThrowsConflictException()
    {
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Paid,
            Members = new List<ActiveSessionMember>(),
            Games = new List<ActiveSessionGame>()
        };

        var repo = new Mock<IActiveSessionRepository>();
        repo.Setup(r => r.GetByIdAsync(sessionId)).ReturnsAsync(session);

        var cafeRepo = new Mock<ICafeRepository>();
        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var lobbyRepo = new Mock<ILobbyRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, lobbyRepo.Object, settlementService.Object);

        var request = new PartialCheckoutRequestDto { MemberIds = new List<Guid> { Guid.NewGuid() } };

        await Assert.ThrowsAsync<ConflictException>(
            () => service.PartialCheckoutAsync(cafeId, sessionId, request));
    }

    [Fact]
    public async Task PartialCheckoutAsync_EmptyMemberList_ThrowsBadRequestException()
    {
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Active,
            Members = new List<ActiveSessionMember>(),
            Games = new List<ActiveSessionGame>()
        };

        var repo = new Mock<IActiveSessionRepository>();
        repo.Setup(r => r.GetByIdAsync(sessionId)).ReturnsAsync(session);

        var cafeRepo = new Mock<ICafeRepository>();
        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var lobbyRepo = new Mock<ILobbyRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, lobbyRepo.Object, settlementService.Object);

        var request = new PartialCheckoutRequestDto { MemberIds = new List<Guid>() };

        await Assert.ThrowsAsync<BadRequestException>(
            () => service.PartialCheckoutAsync(cafeId, sessionId, request));
    }

    [Fact]
    public async Task PartialCheckoutAsync_MemberAlreadyFinished_ThrowsConflictException()
    {
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Active,
            Members = new List<ActiveSessionMember>
            {
                new ActiveSessionMember
                {
                    Id = memberId,
                    UserId = Guid.NewGuid(),
                    Status = IndividualSessionStatus.Finished
                }
            },
            Games = new List<ActiveSessionGame>()
        };

        var repo = new Mock<IActiveSessionRepository>();
        repo.Setup(r => r.GetByIdAsync(sessionId)).ReturnsAsync(session);

        var cafeRepo = new Mock<ICafeRepository>();
        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var lobbyRepo = new Mock<ILobbyRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, lobbyRepo.Object, settlementService.Object);

        var request = new PartialCheckoutRequestDto { MemberIds = new List<Guid> { memberId } };

        await Assert.ThrowsAsync<ConflictException>(
            () => service.PartialCheckoutAsync(cafeId, sessionId, request));
    }

    [Fact]
    public async Task PartialCheckoutAsync_ValidRequest_TransitionsToChecking()
    {
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Active,
            Members = new List<ActiveSessionMember>
            {
                new ActiveSessionMember
                {
                    Id = memberId,
                    UserId = Guid.NewGuid(),
                    Status = IndividualSessionStatus.Playing
                }
            },
            Games = new List<ActiveSessionGame>(),
            GameTemplate = new GameTemplate { Id = Guid.NewGuid(), Name = "Catan", PlayTime = 60 },
            CafeTable = new CafeTable { Id = Guid.NewGuid(), Name = "Table 1" },
            CafeInventoryBox = new CafeInventoryBox { Id = Guid.NewGuid(), Barcode = "BV-001" }
        };

        var repo = new Mock<IActiveSessionRepository>();
        repo.Setup(r => r.GetByIdAsync(sessionId)).ReturnsAsync(session);

        var cafeRepo = new Mock<ICafeRepository>();
        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var lobbyRepo = new Mock<ILobbyRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, lobbyRepo.Object, settlementService.Object);

        var request = new PartialCheckoutRequestDto { MemberIds = new List<Guid> { memberId } };

        var result = await service.PartialCheckoutAsync(cafeId, sessionId, request);

        Assert.Equal(GroupSessionStatus.Checking, result.Status);
        Assert.True(result.IsCheckingInventory);
    }

    #endregion

    #region MergeSessionAsync — EX-04: A3 jumps from Group A to Group B

    [Fact]
    public async Task MergeSessionAsync_MemberNotInSourceSession_ThrowsConflictException()
    {
        var cafeId = Guid.NewGuid();
        var sourceSessionId = Guid.NewGuid();
        var targetSessionId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        var sourceSession = new ActiveSession
        {
            Id = sourceSessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Checking,
            Members = new List<ActiveSessionMember>(),
            Games = new List<ActiveSessionGame>()
        };

        var targetSession = new ActiveSession
        {
            Id = targetSessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Active,
            Members = new List<ActiveSessionMember>(),
            Games = new List<ActiveSessionGame>()
        };

        var repo = new Mock<IActiveSessionRepository>();
        repo.Setup(r => r.GetByIdAsync(sourceSessionId)).ReturnsAsync(sourceSession);
        repo.Setup(r => r.GetByIdAsync(targetSessionId)).ReturnsAsync(targetSession);
        repo.Setup(r => r.GetMemberByIdAsync(memberId)).ReturnsAsync((ActiveSessionMember?)null);

        var cafeRepo = new Mock<ICafeRepository>();
        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var lobbyRepo = new Mock<ILobbyRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, lobbyRepo.Object, settlementService.Object);

        var request = new MergeSessionRequestDto { MemberId = memberId, TargetSessionId = targetSessionId };

        await Assert.ThrowsAsync<NotFoundException>(
            () => service.MergeSessionAsync(cafeId, sourceSessionId, request));
    }

    [Fact]
    public async Task MergeSessionAsync_MemberNotSuspendedMutation_ThrowsConflictException()
    {
        var cafeId = Guid.NewGuid();
        var sourceSessionId = Guid.NewGuid();
        var targetSessionId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        var member = new ActiveSessionMember
        {
            Id = memberId,
            ActiveSessionId = sourceSessionId,
            UserId = Guid.NewGuid(),
            Status = IndividualSessionStatus.Playing
        };

        var sourceSession = new ActiveSession
        {
            Id = sourceSessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Active,
            Members = new List<ActiveSessionMember> { member },
            Games = new List<ActiveSessionGame>()
        };

        var targetSession = new ActiveSession
        {
            Id = targetSessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Active,
            Members = new List<ActiveSessionMember>(),
            Games = new List<ActiveSessionGame>()
        };

        var repo = new Mock<IActiveSessionRepository>();
        repo.Setup(r => r.GetByIdAsync(sourceSessionId)).ReturnsAsync(sourceSession);
        repo.Setup(r => r.GetByIdAsync(targetSessionId)).ReturnsAsync(targetSession);
        repo.Setup(r => r.GetMemberByIdAsync(memberId)).ReturnsAsync(member);

        var cafeRepo = new Mock<ICafeRepository>();
        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var lobbyRepo = new Mock<ILobbyRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, lobbyRepo.Object, settlementService.Object);

        var request = new MergeSessionRequestDto { MemberId = memberId, TargetSessionId = targetSessionId };

        await Assert.ThrowsAsync<ConflictException>(
            () => service.MergeSessionAsync(cafeId, sourceSessionId, request));
    }

    [Fact]
    public async Task MergeSessionAsync_TargetSessionNotActive_ThrowsConflictException()
    {
        var cafeId = Guid.NewGuid();
        var sourceSessionId = Guid.NewGuid();
        var targetSessionId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        var member = new ActiveSessionMember
        {
            Id = memberId,
            ActiveSessionId = sourceSessionId,
            UserId = Guid.NewGuid(),
            Status = IndividualSessionStatus.SuspendedMutation
        };

        var sourceSession = new ActiveSession
        {
            Id = sourceSessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Checking,
            Members = new List<ActiveSessionMember> { member },
            Games = new List<ActiveSessionGame>()
        };

        var targetSession = new ActiveSession
        {
            Id = targetSessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Paid,
            Members = new List<ActiveSessionMember>(),
            Games = new List<ActiveSessionGame>()
        };

        var repo = new Mock<IActiveSessionRepository>();
        repo.Setup(r => r.GetByIdAsync(sourceSessionId)).ReturnsAsync(sourceSession);
        repo.Setup(r => r.GetByIdAsync(targetSessionId)).ReturnsAsync(targetSession);
        repo.Setup(r => r.GetMemberByIdAsync(memberId)).ReturnsAsync(member);

        var cafeRepo = new Mock<ICafeRepository>();
        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var lobbyRepo = new Mock<ILobbyRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, lobbyRepo.Object, settlementService.Object);

        var request = new MergeSessionRequestDto { MemberId = memberId, TargetSessionId = targetSessionId };

        await Assert.ThrowsAsync<ConflictException>(
            () => service.MergeSessionAsync(cafeId, sourceSessionId, request));
    }

    [Fact]
    public async Task MergeSessionAsync_CrossCafeAttempt_ThrowsConflictException()
    {
        var cafeId = Guid.NewGuid();
        var sourceSessionId = Guid.NewGuid();
        var targetSessionId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        var member = new ActiveSessionMember
        {
            Id = memberId,
            ActiveSessionId = sourceSessionId,
            UserId = Guid.NewGuid(),
            Status = IndividualSessionStatus.SuspendedMutation
        };

        var sourceSession = new ActiveSession
        {
            Id = sourceSessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Checking,
            Members = new List<ActiveSessionMember> { member },
            Games = new List<ActiveSessionGame>()
        };

        var targetSession = new ActiveSession
        {
            Id = targetSessionId,
            CafeId = Guid.NewGuid(), // different cafe!
            Status = GroupSessionStatus.Active,
            Members = new List<ActiveSessionMember>(),
            Games = new List<ActiveSessionGame>()
        };

        var repo = new Mock<IActiveSessionRepository>();
        repo.Setup(r => r.GetByIdAsync(sourceSessionId)).ReturnsAsync(sourceSession);
        repo.Setup(r => r.GetByIdAsync(targetSessionId)).ReturnsAsync(targetSession);
        repo.Setup(r => r.GetMemberByIdAsync(memberId)).ReturnsAsync(member);

        var cafeRepo = new Mock<ICafeRepository>();
        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var lobbyRepo = new Mock<ILobbyRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, lobbyRepo.Object, settlementService.Object);

        var request = new MergeSessionRequestDto { MemberId = memberId, TargetSessionId = targetSessionId };

        await Assert.ThrowsAsync<ConflictException>(
            () => service.MergeSessionAsync(cafeId, sourceSessionId, request));
    }

    [Fact]
    public async Task MergeSessionAsync_ValidRequest_MergesMemberIntoTargetSession()
    {
        var cafeId = Guid.NewGuid();
        var sourceSessionId = Guid.NewGuid();
        var targetSessionId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        var member = new ActiveSessionMember
        {
            Id = memberId,
            ActiveSessionId = sourceSessionId,
            UserId = Guid.NewGuid(),
            Status = IndividualSessionStatus.SuspendedMutation
        };

        var sourceSession = new ActiveSession
        {
            Id = sourceSessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Checking,
            Members = new List<ActiveSessionMember> { member },
            Games = new List<ActiveSessionGame>()
        };

        var targetSession = new ActiveSession
        {
            Id = targetSessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Active,
            Members = new List<ActiveSessionMember>(),
            Games = new List<ActiveSessionGame>(),
            GameTemplate = new GameTemplate { Id = Guid.NewGuid(), Name = "Catan", PlayTime = 60 },
            CafeTable = new CafeTable { Id = Guid.NewGuid(), Name = "Table 1" },
            CafeInventoryBox = new CafeInventoryBox { Id = Guid.NewGuid(), Barcode = "BV-001" }
        };

        var repo = new Mock<IActiveSessionRepository>();
        repo.Setup(r => r.GetByIdAsync(sourceSessionId)).ReturnsAsync(sourceSession);
        repo.SetupSequence(r => r.GetByIdAsync(targetSessionId))
            .ReturnsAsync(targetSession)
            .ReturnsAsync(targetSession);
        repo.Setup(r => r.GetMemberByIdAsync(memberId)).ReturnsAsync(member);

        var cafeRepo = new Mock<ICafeRepository>();
        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var lobbyRepo = new Mock<ILobbyRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, lobbyRepo.Object, settlementService.Object);

        var request = new MergeSessionRequestDto { MemberId = memberId, TargetSessionId = targetSessionId };

        var result = await service.MergeSessionAsync(cafeId, sourceSessionId, request);

        Assert.Equal(memberId, result.MemberId);
        Assert.Equal(sourceSessionId, result.SourceSessionId);
        Assert.Equal(targetSessionId, result.TargetSessionId);
        repo.Verify(r => r.UpdateMemberAsync(It.Is<ActiveSessionMember>(m => m.Status == IndividualSessionStatus.Playing)), Times.Once);
    }

    #endregion

    #region PaySessionAsync — BR-09, BR-14, BR-15

    [Fact]
    public async Task PaySessionAsync_SessionNotUnpaid_ThrowsConflictException()
    {
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Active,
            Members = new List<ActiveSessionMember>(),
            Games = new List<ActiveSessionGame>()
        };

        var repo = new Mock<IActiveSessionRepository>();
        repo.Setup(r => r.GetByIdAsync(sessionId)).ReturnsAsync(session);

        var cafeRepo = new Mock<ICafeRepository>();
        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var lobbyRepo = new Mock<ILobbyRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, lobbyRepo.Object, settlementService.Object);

        var request = new PaySessionRequestDto();

        await Assert.ThrowsAsync<ConflictException>(
            () => service.PaySessionAsync(cafeId, sessionId, request));
    }

    [Fact]
    public async Task PaySessionAsync_Br14_PenaltyOnGuestSlot_ThrowsBadRequestException()
    {
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var guestMemberId = Guid.NewGuid();

        var guestMember = new ActiveSessionMember
        {
            Id = guestMemberId,
            UserId = null,
            IsGuestSlot = true,
            Status = IndividualSessionStatus.Playing
        };

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Unpaid,
            StartedAt = DateTime.UtcNow.AddHours(-2),
            Members = new List<ActiveSessionMember> { guestMember },
            Games = new List<ActiveSessionGame>(),
            GameTemplate = new GameTemplate { Id = Guid.NewGuid(), Name = "Catan", PlayTime = 60 },
            CafeTable = new CafeTable { Id = Guid.NewGuid(), Name = "Table 1" },
            CafeInventoryBox = new CafeInventoryBox { Id = Guid.NewGuid(), Barcode = "BV-001" }
        };

        var repo = new Mock<IActiveSessionRepository>();
        repo.Setup(r => r.GetByIdAsync(sessionId)).ReturnsAsync(session);
        repo.Setup(r => r.GetMemberByIdAsync(guestMemberId)).ReturnsAsync(guestMember);

        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId)).ReturnsAsync(new Cafe
        {
            Id = cafeId,
            Name = "Test Cafe",
            Address = "123 St",
            BillingModel = CafePartnerBillingModel.TimeBased,
            BasePrice = 60_000m
        });

        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var lobbyRepo = new Mock<ILobbyRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, lobbyRepo.Object, settlementService.Object);

        var request = new PaySessionRequestDto
        {
            PenaltyItems = new List<ComponentPenaltyItemDto>
            {
                new()
                {
                    ComponentId = Guid.NewGuid(),
                    ComponentName = "Road",
                    PenaltyAmount = 15_000m,
                    ResponsibleMemberId = guestMemberId
                }
            }
        };

        var ex = await Assert.ThrowsAsync<BadRequestException>(
            () => service.PaySessionAsync(cafeId, sessionId, request));

        Assert.Contains("BR-14", ex.Message);
    }

    [Fact]
    public async Task PaySessionAsync_TimeBasedBilling_CalculatesCorrectly()
    {
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Unpaid,
            StartedAt = DateTime.UtcNow.AddHours(-3), // 180 minutes
            Members = new List<ActiveSessionMember>(),
            Games = new List<ActiveSessionGame>(),
            GameTemplate = new GameTemplate { Id = Guid.NewGuid(), Name = "Catan", PlayTime = 60 },
            CafeTable = new CafeTable { Id = Guid.NewGuid(), Name = "Table 1" },
            CafeInventoryBox = new CafeInventoryBox { Id = Guid.NewGuid(), Barcode = "BV-001" }
        };

        var cafe = new Cafe
        {
            Id = cafeId,
            Name = "Test Cafe",
            Address = "123 St",
            BillingModel = CafePartnerBillingModel.TimeBased,
            BasePrice = 60_000m, // first 60 min
            TieredBlockMinutes = 15,
            TieredBlockRate = 10_000m // each additional 15-min block
        };

        var repo = new Mock<IActiveSessionRepository>();
        repo.SetupSequence(r => r.GetByIdAsync(sessionId))
            .ReturnsAsync(session)
            .ReturnsAsync(session)
            .ReturnsAsync(session);

        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId)).ReturnsAsync(cafe);

        var posRepo = new Mock<ICafePosRepository>();
        posRepo.Setup(r => r.GetSessionGamesAsync(sessionId)).ReturnsAsync(new List<ActiveSessionGame>());
        var depositRepo = new Mock<IBookingDepositRepository>();
        depositRepo.Setup(r => r.GetByActiveSessionIdAsync(sessionId)).ReturnsAsync((BookingDeposit?)null);

        var lobbyRepo = new Mock<ILobbyRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, lobbyRepo.Object, settlementService.Object);

        var request = new PaySessionRequestDto();

        var result = await service.PaySessionAsync(cafeId, sessionId, request);

        Assert.Equal(GroupSessionStatus.Paid, result.Session.Status);
        // 180 min: 60_000 (first hour) + ceil(120/15) * 10_000 = 60_000 + 8*10_000 = 140_000
        Assert.Equal(140_000m, result.Subtotal);
    }

    [Fact]
    public async Task PaySessionAsync_TimeBasedBilling_UnderOneHour_ChargesBasePrice()
    {
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Unpaid,
            StartedAt = DateTime.UtcNow.AddMinutes(-45),
            Members = new List<ActiveSessionMember>(),
            Games = new List<ActiveSessionGame>(),
            GameTemplate = new GameTemplate { Id = Guid.NewGuid(), Name = "Catan", PlayTime = 60 },
            CafeTable = new CafeTable { Id = Guid.NewGuid(), Name = "Table 1" },
            CafeInventoryBox = new CafeInventoryBox { Id = Guid.NewGuid(), Barcode = "BV-001" }
        };

        var cafe = new Cafe
        {
            Id = cafeId,
            Name = "Test Cafe",
            Address = "123 St",
            BillingModel = CafePartnerBillingModel.TimeBased,
            BasePrice = 50_000m,
            TieredBlockMinutes = 15,
            TieredBlockRate = 10_000m
        };

        var repo = new Mock<IActiveSessionRepository>();
        repo.SetupSequence(r => r.GetByIdAsync(sessionId))
            .ReturnsAsync(session)
            .ReturnsAsync(session);

        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId)).ReturnsAsync(cafe);

        var posRepo = new Mock<ICafePosRepository>();
        posRepo.Setup(r => r.GetSessionGamesAsync(sessionId)).ReturnsAsync(new List<ActiveSessionGame>());
        var depositRepo = new Mock<IBookingDepositRepository>();
        depositRepo.Setup(r => r.GetByActiveSessionIdAsync(sessionId)).ReturnsAsync((BookingDeposit?)null);

        var lobbyRepo = new Mock<ILobbyRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, lobbyRepo.Object, settlementService.Object);

        var request = new PaySessionRequestDto();

        var result = await service.PaySessionAsync(cafeId, sessionId, request);

        Assert.Equal(50_000m, result.Subtotal); // under 60 min → base price only
    }

    [Fact]
    public async Task PaySessionAsync_PackageModel_ChargesBasePriceOnly()
    {
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Unpaid,
            StartedAt = DateTime.UtcNow.AddHours(-5),
            Members = new List<ActiveSessionMember>(),
            Games = new List<ActiveSessionGame>(),
            GameTemplate = new GameTemplate { Id = Guid.NewGuid(), Name = "Catan", PlayTime = 60 },
            CafeTable = new CafeTable { Id = Guid.NewGuid(), Name = "Table 1" },
            CafeInventoryBox = new CafeInventoryBox { Id = Guid.NewGuid(), Barcode = "BV-001" }
        };

        var cafe = new Cafe
        {
            Id = cafeId,
            Name = "Test Cafe",
            Address = "123 St",
            BillingModel = CafePartnerBillingModel.FlatEntry,
            BasePrice = 80_000m // flat entrance fee
        };

        var repo = new Mock<IActiveSessionRepository>();
        repo.SetupSequence(r => r.GetByIdAsync(sessionId))
            .ReturnsAsync(session)
            .ReturnsAsync(session);

        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId)).ReturnsAsync(cafe);

        var posRepo = new Mock<ICafePosRepository>();
        posRepo.Setup(r => r.GetSessionGamesAsync(sessionId)).ReturnsAsync(new List<ActiveSessionGame>());
        var depositRepo = new Mock<IBookingDepositRepository>();
        depositRepo.Setup(r => r.GetByActiveSessionIdAsync(sessionId)).ReturnsAsync((BookingDeposit?)null);

        var lobbyRepo = new Mock<ILobbyRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, lobbyRepo.Object, settlementService.Object);

        var request = new PaySessionRequestDto();

        var result = await service.PaySessionAsync(cafeId, sessionId, request);

        Assert.Equal(80_000m, result.Subtotal);
    }

    [Fact]
    public async Task PaySessionAsync_Br09_DepositAppliedOnce_ToSessionBill()
    {
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var depositId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Unpaid,
            StartedAt = DateTime.UtcNow.AddMinutes(-120), // 120 minutes (2 hours)
            Members = new List<ActiveSessionMember>(),
            Games = new List<ActiveSessionGame>(),
            GameTemplate = new GameTemplate { Id = Guid.NewGuid(), Name = "Catan", PlayTime = 60 },
            CafeTable = new CafeTable { Id = Guid.NewGuid(), Name = "Table 1" },
            CafeInventoryBox = new CafeInventoryBox { Id = Guid.NewGuid(), Barcode = "BV-001" }
        };

        var deposit = new BookingDeposit
        {
            Id = depositId,
            ActiveSessionId = sessionId,
            Amount = 50_000m,
            Status = BookingDepositStatus.Paid
        };

        var cafe = new Cafe
        {
            Id = cafeId,
            Name = "Test Cafe",
            Address = "123 St",
            BillingModel = CafePartnerBillingModel.TimeBased,
            BasePrice = 60_000m,
            TieredBlockMinutes = 15,
            TieredBlockRate = 10_000m
        };

        var repo = new Mock<IActiveSessionRepository>();
        repo.SetupSequence(r => r.GetByIdAsync(sessionId))
            .ReturnsAsync(session)
            .ReturnsAsync(session)
            .ReturnsAsync(session);

        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId)).ReturnsAsync(cafe);

        var posRepo = new Mock<ICafePosRepository>();
        posRepo.Setup(r => r.GetSessionGamesAsync(sessionId)).ReturnsAsync(new List<ActiveSessionGame>());
        var depositRepo = new Mock<IBookingDepositRepository>();
        depositRepo.Setup(r => r.GetByActiveSessionIdAsync(sessionId)).ReturnsAsync(deposit);

        var lobbyRepo = new Mock<ILobbyRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, lobbyRepo.Object, settlementService.Object);

        var request = new PaySessionRequestDto();

        var result = await service.PaySessionAsync(cafeId, sessionId, request);

        Assert.Equal(50_000m, result.DepositAppliedAmount);
        // 120 min: 60_000 + ceil(60/15)*10_000 = 60_000 + 4*10_000 = 100_000
        // TotalAmount = Subtotal(100_000) + PenaltyAmount(0) - DepositAppliedAmount(50_000) = 50_000
        Assert.Equal(50_000m, result.TotalAmount);
    }

    [Fact]
    public async Task PaySessionAsync_WithPenalty_AddsToTotal()
    {
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        var member = new ActiveSessionMember
        {
            Id = memberId,
            UserId = Guid.NewGuid(),
            Status = IndividualSessionStatus.Playing,
            IsGuestSlot = false
        };

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Unpaid,
            StartedAt = DateTime.UtcNow.AddHours(-1),
            Members = new List<ActiveSessionMember> { member },
            Games = new List<ActiveSessionGame>(),
            GameTemplate = new GameTemplate { Id = Guid.NewGuid(), Name = "Catan", PlayTime = 60 },
            CafeTable = new CafeTable { Id = Guid.NewGuid(), Name = "Table 1" },
            CafeInventoryBox = new CafeInventoryBox { Id = Guid.NewGuid(), Barcode = "BV-001" }
        };

        var cafe = new Cafe
        {
            Id = cafeId,
            Name = "Test Cafe",
            Address = "123 St",
            BillingModel = CafePartnerBillingModel.TimeBased,
            BasePrice = 60_000m,
            TieredBlockMinutes = 15,
            TieredBlockRate = 10_000m
        };

        var repo = new Mock<IActiveSessionRepository>();
        repo.SetupSequence(r => r.GetByIdAsync(sessionId))
            .ReturnsAsync(session)
            .ReturnsAsync(session)
            .ReturnsAsync(session);

        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId)).ReturnsAsync(cafe);

        var posRepo = new Mock<ICafePosRepository>();
        posRepo.Setup(r => r.GetSessionGamesAsync(sessionId)).ReturnsAsync(new List<ActiveSessionGame>());
        var depositRepo = new Mock<IBookingDepositRepository>();
        depositRepo.Setup(r => r.GetByActiveSessionIdAsync(sessionId)).ReturnsAsync((BookingDeposit?)null);

        var lobbyRepo = new Mock<ILobbyRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, lobbyRepo.Object, settlementService.Object);

        var request = new PaySessionRequestDto
        {
            PenaltyItems = new List<ComponentPenaltyItemDto>
            {
                new()
                {
                    ComponentId = Guid.NewGuid(),
                    ComponentName = "Road piece",
                    PenaltyAmount = 15_000m,
                    ResponsibleMemberId = memberId
                }
            }
        };

        var result = await service.PaySessionAsync(cafeId, sessionId, request);

        Assert.Equal(15_000m, result.PenaltyAmount);
        // Subtotal (60_000) + 15_000 penalty - 0 deposit = 75_000
        Assert.Equal(75_000m, result.TotalAmount);
    }

    #endregion

    #region AttachGameAsync — EX-06: Extra game added without scanning

    [Fact]
    public async Task AttachGameAsync_GameAlreadyAssigned_ThrowsConflictException()
    {
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var boxId = Guid.NewGuid();

        var box = new CafeInventoryBox { Id = boxId, Barcode = "BV-EXTRA" };
        var existingGame = new ActiveSessionGame { Id = Guid.NewGuid(), CafeInventoryBoxId = boxId };

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Active,
            Members = new List<ActiveSessionMember>(),
            Games = new List<ActiveSessionGame> { existingGame }
        };

        var repo = new Mock<IActiveSessionRepository>();
        repo.Setup(r => r.GetByIdAsync(sessionId)).ReturnsAsync(session);

        var posRepo = new Mock<ICafePosRepository>();
        posRepo.Setup(r => r.GetBoxByBarcodeAsync(cafeId, "BV-EXTRA")).ReturnsAsync(box);

        var cafeRepo = new Mock<ICafeRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var lobbyRepo = new Mock<ILobbyRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, lobbyRepo.Object, settlementService.Object);

        var request = new AttachGameRequestDto { GameBarcode = "BV-EXTRA" };

        await Assert.ThrowsAsync<ConflictException>(
            () => service.AttachGameAsync(cafeId, sessionId, request));
    }

    [Fact]
    public async Task AttachGameAsync_ValidBarcode_AttachesGameToSession()
    {
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var boxId = Guid.NewGuid();

        var box = new CafeInventoryBox { Id = boxId, Barcode = "BV-EXTRA" };

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Active,
            Members = new List<ActiveSessionMember>(),
            Games = new List<ActiveSessionGame>(),
            GameTemplate = new GameTemplate { Id = Guid.NewGuid(), Name = "Catan", PlayTime = 60 },
            CafeTable = new CafeTable { Id = Guid.NewGuid(), Name = "Table 1" },
            CafeInventoryBox = new CafeInventoryBox { Id = Guid.NewGuid(), Barcode = "BV-001" }
        };

        var repo = new Mock<IActiveSessionRepository>();
        repo.Setup(r => r.GetByIdAsync(sessionId)).ReturnsAsync(session);
        repo.SetupSequence(r => r.GetByIdAsync(sessionId))
            .ReturnsAsync(session)
            .ReturnsAsync(session);

        var posRepo = new Mock<ICafePosRepository>();
        posRepo.Setup(r => r.GetBoxByBarcodeAsync(cafeId, "BV-EXTRA")).ReturnsAsync(box);

        var cafeRepo = new Mock<ICafeRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var lobbyRepo = new Mock<ILobbyRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, lobbyRepo.Object, settlementService.Object);

        var request = new AttachGameRequestDto { GameBarcode = "BV-EXTRA" };

        var result = await service.AttachGameAsync(cafeId, sessionId, request);

        repo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    #endregion

    #region AddLateMemberAsync — EX-08: Late members joining active session

    [Fact]
    public async Task AddLateMemberAsync_EmptyMemberList_ThrowsBadRequestException()
    {
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Active,
            Members = new List<ActiveSessionMember>(),
            Games = new List<ActiveSessionGame>()
        };

        var repo = new Mock<IActiveSessionRepository>();
        repo.Setup(r => r.GetByIdAsync(sessionId)).ReturnsAsync(session);

        var cafeRepo = new Mock<ICafeRepository>();
        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var lobbyRepo = new Mock<ILobbyRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, lobbyRepo.Object, settlementService.Object);

        var request = new AddLateMemberRequestDto { MemberUserIds = new List<Guid>() };

        await Assert.ThrowsAsync<BadRequestException>(
            () => service.AddLateMemberAsync(cafeId, sessionId, request));
    }

    [Fact]
    public async Task AddLateMemberAsync_SessionNotActive_ThrowsConflictException()
    {
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Paid,
            Members = new List<ActiveSessionMember>(),
            Games = new List<ActiveSessionGame>()
        };

        var repo = new Mock<IActiveSessionRepository>();
        repo.Setup(r => r.GetByIdAsync(sessionId)).ReturnsAsync(session);

        var cafeRepo = new Mock<ICafeRepository>();
        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var lobbyRepo = new Mock<ILobbyRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, lobbyRepo.Object, settlementService.Object);

        var request = new AddLateMemberRequestDto { MemberUserIds = new List<Guid> { Guid.NewGuid() } };

        await Assert.ThrowsAsync<ConflictException>(
            () => service.AddLateMemberAsync(cafeId, sessionId, request));
    }

    [Fact]
    public async Task AddLateMemberAsync_ValidMembers_AddsToActiveSession()
    {
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var newUserId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Active,
            Members = new List<ActiveSessionMember>(),
            Games = new List<ActiveSessionGame>(),
            GameTemplate = new GameTemplate { Id = Guid.NewGuid(), Name = "Catan", PlayTime = 60 },
            CafeTable = new CafeTable { Id = Guid.NewGuid(), Name = "Table 1" },
            CafeInventoryBox = new CafeInventoryBox { Id = Guid.NewGuid(), Barcode = "BV-001" }
        };

        var repo = new Mock<IActiveSessionRepository>();
        repo.Setup(r => r.GetByIdAsync(sessionId)).ReturnsAsync(session);
        repo.SetupSequence(r => r.GetByIdAsync(sessionId))
            .ReturnsAsync(session)
            .ReturnsAsync(session);

        var cafeRepo = new Mock<ICafeRepository>();
        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var lobbyRepo = new Mock<ILobbyRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, lobbyRepo.Object, settlementService.Object);

        var request = new AddLateMemberRequestDto { MemberUserIds = new List<Guid> { newUserId } };

        var result = await service.AddLateMemberAsync(cafeId, sessionId, request);

        repo.Verify(r => r.AddMemberAsync(It.Is<ActiveSessionMember>(m => m.UserId == newUserId)), Times.Once);
    }

    #endregion

    #region GetAlternativeCafesAsync — EX-01: Lobby full but cafe out of seats

    [Fact]
    public async Task GetAlternativeCafesAsync_CafeHasGameAndEnoughSeats_ReturnsCafe()
    {
        var gameId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();

        var cafe = new Cafe
        {
            Id = cafeId,
            Name = "Nearby Cafe",
            Address = "456 Nearby St",
            TotalSeats = 10,
            Inventories = new List<CafeGameInventory>
            {
                new CafeGameInventory { GameTemplateId = gameId }
            }
        };

        var repo = new Mock<IActiveSessionRepository>();
        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetNearbyCafesAsync(cafeId, 10)).ReturnsAsync(new List<Cafe> { cafe });

        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var lobbyRepo = new Mock<ILobbyRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, lobbyRepo.Object, settlementService.Object);

        var result = await service.GetAlternativeCafesAsync(cafeId, gameId, 3, DateTime.UtcNow.AddHours(2));

        Assert.Single(result.Cafes);
        Assert.Equal(cafeId, result.Cafes[0].Id);
        Assert.True(result.Cafes[0].HasRequestedGame);
    }

    [Fact]
    public async Task GetAlternativeCafesAsync_CafeMissingGame_ExcludesCafe()
    {
        var gameId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();

        var cafe = new Cafe
        {
            Id = cafeId,
            Name = "Nearby Cafe Without Game",
            Address = "456 Nearby St",
            TotalSeats = 10,
            Inventories = new List<CafeGameInventory>() // no game
        };

        var repo = new Mock<IActiveSessionRepository>();
        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetNearbyCafesAsync(cafeId, 10)).ReturnsAsync(new List<Cafe> { cafe });

        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var lobbyRepo = new Mock<ILobbyRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, lobbyRepo.Object, settlementService.Object);

        var result = await service.GetAlternativeCafesAsync(cafeId, gameId, 3, DateTime.UtcNow.AddHours(2));

        Assert.Empty(result.Cafes);
    }

    [Fact]
    public async Task GetAlternativeCafesAsync_InsufficientSeats_ExcludesCafe()
    {
        var gameId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();

        var cafe = new Cafe
        {
            Id = cafeId,
            Name = "Small Cafe",
            Address = "Small St",
            TotalSeats = 2, // not enough for 4 people
            Inventories = new List<CafeGameInventory>
            {
                new CafeGameInventory { GameTemplateId = gameId }
            }
        };

        var repo = new Mock<IActiveSessionRepository>();
        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetNearbyCafesAsync(cafeId, 10)).ReturnsAsync(new List<Cafe> { cafe });

        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var lobbyRepo = new Mock<ILobbyRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, lobbyRepo.Object, settlementService.Object);

        var result = await service.GetAlternativeCafesAsync(cafeId, gameId, 4, DateTime.UtcNow.AddHours(2));

        Assert.Empty(result.Cafes);
    }

    #endregion

    #region AddGuestSlotAsync

    [Fact]
    public async Task AddGuestSlotAsync_SessionNotActive_ThrowsConflictException()
    {
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Paid,
            Members = new List<ActiveSessionMember>(),
            Games = new List<ActiveSessionGame>()
        };

        var repo = new Mock<IActiveSessionRepository>();
        repo.Setup(r => r.GetByIdAsync(sessionId)).ReturnsAsync(session);

        var cafeRepo = new Mock<ICafeRepository>();
        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var lobbyRepo = new Mock<ILobbyRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, lobbyRepo.Object, settlementService.Object);

        var request = new AddGuestSlotRequestDto { DisplayName = "Guest" };

        await Assert.ThrowsAsync<ConflictException>(
            () => service.AddGuestSlotAsync(cafeId, sessionId, request));
    }

    #endregion
}


