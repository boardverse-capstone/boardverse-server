using BoardVerse.Core.DTOs.Receipt;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho ReceiptService — receipt cho session đã thanh toán + revenue report.
/// BR-POS-15: chỉ tạo receipt cho session Status = Paid.
/// </summary>
public class ReceiptServiceTests
{
    private static BoardVerseDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<BoardVerseDbContext>()
            .UseInMemoryDatabase($"ReceiptServiceTests-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new BoardVerseDbContext(options);
    }

    private static Cafe BuildCafe(Guid id) => new()
    {
        Id = id,
        Name = "Test Cafe",
        Address = "123 Test Street",
        IsActive = true
    };

    private static GameTemplate BuildGame(Guid id) => new()
    {
        Id = id,
        Name = "Catan",
        NameSearchKey = "catan",
        MinPlayers = 3,
        MaxPlayers = 4,
        PlayTime = 60
    };

    private static CafeTable BuildTable(Guid cafeId) => new()
    {
        Id = Guid.NewGuid(),
        CafeId = cafeId,
        Name = "Table 1",
        SeatCount = 4,
        IsActive = true
    };

    private static User BuildUser(Guid id) => new()
    {
        Id = id,
        Username = "alice",
        Email = "alice@test.local"
    };

    private static ActiveSession BuildPaidSession(
        Guid cafeId,
        Guid gameId,
        DateTime startedAt,
        DateTime paidAt) => new()
    {
        Id = Guid.NewGuid(),
        CafeId = cafeId,
        HostId = Guid.NewGuid(),
        GameTemplateId = gameId,
        CafeTableId = Guid.NewGuid(),
        Status = GroupSessionStatus.Paid,
        StartedAt = startedAt,
        EndedAt = paidAt,
        PaidAt = paidAt,
        Subtotal = 60_000m,
        DepositAppliedAmount = 0m,
        PenaltyAmount = 0m,
        TotalAmount = 60_000m,
        TotalMinutesPlayed = 60
    };

    #region GenerateSessionReceiptAsync

