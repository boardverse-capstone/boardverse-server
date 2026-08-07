using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.Admin
{
    // === Tournament DTOs ===

    public class AdminTournamentListItemDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public Guid CafeId { get; set; }
        public string CafeName { get; set; } = string.Empty;
        public string GameName { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime RegistrationDeadline { get; set; }
        public int MinParticipants { get; set; }
        public int MaxParticipants { get; set; }
        public int CurrentParticipants { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class AdminTournamentDetailDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public Guid CafeId { get; set; }
        public string CafeName { get; set; } = string.Empty;
        public Guid GameTemplateId { get; set; }
        public string GameName { get; set; } = string.Empty;
        public Guid CreatedByManagerId { get; set; }
        public int RegisteredCount { get; set; }
        public int CheckedInCount { get; set; }
        public bool HasThirdPlaceMatch { get; set; }
        public int WinnerKarmaBonus { get; set; }
        public int FinalistKarmaBonus { get; set; }
        public int NoShowKarmaPenalty { get; set; }
        public IReadOnlyList<AdminTournamentParticipantDto> Participants { get; set; } = [];
        public DateTime StartTime { get; set; }
        public DateTime RegistrationDeadline { get; set; }
        public int RoundDurationMinutes { get; set; }
        public int MinParticipants { get; set; }
        public int MaxParticipants { get; set; }
        public int CurrentParticipants { get; set; }
        public decimal EntryFee { get; set; }
        public int TotalRounds { get; set; }
        public int PreliminaryRounds { get; set; }
        public int FinalistCount { get; set; }
        public int CurrentRound { get; set; }
        public DateTime? StartedAt { get; set; }
        public int MinKarmaRequirement { get; set; }
        public int MinEloRequirement { get; set; }
        public int MaxEloRequirement { get; set; }
        public string PairingMode { get; set; } = string.Empty;
        public bool AutoExtendOnShortage { get; set; }
        public int MaxExtensionCount { get; set; }
        public int ExtensionCount { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? CancellationReason { get; set; }
        public DateTime? CancelledAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        // Cafe detail fields
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string? PhoneNumber { get; set; }
        public string? ManagerName { get; set; }
        public string? ManagerEmail { get; set; }
        public string? PartnerOperationalStatus { get; set; }
        public string? PartnerOperationalStatusReason { get; set; }
        public DateTime? PartnerOperationalStatusChangedAt { get; set; }
        public TimeSpan? WeekdayOpen { get; set; }
        public TimeSpan? WeekdayClose { get; set; }
        public TimeSpan? WeekendOpen { get; set; }
        public TimeSpan? WeekendClose { get; set; }
        public int NumberOfTables { get; set; }
        public int NumberOfPrivateRooms { get; set; }
        public int NumberOfGamesOwned { get; set; }
        public string PopularGamesList { get; set; } = string.Empty;
        public bool HasGameMaster { get; set; }
        public bool IsPricingLocked { get; set; }
        public string? SePayMerchantId { get; set; }
        public string? SePayApiKey { get; set; }
        public string? SePaySecretKey { get; set; }
        public string? SePayReturnUrl { get; set; }
        public int TieredBlockMinutes { get; set; }
        public int DefaultHoldDurationMinutes { get; set; }
        public string RefundPolicy { get; set; } = string.Empty;
        public IReadOnlyList<AdminCafeStaffDto> StaffMembers { get; set; } = [];
        public int StaffCount { get; set; }
        public int InventoryCount { get; set; }
    }

    public class AdminCafeStaffDto
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string? Username { get; set; }
        public string? DisplayName { get; set; }
        public string? Email { get; set; }
        public DateTime AssignedAt { get; set; }
        public bool IsActive { get; set; }
    }

    public class AdminTournamentParticipantDto
    {
        public Guid Id { get; set; }
        public Guid TournamentId { get; set; }
        public Guid? UserId { get; set; }
        public string? Username { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string Status { get; set; } = string.Empty;
        public int? Rank { get; set; }
        public int? EloChange { get; set; }
        public int CurrentElo { get; set; }
        public bool IsWalkIn { get; set; }
        public DateTime? CheckedInAt { get; set; }
        public DateTime? WithdrawnAt { get; set; }
        public DateTime RegisteredAt { get; set; }
        public int? FinalRank { get; set; }
        public int? SwissWins { get; set; }
        public int? SwissDraws { get; set; }
        public int? TotalPrestigePoints { get; set; }
    }

    public class AdminCreateTournamentRequestDto
    {
        [Required(ErrorMessage = ApiErrorMessages.Validation.NameRequired)]
        [StringLength(200, MinimumLength = 5, ErrorMessage = "Tên giải đấu phải từ 5 đến 200 ký tự.")]
        public string Title { get; set; } = string.Empty;

        [StringLength(2000, ErrorMessage = ApiErrorMessages.Validation.DescriptionMax2000)]
        public string? Description { get; set; }

        [Required(ErrorMessage = "CafeId là bắt buộc.")]
        public Guid CafeId { get; set; }

        [Required(ErrorMessage = "GameTemplateId là bắt buộc.")]
        public Guid GameTemplateId { get; set; }

        [Required(ErrorMessage = "Thời gian bắt đầu là bắt buộc.")]
        public DateTime StartTime { get; set; }

        [Required(ErrorMessage = "Hạn đăng ký là bắt buộc.")]
        public DateTime RegistrationDeadline { get; set; }

        [Range(0, 10000000, ErrorMessage = "Entry fee phải từ 0 đến 10.000.000 VND.")]
        public decimal EntryFee { get; set; } = 0;

        [Range(1, 480, ErrorMessage = "Thời lượng vòng đấu phải từ 1 đến 480 phút.")]
        public int RoundDurationMinutes { get; set; } = 45;

        [Range(4, 32, ErrorMessage = "Số người tối thiểu phải từ 4 đến 32 và là bội số của 4.")]
        public int MinParticipants { get; set; } = 4;

        [Range(4, 32, ErrorMessage = "Số người tối đa phải từ 4 đến 32 và là bội số của 4.")]
        public int MaxParticipants { get; set; } = 32;

        [Range(0, 4, ErrorMessage = "Tổng số vòng phải từ 1 đến 4.")]
        public int TotalRounds { get; set; } = 4;

        [Range(1, 3, ErrorMessage = "Số vòng Swiss phải từ 1 đến 3.")]
        public int PreliminaryRounds { get; set; } = 3;

        [Range(2, 8, ErrorMessage = "Số người vào Final phải từ 2 đến 8.")]
        public int FinalistCount { get; set; } = 4;

        [Range(0, 100, ErrorMessage = "Yêu cầu Karma phải từ 0 đến 100.")]
        public int MinKarmaRequirement { get; set; } = 0;

        [Range(0, 3000, ErrorMessage = "Elo tối thiểu phải từ 0 đến 3000.")]
        public int MinEloRequirement { get; set; } = 0;

        [Range(0, 3000, ErrorMessage = "Elo tối đa phải từ 0 đến 3000.")]
        public int MaxEloRequirement { get; set; } = 3000;

        public TournamentPairingMode PairingMode { get; set; } = TournamentPairingMode.Auto;
        public bool AutoExtendOnShortage { get; set; } = false;
        public int MaxExtensionCount { get; set; } = 2;
        public int ExtensionMinutesPerAttempt { get; set; } = 30;
    }

    public class AdminUpdateTournamentRequestDto
    {
        [StringLength(200, MinimumLength = 5, ErrorMessage = "Tên giải đấu phải từ 5 đến 200 ký tự.")]
        public string? Title { get; set; }

        [StringLength(2000, ErrorMessage = ApiErrorMessages.Validation.DescriptionMax2000)]
        public string? Description { get; set; }

        public DateTime? StartTime { get; set; }
        public DateTime? RegistrationDeadline { get; set; }

        [Range(0, 10000000, ErrorMessage = "Entry fee phải từ 0 đến 10.000.000 VND.")]
        public decimal? EntryFee { get; set; }

        [Range(1, 480, ErrorMessage = "Thời lượng vòng đấu phải từ 1 đến 480 phút.")]
        public int? RoundDurationMinutes { get; set; }

        [Range(4, 32, ErrorMessage = "Số người tối thiểu phải từ 4 đến 32.")]
        public int? MinParticipants { get; set; }

        [Range(4, 32, ErrorMessage = "Số người tối đa phải từ 4 đến 32.")]
        public int? MaxParticipants { get; set; }

        [Range(0, 100, ErrorMessage = "Yêu cầu Karma phải từ 0 đến 100.")]
        public int? MinKarmaRequirement { get; set; }

        [Range(0, 3000, ErrorMessage = "Elo tối thiểu phải từ 0 đến 3000.")]
        public int? MinEloRequirement { get; set; }

        [Range(0, 3000, ErrorMessage = "Elo tối đa phải từ 0 đến 3000.")]
        public int? MaxEloRequirement { get; set; }

        public TournamentPairingMode? PairingMode { get; set; }
        public bool? AutoExtendOnShortage { get; set; }
        public int? MaxExtensionCount { get; set; }
        public int? ExtensionMinutesPerAttempt { get; set; }
    }

    public class AdminTournamentListResponseDto
    {
        public IReadOnlyList<AdminTournamentListItemDto> Items { get; set; } = [];
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages { get; set; }
        public bool HasPreviousPage { get; set; }
        public bool HasNextPage { get; set; }
    }

    public class AdminTournamentParticipantsResponseDto
    {
        public Guid TournamentId { get; set; }
        public string TournamentTitle { get; set; } = string.Empty;
        public IReadOnlyList<AdminTournamentParticipantDto> Participants { get; set; } = [];
        public int TotalCount { get; set; }
    }

    // === Report DTOs ===

    public class AdminDashboardOverviewDto
    {
        public int TotalUsers { get; set; }
        public int TotalCafes { get; set; }
        public int ActiveCafes { get; set; }
        public int TotalTournaments { get; set; }
        public int DraftTournaments { get; set; }
        public int RegistrationOpenTournaments { get; set; }
        public int ActiveTournaments { get; set; }
        public int TotalLobbies { get; set; }
        public int FailedLobbies { get; set; }
        public int TotalBookings { get; set; }
        public int PendingBookings { get; set; }
        public int ConfirmedBookings { get; set; }
        public int CheckedInBookings { get; set; }
        public int CompletedBookings { get; set; }
        public decimal TotalRevenueVnd { get; set; }
        public int TotalDeposits { get; set; }
        public decimal TotalDepositsAmountVnd { get; set; }
        public int PendingDeposits { get; set; }
        public int CancelledDeposits { get; set; }
        public int TotalLobbyFailures { get; set; }
        public int TimeoutFailures { get; set; }
        public int HostCancelledFailures { get; set; }
        public int RejectedByCafeFailures { get; set; }
        public int ExpiredByCafeFailures { get; set; }
        public DateTime GeneratedAt { get; set; }
    }

    public class AdminLobbyFailuresReportDto
    {
        public IReadOnlyList<AdminLobbyFailureItemDto> Items { get; set; } = [];
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
        public int TimeoutCount { get; set; }
        public int HostCancelledCount { get; set; }
        public int RejectedByCafeCount { get; set; }
        public int ExpiredByCafeCount { get; set; }
    }

    public class AdminLobbyFailureItemDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? GameTemplateName { get; set; }
        public string? HostUsername { get; set; }
        public string Status { get; set; } = string.Empty;
        public string FailureType { get; set; } = string.Empty;
        public int MemberCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ClosedAt { get; set; }
    }

    public class AdminDepositItemDto
    {
        public Guid Id { get; set; }
        public string OrderId { get; set; } = string.Empty;
        public Guid UserId { get; set; }
        public string? Username { get; set; }
        public Guid CafeId { get; set; }
        public string? CafeName { get; set; }
        public decimal Amount { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class AdminDepositsReportDto
    {
        public IReadOnlyList<AdminDepositItemDto> Items { get; set; } = [];
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
        public int TotalPending { get; set; }
        public int TotalPaid { get; set; }
        public int TotalRefunded { get; set; }
        public int TotalForfeited { get; set; }
        public decimal TotalPendingAmount { get; set; }
        public decimal TotalPaidAmount { get; set; }
        public decimal TotalRefundedAmount { get; set; }
        public decimal TotalForfeitedAmount { get; set; }
        public int TotalDeposits { get; set; }
        public decimal TotalAmountVnd { get; set; }
        public int PendingDeposits { get; set; }
        public decimal PendingAmountVnd { get; set; }
        public int PaidDeposits { get; set; }
        public decimal PaidAmountVnd { get; set; }
        public int RefundedDeposits { get; set; }
        public decimal RefundedAmountVnd { get; set; }
        public int ForfeitedDeposits { get; set; }
        public decimal ForfeitedAmountVnd { get; set; }
        public decimal AverageDepositVnd { get; set; }
        public IReadOnlyList<AdminDepositTrendItemDto> Trend { get; set; } = [];
    }

    public class AdminDepositTrendItemDto
    {
        public DateOnly Date { get; set; }
        public int Count { get; set; }
        public decimal AmountVnd { get; set; }
    }

    public class AdminCafePerformanceDto
    {
        public Guid CafeId { get; set; }
        public string CafeName { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int TotalBookings { get; set; }
        public int CompletedBookings { get; set; }
        public int CancelledBookings { get; set; }
        public int NoShowBookings { get; set; }
        public decimal CompletionRate { get; set; }
        public int TotalLobbies { get; set; }
        public int FailedLobbies { get; set; }
        public decimal FailureRate { get; set; }
        public int TotalTournaments { get; set; }
        public decimal TotalRevenueVnd { get; set; }
        public decimal TotalDepositsVnd { get; set; }
        public DateTime GeneratedAt { get; set; }
        public bool ActiveCafe { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class AdminCafePerformanceReportDto
    {
        public IReadOnlyList<AdminCafePerformanceDto> Cafes { get; set; } = [];
        public int TotalCafes { get; set; }
        public decimal AverageCompletionRate { get; set; }
        public decimal AverageFailureRate { get; set; }
        public decimal TotalRevenueVnd { get; set; }
        public DateTime GeneratedAt { get; set; }
    }

    // === Admin Cafe DTOs ===

    public class AdminCafeListItemDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public Guid ManagerId { get; set; }
        public string ManagerName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int TotalSeats { get; set; }
        public int NumberOfTables { get; set; }
        public int NumberOfGamesOwned { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsActive { get; set; }
        public decimal DepositPercentage { get; set; }
        public bool HasSePayConfigured { get; set; }
        public int StaffCount { get; set; }
    }

    public class AdminCafeDetailDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Description { get; set; }
        public Guid ManagerId { get; set; }
        public string ManagerName { get; set; } = string.Empty;
        public string? ManagerEmail { get; set; }

        // Operational Status
        public string PartnerOperationalStatus { get; set; } = string.Empty;
        public string? PartnerOperationalStatusReason { get; set; }
        public DateTime? PartnerOperationalStatusChangedAt { get; set; }
        public TimeSpan? WeekdayOpen { get; set; }
        public TimeSpan? WeekdayClose { get; set; }
        public TimeSpan? WeekendOpen { get; set; }
        public TimeSpan? WeekendClose { get; set; }

        // Profile
        public int NumberOfTables { get; set; }
        public int NumberOfPrivateRooms { get; set; }
        public int TotalSeats { get; set; }
        public int NumberOfGamesOwned { get; set; }
        public string PopularGamesList { get; set; } = string.Empty;
        public bool HasGameMaster { get; set; }

        // Billing
        public string BillingModel { get; set; } = string.Empty;
        public decimal BasePrice { get; set; }
        public decimal? TieredBlockRate { get; set; }
        public int TieredBlockMinutes { get; set; }
        public bool IsPricingLocked { get; set; }

        // Deposit
        public decimal DepositPercentage { get; set; }
        public int DefaultHoldDurationMinutes { get; set; }
        public string RefundPolicy { get; set; } = string.Empty;

        // SePay
        public bool HasSePayConfigured { get; set; }

        // Audit
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool IsActive { get; set; }
    }

    public class AdminCreateCafeRequestDto
    {
        [Required(ErrorMessage = ApiErrorMessages.Validation.NameRequired)]
        [StringLength(200, MinimumLength = 5, ErrorMessage = "Tên quán phải từ 5 đến 200 ký tự.")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = ApiErrorMessages.Validation.AddressMax500)]
        [StringLength(500, MinimumLength = 10, ErrorMessage = "Địa chỉ phải từ 10 đến 500 ký tự.")]
        public string Address { get; set; } = string.Empty;

        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        [StringLength(50, ErrorMessage = ApiErrorMessages.Validation.PhoneMax50)]
        public string? PhoneNumber { get; set; }

        [StringLength(2000, ErrorMessage = ApiErrorMessages.Validation.DescriptionMax2000)]
        public string? Description { get; set; }

        [Required(ErrorMessage = "ManagerId là bắt buộc.")]
        public Guid ManagerId { get; set; }

        [Range(1, 10000, ErrorMessage = "Số ghế phải từ 1 đến 10000.")]
        public int TotalSeats { get; set; } = 20;

        [Range(0, 1000, ErrorMessage = "Số bàn phải từ 0 đến 1000.")]
        public int NumberOfTables { get; set; } = 5;

        [Range(0, 1000, ErrorMessage = "Số phòng riêng phải từ 0 đến 1000.")]
        public int NumberOfPrivateRooms { get; set; } = 0;

        [Range(0, 100000, ErrorMessage = "Số game sở hữu phải từ 0 đến 100000.")]
        public int NumberOfGamesOwned { get; set; } = 10;

        public CafePartnerBillingModel BillingModel { get; set; } = CafePartnerBillingModel.TimeBased;

        [Range(0, 10000000, ErrorMessage = "Giá cơ bản phải từ 0 đến 10.000.000 VND.")]
        public decimal BasePrice { get; set; } = 50000m;

        public decimal? TieredBlockRate { get; set; }

        [Range(1, 1440, ErrorMessage = "Thời gian block phải từ 1 đến 1440 phút.")]
        public int TieredBlockMinutes { get; set; } = 15;

        [Range(0, 0.5, ErrorMessage = "Phần trăm cọc không được vượt quá 50%.")]
        public decimal DepositPercentage { get; set; } = 0.5m;

        [Range(1, 30, ErrorMessage = "Thời gian giữ chỗ phải từ 1 đến 30 phút.")]
        public int DefaultHoldDurationMinutes { get; set; } = 30;

        public DepositRefundPolicy RefundPolicy { get; set; } = DepositRefundPolicy.Partial;

        public string? SePayApiKey { get; set; }
        public string? SePayMerchantId { get; set; }
        public string? SePaySecretKey { get; set; }
        public string? SePayReturnUrl { get; set; }
    }

    public class AdminUpdateCafeRequestDto
    {
        [StringLength(200, MinimumLength = 5, ErrorMessage = "Tên quán phải từ 5 đến 200 ký tự.")]
        public string? Name { get; set; }

        [StringLength(500, MinimumLength = 10, ErrorMessage = "Địa chỉ phải từ 10 đến 500 ký tự.")]
        public string? Address { get; set; }

        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        [StringLength(50, ErrorMessage = ApiErrorMessages.Validation.PhoneMax50)]
        public string? PhoneNumber { get; set; }

        [StringLength(2000, ErrorMessage = ApiErrorMessages.Validation.DescriptionMax2000)]
        public string? Description { get; set; }

        [Range(1, 10000, ErrorMessage = "Số ghế phải từ 1 đến 10000.")]
        public int? TotalSeats { get; set; }

        [Range(0, 1000, ErrorMessage = "Số bàn phải từ 0 đến 1000.")]
        public int? NumberOfTables { get; set; }

        [Range(0, 1000, ErrorMessage = "Số phòng riêng phải từ 0 đến 1000.")]
        public int? NumberOfPrivateRooms { get; set; }

        [Range(0, 100000, ErrorMessage = "Số game sở hữu phải từ 0 đến 100000.")]
        public int? NumberOfGamesOwned { get; set; }

        public CafePartnerBillingModel? BillingModel { get; set; }

        [Range(0, 10000000, ErrorMessage = "Giá cơ bản phải từ 0 đến 10.000.000 VND.")]
        public decimal? BasePrice { get; set; }

        public decimal? TieredBlockRate { get; set; }

        [Range(1, 1440, ErrorMessage = "Thời gian block phải từ 1 đến 1440 phút.")]
        public int? TieredBlockMinutes { get; set; }

        [Range(0, 0.5, ErrorMessage = "Phần trăm cọc không được vượt quá 50%.")]
        public decimal? DepositPercentage { get; set; }

        [Range(1, 30, ErrorMessage = "Thời gian giữ chỗ phải từ 1 đến 30 phút.")]
        public int? DefaultHoldDurationMinutes { get; set; }

        public DepositRefundPolicy? RefundPolicy { get; set; }

        public string? SePayApiKey { get; set; }
        public string? SePayMerchantId { get; set; }
        public string? SePaySecretKey { get; set; }
        public string? SePayReturnUrl { get; set; }
    }

    public class AdminCafeListResponseDto
    {
        public IReadOnlyList<AdminCafeListItemDto> Items { get; set; } = [];
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages { get; set; }
        public bool HasPreviousPage { get; set; }
        public bool HasNextPage { get; set; }
    }
}
