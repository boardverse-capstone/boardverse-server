using BoardVerse.Core.DTOs.Receipt;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services
{
    public class ReceiptService : IReceiptService
    {
        private readonly BoardVerseDbContext _dbContext;
        private readonly ILogger<ReceiptService> _logger;

        public ReceiptService(BoardVerseDbContext dbContext, ILogger<ReceiptService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task<SessionReceiptDto> GenerateSessionReceiptAsync(Guid sessionId, CancellationToken cancellationToken = default)
        {
            var session = await _dbContext.ActiveSessions
                .Include(s => s.Cafe)
                .Include(s => s.GameTemplate)
                .Include(s => s.CafeTable)
                .Include(s => s.Members)
                    .ThenInclude(m => m.User)
                .FirstOrDefaultAsync(s => s.Id == sessionId);

            if (session == null)
            {
                throw new NotFoundException(ApiErrorMessages.Pos.SessionNotFoundById(sessionId));
            }

            if (session.Status != GroupSessionStatus.Paid)
            {
                throw new ConflictException(ApiErrorMessages.Receipt.OnlyForPaidSession(session.Status.ToString()));
            }

            var memberItems = session.Members.Select(m => new MemberReceiptItemDto
            {
                MemberId = m.Id,
                UserId = m.UserId,
                DisplayName = m.IsGuestSlot
                    ? m.GuestDisplayName ?? "Khách vô danh"
                    : m.User?.Username ?? $"User_{m.UserId?.ToString()[..8] ?? "unknown"}",
                IsGuestSlot = m.IsGuestSlot,
                DurationMinutes = m.TotalMinutesPlayed,
                Subtotal = CalculateMemberSubtotal(session, m),
                DepositApplied = m.DepositAppliedAmount,
                Penalty = m.PenaltyAmount,
                Total = CalculateMemberTotal(session, m)
            }).ToList();

            var receipt = new SessionReceiptDto
            {
                SessionId = session.Id,
                CafeName = session.Cafe?.Name ?? "Unknown Cafe",
                CafeAddress = session.Cafe?.Address ?? "Unknown Address",
                SessionStart = session.StartedAt,
                SessionEnd = session.EndedAt ?? DateTime.UtcNow,
                DurationMinutes = session.TotalMinutesPlayed,
                GameName = session.GameTemplate?.Name ?? "Unknown Game",
                TableName = session.CafeTable?.Name,
                Members = memberItems,
                TotalSubtotal = session.Subtotal,
                TotalDepositApplied = session.DepositAppliedAmount,
                TotalPenalty = session.PenaltyAmount,
                GrandTotal = session.TotalAmount,
                PaidAt = session.PaidAt ?? DateTime.UtcNow
            };

            _logger.LogInformation(
                "Generated receipt for session {SessionId}, Cafe {CafeName}, Members {MemberCount}",
                sessionId, receipt.CafeName, memberItems.Count);

            return receipt;
        }

        public async Task<RevenueReportDto> GetRevenueReportAsync(
            Guid cafeId,
            DateOnly startDate,
            DateOnly endDate,
            string granularity, CancellationToken cancellationToken = default)
        {
            var cafe = await _dbContext.Cafes.FirstOrDefaultAsync(c => c.Id == cafeId);
            if (cafe == null)
            {
                throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));
            }

            var validGranularity = granularity?.ToLowerInvariant() switch
            {
                "weekly" => "weekly",
                "monthly" => "monthly",
                _ => "daily"
            };

            var paidSessions = await _dbContext.ActiveSessions
                .Include(s => s.GameTemplate)
                .Include(s => s.Members)
                .Where(s => s.CafeId == cafeId
                    && s.Status == GroupSessionStatus.Paid
                    && s.PaidAt != null
                    && s.PaidAt.Value.Date >= startDate.ToDateTime(TimeOnly.MinValue).Date
                    && s.PaidAt.Value.Date <= endDate.ToDateTime(TimeOnly.MaxValue).Date)
                .ToListAsync();

            var periods = BuildPeriods(startDate, endDate, validGranularity, paidSessions);

            var report = new RevenueReportDto
            {
                CafeId = cafeId,
                CafeName = cafe.Name,
                StartDate = startDate,
                EndDate = endDate,
                Granularity = validGranularity,
                TotalRevenue = paidSessions.Sum(s => s.TotalAmount),
                TotalDepositsApplied = paidSessions.Sum(s => s.DepositAppliedAmount),
                TotalPenalties = paidSessions.Sum(s => s.PenaltyAmount),
                TotalSessions = paidSessions.Count,
                TotalMembers = paidSessions.Sum(s => s.Members.Count),
                Periods = periods
            };

            _logger.LogInformation(
                "Generated revenue report for Cafe {CafeId}, Period {StartDate} to {EndDate}, Sessions {SessionCount}",
                cafeId, startDate, endDate, paidSessions.Count);

            return report;
        }

        private List<RevenuePeriodDto> BuildPeriods(
            DateOnly startDate,
            DateOnly endDate,
            string granularity,
            List<ActiveSession> sessions)
        {
            var periods = new List<RevenuePeriodDto>();

            if (granularity == "daily")
            {
                for (var date = startDate; date <= endDate; date = date.AddDays(1))
                {
                    var daySessions = sessions
                        .Where(s => s.PaidAt?.Date == date.ToDateTime(TimeOnly.MinValue).Date)
                        .ToList();

                    periods.Add(BuildPeriod(
                        date.ToString("yyyy-MM-dd"),
                        date,
                        date,
                        daySessions));
                }
            }
            else if (granularity == "weekly")
            {
                var weekStart = startDate;
                while (weekStart <= endDate)
                {
                    var weekEnd = weekStart.AddDays(6);
                    if (weekEnd > endDate) weekEnd = endDate;

                    var weekSessions = sessions
                        .Where(s => s.PaidAt?.Date >= weekStart.ToDateTime(TimeOnly.MinValue).Date
                            && s.PaidAt?.Date <= weekEnd.ToDateTime(TimeOnly.MinValue).Date)
                        .ToList();

                    periods.Add(BuildPeriod(
                        $"W{GetIso8601WeekOfYear(weekStart)}",
                        weekStart,
                        weekEnd,
                        weekSessions));

                    weekStart = weekStart.AddDays(7);
                }
            }
            else // monthly
            {
                var monthStart = new DateOnly(startDate.Year, startDate.Month, 1);
                while (monthStart <= endDate)
                {
                    var monthEnd = monthStart.AddMonths(1).AddDays(-1);
                    if (monthEnd > endDate) monthEnd = endDate;

                    var monthSessions = sessions
                        .Where(s => s.PaidAt?.Date >= monthStart.ToDateTime(TimeOnly.MinValue).Date
                            && s.PaidAt?.Date <= monthEnd.ToDateTime(TimeOnly.MinValue).Date)
                        .ToList();

                    periods.Add(BuildPeriod(
                        monthStart.ToString("yyyy-MM"),
                        monthStart,
                        monthEnd,
                        monthSessions));

                    monthStart = monthStart.AddMonths(1);
                }
            }

            return periods;
        }

        private RevenuePeriodDto BuildPeriod(
            string periodKey,
            DateOnly periodStart,
            DateOnly periodEnd,
            List<ActiveSession> sessions)
        {
            var gameBreakdown = sessions
                .GroupBy(s => s.GameTemplateId)
                .Select(g => new RevenueGameBreakdownDto
                {
                    GameTemplateId = g.Key,
                    GameName = g.FirstOrDefault()?.GameTemplate?.Name ?? "Unknown",
                    SessionCount = g.Count(),
                    Revenue = g.Sum(s => s.TotalAmount)
                })
                .ToList();

            return new RevenuePeriodDto
            {
                PeriodKey = periodKey,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                Revenue = sessions.Sum(s => s.TotalAmount),
                DepositsApplied = sessions.Sum(s => s.DepositAppliedAmount),
                Penalties = sessions.Sum(s => s.PenaltyAmount),
                SessionCount = sessions.Count,
                MemberCount = sessions.Sum(s => s.Members.Count),
                ByGame = gameBreakdown
            };
        }

        private static decimal CalculateMemberSubtotal(ActiveSession session, ActiveSessionMember member)
        {
            if (session.GameTemplate == null)
                return 0;

            var cafe = session.Cafe;
            if (cafe == null)
                return 0;

            var minutes = member.TotalMinutesPlayed > 0
                ? member.TotalMinutesPlayed
                : session.TotalMinutesPlayed;

            if (minutes <= 0)
                return 0;

            if (cafe.BillingModel == CafePartnerBillingModel.FlatEntry)
            {
                return cafe.BasePrice;
            }

            // TimeBased: base price + tiered blocks
            if (minutes <= 60)
            {
                return cafe.BasePrice;
            }

            // DEFENSIVE: TimeBased phải có TieredBlockRate
            // Nếu null → fallback an toàn: tính như FlatEntry (chỉ giờ đầu)
            if (!cafe.TieredBlockRate.HasValue || cafe.TieredBlockRate <= 0)
            {
                if (cafe.BillingModel == CafePartnerBillingModel.TimeBased)
                {
                    return cafe.BasePrice;
                }
                return cafe.BasePrice;
            }

            var remainingMinutes = minutes - 60;
            var blockMinutes = cafe.TieredBlockMinutes > 0 ? cafe.TieredBlockMinutes : 15;
            var blockPrice = cafe.TieredBlockRate.Value;

            var additionalBlocks = (int)Math.Ceiling((double)remainingMinutes / blockMinutes);
            return cafe.BasePrice + (additionalBlocks * blockPrice);
        }

        private static decimal CalculateMemberTotal(ActiveSession session, ActiveSessionMember member)
        {
            var subtotal = CalculateMemberSubtotal(session, member);
            var penalty = member.PenaltyAmount;
            var deposit = member.DepositAppliedAmount;

            // BR-15: Total = Subtotal + Penalty - DepositApplied
            // BR-09: Deposit là phí giữ chỗ, KHÔNG trừ vào hóa đơn
            // Nhưng schema có DepositAppliedAmount nên vẫn tính theo formula
            return Math.Max(0, subtotal + penalty - deposit);
        }

        private static int GetIso8601WeekOfYear(DateOnly date)
        {
            var dt = date.ToDateTime(TimeOnly.MinValue);
            var day = System.Globalization.CultureInfo.InvariantCulture.Calendar.GetDayOfWeek(dt);
            if (day < DayOfWeek.Monday)
            {
                dt = dt.AddDays(-7);
            }
            var week = System.Globalization.CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(
                dt,
                System.Globalization.CalendarWeekRule.FirstFourDayWeek,
                DayOfWeek.Monday);
            return week;
        }
    }
}
