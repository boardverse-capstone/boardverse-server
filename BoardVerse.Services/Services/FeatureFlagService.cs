using System.Collections.Concurrent;
using System.Globalization;
using BoardVerse.Services.IServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services
{
    /// <summary>
    /// Implementation của <see cref="IFeatureFlagService"/>.
    /// Đọc từ <see cref="IConfiguration"/> (appsettings.json) + in-memory metrics store.
    ///
    /// <para>
    /// Service này là SINGLETON-friendly vì chỉ giữ immutable config + thread-safe ConcurrentDictionary.
    /// Tuy nhiên đăng ký <c>Scoped</c> để tương thích với convention của project (xem Program.cs).
    /// </para>
    /// </summary>
    public class FeatureFlagService : IFeatureFlagService
    {
        // === Configuration keys ===
        private const string KeyHostDepositDiscountEnabled = "Features:HostDepositDiscountEnabled";
        private const string KeyMemberBvcPaymentEnabled = "Features:MemberBvcPaymentEnabled";
        private const string KeyPilotCafeIds = "Pilot:CafeIds";

        // === Metrics keys (counters) ===
        private const string CounterDiscountApplied = "discount.applied.count";
        private const string CounterDiscountSkippedPrefix = "discount.skipped."; // + reason
        private const string CounterMemberBvcSuccess = "member.bvc.payment.success.count";
        private const string CounterMemberBvcFail = "member.bvc.payment.fail.count";
        private const string CounterMergeRefundSuccess = "merge.refund.success.count";
        private const string CounterMergeRefundFail = "merge.refund.fail.count";
        private const string CounterPaymentDispute = "payment.dispute.count";

        // === Metrics keys (gauges) ===
        private const string GaugeMemberBvcSuccessRate = "member.bvc.payment.success_rate";
        private const string GaugeMergeRefundSuccessRate = "merge.refund.success_rate";
        private const string GaugeDisputeRate = "payment.dispute_rate";

        private readonly IConfiguration _configuration;
        private readonly ILogger<FeatureFlagService> _logger;

        // === Metrics state (thread-safe) ===
        private readonly ConcurrentDictionary<string, long> _counters = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, double> _gauges = new(StringComparer.OrdinalIgnoreCase);

        // === Pilot cafe cache (parsed once at construction) ===
        private readonly IReadOnlyList<Guid> _pilotCafeIds;

        public FeatureFlagService(IConfiguration configuration, ILogger<FeatureFlagService> logger)
        {
            _configuration = configuration;
            _logger = logger;
            _pilotCafeIds = ParsePilotCafeIds(configuration.GetSection("Pilot").GetValue<string>("CafeIds"));
        }

        // === Feature flags (Task C5.1, C5.2) ===

        public bool IsHostDepositDiscountEnabled()
            => ReadBool(KeyHostDepositDiscountEnabled, defaultValue: false);

        public bool IsMemberBvcPaymentEnabled()
            => ReadBool(KeyMemberBvcPaymentEnabled, defaultValue: false);

        // === Pilot cafe list (Task C5.4) ===

        public bool IsPilotCafe(Guid cafeId)
            => _pilotCafeIds.Contains(cafeId);

        public IReadOnlyList<string> GetPilotCafeIds()
            => _pilotCafeIds.Select(g => g.ToString()).ToList();

        // === Metrics counters ===

        public void IncrementCounter(string name, long value = 1)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                _logger.LogWarning("IncrementCounter called with empty key.");
                return;
            }
            if (value <= 0)
            {
                _logger.LogWarning("IncrementCounter called with non-positive value {Value} for {Key}.", value, name);
                return;
            }
            _counters.AddOrUpdate(name, value, (_, current) => current + value);
        }

        public long GetCounter(string name)
            => _counters.TryGetValue(name, out var value) ? value : 0L;

        public IReadOnlyDictionary<string, long> GetAllCounters()
            => new Dictionary<string, long>(_counters, StringComparer.OrdinalIgnoreCase);

        // === Metrics gauges ===

        public void RecordGauge(string name, double value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                _logger.LogWarning("RecordGauge called with empty key.");
                return;
            }
            _gauges.AddOrUpdate(name, value, (_, _) => value);
        }

        public double GetGauge(string name)
            => _gauges.TryGetValue(name, out var value) ? value : 0d;

        public IReadOnlyDictionary<string, double> GetAllGauges()
            => new Dictionary<string, double>(_gauges, StringComparer.OrdinalIgnoreCase);

        // === Convenience hooks (Task C5.5) ===

        public void RecordDiscountApplied()
            => IncrementCounter(CounterDiscountApplied);

        public void RecordDiscountSkipped(string reason)
        {
            var safeReason = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason.Trim();
            // Sanitize để tránh metric explosion với whitespace/newlines/special chars.
            safeReason = safeReason.Replace('\n', '_').Replace('\r', '_').Replace(' ', '_');
            IncrementCounter(CounterDiscountSkippedPrefix + safeReason);
        }

        public void RecordMemberBvcPayment(bool success)
        {
            if (success)
            {
                IncrementCounter(CounterMemberBvcSuccess);
            }
            else
            {
                IncrementCounter(CounterMemberBvcFail);
            }
            UpdateRateGauge(GaugeMemberBvcSuccessRate, CounterMemberBvcSuccess, CounterMemberBvcFail);
        }

        public void RecordMergeRefund(bool success)
        {
            if (success)
            {
                IncrementCounter(CounterMergeRefundSuccess);
            }
            else
            {
                IncrementCounter(CounterMergeRefundFail);
            }
            UpdateRateGauge(GaugeMergeRefundSuccessRate, CounterMergeRefundSuccess, CounterMergeRefundFail);
        }

        public void RecordPaymentDispute()
        {
            IncrementCounter(CounterPaymentDispute);
            // gauge = disputes / (applied discounts + skipped + disputes), fallback to count nếu ratio 0/0.
            var applied = GetCounter(CounterDiscountApplied);
            var skipped = _counters
                .Where(kv => kv.Key.StartsWith(CounterDiscountSkippedPrefix, StringComparison.OrdinalIgnoreCase))
                .Sum(kv => kv.Value);
            var disputes = GetCounter(CounterPaymentDispute);
            var denom = applied + skipped + disputes;
            if (denom > 0)
            {
                RecordGauge(GaugeDisputeRate, (double)disputes / denom);
            }
            else
            {
                RecordGauge(GaugeDisputeRate, 0d);
            }
        }

        // === Private helpers ===

        private bool ReadBool(string key, bool defaultValue)
        {
            var raw = _configuration[key];
            if (string.IsNullOrWhiteSpace(raw))
            {
                return defaultValue;
            }
            var trimmed = raw.Trim();
            return trimmed.ToLowerInvariant() switch
            {
                "true" => true,
                "false" => false,
                "1" => true,
                "0" => false,
                "yes" => true,
                "no" => false,
                "on" => true,
                "off" => false,
                _ => bool.TryParse(trimmed, out var parsed) ? parsed : defaultValue
            };
        }

        private static IReadOnlyList<Guid> ParsePilotCafeIds(string? csv)
        {
            if (string.IsNullOrWhiteSpace(csv))
            {
                return Array.Empty<Guid>();
            }

            var result = new List<Guid>();
            foreach (var token in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (Guid.TryParse(token, CultureInfo.InvariantCulture, out var parsed))
                {
                    result.Add(parsed);
                }
            }
            return result;
        }

        private void UpdateRateGauge(string gaugeKey, string successKey, string failKey)
        {
            var successCount = GetCounter(successKey);
            var failCount = GetCounter(failKey);
            var total = successCount + failCount;
            if (total > 0)
            {
                RecordGauge(gaugeKey, (double)successCount / total);
            }
            else
            {
                RecordGauge(gaugeKey, 0d);
            }
        }
    }
}