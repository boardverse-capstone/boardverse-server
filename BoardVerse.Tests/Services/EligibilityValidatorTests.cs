using BoardVerse.Core.DTOs.Reservation;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho EligibilityValidator — cross-role BR-USER-LIMIT-01/04/05 + BR-RISK-04.
/// </summary>
public class EligibilityValidatorTests
{
    private readonly EligibilityValidator _validator = new();

    private static HostReservationContext BuildHostContext(
        bool hasActiveHostLobby = false,
        bool hasActiveMemberLobby = false,
        bool isSuspended = false,
        bool isBanned = false)
    {
        return new HostReservationContext
        {
            HostId = Guid.NewGuid(),
            CafeId = Guid.NewGuid(),
            PlayDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
            PreferredScheduledStart = DateTime.UtcNow.AddHours(2),
            PreferredScheduledEnd = DateTime.UtcNow.AddHours(5),
            RecruitmentDeadline = DateTime.UtcNow.AddHours(2),
            Now = DateTime.UtcNow,
            WalletHeldBalance = 0,
            FinalDeposit = 100,
            HasActiveHostLobby = hasActiveHostLobby,
            HasActiveMemberLobby = hasActiveMemberLobby,
            IsAccountSuspended = isSuspended,
            IsAccountBanned = isBanned,
        };
    }

    private static MemberJoinContext BuildMemberContext(
        bool hasActiveHostLobby = false,
        int activeMemberLobbyCount = 0,
        bool isSuspended = false,
        bool isBanned = false)
    {
        return new MemberJoinContext
        {
            UserId = Guid.NewGuid(),
            CafeId = Guid.NewGuid(),
            PlayDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
            RecruitmentDeadline = DateTime.UtcNow.AddHours(2),
            Now = DateTime.UtcNow,
            HasActiveHostLobby = hasActiveHostLobby,
            ActiveMemberLobbyCount = activeMemberLobbyCount,
            IsAccountSuspended = isSuspended,
            IsAccountBanned = isBanned,
        };
    }

    // ===== Bug #1 regression: Suspended vs Banned phải trả message riêng =====

    [Fact]
    public void ValidateHostCanCreate_Should_ThrowBannedMessage_When_UserBanned()
    {
        // Arrange
        var ctx = BuildHostContext(isBanned: true);

        // Act + Assert
        var ex = Assert.Throws<ForbiddenException>(() => _validator.ValidateHostCanCreate(ctx));
        Assert.Equal(ApiErrorMessages.Reservation.BannedCannotCreateLobby, ex.Message);
        Assert.Contains("cấm vĩnh viễn", ex.Message);
    }

    [Fact]
    public void ValidateHostCanCreate_Should_ThrowSuspendedMessage_When_UserSuspended()
    {
        // Arrange
        var ctx = BuildHostContext(isSuspended: true);

        // Act + Assert
        var ex = Assert.Throws<ForbiddenException>(() => _validator.ValidateHostCanCreate(ctx));
        Assert.Equal(ApiErrorMessages.Reservation.SuspendedCannotCreateLobby, ex.Message);
        Assert.DoesNotContain("cấm vĩnh viễn", ex.Message);
        Assert.Contains("tạm khóa", ex.Message);
    }

    [Fact]
    public void ValidateMemberCanJoin_Should_ThrowSuspendedMessage_When_UserSuspended()
    {
        // Arrange
        var ctx = BuildMemberContext(isSuspended: true);

        // Act + Assert
        var ex = Assert.Throws<ForbiddenException>(() => _validator.ValidateMemberCanJoin(ctx));
        Assert.Equal(ApiErrorMessages.Reservation.SuspendedCannotCreateLobby, ex.Message);
        Assert.DoesNotContain("cấm vĩnh viễn", ex.Message);
    }

    [Fact]
    public void ValidateMemberCanJoin_Should_ThrowBannedMessage_When_UserBanned()
    {
        // Arrange
        var ctx = BuildMemberContext(isBanned: true);

        // Act + Assert
        var ex = Assert.Throws<ForbiddenException>(() => _validator.ValidateMemberCanJoin(ctx));
        Assert.Equal(ApiErrorMessages.Reservation.BannedCannotCreateLobby, ex.Message);
    }

    // ===== Bug #2 regression: Host vừa có host lobby vừa có member lobby =====
    // BR-USER-LIMIT-01: tổng ≤ 2. Sau khi đạt 2, phải throw message phù hợp (host hoặc member).