    [Fact]
    public async Task GenerateSessionReceiptAsync_SessionNotFound_ThrowsNotFound()
    {
        var db = CreateInMemoryDb();
        var sut = new ReceiptService(db, NullLogger<ReceiptService>.Instance);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            sut.GenerateSessionReceiptAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GenerateSessionReceiptAsync_SessionNotPaid_ThrowsConflict()
    {
        var db = CreateInMemoryDb();
        var cafeId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        db.Cafes.Add(BuildCafe(cafeId));
        db.GameTemplates.Add(BuildGame(gameId));
        var session = new ActiveSession
        {
            Id = Guid.NewGuid(),
            CafeId = cafeId,
            HostId = Guid.NewGuid(),
            GameTemplateId = gameId,
            CafeTableId = Guid.NewGuid(),
            Status = GroupSessionStatus.Active,
            StartedAt = DateTime.UtcNow.AddHours(-1)
        };
        db.ActiveSessions.Add(session);
        await db.SaveChangesAsync();

        var sut = new ReceiptService(db, NullLogger<ReceiptService>.Instance);

        await Assert.ThrowsAsync<ConflictException>(() =>
            sut.GenerateSessionReceiptAsync(session.Id));
    }

    [Fact]
    public async Task GenerateSessionReceiptAsync_PaidSessionWithoutMembers_GeneratesEmptyMembersList()
    {
        var db = CreateInMemoryDb();
        var cafeId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        db.Cafes.Add(BuildCafe(cafeId));
        db.GameTemplates.Add(BuildGame(gameId));
        var session = BuildPaidSession(cafeId, gameId, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow);
        db.ActiveSessions.Add(session);
        await db.SaveChangesAsync();

        var sut = new ReceiptService(db, NullLogger<ReceiptService>.Instance);
        var receipt = await sut.GenerateSessionReceiptAsync(session.Id);

        Assert.Equal(session.Id, receipt.SessionId);
        Assert.Equal("Test Cafe", receipt.CafeName);
        Assert.Equal("Catan", receipt.GameName);
        Assert.Empty(receipt.Members);
        Assert.Equal(60_000m, receipt.GrandTotal);
    }

    [Fact]
    public async Task GenerateSessionReceiptAsync_PaidSessionWithMembers_IncludesMembers()
    {
        var db = CreateInMemoryDb();
        var cafeId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        db.Cafes.Add(BuildCafe(cafeId));
        db.GameTemplates.Add(BuildGame(gameId));
        var user1 = BuildUser(Guid.NewGuid());
        var user2 = BuildUser(Guid.NewGuid());
        db.Users.AddRange(user1, user2);

        var session = BuildPaidSession(cafeId, gameId, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow);
        db.ActiveSessions.Add(session);
        db.ActiveSessionMembers.Add(new ActiveSessionMember
        {
            Id = Guid.NewGuid(),
            ActiveSessionId = session.Id,
            UserId = user1.Id,
            IsGuestSlot = false,
            TotalMinutesPlayed = 60,
            Subtotal = 30_000m,
            DepositAppliedAmount = 0m,
            PenaltyAmount = 0m,
            TotalAmount = 30_000m
        });
        db.ActiveSessionMembers.Add(new ActiveSessionMember
        {
            Id = Guid.NewGuid(),
            ActiveSessionId = session.Id,
            UserId = user2.Id,
            IsGuestSlot = false,
            TotalMinutesPlayed = 60,
            Subtotal = 30_000m,
            DepositAppliedAmount = 0m,
            PenaltyAmount = 0m,
            TotalAmount = 30_000m
        });
        await db.SaveChangesAsync();

        var sut = new ReceiptService(db, NullLogger<ReceiptService>.Instance);
        var receipt = await sut.GenerateSessionReceiptAsync(session.Id);

        Assert.Equal(2, receipt.Members.Count);
        Assert.Equal(60_000m, receipt.TotalSubtotal);
    }

    [Fact]
    public async Task GenerateSessionReceiptAsync_GuestSlot_ShowsGuestDisplayName()
    {
        var db = CreateInMemoryDb();
        var cafeId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        db.Cafes.Add(BuildCafe(cafeId));
        db.GameTemplates.Add(BuildGame(gameId));

        var session = BuildPaidSession(cafeId, gameId, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow);
        db.ActiveSessions.Add(session);
        db.ActiveSessionMembers.Add(new ActiveSessionMember
        {
            Id = Guid.NewGuid(),
            ActiveSessionId = session.Id,
            UserId = null,
            IsGuestSlot = true,
            GuestDisplayName = "Walk-in Bob",
            TotalMinutesPlayed = 30,
            Subtotal = 15_000m,
            DepositAppliedAmount = 0m,
            PenaltyAmount = 0m,
            TotalAmount = 15_000m
        });
        await db.SaveChangesAsync();

        var sut = new ReceiptService(db, NullLogger<ReceiptService>.Instance);
        var receipt = await sut.GenerateSessionReceiptAsync(session.Id);

        Assert.Single(receipt.Members);
        Assert.Equal("Walk-in Bob", receipt.Members[0].DisplayName);
        Assert.True(receipt.Members[0].IsGuestSlot);
    }

    #endregion

    #region GenerateMemberReceiptAsync (M2/C2.15 — Gap #32)

    /// <summary>
    /// Helper: tạo 1 paid session + 1 member với PaidBvcAmount > 0
    /// để test format M2 fields trên MemberReceiptDto.
    /// </summary>
    private static (ActiveSession session, ActiveSessionMember member, User user) BuildPaidSessionWithMember(
        long paidBvcAmount = 50_000L)
    {
        var cafeId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var session = BuildPaidSession(cafeId, gameId, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow);
        var user = new User
        {
            Id = userId,
            Username = "alice",
            Email = "alice@test.local"
        };
        var member = new ActiveSessionMember
        {
            Id = Guid.NewGuid(),
            ActiveSessionId = session.Id,
            UserId = userId,
            IsGuestSlot = false,
            IsHost = true,
            TotalMinutesPlayed = 60,
            Subtotal = 60_000m,
            DepositAppliedAmount = 10_000m,
            PenaltyAmount = 0m,
            TotalAmount = 50_000m,
            PaidBvcAmount = paidBvcAmount,
            PaidCashRemainder = 0m,
            PaymentMethod = "Bvc",
            PaymentStatus = MemberPaymentStatus.PaidBvc,
            PaidAt = DateTime.UtcNow
        };

        return (session, member, user);
    }

    [Fact]
    public async Task GenerateMemberReceiptAsync_FormatJson_ReturnsJsonContentType()
    {
        var db = CreateInMemoryDb();
        var (session, member, user) = BuildPaidSessionWithMember();
        db.Cafes.Add(BuildCafe(session.CafeId));
        db.GameTemplates.Add(BuildGame(session.GameTemplateId));
        db.Users.Add(user);
        db.ActiveSessions.Add(session);
        db.ActiveSessionMembers.Add(member);
        await db.SaveChangesAsync();

        var sut = new ReceiptService(db, NullLogger<ReceiptService>.Instance);
        var receiptFile = await sut.GenerateMemberReceiptAsync(session.Id, member.Id, "json");

        Assert.Equal("application/json", receiptFile.ContentType);
        Assert.EndsWith(".json", receiptFile.FileName);
        Assert.NotEmpty(receiptFile.Bytes);

        // Verify bytes deserialize thành MemberReceiptDto hợp lệ.
        var json = System.Text.Encoding.UTF8.GetString(receiptFile.Bytes);
        Assert.Contains("\"sessionId\"", json);
        Assert.Contains("\"paidBvcAmount\"", json);
        Assert.Contains("alice", json); // DisplayName
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("png")]
    public async Task GenerateMemberReceiptAsync_FormatPdfOrPng_ThrowsBadRequest(string format)
    {
        var db = CreateInMemoryDb();
        var (session, member, user) = BuildPaidSessionWithMember();
        db.Cafes.Add(BuildCafe(session.CafeId));
        db.GameTemplates.Add(BuildGame(session.GameTemplateId));
        db.Users.Add(user);
        db.ActiveSessions.Add(session);
        db.ActiveSessionMembers.Add(member);
        await db.SaveChangesAsync();

        var sut = new ReceiptService(db, NullLogger<ReceiptService>.Instance);

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            sut.GenerateMemberReceiptAsync(session.Id, member.Id, format));

        Assert.Contains(format, ex.Message);
        Assert.Contains("json", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("xml")]
    [InlineData("docx")]
    public async Task GenerateMemberReceiptAsync_InvalidFormat_ThrowsBadRequest(string format)
    {
        var db = CreateInMemoryDb();
        var (session, member, user) = BuildPaidSessionWithMember();
        db.Cafes.Add(BuildCafe(session.CafeId));
        db.GameTemplates.Add(BuildGame(session.GameTemplateId));
        db.Users.Add(user);
        db.ActiveSessions.Add(session);
        db.ActiveSessionMembers.Add(member);
        await db.SaveChangesAsync();

        var sut = new ReceiptService(db, NullLogger<ReceiptService>.Instance);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            sut.GenerateMemberReceiptAsync(session.Id, member.Id, format));
    }

