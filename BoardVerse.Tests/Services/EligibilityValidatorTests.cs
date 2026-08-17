using BoardVerse.Core.DTOs.Reservation;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Messages;
using BoardVerse.Services.Services;

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
            TimeSlot = TimeSlot.Evening,
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
            TimeSlot = TimeSlot.Evening,
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
        Assert.Equal(ApiErrorMessages.Reservation.MemberCannotCreateLobby, ex.Message);
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
}
