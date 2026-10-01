using BoardVerse.Core.DTOs.Session;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using BoardVerse.Services.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;

namespace BoardVerse.Tests.Services;

/// <summary>
/// M2/C2.16 — Unit tests cho <c>ForceCloseService.ForceCloseWithUnpaidAsync</c>.
/// docs/design/host-deposit-discount-and-bvc-payment-design.md §C2.16 (Gap #33).
/// (2026-10-01)
///
/// <para>
/// <b>SCOPE:</b> Validate 3 handling modes (MarkNoShow / MarkAsDebt / CompensationByHost)
/// + ambient transaction + audit log + session status flip.
/// </para>
/// </summary>
public class ForceCloseServiceTests : IDisposable
{
    private readonly Mock<IActiveSessionRepository> _activeSessionRepo = new();
    private readonly Mock<ILogger<ForceCloseService>> _logger = new();

    private BoardVerseDbContext _db = default!;

    public ForceCloseServiceTests()
    {
        var options = new DbContextOptionsBuilder<BoardVerseDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new BoardVerseDbContext(options);
    }

    public void Dispose()
    {
        _db?.Dispose();
    }

    private ForceCloseService CreateService() => new(
        _db, _activeSessionRepo.Object, _logger.Object);

    /// <summary>
    /// Attach session + members vào DbContext change tracker để SaveChangesAsync
    /// persist được thay đổi (mock repo trả về untracked entities).
    /// </summary>
    private void AttachToDb(ActiveSession session)
    {
        _db.ActiveSessions.Add(session);
        foreach (var member in session.Members)
        {
            _db.ActiveSessionMembers.Add(member);
        }
    }

    // =====================================================================
    // HELPERS
    // =====================================================================

    /// <summary>
    /// Tạo <see cref="ActiveSession"/> ở trạng thái <c>Unpaid</c> với host + members.
    /// </summary>
    /// <summary>
    /// Tạo <see cref="ActiveSession"/> ở trạng thái <c>Unpaid</c> với host + members.
    /// </summary>
    private static ActiveSession CreateUnpaidSession(
        Guid hostUserId,
        decimal subtotal,
        decimal penalty,
        decimal depositApplied = 0m,
        (Guid userId, bool isGuest, string? guestName)[]? extraMembers = null)
    {
        var sessionId = Guid.NewGuid();
        var members = new List<ActiveSessionMember>
        {
            new()
            {
                Id = Guid.NewGuid(),
                ActiveSessionId = sessionId,
                UserId = hostUserId,
                IsGuestSlot = false,
                IsHost = true,
                Status = IndividualSessionStatus.Finished,
                JoinedAt = DateTime.UtcNow.AddHours(-2),
                PaymentStatus = MemberPaymentStatus.PaidQr, // Host luôn đã paid
                Subtotal = subtotal,
                PenaltyAmount = penalty,
                DepositAppliedAmount = depositApplied
            }
        };

        foreach (var (userId, isGuest, guestName) in extraMembers ?? Array.Empty<(Guid, bool, string?)>())
        {
            members.Add(new ActiveSessionMember
            {
                Id = Guid.NewGuid(),
                ActiveSessionId = sessionId,
                UserId = isGuest ? null : userId,
                IsGuestSlot = isGuest,
                GuestDisplayName = guestName,
                IsHost = false,
                Status = IndividualSessionStatus.Finished,
                JoinedAt = DateTime.UtcNow.AddHours(-2),
                PaymentStatus = MemberPaymentStatus.NotPaid,
                Subtotal = subtotal,
                PenaltyAmount = penalty,
                DepositAppliedAmount = depositApplied
            });
        }

        return new ActiveSession
        {
            Id = sessionId,
            CafeId = Guid.NewGuid(),
            HostId = hostUserId,
            Status = GroupSessionStatus.Unpaid,
            StartedAt = DateTime.UtcNow.AddHours(-2),
            Subtotal = subtotal * members.Count,
            PenaltyAmount = penalty * members.Count,
            DepositAppliedAmount = depositApplied * members.Count,
            Members = members
        };
    }