    [Fact]
    public async Task GenerateMemberReceiptAsync_FormatCaseInsensitive_JsonAccepted()
    {
        var db = CreateInMemoryDb();
        var (session, member, user) = BuildPaidSessionWithMember();
        db.Cafes.Add(BuildCafe(session.CafeId));
        db.GameTemplates.Add(BuildGame(session.GameTemplateId));
        db.Users.Add(user);
        db.ActiveSessions.Add(session);
        db.ActiveSessionMembers.Add(member);
        await db.SaveChangesAsync();

        var sut = new ReceiptService(db, NullLogger<ReceiptService>.Instance);

        // "JSON", "Json", "  json  " đều phải pass.
        var upperReceipt = await sut.GenerateMemberReceiptAsync(session.Id, member.Id, "JSON");
        Assert.Equal("application/json", upperReceipt.ContentType);

        var paddedReceipt = await sut.GenerateMemberReceiptAsync(session.Id, member.Id, "  json  ");
        Assert.Equal("application/json", paddedReceipt.ContentType);
    }

    [Fact]
    public async Task GenerateMemberReceiptAsync_PaidSessionWithBvcPayment_IncludesM2FieldsInJson()
    {
        var db = CreateInMemoryDb();
        var (session, member, user) = BuildPaidSessionWithMember(paidBvcAmount: 50_000L);
        db.Cafes.Add(BuildCafe(session.CafeId));
        db.GameTemplates.Add(BuildGame(session.GameTemplateId));
        db.Users.Add(user);
        db.ActiveSessions.Add(session);
        db.ActiveSessionMembers.Add(member);
        await db.SaveChangesAsync();

        var sut = new ReceiptService(db, NullLogger<ReceiptService>.Instance);
        var receiptFile = await sut.GenerateMemberReceiptAsync(session.Id, member.Id, "json");

        var json = System.Text.Encoding.UTF8.GetString(receiptFile.Bytes);
        // M2 BVC fields phải có mặt trong JSON output (System.Text.Json indented dùng ": ").
        Assert.Contains("\"paidBvcAmount\": 50000", json);
        Assert.Contains("\"paidCashRemainder\": 0", json);
        Assert.Contains("\"paymentMethod\": \"Bvc\"", json);
        Assert.Contains("\"paymentStatus\": \"PaidBvc\"", json);
    }