    [Fact]
    public void ValidateHostCanCreate_Should_ThrowMemberLimit_When_HostAndMemberBothActive()
    {
        // Arrange: user vừa host 1 lobby vừa member 1 lobby khác (đã max 2).
        var ctx = BuildHostContext(hasActiveHostLobby: true, hasActiveMemberLobby: true);

        // Act + Assert
        var ex = Assert.Throws<ForbiddenException>(() => _validator.ValidateHostCanCreate(ctx));
        // Ưu tiên member message vì user đã chủ động tham gia lobby khác trước.
        Assert.Equal(ApiErrorMessages.Reservation.MemberCannotCreateLobby, ex.Message);
    }

    [Fact]
    public void ValidateHostCanCreate_Should_ThrowHostLimit_When_OnlyHostActive()
    {
        // Arrange: user chỉ host 1 lobby (chưa max).
        var ctx = BuildHostContext(hasActiveHostLobby: true, hasActiveMemberLobby: false);

        // Act + Assert
        var ex = Assert.Throws<ForbiddenException>(() => _validator.ValidateHostCanCreate(ctx));
        Assert.Equal(ApiErrorMessages.Reservation.ActiveLobbyHostLimitReached, ex.Message);
    }

    [Fact]
    public void ValidateHostCanCreate_Should_ThrowMemberCannotCreate_When_OnlyMemberActive()
    {
        // Arrange: user chỉ member 1 lobby (chưa max).
        var ctx = BuildHostContext(hasActiveHostLobby: false, hasActiveMemberLobby: true);

        // Act + Assert
        var ex = Assert.Throws<ForbiddenException>(() => _validator.ValidateHostCanCreate(ctx));
        Assert.Equal(ApiErrorMessages.Reservation.MemberCannotCreateLobby, ex.Message);
    }

    [Fact]
    public void ValidateHostCanCreate_Should_NotThrow_When_NoActiveLobby()
    {
        // Arrange: user chưa có lobby nào.
        var ctx = BuildHostContext();

        // Act + Assert: không throw (ngoại trừ check overlap / playDate khác)
        // Vì context không set HasOverlapHostLobby/HasActiveLobbyOnPlayDate nên pass.
        _validator.ValidateHostCanCreate(ctx);
    }

    // ===== BR-USER-LIMIT-05: ĐÃ BỎ — Host có thể join lobby khác =====

    [Fact]
    public void ValidateMemberCanJoin_Should_Pass_When_OnlyHostActive_AndTotalBelow2()
    {
        // Arrange: user chỉ host 1 lobby (tổng = 1, chưa max).
        // BR-USER-LIMIT-05 ĐÃ BỎ — Host được phép join lobby khác nếu không overlap.
        var ctx = BuildMemberContext(hasActiveHostLobby: true, activeMemberLobbyCount: 0);

        // Act + Assert: không throw vì BR-USER-LIMIT-05 đã bỏ
        _validator.ValidateMemberCanJoin(ctx);
    }

    [Fact]
    public void ValidateMemberCanJoin_Should_ThrowMemberLimit_When_HostAndMemberBothActive()
    {
        // Arrange: user vừa host 1 lobby vừa member 1 lobby khác (tổng = 2, đã max).
        // BR-USER-LIMIT-01 chặn trước → throw MemberCannotCreateLobby vì member limit đạt max.
        var ctx = BuildMemberContext(hasActiveHostLobby: true, activeMemberLobbyCount: 1);

        // Act + Assert
        var ex = Assert.Throws<ForbiddenException>(() => _validator.ValidateMemberCanJoin(ctx));
        Assert.Equal(ApiErrorMessages.Reservation.TotalLobbyLimitReached, ex.Message);
    }

    [Fact]
    public void ValidateMemberCanJoin_Should_ThrowMemberLimit_When_OnlyMemberActive()
    {
        // Arrange
        var ctx = BuildMemberContext(hasActiveHostLobby: false, activeMemberLobbyCount: 1);

        // Act + Assert
        var ex = Assert.Throws<ForbiddenException>(() => _validator.ValidateMemberCanJoin(ctx));
        Assert.Equal(ApiErrorMessages.Reservation.ActiveLobbyMemberLimitReached, ex.Message);
    }

    [Fact]
    public void ValidateMemberCanJoin_Should_NotThrow_When_NoActiveLobby()
    {
        // Arrange
        var ctx = BuildMemberContext();

        // Act + Assert
        _validator.ValidateMemberCanJoin(ctx);
    }

