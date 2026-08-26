using BoardVerse.Core.DTOs.Reservation;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Messages;
using BoardVerse.Services.Services;
using Moq;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Edge case tests cho BR-USER-LIMIT-03 cap matrix:
/// - Regular user: 500.000 BVC cap
/// - VIP user: 1.000.000 BVC cap (BR-USER-LIMIT-03 priority cao nhất)
/// - Risk user (riskMultiplier ≥ 1.25): 200.000 BVC cap
///
/// Test xác nhận rằng ResolveTotalDepositCap có 3 mức rõ ràng và
/// projected = WalletHeldBalance + FinalDeposit mới là giá trị bị cap,
/// không phải riêng FinalDeposit.
/// </summary>
public class EligibilityValidatorAdvancedLimitTests
{
    private readonly EligibilityValidator _validator = new();

    private static HostReservationContext BuildContextWith(
        long walletHeldBalance,
        long finalDeposit,
        bool isVip = false,
        bool isRiskMultiplierHigh = false,
        bool isAccountSuspended = false,
        bool isAccountBanned = false)
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
            WalletHeldBalance = walletHeldBalance,
            FinalDeposit = finalDeposit,
            IsVip = isVip,
            IsRiskMultiplierHigh = isRiskMultiplierHigh,
            IsAccountSuspended = isAccountSuspended,
            IsAccountBanned = isAccountBanned,
        };
    }

    // ===== Regular user (default): cap 500k =====

    [Fact]
    public void Cap_RegularUser_PassAtExactly500k()
    {
        // projected = 0 held + 500k deposit = 500k = cap → không throw
        var ctx = BuildContextWith(0, 500_000);
        _validator.ValidateHostCanCreate(ctx);
    }

    [Fact]
    public void Cap_RegularUser_FailJustOneBvcOver500k()
    {
        // projected = 1 held + 500_000 deposit = 500_001 > 500k → throw
        var ctx = BuildContextWith(1, 500_000);

        var ex = Assert.Throws<ConflictException>(() => _validator.ValidateHostCanCreate(ctx));
        Assert.Contains("500", ex.Message);
    }

    // ===== VIP user: cap 1M =====

    [Fact]
    public void Cap_VipUser_PassAtExactly1M()
    {
        // VIP projected = 1M = cap → OK
        var ctx = BuildContextWith(0, 1_000_000, isVip: true);
        _validator.ValidateHostCanCreate(ctx);
    }

    [Fact]
    public void Cap_VipUser_FailJustOneBvcOver1M()
    {
        // VIP projected = 1_000_001 > 1M → throw với message VIP
        var ctx = BuildContextWith(1, 1_000_000, isVip: true);

        var ex = Assert.Throws<ConflictException>(() => _validator.ValidateHostCanCreate(ctx));
        Assert.Equal(
            ApiErrorMessages.Reservation.HeldDepositCapExceeded(1, 1_000_000, "VIP"),
            ex.Message);
    }

    [Fact]
    public void Cap_VipUser_PassRegularUserAmountWithinVipCap()
    {
        // VIP user creating regular-sized lobby → no cap issue
        var ctx = BuildContextWith(450_000, 60_000, isVip: true);
        _validator.ValidateHostCanCreate(ctx);
    }

    // ===== Risk user: cap 200k =====

    [Fact]
    public void Cap_RiskUser_PassAtExactly200k()
    {
        // Risk projected = 200k = cap → OK
        var ctx = BuildContextWith(0, 200_000, isRiskMultiplierHigh: true);
        _validator.ValidateHostCanCreate(ctx);
    }

    [Fact]
    public void Cap_RiskUser_FailJustOneBvcOver200k()
    {
        // Risk projected = 200_001 > 200k → throw với message risk cao
        var ctx = BuildContextWith(1, 200_000, isRiskMultiplierHigh: true);

        var ex = Assert.Throws<ConflictException>(() => _validator.ValidateHostCanCreate(ctx));
        Assert.Equal(
            ApiErrorMessages.Reservation.HeldDepositCapExceeded(1, 200_000, "risk cao"),
            ex.Message);
    }

    [Fact]
    public void Cap_RiskUser_BlockRegularSizedLobby_WhenExceedsCap()
    {
        // Even a 60k lobby fails because risk user already held 150k → 210k > 200k
        var ctx = BuildContextWith(150_000, 60_000, isRiskMultiplierHigh: true);

        var ex = Assert.Throws<ConflictException>(() => _validator.ValidateHostCanCreate(ctx));
        Assert.Contains("200", ex.Message);
    }

    // ===== Priority: VIP > Risk > Regular =====

    [Fact]
    public void Cap_Priority_VipBeatsRisk_WhenBothFlagsTrue()
    {
        // VIP trước risk (theo ResolveTotalDepositCap logic) → cap = 1M
        // Held 900k + 60k = 960k < 1M → OK
        var ctx = BuildContextWith(900_000, 60_000, isVip: true, isRiskMultiplierHigh: true);
        _validator.ValidateHostCanCreate(ctx);
    }

    [Fact]
    public void Cap_RegularUser_NoVipFlag_StillSubjectTo500kCap()
    {
        // Default (no flags) = regular user → 500k cap
        var ctx = BuildContextWith(450_000, 60_000, isVip: false, isRiskMultiplierHigh: false);

        var ex = Assert.Throws<ConflictException>(() => _validator.ValidateHostCanCreate(ctx));
        Assert.Contains("thường", ex.Message);
    }

    // ===== Edge: zero held, zero deposit =====

    [Fact]
    public void Cap_ZeroHeldAndDeposit_AlwaysPass()
    {
        // Empty wallet + free lobby → no cap issue (BR-USER-LIMIT-03 chỉ check projected > cap)
        var ctx = BuildContextWith(0, 0);
        _validator.ValidateHostCanCreate(ctx);
    }

    // ===== Suspended user bypass cap check (BR-RISK-04 trước BR-USER-LIMIT-03) =====

    [Fact]
    public void Cap_SuspendedUser_BlocksWithSuspendedMessage_BeforeCapCheck()
    {
        // Suspended check phải chạy trước cap check → message suspended, không phải cap
        var ctx = BuildContextWith(0, 50_000, isAccountSuspended: true);

        var ex = Assert.Throws<ForbiddenException>(() => _validator.ValidateHostCanCreate(ctx));
        Assert.Equal(ApiErrorMessages.Reservation.SuspendedCannotCreateLobby, ex.Message);
    }

    [Fact]
    public void Cap_BannedUser_BlocksWithBannedMessage_BeforeCapCheck()
    {
        var ctx = BuildContextWith(0, 50_000, isAccountBanned: true);

        var ex = Assert.Throws<ForbiddenException>(() => _validator.ValidateHostCanCreate(ctx));
        Assert.Equal(ApiErrorMessages.Reservation.BannedCannotCreateLobby, ex.Message);
    }
}
