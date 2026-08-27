using BoardVerse.Core.Common;
using BoardVerse.Core.Data;
using BoardVerse.Core.DTOs.Cafe;
using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Helpers;
using BoardVerse.Core.Messages;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.IServices;

namespace BoardVerse.Services.Services
{
    public class CafeService : ICafeService
    {
        private readonly ICafeRepository _cafeRepository;
        private readonly IUserProfileRepository _userProfileRepository;
        private readonly ISystemConfigurationProvider _systemConfigurationProvider;
        private readonly IBookingRepository _bookingRepository;
        private readonly ILobbyHubService _hubService;
        private readonly IPushNotificationService _pushNotificationService;
        private readonly ILobbyRepository _lobbyRepository;
        private readonly IReservationRepository _reservationRepository;

        public CafeService(
            ICafeRepository cafeRepository,
            IUserProfileRepository userProfileRepository,
            ISystemConfigurationProvider systemConfigurationProvider,
            IBookingRepository bookingRepository,
            ILobbyHubService hubService,
            IPushNotificationService pushNotificationService,
            ILobbyRepository lobbyRepository,
            IReservationRepository reservationRepository)
        {
            _cafeRepository = cafeRepository;
            _userProfileRepository = userProfileRepository;
            _systemConfigurationProvider = systemConfigurationProvider;
            _bookingRepository = bookingRepository;
            _hubService = hubService;
            _pushNotificationService = pushNotificationService;
            _lobbyRepository = lobbyRepository;
            _reservationRepository = reservationRepository;
        }

        public async Task<CafeDto> GetCafeAsync(Guid cafeId)
        {
            var cafe = await _cafeRepository.GetActiveByIdAsync(cafeId);
            if (cafe == null)
            {
                throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));
            }

