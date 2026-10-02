using BoardVerse.Core.DTOs.Reports;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services
{
    /// <summary>
    /// Service tổng hợp payment breakdown per cafe per month.
    /// M2 Phase 6 / Task C5.3 — docs/design/host-deposit-discount-and-bvc-payment-design.md §C5.
    ///
    /// <para>
    /// Query trực tiếp từ 3 tables:
    /// </para>
    /// <list type="number">
    ///   <item><c>MemberPaymentAuditLogs</c> — BVC member payment + cash + QR (replicated) + refund flag.</item>
    ///   <item><c>MemberDepositAuditLogs</c> — deposit capture + merge refund.</item>
    ///   <item><c>ActiveSessions</c> — session count (Status = Paid).</item>
    /// </list>
    ///
    /// <para>
    /// V1 limitation: không filter theo cafe staff ownership. Tất cả admin xem được toàn bộ data.
    /// Filter theo cafeId (optional) — nếu truyền thì giới hạn session ở cafe đó, và aggregate deposit/payment
    /// theo active session của cafe đó (qua MemberId lookup).
    /// </para>
    /// </summary>
    public class PaymentBreakdownReportService : IPaymentBreakdownReportService
    {
        private const string PaymentMethodBvc = "Bvc";
        private const string PaymentMethodCash = "Cash";
        private const string PaymentMethodBvcPartial = "BvcPartial";

        private const string ActionCapturedOnGroupAPay = "Captured_OnGroupAPay";
        private const string ActionRefundedOnMerge = "Refunded_OnMerge";
        private const string ActionCapturedOnHostDiscount = "Captured_OnHostDiscount";

        private readonly BoardVerseDbContext _dbContext;
        private readonly ILogger<PaymentBreakdownReportService> _logger;

        public PaymentBreakdownReportService(
            BoardVerseDbContext dbContext,
            ILogger<PaymentBreakdownReportService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        /// <summary>
        /// Tạo report payment breakdown cho khoảng thời gian [fromMonth, toMonth] (UTC months).
        /// </summary>
        /// <param name="cafeId">Optional cafe filter. Null = tất cả cafe.</param>
        /// <param name="fromMonth">Tháng bắt đầu (yyyy-MM-01). Null = tháng hiện tại.</param>
        /// <param name="toMonth">Tháng kết thúc (yyyy-MM-01). Null = tháng hiện tại.</param>
        public async Task<PaymentBreakdownReportDto> GenerateReportAsync(
            Guid? cafeId,
            DateTime? fromMonth,
            DateTime? toMonth,
            CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;
            var effectiveFrom = NormalizeToMonthStart(fromMonth ?? new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc));
            var effectiveTo = NormalizeToMonthStart(toMonth ?? effectiveFrom);

            // Nếu fromMonth > toMonth thì swap để tránh query rỗng.
            if (effectiveFrom > effectiveTo)
            {
                (effectiveFrom, effectiveTo) = (effectiveTo, effectiveFrom);
            }

            var monthStarts = EnumerateMonths(effectiveFrom, effectiveTo);

            var report = new PaymentBreakdownReportDto
            {
                CafeId = cafeId,
                FromMonth = effectiveFrom,
                ToMonth = effectiveTo
            };

            // === Lookup cafe IDs that have ANY active session in the range ===
            // Nếu cafeId filter được truyền → chỉ xét cafe đó.
            var cafeIds = cafeId.HasValue
                ? new List<Guid> { cafeId.Value }
                : await _dbContext.ActiveSessions
                        .Where(s => s.Status == GroupSessionStatus.Paid && s.CreatedAt >= effectiveFrom && s.CreatedAt < NextMonth(effectiveTo))
                        .Select(s => s.CafeId)
                        .Distinct()
                        .ToListAsync(cancellationToken);

            // === Query 1: MemberPaymentAuditLogs (BVC + Cash replicated) ===
            var paymentRowsQuery = BuildPaymentAuditQuery(cafeId, effectiveFrom, effectiveTo, monthStarts);

            // === Query 2: MemberDepositAuditLogs (Discount capture + Merge refund) ===
            var depositRowsQuery = BuildDepositAuditQuery(cafeId, effectiveFrom, effectiveTo, monthStarts);

            // === Query 3: MemberPayments table (raw cash/QR từ POS staff — VND) ===
            // Theo task description: "Also query existing MemberPayments table for cash/QR amounts"
            var memberPaymentRowsQuery = BuildMemberPaymentQuery(cafeId, effectiveFrom, effectiveTo, monthStarts);

            // === Query 4: Session count (Paid sessions per cafe per month) ===
            var sessionRowsQuery = BuildSessionCountQuery(cafeId, effectiveFrom, effectiveTo, monthStarts);

            // Run all 4 queries in parallel.
            var paymentTask = paymentRowsQuery.ToListAsync(cancellationToken);
            var depositTask = depositRowsQuery.ToListAsync(cancellationToken);
            var memberPaymentTask = memberPaymentRowsQuery.ToListAsync(cancellationToken);
            var sessionTask = sessionRowsQuery.ToListAsync(cancellationToken);

            await Task.WhenAll(paymentTask, depositTask, memberPaymentTask, sessionTask);

            // === Merge rows by (CafeId, Month) ===
            // Index các kết quả theo composite key.
            var paymentIndex = paymentTask.Result.ToDictionary(r => (r.CafeId, r.Month));
            var depositIndex = depositTask.Result.ToDictionary(r => (r.CafeId, r.Month));
            var memberPaymentIndex = memberPaymentTask.Result.ToDictionary(r => (r.CafeId, r.Month));
            var sessionIndex = sessionTask.Result.ToDictionary(r => (r.CafeId, r.Month));

            foreach (var c in cafeIds)
            {
                foreach (var m in monthStarts)
                {
                    var key = (CafeId: c, Month: m);
                    paymentIndex.TryGetValue(key, out var p);
                    depositIndex.TryGetValue(key, out var d);
                    memberPaymentIndex.TryGetValue(key, out var mp);
                    sessionIndex.TryGetValue(key, out var s);

                    // Nếu không có payment/deposit/session nào → skip row rỗng.
                    if (p == null && d == null && mp == null && s == null)
                    {
                        continue;
                    }

                    var row = new PaymentBreakdownRowDto
                    {
                        CafeId = c,
                        Month = m,
                        TotalDiscountGroup = d?.DiscountGroupBvc ?? 0L,
                        TotalDiscountHostOnly = d?.HostDiscountBvc ?? 0L,
                        TotalMemberBvc = (p?.BvcFull ?? 0L) + (p?.BvcPartial ?? 0L),
                        TotalMemberBvcPartial = p?.BvcPartial ?? 0L,
                        TotalCash = mp?.Cash ?? 0m,
                        TotalQr = mp?.Qr ?? 0m,
                        TotalRefundMerge = d?.RefundMergeBvc ?? 0L,
                        TotalRefundBill = p?.RefundBvc ?? 0L,
                        SessionCount = s?.SessionCount ?? 0
                    };

                    report.Rows.Add(row);
                }
            }

            return report;
        }

        // === Internal query builders ===

        private IQueryable<PaymentAuditRow> BuildPaymentAuditQuery(
            Guid? cafeIdFilter, DateTime fromMonth, DateTime toMonth, IReadOnlyList<DateTime> monthStarts)
        {
            var rangeStart = fromMonth;
            var rangeEnd = NextMonth(toMonth);

            // MemberPaymentAuditLog có CAFE numId gián tiếp qua Member → ActiveSession → CafeId.
            // Query: aggregate AmountBvc, AmountCash theo (CafeId, Month).
            var baseQuery =
                from log in _dbContext.MemberPaymentAuditLogs
                join member in _dbContext.ActiveSessionMembers on log.MemberId equals member.Id
                join session in _dbContext.ActiveSessions on member.ActiveSessionId equals session.Id
                where log.CreatedAt >= rangeStart && log.CreatedAt < rangeEnd
                select new { log, member, session };

            if (cafeIdFilter.HasValue)
            {
                baseQuery = baseQuery.Where(x => x.session.CafeId == cafeIdFilter.Value);
            }

            return baseQuery
                .GroupBy(x => new { x.session.CafeId, Month = TruncateToMonth(x.log.CreatedAt) })
                .Select(g => new PaymentAuditRow
                {
                    CafeId = g.Key.CafeId,
                    Month = g.Key.Month,
                    // BvcFull: PaymentMethod = 'Bvc', sum AmountBvc
                    BvcFull = g.Sum(x => x.log.PaymentMethod == PaymentMethodBvc ? (long?)x.log.AmountBvc : 0L) ?? 0L,
                    // BvcPartial: PaymentMethod = 'BvcPartial', sum AmountBvc
                    BvcPartial = g.Sum(x => x.log.PaymentMethod == PaymentMethodBvcPartial ? (long?)x.log.AmountBvc : 0L) ?? 0L,
                    // Cash: replicated row (PaymentMethod = 'Cash'), sum AmountCash
                    Cash = g.Sum(x => x.log.PaymentMethod == PaymentMethodCash ? (decimal?)x.log.AmountCash : 0m) ?? 0m,
                    // Qr: chưa có PaymentMethod = 'Qr' trong MemberPaymentAuditLog — fallback 0 (cash/QR tách ở MemberPayments query riêng)
                    // RefundBvc: log.RefundedAt IS NOT NULL, sum AmountBvc
                    RefundBvc = g.Sum(x => x.log.RefundedAt != null ? (long?)x.log.AmountBvc : 0L) ?? 0L
                });
        }

        private IQueryable<DepositAuditRow> BuildDepositAuditQuery(
            Guid? cafeIdFilter, DateTime fromMonth, DateTime toMonth, IReadOnlyList<DateTime> monthStarts)
        {
            var rangeStart = fromMonth;
            var rangeEnd = NextMonth(toMonth);

            // MemberDepositAuditLog → Member → ActiveSession → CafeId
            var baseQuery =
                from log in _dbContext.MemberDepositAuditLogs
                join member in _dbContext.ActiveSessionMembers on log.MemberId equals member.Id
                join session in _dbContext.ActiveSessions on member.ActiveSessionId equals session.Id
                where log.CreatedAt >= rangeStart && log.CreatedAt < rangeEnd
                select new { log, member, session };

            if (cafeIdFilter.HasValue)
            {
                baseQuery = baseQuery.Where(x => x.session.CafeId == cafeIdFilter.Value);
            }

            return baseQuery
                .GroupBy(x => new { x.session.CafeId, Month = TruncateToMonth(x.log.CreatedAt) })
                .Select(g => new DepositAuditRow
                {
                    CafeId = g.Key.CafeId,
                    Month = g.Key.Month,
                    // DiscountGroupBvc: action = 'Captured_OnGroupAPay', sum AmountBvc
                    DiscountGroupBvc = g.Sum(x => x.log.Action == ActionCapturedOnGroupAPay ? (long?)x.log.AmountBvc : 0L) ?? 0L,
                    // HostDiscountBvc: action = 'Captured_OnHostDiscount' (M1 §B-only docs use 'Captured_OnGroupAPay'; included here cho future-proof)
                    HostDiscountBvc = g.Sum(x => x.log.Action == ActionCapturedOnHostDiscount ? (long?)x.log.AmountBvc : 0L) ?? 0L,
                    // RefundMergeBvc: action = 'Refunded_OnMerge', sum AmountBvc
                    RefundMergeBvc = g.Sum(x => x.log.Action == ActionRefundedOnMerge ? (long?)x.log.AmountBvc : 0L) ?? 0L
                });
        }

        private IQueryable<MemberPaymentRow> BuildMemberPaymentQuery(
            Guid? cafeIdFilter, DateTime fromMonth, DateTime toMonth, IReadOnlyList<DateTime> monthStarts)
        {
            var rangeStart = fromMonth;
            var rangeEnd = NextMonth(toMonth);

            // MemberPayments → Member → ActiveSession → CafeId
            // Đây là table từ pre-M2 (cash/QR POS staff audit) — dùng để consolidate Cash + QR amounts.
            var baseQuery =
                from p in _dbContext.MemberPayments
                join member in _dbContext.ActiveSessionMembers on p.MemberId equals member.Id
                join session in _dbContext.ActiveSessions on member.ActiveSessionId equals session.Id
                where p.CreatedAt >= rangeStart && p.CreatedAt < rangeEnd
                select new { p, member, session };

            if (cafeIdFilter.HasValue)
            {
                baseQuery = baseQuery.Where(x => x.session.CafeId == cafeIdFilter.Value);
            }

            return baseQuery
                .GroupBy(x => new { x.session.CafeId, Month = TruncateToMonth(x.p.CreatedAt) })
                .Select(g => new MemberPaymentRow
                {
                    CafeId = g.Key.CafeId,
                    Month = g.Key.Month,
                    Cash = g.Sum(x => x.p.PaymentMethod == "CASH" ? (decimal?)x.p.Amount : 0m) ?? 0m,
                    Qr = g.Sum(x => x.p.PaymentMethod == "QR_CODE" ? (decimal?)x.p.Amount : 0m) ?? 0m
                });
        }

        private IQueryable<SessionCountRow> BuildSessionCountQuery(
            Guid? cafeIdFilter, DateTime fromMonth, DateTime toMonth, IReadOnlyList<DateTime> monthStarts)
        {
            var rangeStart = fromMonth;
            var rangeEnd = NextMonth(toMonth);

            // ActiveSession.PaidAt (nếu có) hoặc CretaedAt — ưu tiên PaidAt.
            // Per task: "group by DATE_TRUNC('month', CreatedAt)" → dùng CreatedAt ở đây cho simplicity.
            var baseQuery = _dbContext.ActiveSessions
                .Where(s => s.Status == GroupSessionStatus.Paid && s.CreatedAt >= rangeStart && s.CreatedAt < rangeEnd);

            if (cafeIdFilter.HasValue)
            {
                baseQuery = baseQuery.Where(s => s.CafeId == cafeIdFilter.Value);
            }

            return baseQuery
                .GroupBy(s => new { s.CafeId, Month = TruncateToMonth(s.CreatedAt) })
                .Select(g => new SessionCountRow
                {
                    CafeId = g.Key.CafeId,
                    Month = g.Key.Month,
                    SessionCount = g.Count()
                });
        }

        // === Helpers ===

        private static DateTime NormalizeToMonthStart(DateTime input)
        {
            // Set day=1, time=00:00:00, kind=Utc.
            return new DateTime(input.Year, input.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        }

        private static DateTime NextMonth(DateTime monthStart)
        {
            return monthStart.AddMonths(1);
        }

        private static IReadOnlyList<DateTime> EnumerateMonths(DateTime fromMonth, DateTime toMonth)
        {
            var months = new List<DateTime>();
            var cursor = fromMonth;
            while (cursor <= toMonth)
            {
                months.Add(cursor);
                cursor = cursor.AddMonths(1);
            }
            return months;
        }

        /// <summary>
        /// Truncate DateTime về đầu tháng UTC. EF Core không có hàm DATE_TRUNC mặc định nên dùng expression thuần C#.
        /// </summary>
        private static DateTime TruncateToMonth(DateTime dt)
        {
            var utc = dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
            return new DateTime(utc.Year, utc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        }

        // === Result DTOs (private) ===

        private sealed class PaymentAuditRow
        {
            public Guid CafeId { get; set; }
            public DateTime Month { get; set; }
            public long BvcFull { get; set; }
            public long BvcPartial { get; set; }
            public decimal Cash { get; set; }
            public long RefundBvc { get; set; }
        }

        private sealed class DepositAuditRow
        {
            public Guid CafeId { get; set; }
            public DateTime Month { get; set; }
            public long DiscountGroupBvc { get; set; }
            public long HostDiscountBvc { get; set; }
            public long RefundMergeBvc { get; set; }
        }

        private sealed class MemberPaymentRow
        {
            public Guid CafeId { get; set; }
            public DateTime Month { get; set; }
            public decimal Cash { get; set; }
            public decimal Qr { get; set; }
        }

        private sealed class SessionCountRow
        {
            public Guid CafeId { get; set; }
            public DateTime Month { get; set; }
            public int SessionCount { get; set; }
        }
    }
}