    // ===== BR-DEMO-01: Demo mode bypass =====
    // ===== BR-USER-LIMIT-03: Cap tổng heldBalance =====
    // 500k thường / 1M VIP / 200k risk cao. Host phải tính được projected = held + finalDeposit.

    [Fact]
    public void ValidateHostCanCreate_Should_ThrowHeldCapExceeded_When_RegularUserExceeds500k()
    {
        // Arrange: regular user (IsVip=false, IsRiskMultiplierHigh=false) đã held 450k,
        // finalDeposit = 60k → projected = 510k > 500k → vượt cap regular.
        var ctx = BuildHostContext();
        ctx.IsVip = false;
        ctx.IsRiskMultiplierHigh = false;
        ctx.WalletHeldBalance = 450_000;
        ctx.FinalDeposit = 60_000;

        // Act + Assert
        var ex = Assert.Throws<ConflictException>(() => _validator.ValidateHostCanCreate(ctx));
        Assert.Equal(ApiErrorMessages.Reservation.HeldDepositCapExceeded(450_000, 500_000, "thường"), ex.Message);
    }

    [Fact]
    public void ValidateHostCanCreate_Should_NotThrow_When_RegularUserExactlyAtCap()
    {
        // Arrange: regular user held 440k + finalDeposit 60k = 500k = cap → không vượt, vẫn OK.
        var ctx = BuildHostContext();
        ctx.WalletHeldBalance = 440_000;
        ctx.FinalDeposit = 60_000;

        // Act + Assert: không throw
        _validator.ValidateHostCanCreate(ctx);
    }

    [Fact]
    public void ValidateHostCanCreate_Should_AllowVipUser_UpTo1MillionBvc()
    {
        // Arrange: VIP user held 950k + finalDeposit 50k = 1M = VIP cap → OK.
        var ctx = BuildHostContext();
        ctx.IsVip = true;
        ctx.WalletHeldBalance = 950_000;
        ctx.FinalDeposit = 50_000;

        // Act + Assert: không throw vì projected = 1M = cap
        _validator.ValidateHostCanCreate(ctx);
    }

    [Fact]
    public void ValidateHostCanCreate_Should_ThrowHeldCapExceeded_When_VipUserExceeds1MillionBvc()
    {
        // Arrange: VIP user held 950k + finalDeposit 60k = 1.01M > 1M cap.
        var ctx = BuildHostContext();
        ctx.IsVip = true;
        ctx.WalletHeldBalance = 950_000;
        ctx.FinalDeposit = 60_000;

        // Act + Assert
        var ex = Assert.Throws<ConflictException>(() => _validator.ValidateHostCanCreate(ctx));
        Assert.Equal(ApiErrorMessages.Reservation.HeldDepositCapExceeded(950_000, 1_000_000, "VIP"), ex.Message);
    }

    [Fact]
    public void ValidateHostCanCreate_Should_ApplyRiskCap_When_RiskMultiplierHigh()
    {
        // Arrange: riskMultiplierHigh user → cap 200k. Held 150k + finalDeposit 60k = 210k > 200k.
        var ctx = BuildHostContext();
        ctx.IsVip = false;
        ctx.IsRiskMultiplierHigh = true;
        ctx.WalletHeldBalance = 150_000;
        ctx.FinalDeposit = 60_000;

        // Act + Assert
        var ex = Assert.Throws<ConflictException>(() => _validator.ValidateHostCanCreate(ctx));
        Assert.Equal(ApiErrorMessages.Reservation.HeldDepositCapExceeded(150_000, 200_000, "risk cao"), ex.Message);
    }

    [Fact]
    public void ValidateHostCanCreate_Should_NotThrow_When_RiskMultiplierHigh_AtExact200kCap()
    {
        // Arrange: risk user held 140k + finalDeposit 60k = 200k = cap risk.
        var ctx = BuildHostContext();
        ctx.IsRiskMultiplierHigh = true;
        ctx.WalletHeldBalance = 140_000;
        ctx.FinalDeposit = 60_000;

        // Act + Assert: không throw
        _validator.ValidateHostCanCreate(ctx);
    }

    [Fact]
    public void ValidateHostCanCreate_Should_PreferVipCap_OverRiskCap_When_BothFlags()
    {
        // BR-USER-LIMIT-03: ResolveTotalDepositCap kiểm tra IsVip trước IsRiskMultiplierHigh.
        // Đây là data bug defensive — user không nên vừa VIP vừa risk cao,
        // nhưng nếu có thì cap = 1M (VIP), không phải 200k (risk).
        // Held 180k + finalDeposit 30k = 210k > 200k risk nhưng < 1M VIP → không throw.
        var ctx = BuildHostContext();
        ctx.IsVip = true;
        ctx.IsRiskMultiplierHigh = true;
        ctx.WalletHeldBalance = 180_000;
        ctx.FinalDeposit = 30_000; // projected = 210k

        // Act + Assert: KHÔNG throw vì IsVip=true → cap = 1M (VIP)
        _validator.ValidateHostCanCreate(ctx);
    }