            return MapToDto(cafe);
        }

        public async Task<CafeDetailDto> GetCafeDetailAsync(
            Guid cafeId,
            double? latitude = null,
            double? longitude = null,
            bool includeSensitiveInfo = false, CancellationToken cancellationToken = default)
        {
            var cafe = await _cafeRepository.GetCafeDetailAsync(cafeId);
            if (cafe == null)
            {
                throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));
            }

            var utcNow = DateTime.UtcNow;
            var today = DateOnly.FromDateTime(utcNow);
            var todayTimeSlot = ResolveCurrentTimeSlot(utcNow);

            // 1. Lấy available seats hôm nay
            var seatsBySlot = await _cafeRepository.GetAvailableSeatsByTimeSlotAsync(cafeId, today);

            // 2. Tính isCurrentlyOpen dựa vào giờ mở cửa + override
            var isCurrentlyOpen = CalculateIsCurrentlyOpen(cafe, utcNow);

            // 3. Lấy refund tiers
            List<RefundTierDto>? refundTiers = null;
            if (!string.IsNullOrEmpty(cafe.RefundTiersJson) && cafe.RefundTiersJson != "[]")
            {
                try
                {
                    refundTiers = System.Text.Json.JsonSerializer.Deserialize<List<RefundTierDto>>(cafe.RefundTiersJson);
                }
                catch
                {
                    refundTiers = null;
                }
            }

            // 4. Lấy schedule overrides
            var scheduleOverrides = await _cafeRepository.GetScheduleOverridesAsync(
                cafeId,
                fromDate: today,
                toDate: today.AddDays(30));

            // 5. Tính distance nếu có lat/lng
            double? distanceKm = null;
            if (latitude.HasValue && longitude.HasValue && cafe.Latitude.HasValue && cafe.Longitude.HasValue)
            {
                distanceKm = GeoLocationHelper.HaversineKm(
                    latitude.Value, longitude.Value,
                    cafe.Latitude.Value, cafe.Longitude.Value);
            }

            // 6. Tính tổng seats
            var totalAvailable = seatsBySlot.Values.Sum();
            var totalHeld = await _cafeRepository.CountHeldSeatsAsync(cafeId, today);
            var totalInUse = await _cafeRepository.CountInUseSeatsAsync(cafeId, today);

            return new CafeDetailDto
            {
                Id = cafe.Id,
                Name = cafe.Name,
                Address = cafe.Address,
                Latitude = cafe.Latitude,
                Longitude = cafe.Longitude,
                PhoneNumber = cafe.PhoneNumber,
                Description = cafe.Description,
                CreatedAt = cafe.CreatedAt,
                TotalSeats = cafe.TotalSeats,
                BillingModel = CafePartnerStatusMapper.ToApiBillingModel(cafe.BillingModel),
                BasePrice = cafe.BasePrice,
                TieredBlockRate = cafe.TieredBlockRate,
                TieredBlockMinutes = cafe.TieredBlockMinutes,
                DepositPercentage = cafe.DepositPercentage,
                IsPricingLocked = cafe.IsPricingLocked,
                HasSePayConfigured = !string.IsNullOrWhiteSpace(cafe.SePayMerchantId)
                                  && !string.IsNullOrWhiteSpace(cafe.SePayApiKey)
                                  && !string.IsNullOrWhiteSpace(cafe.SePaySecretKey),

                // Operational Status
                OperationalStatus = cafe.PartnerOperationalStatus?.ToString() ?? "ACTIVE",
                OperationalStatusReason = includeSensitiveInfo ? cafe.PartnerOperationalStatusReason : null,
                IsCurrentlyOpen = isCurrentlyOpen,

                // Refund Policy (BR-18)
                RefundPolicy = cafe.RefundPolicy.ToString(),
                RefundTiers = refundTiers,

                // Deposit Configuration (BR-DEPOSIT-03 + BR-NEW-01 defaults)
                DepositRatePerPerson = 10,
                MinDeposit = new CafeMinDepositDto
                {
                    SameDay = 50_000,
                    OneDay = 50_000,
                    TwoDays = 100_000,
                    ThreeToFourDays = 150_000,
                    FiveToSevenDays = 200_000
                },

                // BR-NEW-12: CafeConfig defaults
                CafeConfig = new CafeConfigDto
                {
                    Capacity = cafe.TotalSeats,
                    MaxLobbiesPerUserPerDay = 1,
                    MaxPlayersPerLobbySameDay = 30,
                    MaxPlayersPerLobby1Day = 20,
                    MaxPlayersPerLobby2Days = 15,
                    MaxPlayersPerLobby3To4Days = 10,
                    MaxPlayersPerLobby5To7Days = 6,
                    RequireApprovalForDistant = true,
                    DistantThresholdDays = 2,
                    ApprovalTimeoutHours = 24,
                    MaxTotalDepositPerUser = 500_000,
                    RecruitmentDeadlineBufferMinutes = 120,
                    CancellationGraceMinutes = 15
                },

                // Seat Availability
                AvailableSeats = totalAvailable,
                HeldSeats = totalHeld,
                InUseSeats = totalInUse,
                AvailableSeatsByTimeSlot = seatsBySlot.ToDictionary(
                    kvp => kvp.Key.ToString(),
                    kvp => kvp.Value),

                ScheduleOverrides = scheduleOverrides.Select(o => new CafeScheduleOverrideDto
                {
                    ApplyDate = o.ApplyDate,
                    Reason = null,
                    OpenTime = o.OpenTime,
                    CloseTime = o.CloseTime,
                    IsClosed = o.IsClosed
                }).ToList(),

                // Additional Info
                NumberOfTables = cafe.NumberOfTables,
                NumberOfPrivateRooms = cafe.NumberOfPrivateRooms,
                NumberOfGamesOwned = cafe.NumberOfGamesOwned,
                HasGameMaster = cafe.HasGameMaster,
                DistanceKm = distanceKm
            };
        }

        private static bool CalculateIsCurrentlyOpen(Cafe cafe, DateTime utcNow)
        {
            var localNow = utcNow.AddHours(7); // VN timezone
            var dayOfWeek = localNow.DayOfWeek;
            var isWeekend = dayOfWeek == DayOfWeek.Saturday || dayOfWeek == DayOfWeek.Sunday;

            var openTime = isWeekend ? cafe.WeekendOpen : cafe.WeekdayOpen;
            var closeTime = isWeekend ? cafe.WeekendClose : cafe.WeekdayClose;

            if (!openTime.HasValue || !closeTime.HasValue)
            {
                return true; // Default: mở cửa
            }

            var currentTime = localNow.TimeOfDay;
            var open = openTime.Value;
            var close = closeTime.Value;

            // Handle overnight (close < open means next day)
            if (close < open)
            {
                return currentTime >= open || currentTime <= close;
            }

            return currentTime >= open && currentTime <= close;
        }

        private static TimeSlot ResolveCurrentTimeSlot(DateTime utcNow)
        {
            var localNow = utcNow.AddHours(7); // VN timezone
            var hour = localNow.Hour;

            return hour switch
            {
                >= 6 and < 12 => TimeSlot.Morning,
                >= 12 and < 17 => TimeSlot.Afternoon,
                >= 17 and < 23 => TimeSlot.Evening,
                _ => TimeSlot.LateNight
            };
        }

        // === Legacy MapToDto kept for backward compatibility ===

        public async Task<CafeDto> UpdateCafeAsync(Guid cafeId, Guid managerId, UpdateCafeRequestDto dto, CancellationToken cancellationToken = default)
        {
            var cafe = await EnsureManagerOwnsCafeAsync(cafeId, managerId);

            if (!string.IsNullOrWhiteSpace(dto.Name))
            {
                cafe.Name = dto.Name.Trim();
            }

            if (!string.IsNullOrWhiteSpace(dto.Address))
            {
                cafe.Address = dto.Address.Trim();
            }

            if (dto.PhoneNumber != null)
            {
                cafe.PhoneNumber = string.IsNullOrWhiteSpace(dto.PhoneNumber) ? null : dto.PhoneNumber.Trim();
            }

            if (dto.Description != null)
            {
                cafe.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
            }

            if (dto.Latitude.HasValue && dto.Longitude.HasValue)
            {
                try
                {
                    GeoLocationHelper.ApplyCoordinates(cafe, dto.Latitude.Value, dto.Longitude.Value);
                }
                catch (ArgumentOutOfRangeException ex)
                {
                    throw new BadRequestException(ex.ParamName switch
                    {
                        "latitude" => ApiErrorMessages.Cafe.InvalidLatitudeForCafeUpdate,
                        "longitude" => ApiErrorMessages.Cafe.InvalidLongitudeForCafeUpdate,
                        _ => ApiErrorMessages.Cafe.InvalidLatitudeForCafeUpdate
                    });
                }
            }
            else if (dto.Latitude.HasValue || dto.Longitude.HasValue)
            {
                throw new BadRequestException(ApiErrorMessages.Cafe.LocationCoordinatesPairRequired);
            }

            // SePay Configuration (Session Payment)
            if (dto.SePayMerchantId != null)
            {
                cafe.SePayMerchantId = string.IsNullOrWhiteSpace(dto.SePayMerchantId) ? null : dto.SePayMerchantId.Trim();
            }

            if (dto.SePayApiKey != null)
            {
                cafe.SePayApiKey = string.IsNullOrWhiteSpace(dto.SePayApiKey) ? null : dto.SePayApiKey.Trim();
            }

            if (dto.SePaySecretKey != null)
            {
                cafe.SePaySecretKey = string.IsNullOrWhiteSpace(dto.SePaySecretKey) ? null : dto.SePaySecretKey.Trim();
            }

            if (dto.SePayReturnUrl != null)
            {
                cafe.SePayReturnUrl = string.IsNullOrWhiteSpace(dto.SePayReturnUrl) ? null : dto.SePayReturnUrl.Trim();
            }

            cafe.UpdatedAt = DateTime.UtcNow;
            await _cafeRepository.SaveChangesAsync();

            return MapToDto(cafe);
        }

        public async Task<IEnumerable<ManagerCafeDto>> GetManagerCafesAsync(Guid managerId, CancellationToken cancellationToken = default)
        {
            var cafes = await _cafeRepository.GetCafesByManagerIdAsync(managerId);
            var cafesList = cafes as IReadOnlyList<Cafe> ?? cafes.ToList();
            var results = new List<ManagerCafeDto>(cafesList.Count);
            foreach (var c in cafesList)
            {
                results.Add(await MapToManagerDtoAsync(c, isStaff: false));
            }
            return results;
        }

        public async Task AddStaffAsync(Guid cafeId, Guid currentManagerId, AddStaffRequestDto dto, CancellationToken cancellationToken = default)
        {
            var cafe = await EnsureManagerOwnsCafeAsync(cafeId, currentManagerId);
            var existingUser = await _cafeRepository.GetUserByEmailAsync(dto.Email);
            User staffUser;

            if (existingUser != null)
            {
                if (existingUser.Role is UserRole.Admin or UserRole.Manager)
                {
                    throw new BadRequestException(ApiErrorMessages.Cafe.StaffAdminOrManagerNotAllowed);
                }

                if (existingUser.Role != UserRole.CafeStaff)
                {
                    throw new BadRequestException(
                        ApiErrorMessages.Cafe.StaffWrongRoleMustPromote(dto.Email, existingUser.Role.ToString()));
                }

                if (await _cafeRepository.IsStaffMemberExistsAsync(cafeId, existingUser.Id))
                {
                    throw new ConflictException(ApiErrorMessages.Cafe.StaffAlreadyAssigned);
                }

                staffUser = existingUser;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(dto.Username))
                {
                    throw new BadRequestException(ApiErrorMessages.Cafe.StaffCreateUsernameRequired);
                }

                var username = await ResolveUsernameAsync(dto.Username, excludedUserId: null);
                staffUser = new User
                {
                    Id = Guid.NewGuid(),
                    Email = dto.Email,
                    Username = username,
                    Role = UserRole.CafeStaff,
                    Provider = "Local",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    IsActive = true,
                    IsEmailVerified = true
                };

                ApplyOptionalPassword(staffUser, dto.Password);
                await _cafeRepository.AddUserAsync(staffUser);
            }

            await LinkStaffToCafeAsync(cafe, staffUser);
        }

        public async Task PromoteUserToStaffAsync(Guid cafeId, Guid currentManagerId, PromoteStaffRequestDto dto, CancellationToken cancellationToken = default)
        {
            var cafe = await EnsureManagerOwnsCafeAsync(cafeId, currentManagerId);
            var user = await _cafeRepository.GetUserByEmailAsync(dto.Email);
            if (user == null)
            {
                throw new NotFoundException(ApiErrorMessages.Cafe.StaffUserNotFound);
            }

            if (user.Role is UserRole.Admin or UserRole.Manager)
            {
                throw new BadRequestException(ApiErrorMessages.Cafe.StaffAdminOrManagerNotAllowed);
            }

            if (user.Role == UserRole.CafeStaff)
            {
                throw new BadRequestException(
                    ApiErrorMessages.Cafe.StaffAlreadyCafeStaffMustLink(dto.Email));
            }

            if (!string.IsNullOrWhiteSpace(dto.Username))
            {
                user.Username = await ResolveUsernameAsync(dto.Username, user.Id);
            }

            user.Role = UserRole.CafeStaff;
            user.UpdatedAt = DateTime.UtcNow;
            ApplyOptionalPassword(user, dto.Password);

            if (await _cafeRepository.IsStaffMemberExistsAsync(cafeId, user.Id))
            {
                throw new ConflictException(ApiErrorMessages.Cafe.StaffAlreadyAssigned);
            }

            await LinkStaffToCafeAsync(cafe, user);
        }

        public async Task<PaginatedResponse<StaffDto>> GetStaffListAsync(
            Guid cafeId,
            Guid currentManagerId,
            PaginationParams paginationParams, CancellationToken cancellationToken = default)
        {
            await EnsureManagerOwnsCafeAsync(cafeId, currentManagerId);
            return await _cafeRepository.GetStaffPagedAsync(cafeId, paginationParams);
        }

        public async Task RemoveStaffAsync(Guid cafeId, Guid currentManagerId, Guid staffId, CancellationToken cancellationToken = default)
        {
            await EnsureManagerOwnsCafeAsync(cafeId, currentManagerId);

            var cafeStaff = await _cafeRepository.GetCafeStaffAsync(cafeId, staffId);
            if (cafeStaff == null)
            {
                throw new NotFoundException(ApiErrorMessages.Cafe.StaffNotFound(cafeId, staffId));
            }

            await _cafeRepository.RemoveCafeStaffAsync(cafeStaff);

            var remainingAssignments = await _cafeRepository.CountActiveStaffAssignmentsAsync(staffId);
            if (remainingAssignments == 0)
            {
                var user = await _cafeRepository.GetUserByIdAsync(staffId);
                if (user != null && user.Role == UserRole.CafeStaff)
                {
                    user.Role = UserRole.Player;
                    user.UpdatedAt = DateTime.UtcNow;
                }
            }

            await _cafeRepository.SaveChangesAsync();
        }

        public async Task<IEnumerable<ManagerCafeDto>> GetMyWorkplacesAsync(Guid currentStaffId, CancellationToken cancellationToken = default)
        {
            var cafes = await _cafeRepository.GetCafesByStaffIdAsync(currentStaffId);
            var cafesList = cafes as IReadOnlyList<Cafe> ?? cafes.ToList();
            var results = new List<ManagerCafeDto>(cafesList.Count);
            foreach (var c in cafesList)
            {
                results.Add(await MapToManagerDtoAsync(c, isStaff: true));
            }
            return results;
        }

        /// <summary>
        /// Map Cafe entity → ManagerCafeDto (bao gồm operational details + SePay raw + schedule).
        /// isStaff=true ẩn một số field nhạy cảm (manager-only fields).
        /// Async để query các counter (staff count, upcoming bookings, active lobbies, revenue, seats, ...).
        /// </summary>
        private async Task<ManagerCafeDto> MapToManagerDtoAsync(Cafe cafe, bool isStaff)
        {
            var dto = new ManagerCafeDto
            {
                // === Basic (CafeDto) ===
                Id = cafe.Id,
                Name = cafe.Name,
                Address = cafe.Address,
                Latitude = cafe.Latitude,
                Longitude = cafe.Longitude,
                PhoneNumber = cafe.PhoneNumber,
                Description = cafe.Description,
                CreatedAt = cafe.CreatedAt,
                TotalSeats = cafe.TotalSeats,
                BillingModel = CafePartnerStatusMapper.ToApiBillingModel(cafe.BillingModel),
                BasePrice = cafe.BasePrice,
                TieredBlockRate = cafe.TieredBlockRate,
                TieredBlockMinutes = cafe.TieredBlockMinutes,
                DepositPercentage = cafe.DepositPercentage,
                IsPricingLocked = cafe.IsPricingLocked,
                HasSePayConfigured = !string.IsNullOrWhiteSpace(cafe.SePayMerchantId)
                                  && !string.IsNullOrWhiteSpace(cafe.SePayApiKey)
                                  && !string.IsNullOrWhiteSpace(cafe.SePaySecretKey),

                // === Operational ===
                OperationalStatus = cafe.PartnerOperationalStatus?.ToString() ?? "ACTIVE",
                OperationalStatusReason = cafe.PartnerOperationalStatusReason,

                // === Refund ===
                RefundPolicy = cafe.RefundPolicy.ToString(),

                // === Schedule ===
                WeekdayOpen = cafe.WeekdayOpen.HasValue
                    ? TimeOnly.FromTimeSpan(cafe.WeekdayOpen.Value)
                    : null,
                WeekdayClose = cafe.WeekdayClose.HasValue
                    ? TimeOnly.FromTimeSpan(cafe.WeekdayClose.Value)
                    : null,
                WeekendOpen = cafe.WeekendOpen.HasValue
                    ? TimeOnly.FromTimeSpan(cafe.WeekendOpen.Value)
                    : null,
                WeekendClose = cafe.WeekendClose.HasValue
                    ? TimeOnly.FromTimeSpan(cafe.WeekendClose.Value)
                    : null,

                // === Manager-only fields (ẩn nếu isStaff) ===
                ManagerId = isStaff ? Guid.Empty : cafe.ManagerId,
                DefaultHoldDurationMinutes = cafe.DefaultHoldDurationMinutes,

                // === Audit ===
                UpdatedAt = cafe.UpdatedAt,
                OperationalProfileUpdatedAt = cafe.OperationalProfileUpdatedAt,
            };

            // === Counts (đếm từ DB song song) ===
            var utcNow = DateTime.UtcNow;
            var today = DateOnly.FromDateTime(utcNow);
            var weekEnd = utcNow.AddDays(7);

            int staffCount = cafe.StaffMembers?.Count ?? 0;
            int upcomingBookingsCount;
            int activeLobbiesToday;
            int pendingCafeApprovalCount;
            long heldDepositTotal;
            int availableSeats;
            int heldSeats;
            int inUseSeats;
            Dictionary<TimeSlot, int>? seatsBySlot = null;
            List<CafeScheduleOverride>? scheduleOverrides = null;
            List<RefundTierDto>? refundTiers = null;

            try
            {
                var bookingsWeekTask = _bookingRepository.GetByCafeIdAsync(cafe.Id, utcNow, weekEnd);
                var pendingApprovalTask = _reservationRepository.GetPendingCafeApprovalAsync(
                    new List<Guid> { cafe.Id }, cafe.Id, null, 1, 1);
                var seatsBySlotTask = _cafeRepository.GetAvailableSeatsByTimeSlotAsync(cafe.Id, today);
                var scheduleOverridesTask = _cafeRepository.GetScheduleOverridesAsync(
                    cafe.Id, today, today.AddDays(30));
                var heldSeatsTask = _cafeRepository.CountHeldSeatsAsync(cafe.Id, today);
                var inUseSeatsTask = _cafeRepository.CountInUseSeatsAsync(cafe.Id, today);

                                // Sử dụng 6 await trực tiếp thay vì .Result để tránh deadlock risk
                // (GAP-1 đã fix: trước đây dùng .Result sau Task.WhenAll).
                var bookingsWeek = await bookingsWeekTask;
                var pendingApproval = await pendingApprovalTask;
                seatsBySlot = await seatsBySlotTask;
                scheduleOverrides = await scheduleOverridesTask;
                heldSeats = await heldSeatsTask;
                inUseSeats = await inUseSeatsTask;

                // Upcoming bookings = Confirmed/PendingDeposit chưa kết thúc trong 7 ngày tới
                upcomingBookingsCount = bookingsWeek.Count(b =>
                    b.Status == BookingStatus.PendingDeposit
                    || b.Status == BookingStatus.Confirmed
                    || b.Status == BookingStatus.CheckedIn);

                // Pending cafe approval (BR-NEW-11)
                pendingCafeApprovalCount = pendingApproval.TotalCount;

                // Active lobbies today: Reservation holding/confirmed (GAP-01 fix: dùng PreferredStartTime/EndTime)
                // Count all reservations today for this cafe
                var activeReservationsToday = await _reservationRepository.GetActiveByCafePlayDateAsync(cafe.Id, today);
                activeLobbiesToday = activeReservationsToday.Count;

                // Held deposit total: tổng DepositAmount của các Reservation active tại cafe này
                // (GAP-01 fix: dùng activeReservationsToday thay vì slotResults)
                heldDepositTotal = activeReservationsToday
                    .Where(r => r.Status == ReservationStatus.Holding || r.Status == ReservationStatus.Confirmed)
                    .Sum(r => (long)r.DepositAmount);

                // Seats
                availableSeats = seatsBySlot.Values.Sum();
            }
            catch (Exception)
            {
                // Nếu query fail (DB tạm không khả dụng), fallback 0 để manager dashboard vẫn render.
                upcomingBookingsCount = 0;
                activeLobbiesToday = 0;
                pendingCafeApprovalCount = 0;
                heldDepositTotal = 0;
                availableSeats = 0;
                heldSeats = 0;
                inUseSeats = 0;
            }

            dto.StaffCount = staffCount;
            dto.UpcomingBookingsCount = upcomingBookingsCount;
            dto.ActiveLobbiesToday = activeLobbiesToday;
            dto.PendingCafeApprovalLobbiesCount = pendingCafeApprovalCount;
            dto.HeldDepositTotal = heldDepositTotal;
            dto.AvailableSeats = availableSeats;
            dto.HeldSeats = heldSeats;
            dto.InUseSeats = inUseSeats;
            dto.AvailableSeatsByTimeSlot = seatsBySlot?.ToDictionary(
                kvp => kvp.Key.ToString(), kvp => kvp.Value);

            dto.ScheduleOverrides = scheduleOverrides?
                .Select(o => new CafeScheduleOverrideDto
                {
                    ApplyDate = o.ApplyDate,
                    Reason = null,
                    OpenTime = o.OpenTime,
                    CloseTime = o.CloseTime,
                    IsClosed = o.IsClosed
                }).ToList();

            // Refund tiers
            if (!string.IsNullOrEmpty(cafe.RefundTiersJson) && cafe.RefundTiersJson != "[]")
            {
                try
                {
                    refundTiers = System.Text.Json.JsonSerializer.Deserialize<List<RefundTierDto>>(cafe.RefundTiersJson);
                }
                catch
                {
                    refundTiers = null;
                }
            }
            dto.RefundTiers = refundTiers;

            // Deposit Configuration (BR-DEPOSIT-03 + BR-NEW-01 defaults)
            dto.DepositRatePerPerson = 10;
            dto.MinDeposit = new CafeMinDepositDto
            {
                SameDay = 50_000,
                OneDay = 50_000,
                TwoDays = 100_000,
                ThreeToFourDays = 150_000,
                FiveToSevenDays = 200_000
            };

            // BR-NEW-12: CafeConfig defaults
            dto.CafeConfig = new CafeConfigDto
            {
                Capacity = cafe.TotalSeats,
                MaxLobbiesPerUserPerDay = 1,
                MaxPlayersPerLobbySameDay = 30,
                MaxPlayersPerLobby1Day = 20,
                MaxPlayersPerLobby2Days = 15,
                MaxPlayersPerLobby3To4Days = 10,
                MaxPlayersPerLobby5To7Days = 6,
                RequireApprovalForDistant = true,
                DistantThresholdDays = 2,
                ApprovalTimeoutHours = 24,
                MaxTotalDepositPerUser = 500_000,
                RecruitmentDeadlineBufferMinutes = 120,
                CancellationGraceMinutes = 15
            };

            // POS-related counts
            dto.NumberOfTables = cafe.NumberOfTables;
            dto.NumberOfPrivateRooms = cafe.NumberOfPrivateRooms;
            dto.NumberOfGamesOwned = cafe.NumberOfGamesOwned;
            dto.HasGameMaster = cafe.HasGameMaster;

            // Hide SePay raw từ staff (chỉ manager thấy)
            if (!isStaff)
            {
                dto.SePayMerchantId = cafe.SePayMerchantId;
                dto.SePayBankCode = cafe.SePayBankCode;
                dto.SePayAccountNumber = cafe.SePayAccountNumber;
                dto.SePayReturnUrl = cafe.SePayReturnUrl;
            }

            return dto;
        }

        public async Task<NearbyCafeSearchResultDto> GetNearbyCafesAsync(
            double latitude,
            double longitude,
            double radiusKm,
            Guid? gameTemplateId,
            string? name,
            PaginationParams paginationParams, CancellationToken cancellationToken = default)
        {
            // gameTemplateId is now optional — không filter theo game nếu null/empty.

            radiusKm = await ResolveMatchmakingRadiusKmAsync(radiusKm);

            try
            {
                GeoLocationHelper.ValidateCoordinates(latitude, longitude);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new BadRequestException(ex.ParamName switch
                {
                    "latitude" => ApiErrorMessages.Cafe.InvalidLatitudeForNearbySearch,
                    "longitude" => ApiErrorMessages.Cafe.InvalidLongitudeForNearbySearch,
                    _ => ApiErrorMessages.Cafe.InvalidLatitudeForNearbySearch
                });
            }

            if (radiusKm is < GeoLocationHelper.MinNearbyRadiusKm or > GeoLocationHelper.MaxNearbyRadiusKm)
            {
                throw new BadRequestException(ApiErrorMessages.Cafe.InvalidNearbySearchRadius(
                    GeoLocationHelper.MinNearbyRadiusKm,
                    GeoLocationHelper.MaxNearbyRadiusKm));
            }

            var result = await _cafeRepository.GetNearbyAsync(
                latitude,
                longitude,
                radiusKm,
                gameTemplateId,
                name,
                paginationParams);

            var cafes = result.Data.ToList();
            if (cafes.Count > 0)
            {
                if (gameTemplateId.HasValue && gameTemplateId.Value != Guid.Empty)
                {
                    await _cafeRepository.EnrichNearbyWithGameWaitAsync(cafes, gameTemplateId.Value);
                    result.Data = cafes;
                }

                return new NearbyCafeSearchResultDto
                {
                    Cafes = result,
                    EmptyResultMessage = null,
                    AlternativeSuggestions = []
                };
            }

            if (result.Meta.TotalItems > 0)
            {
                return new NearbyCafeSearchResultDto
                {
                    Cafes = result,
                    EmptyResultMessage = null,
                    AlternativeSuggestions = []
                };
            }

            // Chỉ gợi ý game thay thế khi user đã chọn 1 game cụ thể.
            // Nếu không có gameTemplateId → trả danh sách rỗng (không có gì để gợi ý).
            IReadOnlyList<NearbyAlternativeGameSuggestionDto> alternativeSuggestions = [];
            if (gameTemplateId.HasValue && gameTemplateId.Value != Guid.Empty)
            {
                alternativeSuggestions = await _cafeRepository.GetAlternativeGameSuggestionsAsync(
                    latitude,
                    longitude,
                    radiusKm,
                    gameTemplateId.Value);
            }

            return new NearbyCafeSearchResultDto
            {
                Cafes = result,
                EmptyResultMessage = ApiErrorMessages.Cafe.NoNearbyCafesWithSelectedGameMessage,
                AlternativeSuggestions = alternativeSuggestions
            };
        }

        public async Task<NearbyCafeSearchResultDto> GetNearbyCafesForCurrentUserAsync(
            Guid userId,
            double radiusKm,
            Guid? gameTemplateId,
            string? name,
            PaginationParams paginationParams, CancellationToken cancellationToken = default)
        {
            var profile = await _userProfileRepository.GetProfileByUserIdAsync(userId);
            if (profile?.LastKnownLatitude is not { } latitude
                || profile.LastKnownLongitude is not { } longitude)
            {
                throw new BadRequestException(ApiErrorMessages.Cafe.SavedLocationRequiredForNearbySearch);
            }

            return await GetNearbyCafesAsync(
                latitude,
                longitude,
                radiusKm,
                gameTemplateId,
                name,
                paginationParams);
        }

        public async Task<PaginatedResponse<NearbyCafeDto>> GetAllActiveCafesAsync(
            PaginationParams paginationParams)
        {
            return await _cafeRepository.GetAllActiveCafesAsync(paginationParams);
        }

        public async Task<PaginatedResponse<NearbyCafeDto>> SearchCafesAsync(
            string name,
            double? latitude,
            double? longitude,
            double? radiusKm,
            PaginationParams paginationParams, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new BadRequestException(ApiErrorMessages.Cafe.SearchNameRequired);
            }

            var radius = radiusKm ?? GeoLocationHelper.DefaultNearbyRadiusKm;

            if (radius is < GeoLocationHelper.MinNearbyRadiusKm or > GeoLocationHelper.MaxNearbyRadiusKm)
            {
                throw new BadRequestException(ApiErrorMessages.Cafe.InvalidNearbySearchRadius(
                    GeoLocationHelper.MinNearbyRadiusKm,
                    GeoLocationHelper.MaxNearbyRadiusKm));
            }

            if (latitude.HasValue && longitude.HasValue)
            {
                try
                {
                    GeoLocationHelper.ValidateCoordinates(latitude.Value, longitude.Value);
                }
                catch (ArgumentOutOfRangeException ex)
                {
                    throw new BadRequestException(ex.ParamName switch
                    {
                        "latitude" => ApiErrorMessages.Cafe.InvalidLatitudeForNearbySearch,
                        "longitude" => ApiErrorMessages.Cafe.InvalidLongitudeForNearbySearch,
                        _ => ApiErrorMessages.Cafe.InvalidLatitudeForNearbySearch
                    });
                }
            }

            return await _cafeRepository.SearchCafesAsync(name, latitude, longitude, radius, paginationParams);
        }

        public async Task<AdminCafeOperationalStatusResultDto> SetOperationalStatusByAdminAsync(
            Guid cafeId,
            AdminSetCafeOperationalStatusRequestDto request, CancellationToken cancellationToken = default)
        {
            if (!CafePartnerStatusMapper.TryParseApiOperationalStatus(request.Status, out var status))
            {
                throw new BadRequestException(ApiErrorMessages.CafePartner.InvalidOperationalStatus);
            }

            if (status == CafePartnerOperationalStatus.Banned
                && string.IsNullOrWhiteSpace(request.Reason))
            {
                throw new BadRequestException(ApiErrorMessages.CafePartner.BanReasonRequired);
            }

            var cafe = await _cafeRepository.GetByIdAsync(cafeId);
            if (cafe == null)
            {
                throw new NotFoundException(ApiErrorMessages.Cafe.CafeRecordNotFound(cafeId));
            }

            var utcNow = DateTime.UtcNow;
            var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();

            cafe.PartnerOperationalStatus = status;
            cafe.IsActive = status == CafePartnerOperationalStatus.Active;
            cafe.PartnerOperationalStatusReason = status is CafePartnerOperationalStatus.Inactive
                or CafePartnerOperationalStatus.Banned
                ? reason
                : null;
            cafe.PartnerOperationalStatusChangedAt = utcNow;
            cafe.UpdatedAt = utcNow;

            await _cafeRepository.SaveChangesAsync();

            return new AdminCafeOperationalStatusResultDto
            {
                CafeId = cafe.Id,
                OperationalStatus = CafePartnerStatusMapper.ToApiOperationalStatus(status),
                IsActive = cafe.IsActive,
                Reason = cafe.PartnerOperationalStatusReason
            };
        }

        public async Task UpdateSePayConfigAsync(Guid cafeId, Guid managerId, UpdateSePayConfigRequestDto dto)
        {
            var cafe = await EnsureManagerOwnsCafeAsync(cafeId, managerId);

            if (dto.SePayBankCode != null)
            {
                cafe.SePayBankCode = string.IsNullOrWhiteSpace(dto.SePayBankCode) ? null : dto.SePayBankCode.Trim();
            }

            if (dto.SePayAccountNumber != null)
            {
                cafe.SePayAccountNumber = string.IsNullOrWhiteSpace(dto.SePayAccountNumber) ? null : dto.SePayAccountNumber.Trim();
            }

            cafe.UpdatedAt = DateTime.UtcNow;
            await _cafeRepository.SaveChangesAsync();
        }

        public async Task<RefundPolicyResponseDto> UpdateRefundPolicyAsync(Guid cafeId, Guid managerId, UpdateRefundPolicyRequestDto dto)
        {
            var cafe = await EnsureManagerOwnsCafeAsync(cafeId, managerId);

            cafe.RefundPolicy = dto.Policy;

            // BR-18: Validate tiers khi Policy=Partial
            if (dto.Policy == DepositRefundPolicy.Partial)
            {
                if (dto.PartialTiers == null || dto.PartialTiers.Count == 0)
                {
                    throw new BadRequestException(ApiErrorMessages.Cafe.PartialTiersRequired);
                }
                if (dto.PartialTiers.Count > 5)
                {
                    throw new BadRequestException(ApiErrorMessages.Cafe.PartialTiersMaxFive);
                }

                // Sort giảm dần theo minHours + validate unique
                var sorted = dto.PartialTiers.OrderByDescending(t => t.MinHoursBeforeScheduled).ToList();
                for (var i = 0; i < sorted.Count; i++)
                {
                    if (sorted[i].RefundPercent < 0 || sorted[i].RefundPercent > 100)
                    {
                        throw new BadRequestException(ApiErrorMessages.Cafe.RefundPercentOutOfRange);
                    }
                    if (i > 0 && sorted[i].MinHoursBeforeScheduled == sorted[i - 1].MinHoursBeforeScheduled)
                    {
                        throw new BadRequestException(ApiErrorMessages.Cafe.PartialTiersDuplicateMinHours);
                    }
                }

                cafe.RefundTiersJson = System.Text.Json.JsonSerializer.Serialize(sorted);
            }
            else
            {
                // Full hoặc None → clear tiers
                cafe.RefundTiersJson = "[]";
            }

            cafe.UpdatedAt = DateTime.UtcNow;
            await _cafeRepository.SaveChangesAsync();

            // Parse tiers để trả về
            List<RefundTierDto>? tiers = null;
            if (!string.IsNullOrEmpty(cafe.RefundTiersJson) && cafe.RefundTiersJson != "[]")
            {
                tiers = System.Text.Json.JsonSerializer.Deserialize<List<RefundTierDto>>(cafe.RefundTiersJson);
            }

            return new RefundPolicyResponseDto
            {
                CafeId = cafeId,
                Policy = cafe.RefundPolicy,
                PartialTiers = tiers,
                UpdatedAt = cafe.UpdatedAt
            };
        }

        public async Task<CafePricingConfigResponseDto> UpdatePricingConfigAsync(Guid cafeId, Guid managerId, UpdatePricingConfigRequestDto dto)
        {
            var cafe = await EnsureManagerOwnsCafeAsync(cafeId, managerId);

            // BR-04: chặn sửa giá khi quán đang hoạt động
            if (cafe.IsPricingLocked)
            {
                throw new ConflictException(ApiErrorMessages.Cafe.PricingLockedWhileOpen);
            }

            var oldBasePrice = cafe.BasePrice;

            if (dto.BillingModel.HasValue)
            {
                cafe.BillingModel = dto.BillingModel.Value;
            }
            if (dto.BasePrice.HasValue)
            {
                cafe.BasePrice = dto.BasePrice.Value;
            }
            if (dto.TieredBlockRate.HasValue)
            {
                cafe.TieredBlockRate = dto.TieredBlockRate.Value;
            }
            if (dto.TieredBlockMinutes.HasValue)
            {
                cafe.TieredBlockMinutes = dto.TieredBlockMinutes.Value;
            }

            cafe.OperationalProfileUpdatedAt = DateTime.UtcNow;
            cafe.UpdatedAt = DateTime.UtcNow;
            await _cafeRepository.SaveChangesAsync();

            // BR-04: tìm booking trong tuần của cafe này để broadcast CafePricingChanged
            var weekStart = DateTime.UtcNow;
            var weekEnd = weekStart.AddDays(7);
            var affectedBookings = await _bookingRepository.GetByCafeIdAsync(cafeId, fromDate: weekStart, toDate: weekEnd);
            var affectedCount = affectedBookings.Count;

            // SignalR broadcast — task #13: CafePricingChanged
            // (Mobile client subscribe qua group cafe-{cafeId} nếu có; lobby group không liên quan.)
            await _hubService.NotifyCafePricingChanged(
                cafeId,
                cafe.Name,
                oldBasePrice,
                cafe.BasePrice,
                cafe.OperationalProfileUpdatedAt ?? DateTime.UtcNow,
                affectedCount);

            // Mobile gap #13: FCM push cho tất cả user có booking trong tuần (kể cả walk-in deposit owners)
            // — để họ nhận notification khi app đã đóng/background.
            // Tránh duplicate user (1 user có thể có nhiều booking).
            var affectedUserIds = affectedBookings
                .SelectMany(b => new[]
                {
                    b.Lobby?.HostUserId ?? Guid.Empty,
                    b.BookingDeposit?.UserId ?? Guid.Empty
                })
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();

            if (affectedUserIds.Count > 0)
            {
                await _pushNotificationService.SendToUsersAsync(affectedUserIds, new PushNotificationPayload
                {
                    Type = "CafePricingChanged",
                    Title = "Biểu phí quán đã thay đổi",
                    Body = $"{cafe.Name}: giờ đầu từ {oldBasePrice:N0}đ → {cafe.BasePrice:N0}đ. " +
                           $"Có {affectedCount} đơn đặt chỗ trong tuần bị ảnh hưởng.",
                    Data = new Dictionary<string, string>
                    {
                        { "cafeId", cafeId.ToString() },
                        { "cafeName", cafe.Name },
                        { "oldFirstHourPrice", oldBasePrice.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                        { "newFirstHourPrice", cafe.BasePrice.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                        { "effectiveDate", (cafe.OperationalProfileUpdatedAt ?? DateTime.UtcNow).ToString("o") },
                        { "affectedBookingsCount", affectedCount.ToString() }
                    }
                });
            }

            return new CafePricingConfigResponseDto
            {
                CafeId = cafeId,
                BillingModel = cafe.BillingModel,
                BasePrice = cafe.BasePrice,
                TieredBlockRate = cafe.TieredBlockRate,
                TieredBlockMinutes = cafe.TieredBlockMinutes,
                IsPricingLocked = cafe.IsPricingLocked,
                OperationalProfileUpdatedAt = cafe.OperationalProfileUpdatedAt,
                AffectedBookingsCount = affectedCount
            };
        }

        private async Task<Cafe> EnsureManagerOwnsCafeAsync(Guid cafeId, Guid currentManagerId)
        {
            var cafe = await _cafeRepository.GetByIdAsync(cafeId);
            if (cafe == null)
            {
                throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));
            }

            if (cafe.ManagerId != currentManagerId)
            {
                throw new ForbiddenException(ApiErrorMessages.Cafe.ManagerForbidden(cafeId));
            }

            return cafe;
        }

        private async Task LinkStaffToCafeAsync(Cafe cafe, User staffUser)
        {
            var cafeStaff = new CafeStaff
            {
                CafeId = cafe.Id,
                UserId = staffUser.Id,
                JoinedAt = DateTime.UtcNow
            };

            await _cafeRepository.AddCafeStaffAsync(cafeStaff);
            await _cafeRepository.SaveChangesAsync();
        }

        private async Task<double> ResolveMatchmakingRadiusKmAsync(double radiusKm)
        {
            if (Math.Abs(radiusKm - GeoLocationHelper.DefaultNearbyRadiusKm) > 0.001)
            {
                return radiusKm;
            }

            return await _systemConfigurationProvider.GetDoubleAsync(
                SystemConfigKeys.MatchmakingRadiusKm,
                GeoLocationHelper.DefaultNearbyRadiusKm);
        }

        private async Task<string> ResolveUsernameAsync(string username, Guid? excludedUserId)
        {
            var normalized = username.Trim();
            if (normalized.Length < 3)
            {
                throw new BadRequestException(ApiErrorMessages.Cafe.StaffUsernameTooShort);
            }

            if (await _cafeRepository.UsernameExistsAsync(normalized, excludedUserId))
            {
                throw new ConflictException(ApiErrorMessages.Cafe.StaffUsernameTaken);
            }

            return normalized;
        }

        private static void ApplyOptionalPassword(User user, string? password)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                return;
            }

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
        }

        private static CafeDto MapToDto(Cafe cafe)
        {
            return new()
            {
                Id = cafe.Id,
                Name = cafe.Name,
                Address = cafe.Address,
                Latitude = cafe.Latitude,
                Longitude = cafe.Longitude,
                PhoneNumber = cafe.PhoneNumber,
                Description = cafe.Description,
                CreatedAt = cafe.CreatedAt,
                TotalSeats = cafe.TotalSeats,
                BillingModel = CafePartnerStatusMapper.ToApiBillingModel(cafe.BillingModel),
                BasePrice = cafe.BasePrice,
                TieredBlockRate = cafe.TieredBlockRate,
                TieredBlockMinutes = cafe.TieredBlockMinutes,
                DepositPercentage = cafe.DepositPercentage,
                IsPricingLocked = cafe.IsPricingLocked,
                HasSePayConfigured = !string.IsNullOrWhiteSpace(cafe.SePayMerchantId)
                                  && !string.IsNullOrWhiteSpace(cafe.SePayApiKey)
                                  && !string.IsNullOrWhiteSpace(cafe.SePaySecretKey)
            };
        }

    // ====================================================================
    // ADMIN: FULL CRUD
    // ====================================================================

    public async Task<AdminCafeListResponseDto> GetAdminCafesAsync(
        int page, int pageSize, string? searchTerm, string? status, Guid? managerId, CancellationToken cancellationToken = default)
    {
        // Map query status string → enum + IsActive filter
        // status values: DATA_BLANK | ACTIVE | INACTIVE | BANNED
        bool? isActive = null;
        CafePartnerOperationalStatus? partnerStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalized = status.Trim();
            if (Enum.TryParse<CafePartnerOperationalStatus>(normalized, ignoreCase: true, out var parsed))
            {
                partnerStatus = parsed;
                // Derive IsActive for the IsActive filter (Banned/Inactive/DataBlank all imply !IsActive,
                // Active implies IsActive=true).
                isActive = parsed == CafePartnerOperationalStatus.Active;
            }
            else if (normalized.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                isActive = true;
            }
            else if (normalized.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                isActive = false;
            }
            // Unknown status → return empty (consistent với việc filter invalid input)
            else
            {
                return new AdminCafeListResponseDto
                {
                    Items = [],
                    TotalCount = 0,
                    PageNumber = page,
                    PageSize = pageSize,
                    TotalPages = 0,
                    HasPreviousPage = false,
                    HasNextPage = false
                };
            }
        }

        var (items, totalCount) = await _cafeRepository.GetAdminListAsync(
            page, pageSize, searchTerm, isActive, managerId);

        // Nếu filter theo PartnerOperationalStatus, lọc tiếp trong memory
        // (PostgreSQL enum filter có thể không ánh xạ 1-1 với string query)
        if (partnerStatus.HasValue)
        {
            items = items.Where(c => c.PartnerOperationalStatus == partnerStatus.Value).ToList();
            totalCount = items.Count;
        }

        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        return new AdminCafeListResponseDto
        {
            Items = items.Select(c => new AdminCafeListItemDto
            {
                Id = c.Id,
                Name = c.Name,
                Address = c.Address,
                PhoneNumber = c.PhoneNumber,
                TotalSeats = c.TotalSeats,
                IsActive = c.IsActive,
                DepositPercentage = c.DepositPercentage,
                HasSePayConfigured = !string.IsNullOrWhiteSpace(c.SePayMerchantId),
                ManagerId = c.ManagerId,
                ManagerName = !string.IsNullOrWhiteSpace(c.Manager?.Username)
                    ? c.Manager!.Username
                    : !string.IsNullOrWhiteSpace(c.Manager?.Email)
                        ? c.Manager!.Email
                        : !string.IsNullOrWhiteSpace(c.Manager?.PhoneNumber)
                            ? c.Manager!.PhoneNumber
                            : "N/A",
                NumberOfTables = c.NumberOfTables,
                NumberOfGamesOwned = c.NumberOfGamesOwned,
                StaffCount = c.StaffMembers?.Count ?? 0,
                CreatedAt = c.CreatedAt,
                // PartnerOperationalStatus.ToString(): DataBlank/Active/Inactive/Banned
                // Nếu null (DB legacy) → fallback theo IsActive để UI không hiển thị rỗng.
                Status = c.PartnerOperationalStatus?.ToString()
                    ?? (c.IsActive ? "ACTIVE" : "INACTIVE")
            }).ToList(),
            TotalCount = totalCount,
            PageNumber = page,
            PageSize = pageSize,
            TotalPages = totalPages,
            HasPreviousPage = page > 1,
            HasNextPage = page < totalPages
        };
    }

    public async Task<AdminCafeDetailDto?> GetAdminCafeDetailAsync(Guid cafeId)
    {
        var c = await _cafeRepository.GetByIdWithManagerAsync(cafeId);
        if (c == null)
        {
            return null;
        }

        // Schedule Overrides: admin cần xem/tạo override giờ mở cửa cho ngày lễ
        var utcNow = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(utcNow);
        var scheduleOverrides = await _cafeRepository.GetScheduleOverridesAsync(
            cafeId,
            fromDate: today,
            toDate: today.AddDays(365));

        return new AdminCafeDetailDto
        {
            Id = c.Id,
            Name = c.Name,
            Address = c.Address,
            Latitude = c.Latitude,
            Longitude = c.Longitude,
            PhoneNumber = c.PhoneNumber,
            Description = c.Description,
            ManagerId = c.ManagerId,

            // Manager contact (BR-Audit: cần biết ai chịu trách nhiệm quán)
            ManagerName = !string.IsNullOrWhiteSpace(c.Manager?.Username)
                ? c.Manager!.Username
                : c.Manager?.Email ?? c.Manager?.PhoneNumber ?? "N/A",
            ManagerEmail = c.Manager?.Email,

            // Operational Status
            PartnerOperationalStatus = c.PartnerOperationalStatus?.ToString() ?? string.Empty,
            PartnerOperationalStatusReason = c.PartnerOperationalStatusReason,
            PartnerOperationalStatusChangedAt = c.PartnerOperationalStatusChangedAt,

            // Schedule (TimeSpan? — TimeOnly? trên entity được lưu thành TimeSpan)
            WeekdayOpen = c.WeekdayOpen,
            WeekdayClose = c.WeekdayClose,
            WeekendOpen = c.WeekendOpen,
            WeekendClose = c.WeekendClose,

            // Profile
            NumberOfTables = c.NumberOfTables,
            NumberOfPrivateRooms = c.NumberOfPrivateRooms,
            TotalSeats = c.TotalSeats,
            NumberOfGamesOwned = c.NumberOfGamesOwned,
            PopularGamesList = c.PopularGamesList ?? string.Empty,
            HasGameMaster = c.HasGameMaster,

            // Billing
            BillingModel = c.BillingModel.ToString(),
            BasePrice = c.BasePrice,
            TieredBlockRate = c.TieredBlockRate,
            TieredBlockMinutes = c.TieredBlockMinutes,
            IsPricingLocked = c.IsPricingLocked,

            // Deposit
            DepositPercentage = c.DepositPercentage,
            DefaultHoldDurationMinutes = c.DefaultHoldDurationMinutes,
            RefundPolicy = c.RefundPolicy.ToString(),

            // SePay
            HasSePayConfigured = !string.IsNullOrWhiteSpace(c.SePayMerchantId)
                                 && !string.IsNullOrWhiteSpace(c.SePayApiKey)
                                 && !string.IsNullOrWhiteSpace(c.SePaySecretKey),

            // Schedule Overrides (admin cần xem/tạo override giờ mở cửa cho ngày lễ)
            ScheduleOverrides = scheduleOverrides.Select(o => new CafeScheduleOverrideDto
            {
                ApplyDate = o.ApplyDate,
                Reason = null,
                OpenTime = o.OpenTime,
                CloseTime = o.CloseTime,
                IsClosed = o.IsClosed
            }).ToList(),

            // Audit
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt,
            IsActive = c.IsActive
        };
    }

    public async Task<AdminCafeDetailDto> AdminCreateCafeAsync(AdminCreateCafeRequestDto request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new BadRequestException(ApiErrorMessages.Cafe.NameRequired);
        }

        if (string.IsNullOrWhiteSpace(request.Address))
        {
            throw new BadRequestException(ApiErrorMessages.Cafe.AddressRequired);
        }

        if (request.ManagerId == Guid.Empty)
        {
            throw new BadRequestException(ApiErrorMessages.Cafe.ManagerIdInvalid);
        }

        var manager = await _userProfileRepository.GetByIdWithProfileAsync(request.ManagerId);
        if (manager == null)
        {
            throw new NotFoundException(ApiErrorMessages.Cafe.ManagerNotFound(request.ManagerId));
        }

        var cafe = new Cafe
        {
            Id = Guid.NewGuid(),
            ManagerId = request.ManagerId,
            Name = request.Name.Trim(),
            Address = request.Address.Trim(),
            PhoneNumber = request.PhoneNumber?.Trim(),
            Description = request.Description?.Trim(),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            TotalSeats = request.TotalSeats,
            BillingModel = request.BillingModel,
            BasePrice = request.BasePrice,
            TieredBlockRate = request.TieredBlockRate,
            TieredBlockMinutes = request.TieredBlockMinutes,
            DepositPercentage = request.DepositPercentage,
            IsActive = true,
            IsPricingLocked = false,
            SePayMerchantId = request.SePayMerchantId,
            SePayApiKey = request.SePayApiKey,
            SePaySecretKey = request.SePaySecretKey,
            SePayReturnUrl = request.SePayReturnUrl,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _cafeRepository.AddCafeAsync(cafe);

        var result = await GetAdminCafeDetailAsync(cafe.Id);
        if (result == null)
            throw new InternalServerErrorException(ApiErrorMessages.System.CafeRetrieveFailed(cafe.Id));
        return result;
    }

    public async Task<AdminCafeDetailDto> AdminUpdateCafeAsync(Guid cafeId, AdminUpdateCafeRequestDto request, CancellationToken cancellationToken = default)
    {
        var cafe = await _cafeRepository.GetByIdAsync(cafeId)
            ?? throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            cafe.Name = request.Name.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.Address))
        {
            cafe.Address = request.Address.Trim();
        }

        if (request.PhoneNumber != null)
        {
            cafe.PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();
        }

        if (request.Description != null)
        {
            cafe.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        }

        if (request.Latitude.HasValue && request.Longitude.HasValue)
        {
            try
            {
                GeoLocationHelper.ApplyCoordinates(cafe, request.Latitude.Value, request.Longitude.Value);
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new BadRequestException(ApiErrorMessages.Cafe.CoordinatesInvalid);
            }
        }

        if (request.TotalSeats.HasValue)
        {
            cafe.TotalSeats = request.TotalSeats.Value;
        }

        if (request.BasePrice.HasValue)
        {
            cafe.BasePrice = request.BasePrice.Value;
        }

        if (request.DepositPercentage.HasValue)
        {
            cafe.DepositPercentage = request.DepositPercentage.Value;
        }

        if (request.SePayMerchantId != null)
        {
            cafe.SePayMerchantId = string.IsNullOrWhiteSpace(request.SePayMerchantId) ? null : request.SePayMerchantId.Trim();
        }

        if (request.SePayApiKey != null)
        {
            cafe.SePayApiKey = string.IsNullOrWhiteSpace(request.SePayApiKey) ? null : request.SePayApiKey.Trim();
        }

        if (request.SePaySecretKey != null)
        {
            cafe.SePaySecretKey = string.IsNullOrWhiteSpace(request.SePaySecretKey) ? null : request.SePaySecretKey.Trim();
        }

        if (request.SePayReturnUrl != null)
        {
            cafe.SePayReturnUrl = string.IsNullOrWhiteSpace(request.SePayReturnUrl) ? null : request.SePayReturnUrl.Trim();
        }

        cafe.UpdatedAt = DateTime.UtcNow;
        await _cafeRepository.SaveChangesAsync();

        var result = await GetAdminCafeDetailAsync(cafeId);
        if (result == null)
            throw new InternalServerErrorException(ApiErrorMessages.System.CafeRetrieveFailed(cafeId));
        return result;
    }

    public async Task AdminDeleteCafeAsync(Guid cafeId, CancellationToken cancellationToken = default)
    {
        var cafe = await _cafeRepository.GetByIdAsync(cafeId)
            ?? throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));

        cafe.IsActive = false;
        cafe.UpdatedAt = DateTime.UtcNow;
        await _cafeRepository.SaveChangesAsync();
    }
}
}