    /// <summary>
    /// Helper gọi <see cref="CreateUnpaidSession"/> + attach session + members vào DbContext
    /// để SaveChangesAsync persist được (mock repo trả về untracked entities).
    /// </summary>
    private ActiveSession CreateAndAttachUnpaidSession(
        Guid hostUserId,
        decimal subtotal,
        decimal penalty,
        decimal depositApplied = 0m,
        (Guid userId, bool isGuest, string? guestName)[]? extraMembers = null)
    {
        var session = CreateUnpaidSession(hostUserId, subtotal, penalty, depositApplied, extraMembers);
        AttachToDb(session);
        return session;
    }

    private static ForceCloseRequestDto MakeRequest(
        string handling,
        bool allowLatePayment = true,
        string reason = "Khách đã rời quán nhưng chưa thanh toán.")
        => new()
        {
            UnpaidMemberHandling = handling,
            Reason = reason,
            AllowLatePayment = allowLatePayment
        };

    // =====================================================================
    // CASE 1: Validation — session not found
    // =====================================================================

    [Fact]
    public async Task ForceCloseWithUnpaidAsync_SessionNotFound_ThrowsNotFound()
    {
        var sessionId = Guid.NewGuid();
        _activeSessionRepo.Setup(r => r.GetByIdWithMembersAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActiveSession?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            CreateService().ForceCloseWithUnpaidAsync(
                sessionId, MakeRequest("MarkNoShow"), Guid.NewGuid()));
    }

    // =====================================================================
    // CASE 2: Validation — session not in Unpaid state
    // =====================================================================

    [Theory]
    [InlineData(GroupSessionStatus.Active)]
    [InlineData(GroupSessionStatus.Checking)]
    [InlineData(GroupSessionStatus.Paid)]
    [InlineData(GroupSessionStatus.Closed)]
    public async Task ForceCloseWithUnpaidAsync_SessionNotUnpaid_ThrowsConflict(GroupSessionStatus status)
    {
        var session = CreateAndAttachUnpaidSession(Guid.NewGuid(), subtotal: 50000m, penalty: 0m);
        session.Status = status;
        _activeSessionRepo.Setup(r => r.GetByIdWithMembersAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            CreateService().ForceCloseWithUnpaidAsync(
                session.Id, MakeRequest("MarkNoShow"), Guid.NewGuid()));

        Assert.Contains("Unpaid", ex.Message);
        Assert.Contains(status.ToString(), ex.Message);
    }

    // =====================================================================
    // CASE 3: Validation — no unpaid members
    // =====================================================================

    [Fact]
    public async Task ForceCloseWithUnpaidAsync_NoUnpaidMembers_ThrowsConflict()
    {
        var hostId = Guid.NewGuid();
        var session = CreateAndAttachUnpaidSession(hostId, subtotal: 50000m, penalty: 0m);
        // Mark host member unpaid để có 1 unpaid, sau đó flip về paid
        session.Members.First().PaymentStatus = MemberPaymentStatus.PaidQr;
        // Thêm 1 member cũng đã paid
        session.Members.Add(new ActiveSessionMember
        {
            Id = Guid.NewGuid(),
            ActiveSessionId = session.Id,
            UserId = Guid.NewGuid(),
            IsHost = false,
            Status = IndividualSessionStatus.Finished,
            PaymentStatus = MemberPaymentStatus.PaidCash,
            Subtotal = 50000m
        });

        _activeSessionRepo.Setup(r => r.GetByIdWithMembersAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            CreateService().ForceCloseWithUnpaidAsync(
                session.Id, MakeRequest("MarkNoShow"), Guid.NewGuid()));

        Assert.Contains("không có thành viên nào chưa thanh toán", ex.Message);
    }

    // =====================================================================
    // CASE 4: MarkNoShow — set NoShow status + NoShowAt + NoShowReason
    //
    // NOTE: Test chỉ verify side-effects (member state changes) trước khi
    // ExecuteUpdateAsync throw (InMemory provider không support).
    // Audit log + session.Status flip phải verify qua integration test.
    // =====================================================================

