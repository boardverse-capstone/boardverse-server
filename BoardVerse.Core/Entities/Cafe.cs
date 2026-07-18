using BoardVerse.Core.Enum;
using NetTopologySuite.Geometries;

namespace BoardVerse.Core.Entities
{
    /// <summary>
    /// Quán cafe đối tác (Cafe Partner).
    /// Theo boardverse-business-context.mdc - Section I.
    /// BR-01: Mô hình tính tiền (Thời gian thực hoặc Vào cổng trọn gói)
    /// BR-03: Trần cọc = 50% giá base
    /// BR-05: Tính AvailableSeats = TotalSeats - Reserved - InUse
    /// </summary>
    public class Cafe
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        // === Basic Info ===
        public required string Name { get; set; }
        public required string Address { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public Point? Location { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Description { get; set; }
        public Guid ManagerId { get; set; }

        // === BR-05: Seat Management ===
        /// <summary>Tổng số ghế ngồi trong quán. Dùng để tính AvailableSeats.</summary>
        public int TotalSeats { get; set; }

        // === Operational Status ===
        public CafePartnerOperationalStatus? PartnerOperationalStatus { get; set; }
        public string? PartnerOperationalStatusReason { get; set; }
        public DateTime? PartnerOperationalStatusChangedAt { get; set; }
        public TimeSpan? WeekdayOpen { get; set; }
        public TimeSpan? WeekdayClose { get; set; }
        public TimeSpan? WeekendOpen { get; set; }
        public TimeSpan? WeekendClose { get; set; }

        // === Phase 2+ Operational Profile ===
        public int NumberOfTables { get; set; }
        public int NumberOfPrivateRooms { get; set; }
        public string SpaceImageUrlsJson { get; set; } = "[]";
        public int NumberOfGamesOwned { get; set; }
        public string PopularGamesList { get; set; } = string.Empty;
        public bool HasGameMaster { get; set; }

        // === BR-01/BR-16: Billing Model ===
        /// <summary>Mô hình tính tiền: TimeBased hoặc FlatEntry.</summary>
        public CafePartnerBillingModel BillingModel { get; set; } = CafePartnerBillingModel.TimeBased;

        /// <summary>Giá giờ đầu hoặc giá vé vào cổng tùy theo mô hình.</summary>
        public decimal BasePrice { get; set; }

        /// <summary>Giá mỗi block lũy tiến theo phút (TimeBased). Nullable vì FlatEntry không dùng.</summary>
        public decimal? TieredBlockRate { get; set; }

        /// <summary>Thời gian mỗi block tính tiền (phút). Mặc định 15 phút.</summary>
        public int TieredBlockMinutes { get; set; } = 15;

        /// <summary>JSON array of table names configured on Web POS.</summary>
        public string TableLayoutJson { get; set; } = "[]";
        public DateTime? OperationalProfileUpdatedAt { get; set; }

        // === BR-04: Pricing Lock ===
        /// <summary>True khi quán đang hoạt động; chặn chỉnh sửa biểu phí.</summary>
        public bool IsPricingLocked { get; set; }

        // === BR-02/BR-03: Deposit Configuration ===
        /// <summary>% tiền cọc so với giá base. Tối đa 50%. (BR-03)</summary>
        public decimal DepositPercentage { get; set; } = 0.5m;

        /// <summary>Phút giữ chỗ mặc định khi đặt online. Tối đa 30. (BR-06)</summary>
        public int DefaultHoldDurationMinutes { get; set; } = 30;

        // === SePay Configuration (Session Payment) ===
        /// <summary>SePay Merchant ID của quán cafe (dùng cho session payment).</summary>
        public string? SePayMerchantId { get; set; }

        /// <summary>SePay API Key của quán cafe (dùng cho session payment).</summary>
        public string? SePayApiKey { get; set; }

        /// <summary>SePay Secret Key của quán cafe (dùng cho session payment).</summary>
        public string? SePaySecretKey { get; set; }

        /// <summary>Redirect URL sau khi thanh toán session (SePay sẽ redirect user về).</summary>
        public string? SePayReturnUrl { get; set; }

        /// <summary>Mã ngân hàng cho VietQR fallback (ví dụ: MBBank, Vietinbank).</summary>
        public string? SePayBankCode { get; set; }

        /// <summary>Số tài khoản ngân hàng cho VietQR fallback.</summary>
        public string? SePayAccountNumber { get; set; }

        /// <summary>
        /// FK đến SePayAccount. Ưu tiên dùng SePayAccount, fallback về local fields.
        /// </summary>
        public Guid? SePayAccountId { get; set; }

        // === Audit ===
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        public bool IsActive { get; set; } = true;

        // === Navigation ===
        public virtual User Manager { get; set; } = null!;
        public virtual CafePartnerApplication? PartnerApplication { get; set; }
        public virtual SePayAccount? SePayAccount { get; set; }
        public virtual ICollection<CafeStaff> StaffMembers { get; set; } = new List<CafeStaff>();
        public virtual ICollection<CafeTable> Tables { get; set; } = new List<CafeTable>();
        public virtual ICollection<CafeGameInventory> Inventories { get; set; } = new List<CafeGameInventory>();
        public virtual ICollection<ComponentLossReport> ComponentLossReports { get; set; } = new List<ComponentLossReport>();
    }
}
