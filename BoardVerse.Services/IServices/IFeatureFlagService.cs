using System.Threading;

namespace BoardVerse.Services.IServices
{
    /// <summary>
    /// Feature flag service cho Host Deposit Discount + Member BVC Payment rollout (M2 Phase 6 / Task C5.1-C5.5).
    /// docs/design/host-deposit-discount-and-bvc-payment-design.md §C5.
    ///
    /// <para>
    /// Tất cả flag đọc từ <see cref="Microsoft.Extensions.Configuration.IConfiguration"/> (appsettings.json).
    /// Pilot cafe list đọc từ CSV ở key <c>Pilot:CafeIds</c> (vd: <c>"guid1,guid2"</c>).
    /// Mặc định TẤT CẢ flag = false / empty — production-safe.
    /// </para>
    /// <para>
    /// Metrics counters/gauges dùng cho dashboard in-memory (Phase 6 simple version).
    /// Future: tích hợp Prometheus / Application Insights.
    /// </para>
    /// </summary>
    public interface IFeatureFlagService
    {
        /// <summary>Task C5.1: Bật/tắt Host Deposit Discount flow (POS dropdown). Default false.</summary>
        bool IsHostDepositDiscountEnabled();

        /// <summary>Task C5.2: Bật/tắt Member BVC Payment flow (Case 2 mobile). Default false.</summary>
        bool IsMemberBvcPaymentEnabled();

        /// <summary>Task C5.4: Kiểm tra cafe có trong pilot A/B test list hay không.</summary>
        bool IsPilotCafe(Guid cafeId);

        /// <summary>Task C5.4: Trả về list GUID cafe đang trong pilot.</summary>
        IReadOnlyList<string> GetPilotCafeIds();

        // === Metrics hooks (Task C5.5) ===
        /// <summary>Counter tăng dần (atomic). Tạo key nếu chưa tồn tại.</summary>
        void IncrementCounter(string name, long value = 1);

        /// <summary>Đọc giá trị counter (0 nếu chưa có).</summary>
        long GetCounter(string name);

        /// <summary>Snapshot tất cả counters hiện tại (read-only).</summary>
        IReadOnlyDictionary<string, long> GetAllCounters();

        /// <summary>Gauge chỉ lưu giá trị mới nhất (atomic). Tạo key nếu chưa tồn tại.</summary>
        void RecordGauge(string name, double value);

        /// <summary>Đọc giá trị gauge hiện tại (0 nếu chưa có).</summary>
        double GetGauge(string name);

        /// <summary>Snapshot tất cả gauges hiện tại (read-only).</summary>
        IReadOnlyDictionary<string, double> GetAllGauges();

        /// <summary>Convenience: ghi nhận 1 discount applied (success). Counter <c>discount.applied.count</c>.</summary>
        void RecordDiscountApplied();

        /// <summary>Convenience: ghi nhận 1 discount bị skip với lý do. Counter <c>discount.skipped.{reason}</c>.</summary>
        void RecordDiscountSkipped(string reason);

        /// <summary>Convenience: ghi nhận 1 member BVC payment (success/fail). Counter + gauge.</summary>
        void RecordMemberBvcPayment(bool success);

        /// <summary>Convenience: ghi nhận 1 merge refund (success/fail). Counter + gauge.</summary>
        void RecordMergeRefund(bool success);

        /// <summary>Convenience: ghi nhận 1 payment dispute event. Counter <c>payment.dispute.count</c>.</summary>
        void RecordPaymentDispute();
    }
}