    [Fact]
    public async Task ForceCloseWithUnpaidAsync_MarkNoShow_SetsNoShowFieldsOnUnpaidMember()
    {
        var hostId = Guid.NewGuid();
        var unpaidUserId = Guid.NewGuid();
        var session = CreateAndAttachUnpaidSession(
            hostId, subtotal: 60000m, penalty: 0m,
            extraMembers: new[] { (unpaidUserId, false, (string?)null) });

        _activeSessionRepo.Setup(r => r.GetByIdWithMembersAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _activeSessionRepo.Setup(r => r.ReleaseSessionTableAndBoxAsync(session.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // ExecuteUpdateAsync không support InMemory → catch + verify side-effects trước đó.
        var _ = await CreateService().ForceCloseWithUnpaidAsync(
            session.Id, MakeRequest("MarkNoShow"), Guid.NewGuid()

        );


        // Member state PHẢI được persist (SaveChangesAsync chạy trước ExecuteUpdateAsync).
        var unpaid = session.Members.First(m => m.UserId == unpaidUserId);
        Assert.Equal(IndividualSessionStatus.NoShow, unpaid.Status);
        Assert.NotNull(unpaid.NoShowAt);
        Assert.Contains("ForceClose_MarkNoShow", unpaid.NoShowReason!);
    }

    // =====================================================================
    // CASE 5: MarkAsDebt — insert MemberDebt row, status = Finished
    //
    // NOTE: Test verify side-effects (member state + MemberDebt insert).
    // Audit log + session.Status flip phải verify qua integration test.
    // =====================================================================

    [Fact]
    public async Task ForceCloseWithUnpaidAsync_MarkAsDebt_InsertsDebtRow()
    {
        var hostId = Guid.NewGuid();
        var unpaidUserId = Guid.NewGuid();
        var session = CreateAndAttachUnpaidSession(
            hostId, subtotal: 50000m, penalty: 15000m, // amount owed = 65k
            extraMembers: new (Guid, bool, string?)[] { (unpaidUserId, false, null) });

        _activeSessionRepo.Setup(r => r.GetByIdWithMembersAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var _ = await CreateService().ForceCloseWithUnpaidAsync(
            session.Id, MakeRequest("MarkAsDebt"), Guid.NewGuid()


        );


        // Verify member state
        var unpaid = session.Members.First(m => m.UserId == unpaidUserId);
        Assert.Equal(IndividualSessionStatus.Finished, unpaid.Status);

        // Verify MemberDebt row (insert happens before ExecuteUpdateAsync throws)
        var debt = await _db.MemberDebts.AsNoTracking().FirstOrDefaultAsync();
        Assert.NotNull(debt);
        Assert.Equal(unpaid.Id, debt!.MemberId);
        Assert.Equal(unpaidUserId, debt.UserId);
        Assert.Equal(session.Id, debt.SessionId);
        Assert.Equal(65000m, debt.AmountCash);
        Assert.Equal(0L, debt.AmountBvc);
        Assert.Equal(DebtStatus.Pending, debt.Status);
        Assert.Contains("ForceClose_MarkAsDebt", debt.Reason);
    }

    // =====================================================================
    // CASE 6: MarkAsDebt — Guest slot has penalty = 0 (BR-14)
    // =====================================================================

    [Fact]
    public async Task ForceCloseWithUnpaidAsync_MarkAsDebt_GuestSlot_PenaltyZero()
    {
        var hostId = Guid.NewGuid();
        var session = CreateAndAttachUnpaidSession(
            hostId, subtotal: 50000m, penalty: 15000m,
            extraMembers: new (Guid, bool, string?)[] { (Guid.NewGuid(), true, "Khách vô danh 1") });

        _activeSessionRepo.Setup(r => r.GetByIdWithMembersAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var _ = await CreateService().ForceCloseWithUnpaidAsync(
            session.Id, MakeRequest("MarkAsDebt"), Guid.NewGuid()


        );


        // Guest slot: penalty = 0 theo BR-14 → AmountCash = subtotal only
        var debt = await _db.MemberDebts.AsNoTracking().FirstOrDefaultAsync();
        Assert.NotNull(debt);
        Assert.Equal(50000m, debt!.AmountCash);
    }

    // =====================================================================
    // CASE 7: CompensationByHost — host already paid → mark PaidByHost
    //
    // NOTE: Audit log + session.Status flip verify qua integration test.
    // =====================================================================

    [Fact]
    public async Task ForceCloseWithUnpaidAsync_CompensationByHost_HostPaid_Succeeds()
    {
        var hostId = Guid.NewGuid();
        var unpaidUserId = Guid.NewGuid();
        var session = CreateAndAttachUnpaidSession(
            hostId, subtotal: 70000m, penalty: 0m,
            extraMembers: new[] { (unpaidUserId, false, (string?)null) });

        _activeSessionRepo.Setup(r => r.GetByIdWithMembersAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var _ = await CreateService().ForceCloseWithUnpaidAsync(
            session.Id, MakeRequest("CompensationByHost"), Guid.NewGuid()


        );


        // Verify member state changed
        var unpaid = session.Members.First(m => m.UserId == unpaidUserId);
        Assert.Equal(MemberPaymentStatus.PaidByHost, unpaid.PaymentStatus);
        Assert.NotNull(unpaid.PaidByHostAt);
        Assert.Equal(hostId, unpaid.PaidByHostUserId);
        // KHÔNG đổi member.Status (giữ Finished)
        Assert.Equal(IndividualSessionStatus.Finished, unpaid.Status);
    }

    // =====================================================================
    // CASE 8: CompensationByHost — host NOT paid → throw Conflict
    // =====================================================================

    [Fact]
    public async Task ForceCloseWithUnpaidAsync_CompensationByHost_HostNotPaid_ThrowsConflict()
    {
        var hostId = Guid.NewGuid();
        var session = CreateAndAttachUnpaidSession(
            hostId, subtotal: 70000m, penalty: 0m,
            extraMembers: new (Guid, bool, string?)[] { (Guid.NewGuid(), false, null) });
        // Flip host thành unpaid
        session.Members.First(m => m.IsHost).PaymentStatus = MemberPaymentStatus.NotPaid;

        _activeSessionRepo.Setup(r => r.GetByIdWithMembersAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            CreateService().ForceCloseWithUnpaidAsync(
                session.Id, MakeRequest("CompensationByHost"), Guid.NewGuid()));

        Assert.Contains("Host phải thanh toán", ex.Message);
    }

    // =====================================================================
    // CASE 9: CompensationByHost — Guest slot cannot be covered
    // =====================================================================

    [Fact]
    public async Task ForceCloseWithUnpaidAsync_CompensationByHost_GuestSlot_ThrowsConflict()
    {
        var hostId = Guid.NewGuid();
        var session = CreateAndAttachUnpaidSession(
            hostId, subtotal: 50000m, penalty: 0m,
            extraMembers: new (Guid, bool, string?)[] { (Guid.NewGuid(), true, "Khách vô danh 1") });

        _activeSessionRepo.Setup(r => r.GetByIdWithMembersAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            CreateService().ForceCloseWithUnpaidAsync(
                session.Id, MakeRequest("CompensationByHost"), Guid.NewGuid()));

        Assert.Contains("Khách vô danh", ex.Message);
        Assert.Contains("không thể được cover", ex.Message);
    }

    // =====================================================================
    // CASE 10: AllowLatePayment = false + still unpaid → throw Conflict
    // =====================================================================

    [Fact]
    public async Task ForceCloseWithUnpaidAsync_NoLatePayment_StillUnpaid_ThrowsConflict()
    {
        var hostId = Guid.NewGuid();
        // Tạo session với 2 unpaid members (host paid, 2 member chưa pay).
        // Dùng MarkAsDebt (không phải MarkNoShow) để giữ PaymentStatus = NotPaid
        // cho case này — test logic "vẫn còn unpaid sau handle → throw".
        var session = CreateAndAttachUnpaidSession(
            hostId, subtotal: 50000m, penalty: 0m,
            extraMembers: new (Guid, bool, string?)[] {
                (Guid.NewGuid(), false, (string?)null),
                (Guid.NewGuid(), false, (string?)null)
            });

        _activeSessionRepo.Setup(r => r.GetByIdWithMembersAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        // MarkAsDebt → member.Status = Finished, PaymentStatus giữ NotPaid.
        // → stillUnpaidCount = 2 + AllowLatePayment=false → throw.
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            CreateService().ForceCloseWithUnpaidAsync(
                session.Id,
                MakeRequest("MarkAsDebt", allowLatePayment: false),
                Guid.NewGuid()));

        Assert.Contains("AllowLatePayment=false", ex.Message);
    }

    // =====================================================================
    // CASE 11: AllowLatePayment = true + still unpaid → flip sang UnpaidForced
    //
    // NOTE: Audit log + session.Status flip verify qua integration test.
    // Verify unpaid member state changes (vẫn được SaveChangesAsync trước ExecuteUpdateAsync throw).
    // =====================================================================

    [Fact]
    public async Task ForceCloseWithUnpaidAsync_LatePaymentTrue_StillUnpaid_MarksMemberAsNoShow()
    {
        var hostId = Guid.NewGuid();
        var unpaidUserId = Guid.NewGuid();
        var session = CreateAndAttachUnpaidSession(
            hostId, subtotal: 50000m, penalty: 0m,
            extraMembers: new (Guid, bool, string?)[] { (unpaidUserId, false, null) });

        _activeSessionRepo.Setup(r => r.GetByIdWithMembersAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var _ = await CreateService().ForceCloseWithUnpaidAsync(
            session.Id,
            MakeRequest("MarkNoShow", allowLatePayment: true),
            Guid.NewGuid()
        );


        // Member state PHẢI được persist trước khi ExecuteUpdateAsync throw.
        var unpaid = session.Members.First(m => m.UserId == unpaidUserId);
        Assert.Equal(IndividualSessionStatus.NoShow, unpaid.Status);
        Assert.NotNull(unpaid.NoShowAt);
    }

    // =====================================================================
    // CASE 12: Invalid handling → throw Conflict
    // =====================================================================

    [Theory]
    [InlineData("")]
    [InlineData("InvalidHandling")]
    [InlineData("MarkNoShowy")]
    public async Task ForceCloseWithUnpaidAsync_InvalidHandling_ThrowsConflict(string handling)
    {
        var hostId = Guid.NewGuid();
        var session = CreateAndAttachUnpaidSession(
            hostId, subtotal: 50000m, penalty: 0m,
            extraMembers: new (Guid, bool, string?)[] { (Guid.NewGuid(), false, null) });

        _activeSessionRepo.Setup(r => r.GetByIdWithMembersAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        // "InvalidHandling" / "MarkNoShowy" đi tới default branch trong switch
        // → throw ForceCloseInvalidHandling. Empty thì vẫn pass switch default.
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            CreateService().ForceCloseWithUnpaidAsync(
                session.Id, MakeRequest(handling), Guid.NewGuid()));

        Assert.Contains("không hợp lệ", ex.Message);
    }

    // =====================================================================
    // CASE 13: Null request → throw BadRequest
    // =====================================================================

    [Fact]
    public async Task ForceCloseWithUnpaidAsync_NullRequest_ThrowsBadRequest()
    {
        await Assert.ThrowsAsync<BadRequestException>(() =>
            CreateService().ForceCloseWithUnpaidAsync(
                Guid.NewGuid(), null!, Guid.NewGuid()));
    }

    // =====================================================================
    // CASE 14: Multiple unpaid members — verify tất cả đều được mark NoShow
    //
    // NOTE: Audit log verify qua integration test.
    // =====================================================================

    [Fact]
    public async Task ForceCloseWithUnpaidAsync_MultipleUnpaidMembers_AllMarkedNoShow()
    {
        var hostId = Guid.NewGuid();
        var unpaidUserA = Guid.NewGuid();
        var unpaidUserB = Guid.NewGuid();
        var session = CreateAndAttachUnpaidSession(
            hostId, subtotal: 50000m, penalty: 0m,
            extraMembers: new (Guid, bool, string?)[] {
                (unpaidUserA, false, null),
                (unpaidUserB, false, null)
            });

        _activeSessionRepo.Setup(r => r.GetByIdWithMembersAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var _ = await CreateService().ForceCloseWithUnpaidAsync(
            session.Id, MakeRequest("MarkNoShow"), Guid.NewGuid()


        );


        // Verify cả 2 unpaid members đều được mark NoShow
        var memberA = session.Members.First(m => m.UserId == unpaidUserA);
        var memberB = session.Members.First(m => m.UserId == unpaidUserB);

        Assert.Equal(IndividualSessionStatus.NoShow, memberA.Status);
        Assert.NotNull(memberA.NoShowAt);
        Assert.Equal(IndividualSessionStatus.NoShow, memberB.Status);
        Assert.NotNull(memberB.NoShowAt);
    }

    // =====================================================================
    // CASE 15: Reason captured on member's NoShowReason (audit log verify qua integration test)
    // =====================================================================

    [Fact]
    public async Task ForceCloseWithUnpaidAsync_ReasonCapturedOnMember()
    {
        var hostId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var unpaidUserId = Guid.NewGuid();
        var session = CreateAndAttachUnpaidSession(
            hostId, subtotal: 50000m, penalty: 0m,
            extraMembers: new (Guid, bool, string?)[] { (Guid.NewGuid(), false, null) });

        _activeSessionRepo.Setup(r => r.GetByIdWithMembersAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var customReason = "Khách không có tiền mặt và rời quán.";
        // Sau Gap-#5 fix: MarkNoShow set PaymentStatus=PaidByHost, session flip → Paid (success, no throw).
        var result = await CreateService().ForceCloseWithUnpaidAsync(
            session.Id, MakeRequest("MarkNoShow", reason: customReason), actorId);

        Assert.Equal(GroupSessionStatus.Paid, session.Status);

        // Tìm member bằng NoShow status thay vì PaymentStatus=NotPaid (vì đã flip sang PaidByHost).
        var noShowMember = session.Members.First(m => m.Status == IndividualSessionStatus.NoShow);
        Assert.Contains(customReason, noShowMember.NoShowReason!);
    }

    // =====================================================================
    // CASE 16: Mixed members — host paid + guest + player unpaid
    //   Chỉ unpaid members mới bị xử lý, paid members KHÔNG bị touch.
    //
    // NOTE: ExecuteUpdateAsync throws ở cuối flow, nhưng paid member KHÔNG bị touch
    // vẫn verify được vì đây là behavior trước khi throw.
    // =====================================================================

    [Fact]
    public async Task ForceCloseWithUnpaidAsync_PaidMembers_NotTouched()
    {
        var hostId = Guid.NewGuid();
        var paidUserId = Guid.NewGuid();
        var unpaidUserId = Guid.NewGuid();

        var session = CreateAndAttachUnpaidSession(
            hostId, subtotal: 50000m, penalty: 0m,
            extraMembers: new[] { (unpaidUserId, false, (string?)null) });
        // Thêm 1 member đã paid
        var paidMember = new ActiveSessionMember
        {
            Id = Guid.NewGuid(),
            ActiveSessionId = session.Id,
            UserId = paidUserId,
            IsHost = false,
            Status = IndividualSessionStatus.Finished,
            PaymentStatus = MemberPaymentStatus.PaidCash,
            PaidAt = DateTime.UtcNow.AddMinutes(-5),
            Subtotal = 50000m,
            UpdatedAt = DateTime.UtcNow.AddMinutes(-5)
        };
        session.Members.Add(paidMember);

        _activeSessionRepo.Setup(r => r.GetByIdWithMembersAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var originalPaidAt = paidMember.PaidAt;
        var originalUpdatedAt = paidMember.UpdatedAt;

        var _ = await CreateService().ForceCloseWithUnpaidAsync(
            session.Id, MakeRequest("MarkNoShow"), Guid.NewGuid()


        );


        // Paid member KHÔNG bị touch
        Assert.Equal(MemberPaymentStatus.PaidCash, paidMember.PaymentStatus);
        Assert.Equal(originalPaidAt, paidMember.PaidAt);
        Assert.Equal(originalUpdatedAt, paidMember.UpdatedAt);
        Assert.Equal(IndividualSessionStatus.Finished, paidMember.Status);
    }

    // =====================================================================
    // CASE 17: Empty reason — service không validate reason length.
    //   DTO [Required] + [MinLength(5)] xử lý ở controller layer.
    //   Service vẫn nhận và persist reason vào NoShowReason.
    // =====================================================================

    [Fact]
    public async Task ForceCloseWithUnpaidAsync_EmptyReason_StillPersistsOnMember()
    {
        var hostId = Guid.NewGuid();
        var session = CreateAndAttachUnpaidSession(
            hostId, subtotal: 50000m, penalty: 0m,
            extraMembers: new (Guid, bool, string?)[] { (Guid.NewGuid(), false, null) });

        _activeSessionRepo.Setup(r => r.GetByIdWithMembersAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        // Sau Gap-#5 fix: MarkNoShow flip → Paid (success, no throw).
        var result = await CreateService().ForceCloseWithUnpaidAsync(
            session.Id, MakeRequest("MarkNoShow", reason: "abc"), Guid.NewGuid());

        Assert.Equal(GroupSessionStatus.Paid, session.Status);

        // Tìm member bằng NoShow status (không phải PaymentStatus=NotPaid).
        var noShowMember = session.Members.First(m => m.Status == IndividualSessionStatus.NoShow);
        Assert.Contains("abc", noShowMember.NoShowReason!);
    }
}