    [Fact]
    public async Task GenerateMemberReceiptAsync_SessionNotPaid_ThrowsConflict()
    {
        var db = CreateInMemoryDb();
        var cafeId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        db.Cafes.Add(BuildCafe(cafeId));
        db.GameTemplates.Add(BuildGame(gameId));
        var session = new ActiveSession
        {
            Id = Guid.NewGuid(),
            CafeId = cafeId,
            HostId = Guid.NewGuid(),
            GameTemplateId = gameId,
            CafeTableId = Guid.NewGuid(),
            Status = GroupSessionStatus.Active,
            StartedAt = DateTime.UtcNow.AddHours(-1)
        };
        db.ActiveSessions.Add(session);
        var member = new ActiveSessionMember
        {
            Id = Guid.NewGuid(),
            ActiveSessionId = session.Id,
            UserId = Guid.NewGuid(),
            IsGuestSlot = false
        };
        db.ActiveSessionMembers.Add(member);
        await db.SaveChangesAsync();

        var sut = new ReceiptService(db, NullLogger<ReceiptService>.Instance);

        await Assert.ThrowsAsync<ConflictException>(() =>
            sut.GenerateMemberReceiptAsync(session.Id, member.Id, "json"));
    }

    [Fact]
    public async Task GenerateMemberReceiptAsync_MemberNotInSession_ThrowsNotFound()
    {
        var db = CreateInMemoryDb();
        var (session, _, user) = BuildPaidSessionWithMember();
        db.Cafes.Add(BuildCafe(session.CafeId));
        db.GameTemplates.Add(BuildGame(session.GameTemplateId));
        db.Users.Add(user);
        db.ActiveSessions.Add(session);
        await db.SaveChangesAsync();

        var sut = new ReceiptService(db, NullLogger<ReceiptService>.Instance);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            sut.GenerateMemberReceiptAsync(session.Id, Guid.NewGuid(), "json"));
    }

    [Fact]
    public async Task GenerateMemberReceiptAsync_GuestSlot_HasZeroPenaltyAndNullUserId()
    {
        var db = CreateInMemoryDb();
        var cafeId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        db.Cafes.Add(BuildCafe(cafeId));
        db.GameTemplates.Add(BuildGame(gameId));

        var session = BuildPaidSession(cafeId, gameId, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow);
        db.ActiveSessions.Add(session);
        var guestMember = new ActiveSessionMember
        {
            Id = Guid.NewGuid(),
            ActiveSessionId = session.Id,
            UserId = null,
            IsGuestSlot = true,
            GuestDisplayName = "Walk-in Carol",
            TotalMinutesPlayed = 30,
            Subtotal = 15_000m,
            DepositAppliedAmount = 0m,
            PenaltyAmount = 5_000m, // Penalty set, nhưng BR-14 sẽ zero cho Guest_Slot trên receipt
            TotalAmount = 15_000m,
            PaymentStatus = MemberPaymentStatus.PaidCash
        };
        db.ActiveSessionMembers.Add(guestMember);
        await db.SaveChangesAsync();

        var sut = new ReceiptService(db, NullLogger<ReceiptService>.Instance);
        var receiptFile = await sut.GenerateMemberReceiptAsync(session.Id, guestMember.Id, "json");

        var json = System.Text.Encoding.UTF8.GetString(receiptFile.Bytes);
        // Guest slot: penalty phải = 0 dù DB có 5_000m (BR-14).
        Assert.Contains("\"penaltyAmount\": 0", json);
        // UserId null.
        Assert.Contains("\"userId\": null", json);
        Assert.Contains("\"isGuestSlot\": true", json);
        Assert.Contains("Walk-in Carol", json);
    }

    #endregion

    #region GetRevenueReportAsync

    [Fact]
    public async Task GetRevenueReportAsync_CafeNotFound_ThrowsNotFound()
    {
        var db = CreateInMemoryDb();
        var sut = new ReceiptService(db, NullLogger<ReceiptService>.Instance);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            sut.GetRevenueReportAsync(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7)),
                DateOnly.FromDateTime(DateTime.UtcNow), "daily"));
    }

    [Fact]
    public async Task GetRevenueReportAsync_DailyGranularity_ReturnsSinglePeriodPerDay()
    {
        var db = CreateInMemoryDb();
        var cafeId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        db.Cafes.Add(BuildCafe(cafeId));
        db.GameTemplates.Add(BuildGame(gameId));

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var session = BuildPaidSession(cafeId, gameId, today.ToDateTime(new TimeOnly(10, 0)), DateTime.UtcNow);
        db.ActiveSessions.Add(session);
        await db.SaveChangesAsync();

        var sut = new ReceiptService(db, NullLogger<ReceiptService>.Instance);
        var report = await sut.GetRevenueReportAsync(cafeId, today, today, "daily");

        Assert.Equal("daily", report.Granularity);
        Assert.Single(report.Periods);
        Assert.Equal(1, report.TotalSessions);
        Assert.Equal(60_000m, report.TotalRevenue);
    }

    [Fact]
    public async Task GetRevenueReportAsync_InvalidGranularity_DefaultsToDaily()
    {
        var db = CreateInMemoryDb();
        var cafeId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        db.Cafes.Add(BuildCafe(cafeId));
        db.GameTemplates.Add(BuildGame(gameId));
        await db.SaveChangesAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var sut = new ReceiptService(db, NullLogger<ReceiptService>.Instance);
        var report = await sut.GetRevenueReportAsync(cafeId, today, today, "invalid-granularity");

        Assert.Equal("daily", report.Granularity);
    }

    [Fact]
    public async Task GetRevenueReportAsync_WeeklyGranularity_BuildsWeekBuckets()
    {
        var db = CreateInMemoryDb();
        var cafeId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        db.Cafes.Add(BuildCafe(cafeId));
        db.GameTemplates.Add(BuildGame(gameId));

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var session = BuildPaidSession(cafeId, gameId, today.ToDateTime(new TimeOnly(10, 0)), DateTime.UtcNow);
        db.ActiveSessions.Add(session);
        await db.SaveChangesAsync();

        var sut = new ReceiptService(db, NullLogger<ReceiptService>.Instance);
        var report = await sut.GetRevenueReportAsync(cafeId, today.AddDays(-6), today, "weekly");

        Assert.Equal("weekly", report.Granularity);
        Assert.NotEmpty(report.Periods);
    }

    [Fact]
    public async Task GetRevenueReportAsync_NoSessionsInRange_ReturnsEmptyPeriods()
    {
        var db = CreateInMemoryDb();
        var cafeId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        db.Cafes.Add(BuildCafe(cafeId));
        db.GameTemplates.Add(BuildGame(gameId));
        await db.SaveChangesAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var sut = new ReceiptService(db, NullLogger<ReceiptService>.Instance);
        var report = await sut.GetRevenueReportAsync(cafeId, today.AddDays(-3), today, "daily");

        Assert.Equal(0, report.TotalSessions);
        Assert.Equal(0m, report.TotalRevenue);
    }

    #endregion
}
