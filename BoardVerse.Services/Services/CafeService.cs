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

        public CafeService(
            ICafeRepository cafeRepository,
            IUserProfileRepository userProfileRepository,
            ISystemConfigurationProvider systemConfigurationProvider,
            IBookingRepository bookingRepository,
            ILobbyHubService hubService,
            IPushNotificationService pushNotificationService)
        {
            _cafeRepository = cafeRepository;
            _userProfileRepository = userProfileRepository;
            _systemConfigurationProvider = systemConfigurationProvider;
            _bookingRepository = bookingRepository;
            _hubService = hubService;
            _pushNotificationService = pushNotificationService;
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

        public async Task<CafeDto> UpdateCafeAsync(Guid cafeId, Guid managerId, UpdateCafeRequestDto dto)
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

        public async Task<IEnumerable<CafeDto>> GetManagerCafesAsync(Guid managerId)
        {
            var cafes = await _cafeRepository.GetCafesByManagerIdAsync(managerId);
            return cafes.Select(MapToDto);
        }

        public async Task AddStaffAsync(Guid cafeId, Guid currentManagerId, AddStaffRequestDto dto)
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

        public async Task PromoteUserToStaffAsync(Guid cafeId, Guid currentManagerId, PromoteStaffRequestDto dto)
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
            PaginationParams paginationParams)
        {
            await EnsureManagerOwnsCafeAsync(cafeId, currentManagerId);
            return await _cafeRepository.GetStaffPagedAsync(cafeId, paginationParams);
        }

        public async Task RemoveStaffAsync(Guid cafeId, Guid currentManagerId, Guid staffId)
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

        public async Task<IEnumerable<CafeDto>> GetMyWorkplacesAsync(Guid currentStaffId)
        {
            var cafes = await _cafeRepository.GetCafesByStaffIdAsync(currentStaffId);
            return cafes.Select(MapToDto);
        }

        public async Task<NearbyCafeSearchResultDto> GetNearbyCafesAsync(
            double latitude,
            double longitude,
            double radiusKm,
            Guid gameTemplateId,
            PaginationParams paginationParams)
        {
            if (gameTemplateId == Guid.Empty)
            {
                throw new BadRequestException(ApiErrorMessages.Cafe.GameTemplateIdRequiredForNearbySearch);
            }

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
                paginationParams);

            var cafes = result.Data.ToList();
            if (cafes.Count > 0)
            {
                await _cafeRepository.EnrichNearbyWithGameWaitAsync(cafes, gameTemplateId);
                result.Data = cafes;

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

            var alternativeSuggestions = await _cafeRepository.GetAlternativeGameSuggestionsAsync(
                latitude,
                longitude,
                radiusKm,
                gameTemplateId);

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
            Guid gameTemplateId,
            PaginationParams paginationParams)
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
                paginationParams);
        }

        public async Task<AdminCafeOperationalStatusResultDto> SetOperationalStatusByAdminAsync(
            Guid cafeId,
            AdminSetCafeOperationalStatusRequestDto request)
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
        int page, int pageSize, string? searchTerm, string? status, Guid? managerId)
    {
        bool? isActive = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            isActive = status.Equals("active", StringComparison.OrdinalIgnoreCase) ||
                       status.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        var (items, totalCount) = await _cafeRepository.GetAdminListAsync(
            page, pageSize, searchTerm, isActive, managerId);

        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        return new AdminCafeListResponseDto
        {
            Items = items.Select(c => new AdminCafeListItemDto
            {
                Id = c.Id,
                Name = c.Name,
                Address = c.Address,
                TotalSeats = c.TotalSeats,
                IsActive = c.IsActive,
                DepositPercentage = c.DepositPercentage,
                HasSePayConfigured = !string.IsNullOrWhiteSpace(c.SePayMerchantId),
                ManagerId = c.ManagerId,
                ManagerName = c.Manager?.Username ?? "N/A",
                NumberOfTables = c.NumberOfTables,
                NumberOfGamesOwned = c.NumberOfGamesOwned,
                StaffCount = c.StaffMembers?.Count ?? 0,
                CreatedAt = c.CreatedAt
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
        var c = await _cafeRepository.GetByIdAsync(cafeId);
        if (c == null)
        {
            return null;
        }

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
            TotalSeats = c.TotalSeats,
            IsActive = c.IsActive,
            BillingModel = c.BillingModel.ToString(),
            BasePrice = c.BasePrice,
            TieredBlockRate = c.TieredBlockRate,
            TieredBlockMinutes = c.TieredBlockMinutes,
            DepositPercentage = c.DepositPercentage,
            DefaultHoldDurationMinutes = c.DefaultHoldDurationMinutes,
            RefundPolicy = c.RefundPolicy.ToString(),
            IsPricingLocked = c.IsPricingLocked,
            HasSePayConfigured = !string.IsNullOrWhiteSpace(c.SePayMerchantId),
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt
        };
    }

    public async Task<AdminCafeDetailDto> AdminCreateCafeAsync(AdminCreateCafeRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new BadRequestException("Tên cafe không được để trống.");
        }

        if (string.IsNullOrWhiteSpace(request.Address))
        {
            throw new BadRequestException("Địa chỉ cafe không được để trống.");
        }

        if (request.ManagerId == Guid.Empty)
        {
            throw new BadRequestException("ManagerId không hợp lệ.");
        }

        var manager = await _userProfileRepository.GetByIdWithProfileAsync(request.ManagerId);
        if (manager == null)
        {
            throw new NotFoundException($"Manager {request.ManagerId} không tìm thấy.");
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
            throw new Exception("Failed to retrieve created cafe.");
        return result;
    }

    public async Task<AdminCafeDetailDto> AdminUpdateCafeAsync(Guid cafeId, AdminUpdateCafeRequestDto request)
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
                throw new BadRequestException("Tọa độ không hợp lệ.");
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
            throw new Exception("Failed to retrieve updated cafe.");
        return result;
    }

    public async Task AdminDeleteCafeAsync(Guid cafeId)
    {
        var cafe = await _cafeRepository.GetByIdAsync(cafeId)
            ?? throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));

        cafe.IsActive = false;
        cafe.UpdatedAt = DateTime.UtcNow;
        await _cafeRepository.SaveChangesAsync();
    }
}
}