    // ===== BR-NEW-05: Max 5 lần tạo/hủy / playDate =====

    [Fact]
    public void ValidateHostCanCreate_Should_Throw_When_HostCreateOrCancelReachesLimit()
    {
        // Arrange: host đã tạo+hủy 5 lần cho cùng playDate.
        var ctx = BuildHostContext();
        ctx.HostCreateOrCancelCount = 5;

        // Act + Assert
        var ex = Assert.Throws<ConflictException>(() => _validator.ValidateHostCanCreate(ctx));
        Assert.Equal(ApiErrorMessages.Reservation.HostCreatesCancelsLimitReached(5), ex.Message);
    }

    [Fact]
    public void ValidateHostCanCreate_Should_Throw_When_HostCreateOrCancelExceedsLimit()
    {
        // Arrange: count > 5 (defensive — DB sum có thể trả > 5 nếu race).
        var ctx = BuildHostContext();
        ctx.HostCreateOrCancelCount = 7;

        // Act + Assert: vẫn throw
        Assert.Throws<ConflictException>(() => _validator.ValidateHostCanCreate(ctx));
    }

    [Fact]
    public void ValidateHostCanCreate_Should_NotThrow_When_HostCreateOrCancelBelowLimit()
    {
        // Arrange: count = 4 → còn 1 lần nữa cho phép tạo.
        var ctx = BuildHostContext();
        ctx.HostCreateOrCancelCount = 4;

        // Act + Assert: không throw
        _validator.ValidateHostCanCreate(ctx);
    }

    [Fact]
    public async Task ValidateHostCanCreateAsync_Should_BypassAllLimits_When_DemoModeOn()
    {
        // Arrange: đầy đủ cờ sẽ throw bình thường — host có 1 lobby + member 1 lobby
        // + max-create-or-cancel đạt giới hạn. Demo mode → tất cả skip.
        var ctx = BuildHostContext(
            hasActiveHostLobby: true,
            hasActiveMemberLobby: true);
        ctx.HostCreateOrCancelCount = 10; // > 5
        ctx.WalletHeldBalance = 600_000; // > 500k thường

        var configProvider = new Mock<ISystemConfigurationProvider>();
        configProvider
            .Setup(p => p.GetStringAsync("demo_loosen_lobby_constraints", "false", It.IsAny<CancellationToken>()))
            .ReturnsAsync("true");

        // Act + Assert: KHÔNG throw
        await _validator.ValidateHostCanCreateAsync(
            ctx,
            httpContextAccessor: null,
            configProvider.Object,
            NullLogger.Instance);
    }

    [Fact]
    public async Task ValidateMemberCanJoinAsync_Should_BypassAllLimits_When_DemoModeOn()
    {
        // Arrange: member đã có 1 lobby active + host có 1 lobby → BR-USER-LIMIT-01 đạt max 2.
        var ctx = BuildMemberContext(
            hasActiveHostLobby: true,
            activeMemberLobbyCount: 1);

        var configProvider = new Mock<ISystemConfigurationProvider>();
        configProvider
            .Setup(p => p.GetStringAsync("demo_loosen_lobby_constraints", "false", It.IsAny<CancellationToken>()))
            .ReturnsAsync("true");

        // Act + Assert: KHÔNG throw
        await _validator.ValidateMemberCanJoinAsync(
            ctx,
            httpContextAccessor: null,
            configProvider.Object,
            NullLogger.Instance);
    }

    [Fact]
    public async Task ValidateHostCanCreateAsync_Should_StillThrow_When_DemoModeOff()
    {
        // Arrange: control — demo mode OFF → vẫn throw bình thường.
        var ctx = BuildHostContext(hasActiveHostLobby: true);

        var configProvider = new Mock<ISystemConfigurationProvider>();
        configProvider
            .Setup(p => p.GetStringAsync("demo_loosen_lobby_constraints", "false", It.IsAny<CancellationToken>()))
            .ReturnsAsync("false");

        // Act + Assert
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _validator.ValidateHostCanCreateAsync(
                ctx,
                httpContextAccessor: null,
                configProvider.Object,
                NullLogger.Instance));
    }
}
