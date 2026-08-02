using BoardVerse.Core.Enum;
using BoardVerse.Services.Services;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho EligibilityValidator — pure function (BR-USER-LIMIT-*, BR-NEW-*, BR-RISK-04).
/// BR §XXI-1 review checklist #1 "BR-USER-LIMIT-04/05 validate trước khi tạo/join lobby"
/// và #9 "Cap tổng heldBalance (BR-USER-LIMIT-03) theo user type".
/// </summary>
public class EligibilityValidatorTests
{
    private readonly EligibilityValidator _validator = new();

    private static HostReservationContext BuildHostContext() => new()
    {
        HostId = Guid.NewGuid(),
        CafeId = Guid.NewGuid(),
        PlayDate = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(2)),
        TimeSlot = TimeSlot.Evening,
        RecruitmentDeadline = DateTime.UtcNow.AddHours(2),
        Now = DateTime.UtcNow,
        FinalDeposit = 100_000,
        WalletHeldBalance = 0
    };

    // ===== Happy path =====

    [Fact]
    public void ValidateHostCanCreate_DefaultContext_DoesNotThrow()
    {
        // Arrange
        var context = BuildHostContext();

        // Act & Assert
        _validator.ValidateHostCanCreate(context);
    }

    // ===== BR-RISK-04: suspended/banned chặn tạo lobby =====

    [Fact]
    public void ValidateHostCanCreate_AccountSuspended_Throws()
    {
        // Arrange
        var context = BuildHostContext();
        context.IsAccountSuspended = true;

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => _validator.ValidateHostCanCreate(context));
    }

    [Fact]
    public void ValidateHostCanCreate_AccountBanned_Throws()
    {
        // Arrange
        var context = BuildHostContext();
        context.IsAccountBanned = true;

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => _validator.ValidateHostCanCreate(context));
    }

    // ===== BR-USER-LIMIT-01: max 1 host lobby active =====

    [Fact]
    public void ValidateHostCanCreate_AlreadyHasActiveHostLobby_Throws()
    {
        // Arrange
        var context = BuildHostContext();
        context.HasActiveHostLobby = true;

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => _validator.ValidateHostCanCreate(context));
    }

    [Fact]
    public void ValidateHostCanCreate_AlreadyMemberOfActiveLobby_Throws()
    {
        // Arrange (BR-USER-LIMIT-04)
        var context = BuildHostContext();
        context.HasActiveMemberLobby = true;

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => _validator.ValidateHostCanCreate(context));
    }

    // ===== BR-USER-LIMIT-02: overlap =====

    [Fact]
    public void ValidateHostCanCreate_OverlappingLobby_Throws()
    {
        // Arrange
        var context = BuildHostContext();
        context.HasOverlapHostLobby = true;
        context.OverlapOtherDeadline = DateTime.UtcNow.AddHours(3);
        context.OverlapOtherStart = DateTime.UtcNow.AddHours(4);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => _validator.ValidateHostCanCreate(context));
    }

    // ===== BR-NEW-02: 1 lobby active / playDate =====

    [Fact]
    public void ValidateHostCanCreate_AlreadyHasLobbyOnSamePlayDate_Throws()
    {
        // Arrange
        var context = BuildHostContext();
        context.HasActiveLobbyOnPlayDate = true;

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => _validator.ValidateHostCanCreate(context));
    }

    // ===== BR-NEW-08: 1 lobby / playDate+slot / cafe / user =====

    [Fact]
    public void ValidateHostCanCreate_AlreadyHasLobbySameCafeSlot_Throws()
    {
        // Arrange
        var context = BuildHostContext();
        context.HasActiveLobbyOnCafeSlot = true;

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => _validator.ValidateHostCanCreate(context));
    }

    // ===== BR-NEW-05: tối đa 5 lần tạo/hủy / playDate =====

    [Fact]
    public void ValidateHostCanCreate_HostCreateCancelCountOverLimit_Throws()
    {
        // Arrange
        var context = BuildHostContext();
        context.HostCreateOrCancelCount = 5; // >= 5 → reject

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => _validator.ValidateHostCanCreate(context));
    }

    [Fact]
    public void ValidateHostCanCreate_HostCreateCancelCountAt4_DoesNotThrow()
    {
        // Arrange
        var context = BuildHostContext();
        context.HostCreateOrCancelCount = 4; // < 5 → ok

        // Act & Assert
        _validator.ValidateHostCanCreate(context);
    }

    // ===== BR-USER-LIMIT-03: cap tổng heldBalance =====

    [Fact]
    public void ValidateHostCanCreate_RegularUser_HeldBalanceNear500k_Throws()
    {
        // Arrange
        var context = BuildHostContext();
        context.WalletHeldBalance = 450_000;
        context.FinalDeposit = 100_000; // projected = 550k > 500k cap

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => _validator.ValidateHostCanCreate(context));
    }

    [Fact]
    public void ValidateHostCanCreate_RegularUser_HeldBalanceBelow500k_DoesNotThrow()
    {
        // Arrange
        var context = BuildHostContext();
        context.WalletHeldBalance = 300_000;
        context.FinalDeposit = 100_000; // projected = 400k < 500k cap

        // Act & Assert
        _validator.ValidateHostCanCreate(context);
    }

    [Fact]
    public void ValidateHostCanCreate_VipUser_HeldBalanceNear1M_DoesNotThrow()
    {
        // Arrange
        var context = BuildHostContext();
        context.IsVip = true;
        context.WalletHeldBalance = 900_000;
        context.FinalDeposit = 100_000; // projected = 1M ≤ 1M VIP cap

        // Act & Assert
        _validator.ValidateHostCanCreate(context);
    }

    [Fact]
    public void ValidateHostCanCreate_RiskUser_HeldBalanceOver200k_Throws()
    {
        // Arrange
        var context = BuildHostContext();
        context.IsRiskMultiplierHigh = true;
        context.WalletHeldBalance = 150_000;
        context.FinalDeposit = 100_000; // projected = 250k > 200k risk cap

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => _validator.ValidateHostCanCreate(context));
    }

    [Fact]
    public void ValidateHostCanCreate_RiskUser_HeldBalanceUnder200k_DoesNotThrow()
    {
        // Arrange
        var context = BuildHostContext();
        context.IsRiskMultiplierHigh = true;
        context.WalletHeldBalance = 100_000;
        context.FinalDeposit = 50_000; // projected = 150k ≤ 200k risk cap

        // Act & Assert
        _validator.ValidateHostCanCreate(context);
    }

    // ===== BR-NEW-10: cooling-off không cho tạo lobby > 1 ngày =====

    [Fact]
    public void ValidateHostCanCreate_CoolingOff_DistantPlayDate_Throws()
    {
        // Arrange
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var context = BuildHostContext();
        context.IsCoolingOff = true;
        context.Now = now;
        context.PlayDate = DateOnly.FromDateTime(now).AddDays(5); // 5 ngày tới
        context.RecruitmentDeadline = now.AddDays(5).AddHours(-1);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => _validator.ValidateHostCanCreate(context));
    }

    [Fact]
    public void ValidateHostCanCreate_CoolingOff_SameDayPlayDate_DoesNotThrow()
    {
        // Arrange
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var context = BuildHostContext();
        context.IsCoolingOff = true;
        context.Now = now;
        context.PlayDate = DateOnly.FromDateTime(now); // same day
        context.RecruitmentDeadline = now.AddHours(5);

        // Act & Assert
        _validator.ValidateHostCanCreate(context);
    }

    [Fact]
    public void ValidateHostCanCreate_CoolingOff_NextDayPlayDate_DoesNotThrow()
    {
        // Arrange
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var context = BuildHostContext();
        context.IsCoolingOff = true;
        context.Now = now;
        context.PlayDate = DateOnly.FromDateTime(now).AddDays(1); // 1 ngày tới
        context.RecruitmentDeadline = now.AddDays(1).AddHours(-1);

        // Act & Assert
        _validator.ValidateHostCanCreate(context);
    }

    // ===== ValidateMemberCanJoin =====

    private static MemberJoinContext BuildMemberContext() => new()
    {
        UserId = Guid.NewGuid(),
        CafeId = Guid.NewGuid(),
        PlayDate = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(2)),
        TimeSlot = TimeSlot.Evening,
        RecruitmentDeadline = DateTime.UtcNow.AddHours(2),
        Now = DateTime.UtcNow
    };

    [Fact]
    public void ValidateMemberCanJoin_DefaultContext_DoesNotThrow()
    {
        // Arrange
        var context = BuildMemberContext();

        // Act & Assert
        _validator.ValidateMemberCanJoin(context);
    }

    [Fact]
    public void ValidateMemberCanJoin_SuspendedAccount_Throws()
    {
        // Arrange
        var context = BuildMemberContext();
        context.IsAccountSuspended = true;

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => _validator.ValidateMemberCanJoin(context));
    }

    [Fact]
    public void ValidateMemberCanJoin_HostCannotJoinOtherLobby_Throws()
    {
        // Arrange (BR-USER-LIMIT-05)
        var context = BuildMemberContext();
        context.HasActiveHostLobby = true;

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => _validator.ValidateMemberCanJoin(context));
    }

    [Fact]
    public void ValidateMemberCanJoin_AlreadyOneMemberLobby_Throws()
    {
        // Arrange (BR-USER-LIMIT-01)
        var context = BuildMemberContext();
        context.ActiveMemberLobbyCount = 1;

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => _validator.ValidateMemberCanJoin(context));
    }

    [Fact]
    public void ValidateMemberCanJoin_OverlappingMemberLobby_Throws()
    {
        // Arrange
        var context = BuildMemberContext();
        context.HasOverlapMemberLobby = true;

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => _validator.ValidateMemberCanJoin(context));
    }

    [Fact]
    public void ValidateMemberCanJoin_AlreadyMemberOnSamePlayDate_Throws()
    {
        // Arrange
        var context = BuildMemberContext();
        context.ActiveMemberLobbyOnPlayDateCount = 1;

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => _validator.ValidateMemberCanJoin(context));
    }
}
