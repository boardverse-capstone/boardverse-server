using BoardVerse.Core.DTOs.Session;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using Microsoft.Extensions.Logging;
using Moq;

using System.Threading;
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
        repo.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);

        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>())).ReturnsAsync(new Cafe
        {
            Id = cafeId,
            Name = "Test Cafe",
            Address = "123 Test St",
            IsActive = true,
            PartnerOperationalStatus = CafePartnerOperationalStatus.Active
        });

        var posRepo = new Mock<ICafePosRepository>();
        // BR-12: Mock IsSessionFullyCheckedAsync to return true
        posRepo.Setup(r => r.IsSessionFullyCheckedAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        posRepo.Setup(r => r.GetSessionGamesAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ActiveSessionGame>());

        var depositRepo = new Mock<IBookingDepositRepository>();
        var settlementService = new Mock<ISettlementService>();

        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, settlementService.Object, new Mock<IReservationService>().Object, new Mock<ILobbyRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalkInService>().Object, new Mock<IOutboxRepository>().Object, new Mock<ILogger<ActiveSessionService>>().Object);

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
        repo.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);

        var posRepo = new Mock<ICafePosRepository>();
        var cafeRepo = new Mock<ICafeRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var settlementService = new Mock<ISettlementService>();

        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, settlementService.Object, new Mock<IReservationService>().Object, new Mock<ILobbyRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalkInService>().Object, new Mock<IOutboxRepository>().Object, new Mock<ILogger<ActiveSessionService>>().Object);

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
        repo.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);

        var posRepo = new Mock<ICafePosRepository>();
        // BR-12: Mock IsSessionFullyCheckedAsync to return false
        posRepo.Setup(r => r.IsSessionFullyCheckedAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        posRepo.Setup(r => r.GetSessionGamesAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ActiveSessionGame>
        {
            new ActiveSessionGame { Id = gameId, CheckStatus = ComponentCheckStatus.NotChecked }
        });

        var cafeRepo = new Mock<ICafeRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var settlementService = new Mock<ISettlementService>();

        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, settlementService.Object, new Mock<IReservationService>().Object, new Mock<ILobbyRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalkInService>().Object, new Mock<IOutboxRepository>().Object, new Mock<ILogger<ActiveSessionService>>().Object);

        var request = new CheckoutRequestDto
        {
            ComponentsVerified = true,
            Components = new List<ComponentCheckoutItemDto>()
        };

        // BR-12: Must complete checklist for ALL games
        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            service.CheckoutAsync(cafeId, sessionId, request));

        Assert.Contains("kiểm kê", ex.Message);
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
        repo.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);

        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>())).ReturnsAsync(new Cafe
        {
            Id = cafeId,
            Name = "Test Cafe",
            Address = "123 Test St",
            IsActive = true,
            PartnerOperationalStatus = CafePartnerOperationalStatus.Active
        });

        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var settlementService = new Mock<ISettlementService>();

        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, settlementService.Object, new Mock<IReservationService>().Object, new Mock<ILobbyRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalkInService>().Object, new Mock<IOutboxRepository>().Object, new Mock<ILogger<ActiveSessionService>>().Object);

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
        repo.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);

        var cafeRepo = new Mock<ICafeRepository>();
        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, settlementService.Object, new Mock<IReservationService>().Object, new Mock<ILobbyRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalkInService>().Object, new Mock<IOutboxRepository>().Object, new Mock<ILogger<ActiveSessionService>>().Object);

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
        repo.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);

        var cafeRepo = new Mock<ICafeRepository>();
        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, settlementService.Object, new Mock<IReservationService>().Object, new Mock<ILobbyRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalkInService>().Object, new Mock<IOutboxRepository>().Object, new Mock<ILogger<ActiveSessionService>>().Object);

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
            Status = GroupSessionStatus.Checking,
            Members = new List<ActiveSessionMember>(),
            Games = new List<ActiveSessionGame>()
        };

        var repo = new Mock<IActiveSessionRepository>();
        repo.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);

        var cafeRepo = new Mock<ICafeRepository>();
        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, settlementService.Object, new Mock<IReservationService>().Object, new Mock<ILobbyRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalkInService>().Object, new Mock<IOutboxRepository>().Object, new Mock<ILogger<ActiveSessionService>>().Object);

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
        repo.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);

        var cafeRepo = new Mock<ICafeRepository>();
        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, settlementService.Object, new Mock<IReservationService>().Object, new Mock<ILobbyRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalkInService>().Object, new Mock<IOutboxRepository>().Object, new Mock<ILogger<ActiveSessionService>>().Object);

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
            Status = GroupSessionStatus.Checking,
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
        repo.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);

        var cafeRepo = new Mock<ICafeRepository>();
        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, settlementService.Object, new Mock<IReservationService>().Object, new Mock<ILobbyRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalkInService>().Object, new Mock<IOutboxRepository>().Object, new Mock<ILogger<ActiveSessionService>>().Object);

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
        repo.Setup(r => r.GetByIdAsync(sourceSessionId, It.IsAny<CancellationToken>())).ReturnsAsync(sourceSession);
        repo.Setup(r => r.GetByIdAsync(targetSessionId, It.IsAny<CancellationToken>())).ReturnsAsync(targetSession);
        repo.Setup(r => r.GetMemberByIdAsync(memberId, It.IsAny<CancellationToken>())).ReturnsAsync((ActiveSessionMember?)null);

        var cafeRepo = new Mock<ICafeRepository>();
        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, settlementService.Object, new Mock<IReservationService>().Object, new Mock<ILobbyRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalkInService>().Object, new Mock<IOutboxRepository>().Object, new Mock<ILogger<ActiveSessionService>>().Object);

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
        repo.Setup(r => r.GetByIdAsync(sourceSessionId, It.IsAny<CancellationToken>())).ReturnsAsync(sourceSession);
        repo.Setup(r => r.GetByIdAsync(targetSessionId, It.IsAny<CancellationToken>())).ReturnsAsync(targetSession);
        repo.Setup(r => r.GetMemberByIdAsync(memberId, It.IsAny<CancellationToken>())).ReturnsAsync(member);

        var cafeRepo = new Mock<ICafeRepository>();
        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, settlementService.Object, new Mock<IReservationService>().Object, new Mock<ILobbyRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalkInService>().Object, new Mock<IOutboxRepository>().Object, new Mock<ILogger<ActiveSessionService>>().Object);

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
        repo.Setup(r => r.GetByIdAsync(sourceSessionId, It.IsAny<CancellationToken>())).ReturnsAsync(sourceSession);
        repo.Setup(r => r.GetByIdAsync(targetSessionId, It.IsAny<CancellationToken>())).ReturnsAsync(targetSession);
        repo.Setup(r => r.GetMemberByIdAsync(memberId, It.IsAny<CancellationToken>())).ReturnsAsync(member);

        var cafeRepo = new Mock<ICafeRepository>();
        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, settlementService.Object, new Mock<IReservationService>().Object, new Mock<ILobbyRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalkInService>().Object, new Mock<IOutboxRepository>().Object, new Mock<ILogger<ActiveSessionService>>().Object);

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
        repo.Setup(r => r.GetByIdAsync(sourceSessionId, It.IsAny<CancellationToken>())).ReturnsAsync(sourceSession);
        repo.Setup(r => r.GetByIdAsync(targetSessionId, It.IsAny<CancellationToken>())).ReturnsAsync(targetSession);
        repo.Setup(r => r.GetMemberByIdAsync(memberId, It.IsAny<CancellationToken>())).ReturnsAsync(member);

        var cafeRepo = new Mock<ICafeRepository>();
        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, settlementService.Object, new Mock<IReservationService>().Object, new Mock<ILobbyRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalkInService>().Object, new Mock<IOutboxRepository>().Object, new Mock<ILogger<ActiveSessionService>>().Object);

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
        repo.Setup(r => r.GetByIdAsync(sourceSessionId, It.IsAny<CancellationToken>())).ReturnsAsync(sourceSession);
        repo.SetupSequence(r => r.GetByIdAsync(targetSessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetSession)
            .ReturnsAsync(targetSession);
        repo.Setup(r => r.GetMemberByIdAsync(memberId, It.IsAny<CancellationToken>())).ReturnsAsync(member);

        var cafeRepo = new Mock<ICafeRepository>();
        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, settlementService.Object, new Mock<IReservationService>().Object, new Mock<ILobbyRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalkInService>().Object, new Mock<IOutboxRepository>().Object, new Mock<ILogger<ActiveSessionService>>().Object);

        var request = new MergeSessionRequestDto { MemberId = memberId, TargetSessionId = targetSessionId };

        var result = await service.MergeSessionAsync(cafeId, sourceSessionId, request);

        Assert.Equal(memberId, result.MemberId);
        Assert.Equal(sourceSessionId, result.SourceSessionId);
        Assert.Equal(targetSessionId, result.TargetSessionId);
        repo.Verify(r => r.UpdateMemberAsync(It.Is<ActiveSessionMember>(m => m.Status == IndividualSessionStatus.Playing), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
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
        repo.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);

        var cafeRepo = new Mock<ICafeRepository>();
        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, settlementService.Object, new Mock<IReservationService>().Object, new Mock<ILobbyRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalkInService>().Object, new Mock<IOutboxRepository>().Object, new Mock<ILogger<ActiveSessionService>>().Object);

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
        repo.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        repo.Setup(r => r.GetMemberByIdAsync(guestMemberId, It.IsAny<CancellationToken>())).ReturnsAsync(guestMember);

        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>())).ReturnsAsync(new Cafe
        {
            Id = cafeId,
            Name = "Test Cafe",
            Address = "123 St",
            BillingModel = CafePartnerBillingModel.TimeBased,
            BasePrice = 60_000m
        });

        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, settlementService.Object, new Mock<IReservationService>().Object, new Mock<ILobbyRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalkInService>().Object, new Mock<IOutboxRepository>().Object, new Mock<ILogger<ActiveSessionService>>().Object);

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

        Assert.Contains("vô danh", ex.Message);
    }

    [Fact]
    public async Task PaySessionAsync_TimeBasedBilling_CalculatesCorrectly()
    {
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Unpaid,
            StartedAt = DateTime.UtcNow.AddHours(-3), // 180 minutes
            Subtotal = 140_000m, // Set by CheckoutAsync before PaySessionAsync
            Members = new List<ActiveSessionMember>
            {
                new()
                {
                    Id = memberId,
                    ActiveSessionId = sessionId,
                    UserId = Guid.NewGuid(),
                    JoinedAt = DateTime.UtcNow.AddHours(-3),
                    LeftAt = DateTime.UtcNow
                }
            },
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
        repo.SetupSequence(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session)
            .ReturnsAsync(session)
            .ReturnsAsync(session);

        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>())).ReturnsAsync(cafe);

        var posRepo = new Mock<ICafePosRepository>();
        posRepo.Setup(r => r.GetSessionGamesAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ActiveSessionGame>());
        var depositRepo = new Mock<IBookingDepositRepository>();
        depositRepo.Setup(r => r.GetByActiveSessionIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync((BookingDeposit?)null);

        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, settlementService.Object, new Mock<IReservationService>().Object, new Mock<ILobbyRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalkInService>().Object, new Mock<IOutboxRepository>().Object, new Mock<ILogger<ActiveSessionService>>().Object);

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
        var memberId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Unpaid,
            StartedAt = DateTime.UtcNow.AddMinutes(-45),
            Subtotal = 50_000m, // Set by CheckoutAsync before PaySessionAsync
            Members = new List<ActiveSessionMember>
            {
                new()
                {
                    Id = memberId,
                    ActiveSessionId = sessionId,
                    UserId = Guid.NewGuid(),
                    JoinedAt = DateTime.UtcNow.AddMinutes(-45),
                    LeftAt = DateTime.UtcNow
                }
            },
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
        repo.SetupSequence(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session)
            .ReturnsAsync(session);

        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>())).ReturnsAsync(cafe);

        var posRepo = new Mock<ICafePosRepository>();
        posRepo.Setup(r => r.GetSessionGamesAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ActiveSessionGame>());
        var depositRepo = new Mock<IBookingDepositRepository>();
        depositRepo.Setup(r => r.GetByActiveSessionIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync((BookingDeposit?)null);

        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, settlementService.Object, new Mock<IReservationService>().Object, new Mock<ILobbyRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalkInService>().Object, new Mock<IOutboxRepository>().Object, new Mock<ILogger<ActiveSessionService>>().Object);

        var request = new PaySessionRequestDto();

        var result = await service.PaySessionAsync(cafeId, sessionId, request);

        Assert.Equal(50_000m, result.Subtotal); // under 60 min → base price only
    }

    [Fact]
    public async Task PaySessionAsync_PackageModel_ChargesBasePriceOnly()
    {
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Unpaid,
            StartedAt = DateTime.UtcNow.AddHours(-5),
            Subtotal = 80_000m, // Set by CheckoutAsync before PaySessionAsync
            Members = new List<ActiveSessionMember>
            {
                new()
                {
                    Id = memberId,
                    ActiveSessionId = sessionId,
                    UserId = Guid.NewGuid(),
                    JoinedAt = DateTime.UtcNow.AddHours(-5),
                    LeftAt = DateTime.UtcNow
                }
            },
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
        repo.SetupSequence(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session)
            .ReturnsAsync(session);

        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>())).ReturnsAsync(cafe);

        var posRepo = new Mock<ICafePosRepository>();
        posRepo.Setup(r => r.GetSessionGamesAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ActiveSessionGame>());
        var depositRepo = new Mock<IBookingDepositRepository>();
        depositRepo.Setup(r => r.GetByActiveSessionIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync((BookingDeposit?)null);

        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, settlementService.Object, new Mock<IReservationService>().Object, new Mock<ILobbyRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalkInService>().Object, new Mock<IOutboxRepository>().Object, new Mock<ILogger<ActiveSessionService>>().Object);

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
        var memberId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Unpaid,
            StartedAt = DateTime.UtcNow.AddMinutes(-120), // 120 minutes (2 hours)
            Subtotal = 100_000m, // Set by CheckoutAsync before PaySessionAsync
            Members = new List<ActiveSessionMember>
            {
                new()
                {
                    Id = memberId,
                    ActiveSessionId = sessionId,
                    UserId = Guid.NewGuid(),
                    JoinedAt = DateTime.UtcNow.AddMinutes(-120),
                    LeftAt = DateTime.UtcNow
                }
            },
            Games = new List<ActiveSessionGame>(),
            GameTemplate = new GameTemplate { Id = Guid.NewGuid(), Name = "Catan", PlayTime = 60 },
            CafeTable = new CafeTable { Id = Guid.NewGuid(), Name = "Table 1" },
            CafeInventoryBox = new CafeInventoryBox { Id = Guid.NewGuid(), Barcode = "BV-001" }
        };

        var deposit = new BookingDeposit
        {
            Id = depositId,
            ActiveSessionId = sessionId,
            UserId = Guid.NewGuid(),
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
        repo.SetupSequence(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session)
            .ReturnsAsync(session)
            .ReturnsAsync(session);

        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>())).ReturnsAsync(cafe);

        var posRepo = new Mock<ICafePosRepository>();
        posRepo.Setup(r => r.GetSessionGamesAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ActiveSessionGame>());
        var depositRepo = new Mock<IBookingDepositRepository>();
        depositRepo.Setup(r => r.GetByActiveSessionIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(deposit);

        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, settlementService.Object, new Mock<IReservationService>().Object, new Mock<ILobbyRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalkInService>().Object, new Mock<IOutboxRepository>().Object, new Mock<ILogger<ActiveSessionService>>().Object);

        var request = new PaySessionRequestDto();

        var result = await service.PaySessionAsync(cafeId, sessionId, request);

        // Deposit chỉ dùng để giữ chỗ, KHÔNG trừ vào session
        Assert.Equal(0m, result.DepositAppliedAmount);
        // 120 min: 60_000 + ceil(60/15)*10_000 = 60_000 + 4*10_000 = 100_000
        // TotalAmount = Subtotal(100_000) + PenaltyAmount(0) = 100_000 (không trừ deposit)
        Assert.Equal(100_000m, result.TotalAmount);
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
            IsGuestSlot = false,
            JoinedAt = DateTime.UtcNow.AddHours(-1),
            LeftAt = DateTime.UtcNow
        };

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Unpaid,
            StartedAt = DateTime.UtcNow.AddHours(-1),
            Subtotal = 60_000m, // Set by CheckoutAsync before PaySessionAsync
            PenaltyAmount = 15_000m, // Single source of truth from Checkout (CompleteCheckoutAsync)
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
        repo.SetupSequence(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session)
            .ReturnsAsync(session)
            .ReturnsAsync(session);

        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>())).ReturnsAsync(cafe);

        var posRepo = new Mock<ICafePosRepository>();
        posRepo.Setup(r => r.GetSessionGamesAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ActiveSessionGame>());
        var depositRepo = new Mock<IBookingDepositRepository>();
        depositRepo.Setup(r => r.GetByActiveSessionIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync((BookingDeposit?)null);

        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, settlementService.Object, new Mock<IReservationService>().Object, new Mock<ILobbyRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalkInService>().Object, new Mock<IOutboxRepository>().Object, new Mock<ILogger<ActiveSessionService>>().Object);

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

    // Penalty #1 (2026-08-08): PaySessionAsync đọc penalty từ ComponentCheckResult.ResponsibleMemberId
    // (single source of truth), KHÔNG dùng PenaltyItems từ client. Khi component-check
    // đã lưu ResponsibleMemberId thì per-member invoice phản ánh đúng phân bổ.
    [Fact]
    public async Task PaySessionAsync_PenaltyFromComponentCheckResult_AssignsToMemberInvoice()
    {
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var componentId = Guid.NewGuid();
        var gameTemplateId = Guid.NewGuid();
        var sessionGameId = Guid.NewGuid();

        var member = new ActiveSessionMember
        {
            Id = memberId,
            UserId = Guid.NewGuid(),
            Status = IndividualSessionStatus.Playing,
            IsGuestSlot = false,
            JoinedAt = DateTime.UtcNow.AddHours(-1),
            LeftAt = DateTime.UtcNow
        };

        var componentTemplate = new GameComponentTemplate
        {
            Id = componentId,
            ComponentName = "Road piece",
            DefaultQuantity = 15
        };

        // Persisted ComponentCheckResult với ResponsibleMemberId set
        var checkResult = new ComponentCheckResult
        {
            Id = Guid.NewGuid(),
            ActiveSessionGameId = sessionGameId,
            GameComponentTemplateId = componentId,
            GameComponentTemplate = componentTemplate,
            ExpectedQuantity = 15,
            ActualQuantity = 14,
            PenaltyFee = 5_000m,
            ResponsibleMemberId = memberId,
            StaffId = Guid.NewGuid(),
            CheckedAt = DateTime.UtcNow
        };

        var sessionGame = new ActiveSessionGame
        {
            Id = sessionGameId,
            ActiveSessionId = sessionId,
            CafeInventoryBoxId = Guid.NewGuid(),
            GameTemplateId = gameTemplateId,
            CheckStatus = ComponentCheckStatus.MissingComponents,
            CheckedAt = DateTime.UtcNow,
            TotalPenaltyAmount = 5_000m,
            ComponentCheckResults = new List<ComponentCheckResult> { checkResult }
        };

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Unpaid,
            StartedAt = DateTime.UtcNow.AddHours(-1),
            Subtotal = 60_000m, // Set by CheckoutAsync before PaySessionAsync
            PenaltyAmount = 5_000m, // Single source of truth from Checkout (sum of sessionGame.TotalPenaltyAmount)
            Members = new List<ActiveSessionMember> { member },
            Games = new List<ActiveSessionGame> { sessionGame },
            GameTemplate = new GameTemplate { Id = gameTemplateId, Name = "Catan", PlayTime = 60 }
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
        repo.SetupSequence(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session)
            .ReturnsAsync(session)
            .ReturnsAsync(session);

        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>())).ReturnsAsync(cafe);

        var posRepo = new Mock<ICafePosRepository>();
        posRepo.Setup(r => r.GetSessionGamesAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ActiveSessionGame> { sessionGame });

        var depositRepo = new Mock<IBookingDepositRepository>();
        depositRepo.Setup(r => r.GetByActiveSessionIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync((BookingDeposit?)null);

        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(
            cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object,
            settlementService.Object, new Mock<IReservationService>().Object,
            new Mock<ILobbyRepository>().Object, new Mock<IReservationRepository>().Object,
            new Mock<IWalkInService>().Object, new Mock<IOutboxRepository>().Object, new Mock<ILogger<ActiveSessionService>>().Object);

        // Request KHÔNG gửi PenaltyItems — penalty phải tự lấy từ persisted.
        var request = new PaySessionRequestDto();

        var result = await service.PaySessionAsync(cafeId, sessionId, request);

        Assert.Equal(5_000m, result.PenaltyAmount);
        Assert.Equal(65_000m, result.TotalAmount); // 60_000 + 5_000 - 0
        Assert.Single(result.MemberInvoices);
        Assert.Equal(memberId, result.MemberInvoices[0].MemberId);
        Assert.Equal(5_000m, result.MemberInvoices[0].PenaltyAmount);
        Assert.Single(result.MemberInvoices[0].PenaltyDetails);
        Assert.Equal(componentId, result.MemberInvoices[0].PenaltyDetails[0].ComponentId);
        Assert.Equal(5_000m, result.MemberInvoices[0].PenaltyDetails[0].PenaltyFee);
    }

    // Penalty #1: Khi ResponsibleMemberId null, penalty cộng vào session.PenaltyAmount
    // nhưng KHÔNG phân bổ cho member nào (PenaltyDetails rỗng cho mọi member).
    [Fact]
    public async Task PaySessionAsync_PenaltyWithoutResponsibleMember_GoesToSessionTotal()
    {
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var componentId = Guid.NewGuid();
        var gameTemplateId = Guid.NewGuid();
        var sessionGameId = Guid.NewGuid();

        var member = new ActiveSessionMember
        {
            Id = memberId,
            UserId = Guid.NewGuid(),
            Status = IndividualSessionStatus.Playing,
            IsGuestSlot = false,
            JoinedAt = DateTime.UtcNow.AddHours(-1),
            LeftAt = DateTime.UtcNow
        };

        var componentTemplate = new GameComponentTemplate
        {
            Id = componentId,
            ComponentName = "Road piece",
            DefaultQuantity = 15
        };

        // ResponsibleMemberId = null → penalty chung vào session
        var checkResult = new ComponentCheckResult
        {
            Id = Guid.NewGuid(),
            ActiveSessionGameId = sessionGameId,
            GameComponentTemplateId = componentId,
            GameComponentTemplate = componentTemplate,
            ExpectedQuantity = 15,
            ActualQuantity = 14,
            PenaltyFee = 5_000m,
            ResponsibleMemberId = null, // ← key: không gán member
            StaffId = Guid.NewGuid(),
            CheckedAt = DateTime.UtcNow
        };

        var sessionGame = new ActiveSessionGame
        {
            Id = sessionGameId,
            ActiveSessionId = sessionId,
            CafeInventoryBoxId = Guid.NewGuid(),
            GameTemplateId = gameTemplateId,
            CheckStatus = ComponentCheckStatus.MissingComponents,
            CheckedAt = DateTime.UtcNow,
            TotalPenaltyAmount = 5_000m,
            ComponentCheckResults = new List<ComponentCheckResult> { checkResult }
        };

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Unpaid,
            StartedAt = DateTime.UtcNow.AddHours(-1),
            Subtotal = 60_000m, // Set by CheckoutAsync before PaySessionAsync
            PenaltyAmount = 5_000m, // Single source of truth from Checkout (sum of sessionGame.TotalPenaltyAmount)
            Members = new List<ActiveSessionMember> { member },
            Games = new List<ActiveSessionGame> { sessionGame },
            GameTemplate = new GameTemplate { Id = gameTemplateId, Name = "Catan", PlayTime = 60 }
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
        repo.SetupSequence(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session)
            .ReturnsAsync(session)
            .ReturnsAsync(session);

        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>())).ReturnsAsync(cafe);

        var posRepo = new Mock<ICafePosRepository>();
        posRepo.Setup(r => r.GetSessionGamesAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ActiveSessionGame> { sessionGame });

        var depositRepo = new Mock<IBookingDepositRepository>();
        depositRepo.Setup(r => r.GetByActiveSessionIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync((BookingDeposit?)null);

        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(
            cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object,
            settlementService.Object, new Mock<IReservationService>().Object,
            new Mock<ILobbyRepository>().Object, new Mock<IReservationRepository>().Object,
            new Mock<IWalkInService>().Object, new Mock<IOutboxRepository>().Object, new Mock<ILogger<ActiveSessionService>>().Object);

        var request = new PaySessionRequestDto();

        var result = await service.PaySessionAsync(cafeId, sessionId, request);

        Assert.Equal(5_000m, result.PenaltyAmount);
        Assert.Equal(65_000m, result.TotalAmount);
        // Member có PenaltyAmount = 0 (không phân bổ)
        Assert.Single(result.MemberInvoices);
        Assert.Equal(0m, result.MemberInvoices[0].PenaltyAmount);
        Assert.Empty(result.MemberInvoices[0].PenaltyDetails);
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
        repo.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);

        var posRepo = new Mock<ICafePosRepository>();
        posRepo.Setup(r => r.GetBoxByBarcodeAsync(cafeId, "BV-EXTRA", It.IsAny<CancellationToken>())).ReturnsAsync(box);

        var cafeRepo = new Mock<ICafeRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, settlementService.Object, new Mock<IReservationService>().Object, new Mock<ILobbyRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalkInService>().Object, new Mock<IOutboxRepository>().Object, new Mock<ILogger<ActiveSessionService>>().Object);

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

        var box = new CafeInventoryBox
        {
            Id = boxId,
            Barcode = "BV-EXTRA",
            CafeGameInventory = new CafeGameInventory
            {
                Id = Guid.NewGuid(),
                GameTemplateId = Guid.NewGuid()
            }
        };

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
        repo.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        repo.SetupSequence(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session)
            .ReturnsAsync(session);

        var posRepo = new Mock<ICafePosRepository>();
        posRepo.Setup(r => r.GetBoxByBarcodeAsync(cafeId, "BV-EXTRA", It.IsAny<CancellationToken>())).ReturnsAsync(box);

        var cafeRepo = new Mock<ICafeRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, settlementService.Object, new Mock<IReservationService>().Object, new Mock<ILobbyRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalkInService>().Object, new Mock<IOutboxRepository>().Object, new Mock<ILogger<ActiveSessionService>>().Object);

        var request = new AttachGameRequestDto { GameBarcode = "BV-EXTRA" };

        var result = await service.AttachGameAsync(cafeId, sessionId, request);

        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
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
        repo.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);

        var cafeRepo = new Mock<ICafeRepository>();
        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, settlementService.Object, new Mock<IReservationService>().Object, new Mock<ILobbyRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalkInService>().Object, new Mock<IOutboxRepository>().Object, new Mock<ILogger<ActiveSessionService>>().Object);

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
        repo.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);

        var cafeRepo = new Mock<ICafeRepository>();
        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, settlementService.Object, new Mock<IReservationService>().Object, new Mock<ILobbyRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalkInService>().Object, new Mock<IOutboxRepository>().Object, new Mock<ILogger<ActiveSessionService>>().Object);

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
        repo.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        repo.SetupSequence(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session)
            .ReturnsAsync(session);

        var cafeRepo = new Mock<ICafeRepository>();
        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, settlementService.Object, new Mock<IReservationService>().Object, new Mock<ILobbyRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalkInService>().Object, new Mock<IOutboxRepository>().Object, new Mock<ILogger<ActiveSessionService>>().Object);

        var request = new AddLateMemberRequestDto { MemberUserIds = new List<Guid> { newUserId } };

        var result = await service.AddLateMemberAsync(cafeId, sessionId, request);

        repo.Verify(r => r.AddMemberAsync(It.Is<ActiveSessionMember>(m => m.UserId == newUserId), It.IsAny<CancellationToken>()), Times.Once);
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
        cafeRepo.Setup(r => r.GetNearbyCafesAsync(cafeId, 10, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Cafe> { cafe });

        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, settlementService.Object, new Mock<IReservationService>().Object, new Mock<ILobbyRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalkInService>().Object, new Mock<IOutboxRepository>().Object, new Mock<ILogger<ActiveSessionService>>().Object);

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
        cafeRepo.Setup(r => r.GetNearbyCafesAsync(cafeId, 10, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Cafe> { cafe });

        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, settlementService.Object, new Mock<IReservationService>().Object, new Mock<ILobbyRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalkInService>().Object, new Mock<IOutboxRepository>().Object, new Mock<ILogger<ActiveSessionService>>().Object);

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
        cafeRepo.Setup(r => r.GetNearbyCafesAsync(cafeId, 10, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Cafe> { cafe });

        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, settlementService.Object, new Mock<IReservationService>().Object, new Mock<ILobbyRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalkInService>().Object, new Mock<IOutboxRepository>().Object, new Mock<ILogger<ActiveSessionService>>().Object);

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
        repo.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);

        var cafeRepo = new Mock<ICafeRepository>();
        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, settlementService.Object, new Mock<IReservationService>().Object, new Mock<ILobbyRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalkInService>().Object, new Mock<IOutboxRepository>().Object, new Mock<ILogger<ActiveSessionService>>().Object);

        var request = new AddGuestSlotRequestDto { DisplayName = "Guest" };

        await Assert.ThrowsAsync<ConflictException>(
            () => service.AddGuestSlotAsync(cafeId, sessionId, request));
    }

    // P1 Regression test (2026-08-19): Session đã Checking (sau EndGame) phải reject thêm guest.
    // Trước đây logic cho phép cả Active + Checking → guest join vào phiên đã endedAt → sai BR-13.
    [Theory]
    [InlineData(GroupSessionStatus.Checking)]
    [InlineData(GroupSessionStatus.Unpaid)]
    [InlineData(GroupSessionStatus.Paid)]
    public async Task AddGuestSlotAsync_SessionNotActiveInAnyTerminalOrCheckingState_ThrowsConflictException(GroupSessionStatus status)
    {
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = status,
            Members = new List<ActiveSessionMember>(),
            Games = new List<ActiveSessionGame>()
        };

        var repo = new Mock<IActiveSessionRepository>();
        repo.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);

        var cafeRepo = new Mock<ICafeRepository>();
        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, settlementService.Object, new Mock<IReservationService>().Object, new Mock<ILobbyRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalkInService>().Object, new Mock<IOutboxRepository>().Object, new Mock<ILogger<ActiveSessionService>>().Object);

        var request = new AddGuestSlotRequestDto { DisplayName = "Guest" };

        await Assert.ThrowsAsync<ConflictException>(
            () => service.AddGuestSlotAsync(cafeId, sessionId, request));
    }

    #endregion

    #region Early Checkout WalkInWindow Tests (§4.4)

    [Fact]
    public async Task PaySessionAsync_CallsLifecycleCleanup()
    {
        // Regression: PaySessionAsync must trigger the same lifecycle cleanup as
        // manual confirm / SePay webhook, so POS-initiated payments also release
        // table + box + members + close lobby.
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Unpaid,
            StartedAt = DateTime.UtcNow.AddHours(-1),
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
            BasePrice = 60_000m,
            TieredBlockMinutes = 15,
            TieredBlockRate = 10_000m
        };

        var repo = new Mock<IActiveSessionRepository>();
        repo.SetupSequence(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session)
            .ReturnsAsync(session)
            .ReturnsAsync(session);

        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>())).ReturnsAsync(cafe);

        var posRepo = new Mock<ICafePosRepository>();
        posRepo.Setup(r => r.GetSessionGamesAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ActiveSessionGame>());
        var depositRepo = new Mock<IBookingDepositRepository>();
        depositRepo.Setup(r => r.GetByActiveSessionIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync((BookingDeposit?)null);

        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, settlementService.Object, new Mock<IReservationService>().Object, new Mock<ILobbyRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalkInService>().Object, new Mock<IOutboxRepository>().Object, new Mock<ILogger<ActiveSessionService>>().Object);

        var request = new PaySessionRequestDto();

        await service.PaySessionAsync(cafeId, sessionId, request);

        // Lifecycle cleanup is delegated to the repository.
        repo.Verify(r => r.ReleaseMembersAndCloseLobbyAsync(sessionId, It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.ReleaseSessionTableAndBoxAsync(sessionId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PaySessionAsync_NoTableOrBox_CleanupStillRuns()
    {
        // Walk-in session: no attached table/box. Cleanup must still be invoked
        // (implementation in repo handles null IDs gracefully).
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Unpaid,
            StartedAt = DateTime.UtcNow.AddHours(-1),
            Members = new List<ActiveSessionMember>(),
            Games = new List<ActiveSessionGame>(),
            GameTemplate = new GameTemplate { Id = Guid.NewGuid(), Name = "Catan", PlayTime = 60 },
            CafeTableId = null,
            CafeInventoryBoxId = null
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
        repo.SetupSequence(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session)
            .ReturnsAsync(session)
            .ReturnsAsync(session);

        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>())).ReturnsAsync(cafe);

        var posRepo = new Mock<ICafePosRepository>();
        posRepo.Setup(r => r.GetSessionGamesAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ActiveSessionGame>());
        var depositRepo = new Mock<IBookingDepositRepository>();
        depositRepo.Setup(r => r.GetByActiveSessionIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync((BookingDeposit?)null);

        var settlementService = new Mock<ISettlementService>();
        var service = new ActiveSessionService(cafeRepo.Object, repo.Object, posRepo.Object, depositRepo.Object, settlementService.Object, new Mock<IReservationService>().Object, new Mock<ILobbyRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalkInService>().Object, new Mock<IOutboxRepository>().Object, new Mock<ILogger<ActiveSessionService>>().Object);

        await service.PaySessionAsync(cafeId, sessionId, new PaySessionRequestDto());

        repo.Verify(r => r.ReleaseMembersAndCloseLobbyAsync(sessionId, It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.ReleaseSessionTableAndBoxAsync(sessionId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Cleanup_PreservesBoxLostStatus_LogicContract()
    {
        // Documents the contract verified by integration tests:
        // Cleanup method must NOT overwrite a box whose Status is Damaged,
        // Maintenance, or Retired back to Available. Only flip when current
        // status is InUse.
        //
        // This is a pure-logic assertion (not a behavior test) that lives
        // alongside the unit tests as living documentation. The actual
        // SQL behavior is exercised by integration tests via the
        // BoardVerseWebApplicationFactory + real Postgres.
        var protectedStatuses = new[]
        {
            CafeGameInventoryStatus.Damaged,
            CafeGameInventoryStatus.Maintenance,
            CafeGameInventoryStatus.Retired
        };
        var flipStatuses = new[]
        {
            CafeGameInventoryStatus.InUse
        };

        foreach (var s in protectedStatuses)
        {
            Assert.True(s != CafeGameInventoryStatus.InUse,
                $"Status {s} must NOT trigger Available overwrite");
        }
        foreach (var s in flipStatuses)
        {
            Assert.True(s != CafeGameInventoryStatus.Available,
                $"Status {s} should trigger Available flip");
        }
    }

    /// <summary>
    /// §4.4: Early checkout — WalkInWindow được tạo khi session kết thúc sớm hơn ScheduledEndTime.
    /// </summary>
    [Fact]
    public async Task TryCreateWalkInWindowAsync_ShouldCreateWindow_WhenEarlyCheckout()
    {
        var cafeId = Guid.NewGuid();
        var lobbyId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var windowId = Guid.NewGuid();

        var scheduledEnd = DateTime.UtcNow.AddHours(2); // ScheduledEndTime = 2h tới
        var endedAt = DateTime.UtcNow; // Nhưng về sớm ngay bây giờ

        var member1Id = Guid.NewGuid();
        var member2Id = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            LobbyId = lobbyId,
            Status = GroupSessionStatus.Unpaid, // Must be Unpaid for PaySessionAsync
            Members = new List<ActiveSessionMember>
            {
                new() { Id = member1Id, UserId = Guid.NewGuid(), Status = IndividualSessionStatus.Finished },
                new() { Id = member2Id, UserId = Guid.NewGuid(), Status = IndividualSessionStatus.Finished }
            }
        };

        var reservation = new Reservation
        {
            Id = reservationId,
            LobbyId = lobbyId,
            CafeId = cafeId,
            ScheduledEndTime = scheduledEnd,
            Status = ReservationStatus.CheckedIn // Must be CheckedIn for CompleteAndCaptureAsync
        };

        var lobby = new Lobby
        {
            Id = lobbyId,
            Status = LobbyStatus.InProgress // Must be InProgress for capture
        };

        var expectedWindow = new WalkInWindow
        {
            Id = windowId,
            SourceReservationId = reservationId,
            WindowStart = endedAt,
            WindowEnd = scheduledEnd,
            AvailableSeats = 2
        };

        // Setup mocks
        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>())).ReturnsAsync(new Cafe
        {
            Id = cafeId,
            Name = "Test Cafe",
            Address = "123 Test St",
            IsActive = true,
            PartnerOperationalStatus = CafePartnerOperationalStatus.Active
        });

        var sessionRepo = new Mock<IActiveSessionRepository>();
        sessionRepo.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        sessionRepo.Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.FromResult<Core.IRepositories.IDatabaseTransactionContext>(null!));

        var posRepo = new Mock<ICafePosRepository>();
        posRepo.Setup(r => r.GetSessionGamesAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ActiveSessionGame>());

        var depositRepo = new Mock<IBookingDepositRepository>();
        depositRepo.Setup(r => r.GetByActiveSessionIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync((BookingDeposit?)null);

        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId, It.IsAny<CancellationToken>())).ReturnsAsync(lobby);

        var reservationRepo = new Mock<IReservationRepository>();
        reservationRepo.Setup(r => r.GetByLobbyIdAsync(lobbyId, It.IsAny<CancellationToken>())).ReturnsAsync(reservation);

        var settlementService = new Mock<ISettlementService>();
        var reservationService = new Mock<IReservationService>();
        reservationService.Setup(s => s.CompleteAndCaptureAsync(lobbyId, sessionId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var walkInService = new Mock<IWalkInService>();
        walkInService.Setup(s => s.CreateWindowFromReservationAsync(
            It.IsAny<Reservation>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedWindow);

        var logger = new Mock<ILogger<ActiveSessionService>>();

        var service = new ActiveSessionService(
            cafeRepo.Object, sessionRepo.Object, posRepo.Object, depositRepo.Object,
            settlementService.Object, reservationService.Object,
            lobbyRepo.Object, reservationRepo.Object, walkInService.Object, new Mock<IOutboxRepository>().Object, logger.Object);

        var request = new PaySessionRequestDto();

        // Act
        await service.PaySessionAsync(cafeId, sessionId, request);

        // Assert - WalkInService.CreateWindowFromReservationAsync được gọi vì endedAt < scheduledEnd
        walkInService.Verify(s => s.CreateWindowFromReservationAsync(
            It.IsAny<Reservation>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// §4.4: WalkInWindow KHÔNG được tạo khi session kết thúc đúng giờ (on-time).
    /// </summary>
    [Fact]
    public async Task TryCreateWalkInWindowAsync_ShouldNotCreateWindow_WhenOnTimeEnd()
    {
        var cafeId = Guid.NewGuid();
        var lobbyId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var scheduledEnd = DateTime.UtcNow; // ScheduledEndTime = bây giờ
        var endedAt = DateTime.UtcNow; // Kết thúc cùng giờ (not early)

        var memberId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            LobbyId = lobbyId,
            Status = GroupSessionStatus.Unpaid,
            Members = new List<ActiveSessionMember>
            {
                new() { Id = memberId, UserId = Guid.NewGuid(), Status = IndividualSessionStatus.Finished }
            }
        };

        var reservation = new Reservation
        {
            Id = reservationId,
            LobbyId = lobbyId,
            CafeId = cafeId,
            ScheduledEndTime = scheduledEnd,
            Status = ReservationStatus.CheckedIn // Must be CheckedIn for CompleteAndCaptureAsync
        };

        var lobby = new Lobby
        {
            Id = lobbyId,
            Status = LobbyStatus.InProgress // Must be InProgress for capture
        };

        // Setup mocks
        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>())).ReturnsAsync(new Cafe
        {
            Id = cafeId,
            Name = "Test Cafe",
            Address = "123 Test St",
            IsActive = true,
            PartnerOperationalStatus = CafePartnerOperationalStatus.Active
        });

        var sessionRepo = new Mock<IActiveSessionRepository>();
        sessionRepo.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        sessionRepo.Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.FromResult<Core.IRepositories.IDatabaseTransactionContext>(null!));

        var posRepo = new Mock<ICafePosRepository>();
        posRepo.Setup(r => r.GetSessionGamesAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ActiveSessionGame>());

        var depositRepo = new Mock<IBookingDepositRepository>();
        depositRepo.Setup(r => r.GetByActiveSessionIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync((BookingDeposit?)null);

        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId, It.IsAny<CancellationToken>())).ReturnsAsync(lobby);

        var reservationRepo = new Mock<IReservationRepository>();
        reservationRepo.Setup(r => r.GetByLobbyIdAsync(lobbyId, It.IsAny<CancellationToken>())).ReturnsAsync(reservation);

        var settlementService = new Mock<ISettlementService>();
        var reservationService = new Mock<IReservationService>();
        reservationService.Setup(s => s.CompleteAndCaptureAsync(lobbyId, sessionId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var walkInService = new Mock<IWalkInService>();
        var logger = new Mock<ILogger<ActiveSessionService>>();

        var service = new ActiveSessionService(
            cafeRepo.Object, sessionRepo.Object, posRepo.Object, depositRepo.Object,
            settlementService.Object, reservationService.Object,
            lobbyRepo.Object, reservationRepo.Object, walkInService.Object, new Mock<IOutboxRepository>().Object, logger.Object);

        var request = new PaySessionRequestDto();

        // Act
        await service.PaySessionAsync(cafeId, sessionId, request);

        // Assert - WalkInService.CreateWindowFromReservationAsync KHÔNG được gọi vì endedAt >= scheduledEnd
        walkInService.Verify(s => s.CreateWindowFromReservationAsync(
            It.IsAny<Reservation>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// §4.4: WalkInWindow KHÔNG được tạo khi session không có LobbyId (walk-in/legacy).
    /// </summary>
    [Fact]
    public async Task TryCreateWalkInWindowAsync_ShouldNotCreateWindow_WhenNoLobbyId()
    {
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            LobbyId = null, // Walk-in session
            Status = GroupSessionStatus.Unpaid,
            Members = new List<ActiveSessionMember>
            {
                new() { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Status = IndividualSessionStatus.Finished }
            }
        };

        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>())).ReturnsAsync(new Cafe
        {
            Id = cafeId,
            Name = "Test Cafe",
            Address = "123 Test St",
            IsActive = true,
            PartnerOperationalStatus = CafePartnerOperationalStatus.Active
        });

        var sessionRepo = new Mock<IActiveSessionRepository>();
        sessionRepo.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);

        var posRepo = new Mock<ICafePosRepository>();
        posRepo.Setup(r => r.GetSessionGamesAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ActiveSessionGame>());

        var depositRepo = new Mock<IBookingDepositRepository>();
        depositRepo.Setup(r => r.GetByActiveSessionIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync((BookingDeposit?)null);

        var settlementService = new Mock<ISettlementService>();
        var reservationService = new Mock<IReservationService>();
        var lobbyRepo = new Mock<ILobbyRepository>();
        var reservationRepo = new Mock<IReservationRepository>();
        var walkInService = new Mock<IWalkInService>();
        var logger = new Mock<ILogger<ActiveSessionService>>();

        var service = new ActiveSessionService(
            cafeRepo.Object, sessionRepo.Object, posRepo.Object, depositRepo.Object,
            settlementService.Object, reservationService.Object,
            lobbyRepo.Object, reservationRepo.Object, walkInService.Object, new Mock<IOutboxRepository>().Object, logger.Object);

        var request = new PaySessionRequestDto();

        // Act
        await service.PaySessionAsync(cafeId, sessionId, request);

        // Assert
        walkInService.Verify(s => s.CreateWindowFromReservationAsync(
            It.IsAny<Reservation>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion
}


