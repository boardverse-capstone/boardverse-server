using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BoardVerse.Core.Common;
using BoardVerse.Core.Constants;
using BoardVerse.Core.DTOs.CafePartner;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Messages;
using BoardVerse.Core.Helpers;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.IServices;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services
{
    public class CafePartnerApplicationService : ICafePartnerApplicationService
    {
        private static readonly string[] LicenseExtensions = [".jpg", ".jpeg", ".png", ".pdf"];
        private static readonly string[] SpaceImageExtensions = [".jpg", ".jpeg", ".png"];

        private readonly ICafePartnerApplicationRepository _applicationRepository;
        private readonly IAuthRepository _authRepository;
        private readonly ICafeRepository _cafeRepository;
        private readonly IEmailService _emailService;
        private readonly ILogger<CafePartnerApplicationService> _logger;

        public CafePartnerApplicationService(
            ICafePartnerApplicationRepository applicationRepository,
            IAuthRepository authRepository,
            ICafeRepository cafeRepository,
            IEmailService emailService,
            ILogger<CafePartnerApplicationService> logger)
        {
            _applicationRepository = applicationRepository;
            _authRepository = authRepository;
            _cafeRepository = cafeRepository;
            _emailService = emailService;
            _logger = logger;
        }

        public async Task<CafePartnerApplicationResponseDto> SubmitAsync(
            SubmitCafePartnerApplicationRequestDto request,
            Guid? submittedByUserId = null)
        {
            ValidatePhase1Request(request);

            var email = NormalizeEmail(request.RepresentativeEmail);
            if (await _applicationRepository.HasOpenApplicationByEmailAsync(email))
            {
                throw new OpenCafePartnerApplicationExistsException(
                    ApiErrorMessages.CafePartner.OpenApplicationExists);
            }

            var existingUser = await _authRepository.GetByEmailAsync(email);
            if (existingUser != null && existingUser.Role is UserRole.Admin or UserRole.Manager or UserRole.CafeStaff)
            {
                throw new CafePartnerEmailNotEligibleException(
                    ApiErrorMessages.CafePartner.EmailNotEligibleForApplication);
            }

            if (await _applicationRepository.HasSevereDuplicateAsync(request.BusinessLicense, request.Address.Trim()))
            {
                throw new SevereDataDuplicationException();
            }

            var resolvedSubmitterId = await ResolveSubmittedByUserIdAsync(submittedByUserId, email);
            var now = DateTime.UtcNow;

            var application = new CafePartnerApplication
            {
                Id = Guid.NewGuid(),
                CafeName = request.CafeName.Trim(),
                Address = request.Address.Trim(),
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                PhoneNumber = NormalizePhoneDigits(request.PhoneNumber),
                RepresentativeEmail = email,
                BusinessLicense = request.BusinessLicense.Trim().ToUpperInvariant(),
                BusinessLicenseImageUrl = request.BusinessLicenseImageUrl.Trim(),
                Status = CafePartnerApplicationStatus.PendingApproval,
                SubmittedByUserId = resolvedSubmitterId,
                SubmittedAt = now,
                UpdatedAt = now
            };

            await _applicationRepository.AddAsync(application);
            await _applicationRepository.SaveChangesAsync();

            await SendEmailSafeAsync(
                email,
                ApiEmailMessages.CafePartner.ApplicationReceivedSubject,
                ApiEmailMessages.CafePartner.ApplicationReceivedBody(application.CafeName, application.Id));

            return MapApplicationDto(await GetApplicationOrThrowAsync(application.Id));
        }

        public async Task<CafePartnerApplicationResponseDto> GetByIdAsync(Guid id) =>
            MapApplicationDto(await GetApplicationOrThrowAsync(id));

        public async Task<PaginatedResponse<CafePartnerApplicationResponseDto>> GetAllForAdminAsync(
            AdminCafePartnerApplicationQueryDto query)
        {
            var result = await _applicationRepository.GetPagedAsync(query);
            return new PaginatedResponse<CafePartnerApplicationResponseDto>
            {
                Data = result.Data.Select(MapApplicationDto).ToList(),
                Meta = result.Meta
            };
        }

        public async Task<OnboardPartnerResultDto> ApproveAsync(Guid id, Guid adminId)
        {
            var application = await GetApplicationOrThrowAsync(id);
            if (application.Status != CafePartnerApplicationStatus.PendingApproval)
            {
                throw new CafePartnerApplicationInvalidStatusException(
                    ApiErrorMessages.CafePartner.OnlyPendingApprovalCanBeApproved);
            }

            if (string.IsNullOrWhiteSpace(application.BusinessLicenseImageUrl))
            {
                throw new CafePartnerApplicationInvalidStatusException(
                    ApiErrorMessages.CafePartner.BusinessLicenseImageRequired);
            }

            var email = application.RepresentativeEmail;
            var existingUser = await _authRepository.GetByEmailAsync(email);
            string? temporaryPassword = null;
            var keptExistingPassword = false;

            User managerUser;
            if (existingUser == null)
            {
                temporaryPassword = GenerateTemporaryPassword();
                managerUser = new User
                {
                    Id = Guid.NewGuid(),
                    Username = email,
                    Email = email,
                    PhoneNumber = application.PhoneNumber,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(temporaryPassword),
                    Role = UserRole.Manager,
                    Provider = "Local",
                    IsEmailVerified = true,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await _authRepository.AddUserAsync(managerUser);
            }
            else
            {
                if (existingUser.Role is UserRole.Admin or UserRole.CafeStaff)
                {
                    throw new CafePartnerEmailNotEligibleException(
                        ApiErrorMessages.CafePartner.EmailUsedByRoleAccount(existingUser.Role.ToString()));
                }

                if (existingUser.Role == UserRole.Manager)
                {
                    var existingCafe = (await _cafeRepository.GetCafesByManagerIdAsync(existingUser.Id)).FirstOrDefault();
                    if (existingCafe?.PartnerOperationalStatus != null)
                    {
                        throw new CafePartnerEmailNotEligibleException(
                            ApiErrorMessages.CafePartner.EmailAlreadyManagesPartnerCafe);
                    }
                }

                existingUser.Role = UserRole.Manager;
                existingUser.Username = email;
                existingUser.PhoneNumber = application.PhoneNumber;
                existingUser.IsEmailVerified = true;
                existingUser.IsActive = true;
                existingUser.UpdatedAt = DateTime.UtcNow;

                if (RequiresTemporaryPassword(existingUser))
                {
                    temporaryPassword = GenerateTemporaryPassword();
                    existingUser.PasswordHash = BCrypt.Net.BCrypt.HashPassword(temporaryPassword);
                }
                else
                {
                    keptExistingPassword = true;
                }

                managerUser = existingUser;
            }

            var cafeId = Guid.NewGuid();
            var cafe = new Cafe
            {
                Id = cafeId,
                Name = application.CafeName,
                Address = application.Address,
                PhoneNumber = application.PhoneNumber,
                Description = null,
                ManagerId = managerUser.Id,
                CreatedAt = DateTime.UtcNow,
                IsActive = false,
                PartnerOperationalStatus = CafePartnerOperationalStatus.DataBlank,
                BillingModel = CafePartnerBillingModel.ByHour,
            };

            if (application.Latitude.HasValue && application.Longitude.HasValue)
            {
                GeoLocationHelper.ApplyCoordinates(cafe, application.Latitude.Value, application.Longitude.Value);
            }

            await _applicationRepository.AddCafeAsync(cafe);

            application.Status = CafePartnerApplicationStatus.Approved;
            application.ApprovedAt = DateTime.UtcNow;
            application.ReviewedByAdminId = adminId;
            application.ReviewedAt = DateTime.UtcNow;
            application.CreatedManagerUserId = managerUser.Id;
            application.CreatedCafeId = cafeId;
            application.UpdatedAt = DateTime.UtcNow;

            await _applicationRepository.SaveChangesAsync();

            await SendEmailSafeAsync(
                email,
                ApiEmailMessages.CafePartner.ManagerAccountCreatedSubject,
                ApiEmailMessages.CafePartner.OnboardingApprovedBody(email, temporaryPassword, keptExistingPassword));

            return new OnboardPartnerResultDto
            {
                Application = MapApplicationDto(await GetApplicationOrThrowAsync(id)),
                ManagerUserId = managerUser.Id,
                ManagerEmail = email,
                CafeId = cafeId,
                TemporaryPassword = temporaryPassword
            };
        }

        public async Task<CafePartnerApplicationResponseDto> RejectAsync(
            Guid id,
            Guid adminId,
            RejectCafePartnerApplicationRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.Reason))
            {
                throw new BadRequestException(ApiErrorMessages.CafePartner.RejectionReasonRequired);
            }

            var application = await GetApplicationOrThrowAsync(id);
            if (application.Status != CafePartnerApplicationStatus.PendingApproval)
            {
                throw new CafePartnerApplicationInvalidStatusException(
                    ApiErrorMessages.CafePartner.OnlyPendingApprovalCanBeRejected);
            }

            application.Status = CafePartnerApplicationStatus.Rejected;
            application.RejectionReason = request.Reason.Trim();
            application.ReviewedByAdminId = adminId;
            application.ReviewedAt = DateTime.UtcNow;
            application.UpdatedAt = DateTime.UtcNow;

            await _applicationRepository.SaveChangesAsync();

            await SendEmailSafeAsync(
                application.RepresentativeEmail,
                ApiEmailMessages.CafePartner.ApplicationRejectedSubject,
                ApiEmailMessages.CafePartner.ApplicationRejectedBody(application.RejectionReason!));

            return MapApplicationDto(await GetApplicationOrThrowAsync(id));
        }

        public async Task<ManagerCafeProfileResponseDto> GetMyPartnerProfileAsync(Guid managerUserId)
        {
            var cafe = await GetPartnerCafeForManagerOrThrowAsync(managerUserId);
            return MapManagerCafeProfile(cafe.PartnerApplication!, cafe);
        }

        public async Task<ManagerCafeProfileResponseDto> UpdateOperationalProfileAsync(
            Guid managerUserId,
            UpdateOperationalProfileRequestDto request)
        {
            ValidatePhase2Request(request);

            var cafe = await GetPartnerCafeForManagerOrThrowAsync(managerUserId);
            EnsureOperationalStateAllowsEdit(cafe);

            var existingTableNames = DeserializeStringList(cafe.TableLayoutJson);
            var tableNames = CafePartnerTableLayoutHelper.ResolveTableNames(
                request.NumberOfTables,
                request.TableNames,
                existingTableNames);
            var workingHours = ParseWorkingHours(request.WorkingHours);

            cafe.WeekdayOpen = workingHours.WeekdayOpen;
            cafe.WeekdayClose = workingHours.WeekdayClose;
            cafe.WeekendOpen = workingHours.WeekendOpen;
            cafe.WeekendClose = workingHours.WeekendClose;
            cafe.NumberOfTables = request.NumberOfTables;
            cafe.NumberOfPrivateRooms = request.NumberOfPrivateRooms;
            cafe.SpaceImageUrlsJson = JsonSerializer.Serialize(
                request.SpaceImageUrls.Select(u => u.Trim()).Where(u => !string.IsNullOrWhiteSpace(u)).ToList());
            cafe.NumberOfGamesOwned = request.NumberOfGamesOwned;
            cafe.PopularGamesList = request.PopularGamesList.Trim();
            cafe.HasGameMaster = request.HasGameMaster;
            cafe.BillingModel = request.BillingModel;
            cafe.TableLayoutJson = JsonSerializer.Serialize(tableNames);
            cafe.OperationalProfileUpdatedAt = DateTime.UtcNow;
            cafe.UpdatedAt = DateTime.UtcNow;

            await _cafeRepository.SyncCafeTablesAsync(cafe.Id, tableNames);
            await _cafeRepository.SaveChangesAsync();

            return MapManagerCafeProfile(cafe.PartnerApplication!, cafe);
        }

        public async Task<ManagerCafeProfileResponseDto> ActivateAsync(Guid managerUserId)
        {
            var cafe = await GetPartnerCafeForManagerOrThrowAsync(managerUserId);
            var application = cafe.PartnerApplication!;

            if (!CafePartnerOperationalStatusHelper.CanManagerActivate(cafe.PartnerOperationalStatus))
            {
                throw new CafePartnerApplicationInvalidStatusException(
                    cafe.PartnerOperationalStatus == CafePartnerOperationalStatus.Banned
                        ? ApiErrorMessages.CafePartner.CafeBannedByAdmin
                        : cafe.PartnerOperationalStatus == CafePartnerOperationalStatus.Inactive
                            ? ApiErrorMessages.CafePartner.UseReopenForInactiveCafes
                            : ApiErrorMessages.CafePartner.OnlyDataBlankCafesCanBeActivated);
            }

            return await SetCafeActiveAsync(cafe, application);
        }

        public async Task<ManagerCafeProfileResponseDto> ReopenAsync(Guid managerUserId)
        {
            var cafe = await GetPartnerCafeForManagerOrThrowAsync(managerUserId);
            var application = cafe.PartnerApplication!;

            if (!CafePartnerOperationalStatusHelper.CanManagerReopen(cafe.PartnerOperationalStatus))
            {
                throw new CafePartnerApplicationInvalidStatusException(
                    cafe.PartnerOperationalStatus == CafePartnerOperationalStatus.Banned
                        ? ApiErrorMessages.CafePartner.CafeBannedByAdmin
                        : ApiErrorMessages.CafePartner.OnlyInactiveCafesCanBeReopened);
            }

            return await SetCafeActiveAsync(cafe, application);
        }

        private async Task<ManagerCafeProfileResponseDto> SetCafeActiveAsync(
            Cafe cafe,
            CafePartnerApplication application)
        {
            var blockers = GetActivationBlockers(cafe);
            if (blockers.Count > 0)
            {
                throw new CafePartnerActivationRequirementsNotMetException(
                    ApiErrorMessages.CafePartner.ActivationRequirementsNotMet(blockers));
            }

            var tableNames = DeserializeStringList(cafe.TableLayoutJson);
            await _cafeRepository.SyncCafeTablesAsync(cafe.Id, tableNames);

            cafe.IsActive = true;
            cafe.PartnerOperationalStatus = CafePartnerOperationalStatus.Active;
            cafe.PartnerOperationalStatusReason = null;
            cafe.PartnerOperationalStatusChangedAt = DateTime.UtcNow;
            cafe.UpdatedAt = DateTime.UtcNow;

            await _cafeRepository.SaveChangesAsync();

            await SendEmailSafeAsync(
                application.RepresentativeEmail,
                ApiEmailMessages.CafePartner.CafeActivatedSubject,
                ApiEmailMessages.CafePartner.CafeActivatedBody(application.CafeName));

            return MapManagerCafeProfile(application, cafe);
        }

        public async Task<ManagerCafeProfileResponseDto> DeactivateAsync(Guid managerUserId)
        {
            var cafe = await GetPartnerCafeForManagerOrThrowAsync(managerUserId);

            if (!CafePartnerOperationalStatusHelper.CanManagerPause(cafe.PartnerOperationalStatus))
            {
                throw new CafePartnerApplicationInvalidStatusException(
                    cafe.PartnerOperationalStatus == CafePartnerOperationalStatus.Banned
                        ? ApiErrorMessages.CafePartner.CafeBannedByAdmin
                        : cafe.PartnerOperationalStatus == CafePartnerOperationalStatus.Inactive
                            ? ApiErrorMessages.CafePartner.CafePermanentlyClosed
                            : ApiErrorMessages.CafePartner.OnlyActiveCafesCanBePaused);
            }

            if (await _cafeRepository.HasActiveBookingsAsync(cafe.Id))
            {
                throw new CafePartnerApplicationInvalidStatusException(
                    ApiErrorMessages.CafePartner.CannotPauseWithActiveSessions);
            }

            cafe.IsActive = false;
            cafe.PartnerOperationalStatus = CafePartnerOperationalStatus.DataBlank;
            cafe.PartnerOperationalStatusReason = null;
            cafe.PartnerOperationalStatusChangedAt = DateTime.UtcNow;
            cafe.UpdatedAt = DateTime.UtcNow;

            await _cafeRepository.SaveChangesAsync();
            return MapManagerCafeProfile(cafe.PartnerApplication!, cafe);
        }

        public async Task<ManagerCafeProfileResponseDto> ClosePermanentlyAsync(Guid managerUserId)
        {
            var cafe = await GetPartnerCafeForManagerOrThrowAsync(managerUserId);

            if (!CafePartnerOperationalStatusHelper.CanManagerClosePermanently(cafe.PartnerOperationalStatus))
            {
                throw new CafePartnerApplicationInvalidStatusException(
                    cafe.PartnerOperationalStatus == CafePartnerOperationalStatus.Banned
                        ? ApiErrorMessages.CafePartner.CafeBannedByAdmin
                        : ApiErrorMessages.CafePartner.CafePermanentlyClosed);
            }

            if (await _cafeRepository.HasActiveBookingsAsync(cafe.Id))
            {
                throw new CafePartnerApplicationInvalidStatusException(
                    ApiErrorMessages.CafePartner.CannotCloseWithActiveBookings);
            }

            var utcNow = DateTime.UtcNow;
            cafe.IsActive = false;
            cafe.PartnerOperationalStatus = CafePartnerOperationalStatus.Inactive;
            cafe.PartnerOperationalStatusReason = ApiErrorMessages.CafePartner.ClosedByManagerReason;
            cafe.PartnerOperationalStatusChangedAt = utcNow;
            cafe.UpdatedAt = utcNow;

            await _cafeRepository.SaveChangesAsync();
            return MapManagerCafeProfile(cafe.PartnerApplication!, cafe);
        }

        private async Task<CafePartnerApplication> GetApplicationOrThrowAsync(Guid id) =>
            await _applicationRepository.GetByIdAsync(id)
            ?? throw new CafePartnerApplicationNotFoundException(ApiErrorMessages.CafePartner.ApplicationNotFound(id));

        private async Task<Cafe> GetPartnerCafeForManagerOrThrowAsync(Guid managerUserId) =>
            await _cafeRepository.GetPartnerCafeByManagerIdAsync(managerUserId)
            ?? throw new CafePartnerApplicationNotFoundException(ApiErrorMessages.CafePartner.ApplicationNotFoundForManager);

        private static void EnsureOperationalStateAllowsEdit(Cafe cafe)
        {
            if (cafe.PartnerOperationalStatus == CafePartnerOperationalStatus.Banned)
            {
                throw new CafePartnerApplicationInvalidStatusException(
                    ApiErrorMessages.CafePartner.CafeBannedByAdmin);
            }

            if (cafe.PartnerOperationalStatus == CafePartnerOperationalStatus.Active && cafe.IsActive)
            {
                throw new CafePartnerApplicationInvalidStatusException(
                    ApiErrorMessages.CafePartner.PauseBeforeEditingProfile);
            }
        }

        private static void ValidatePhase1Request(SubmitCafePartnerApplicationRequestDto request)
        {
            if (request.CafeName.Trim().Length is < 5 or > 100)
            {
                throw new BadRequestException(ApiErrorMessages.CafePartner.CafeNameLengthInvalid);
            }

            if (!IsValidVnPhoneNumber(request.PhoneNumber))
            {
                throw new BadRequestException(ApiErrorMessages.CafePartner.PhoneNumberInvalid);
            }

            if (!Regex.IsMatch(request.BusinessLicense.Trim(), @"^[a-zA-Z0-9\-]+$"))
            {
                throw new BadRequestException(ApiErrorMessages.CafePartner.BusinessLicenseAlphanumeric);
            }

            if (!HasAllowedExtension(request.BusinessLicenseImageUrl, LicenseExtensions))
            {
                throw new BadRequestException(ApiErrorMessages.CafePartner.BusinessLicenseImageFormatInvalid);
            }

            try
            {
                GeoLocationHelper.ValidateCoordinates(request.Latitude, request.Longitude);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new BadRequestException(ex.ParamName switch
                {
                    "latitude" => ApiErrorMessages.Profile.InvalidLatitudeForLocationUpdate,
                    "longitude" => ApiErrorMessages.Profile.InvalidLongitudeForLocationUpdate,
                    _ => ApiErrorMessages.Profile.InvalidLatitudeForLocationUpdate
                });
            }
        }

        private static void ValidatePhase2Request(UpdateOperationalProfileRequestDto request)
        {
            if (request.NumberOfTables <= 0)
            {
                throw new BadRequestException(ApiErrorMessages.CafePartner.TableCountMustBePositive);
            }

            if (request.NumberOfPrivateRooms < 0)
            {
                throw new BadRequestException(ApiErrorMessages.CafePartner.PrivateRoomCountCannotBeNegative);
            }

            if (request.NumberOfGamesOwned <= 0)
            {
                throw new BadRequestException(ApiErrorMessages.CafePartner.GamesOwnedMustBePositive);
            }

            if (request.SpaceImageUrls == null || request.SpaceImageUrls.Count < CafePartnerActivationRules.MinSpaceImages)
            {
                throw new BadRequestException(
                    ApiErrorMessages.CafePartner.MinSpaceImagesRequired(CafePartnerActivationRules.MinSpaceImages));
            }

            foreach (var url in request.SpaceImageUrls)
            {
                if (!HasAllowedExtension(url, SpaceImageExtensions))
                {
                    throw new BadRequestException(ApiErrorMessages.CafePartner.SpaceImagesFormatInvalid);
                }
            }

            ParseWorkingHours(request.WorkingHours);
        }

        private static List<string> GetActivationBlockers(Cafe cafe)
        {
            var blockers = new List<string>();

            if (cafe.NumberOfTables < CafePartnerActivationRules.MinPublicTables)
            {
                blockers.Add(ApiErrorMessages.CafePartner.MinPublicTablesRequired(CafePartnerActivationRules.MinPublicTables));
            }

            if (cafe.NumberOfGamesOwned < CafePartnerActivationRules.MinGamesOwned)
            {
                blockers.Add(ApiErrorMessages.CafePartner.MinGamesOwnedRequired(CafePartnerActivationRules.MinGamesOwned));
            }

            var spaceUrls = DeserializeStringList(cafe.SpaceImageUrlsJson);
            if (spaceUrls.Count < CafePartnerActivationRules.MinSpaceImages ||
                spaceUrls.Any(u => !HasAllowedExtension(u, SpaceImageExtensions)))
            {
                blockers.Add(ApiErrorMessages.CafePartner.MinSpaceImagesActivationRequired(CafePartnerActivationRules.MinSpaceImages));
            }

            var tableNames = DeserializeStringList(cafe.TableLayoutJson);
            if (tableNames.Count < cafe.NumberOfTables)
            {
                blockers.Add(ApiErrorMessages.CafePartner.TableLayoutRequired);
            }

            if (string.IsNullOrWhiteSpace(cafe.PopularGamesList))
            {
                blockers.Add(ApiErrorMessages.CafePartner.PopularGamesListRequired);
            }

            if (!cafe.Latitude.HasValue || !cafe.Longitude.HasValue)
            {
                blockers.Add(ApiErrorMessages.CafePartner.GpsLocationRequiredBeforeActivation);
            }

            if (!HasWorkingHoursConfigured(cafe))
            {
                blockers.Add(ApiErrorMessages.CafePartner.WorkingHoursRequiredBeforeActivation);
            }

            return blockers;
        }

        private static bool HasWorkingHoursConfigured(Cafe cafe) =>
            cafe.WeekdayOpen.HasValue
            && cafe.WeekdayClose.HasValue
            && cafe.WeekendOpen.HasValue
            && cafe.WeekendClose.HasValue;

        private static (TimeSpan WeekdayOpen, TimeSpan WeekdayClose, TimeSpan WeekendOpen, TimeSpan WeekendClose) ParseWorkingHours(
            WorkingHoursDto dto)
        {
            var weekdayOpen = ParseTime(dto.WeekdayStart, nameof(dto.WeekdayStart));
            var weekdayClose = ParseTime(dto.WeekdayEnd, nameof(dto.WeekdayEnd));
            var weekendOpen = ParseTime(dto.WeekendStart, nameof(dto.WeekendStart));
            var weekendClose = ParseTime(dto.WeekendEnd, nameof(dto.WeekendEnd));

            if (weekdayOpen >= weekdayClose)
            {
                throw new BadRequestException(ApiErrorMessages.CafePartner.WeekdayHoursInvalid);
            }

            if (weekendOpen >= weekendClose)
            {
                throw new BadRequestException(ApiErrorMessages.CafePartner.WeekendHoursInvalid);
            }

            return (weekdayOpen, weekdayClose, weekendOpen, weekendClose);
        }

        private static TimeSpan ParseTime(string value, string fieldName)
        {
            if (!TimeSpan.TryParseExact(value.Trim(), ["hh\\:mm", "h\\:mm"], CultureInfo.InvariantCulture, out var time))
            {
                throw new BadRequestException(ApiErrorMessages.CafePartner.TimeFormatInvalid(fieldName));
            }

            return time;
        }

        private async Task<Guid?> ResolveSubmittedByUserIdAsync(Guid? submittedByUserId, string contactEmail)
        {
            if (!submittedByUserId.HasValue)
            {
                return null;
            }

            var user = await _authRepository.GetByIdAsync(submittedByUserId.Value)
                ?? throw new BadRequestException(ApiErrorMessages.CafePartner.SubmitterNotFound);

            if (user.Role != UserRole.Player)
            {
                throw new BadRequestException(ApiErrorMessages.CafePartner.SubmitterMustBePlayer);
            }

            if (!string.Equals(NormalizeEmail(user.Email), contactEmail, StringComparison.OrdinalIgnoreCase))
            {
                throw new BadRequestException(ApiErrorMessages.CafePartner.RepresentativeEmailMustMatch);
            }

            return user.Id;
        }

        private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

        private static string NormalizePhoneDigits(string phone) =>
            new string(phone.Where(char.IsDigit).ToArray());

        private static bool IsValidVnPhoneNumber(string phoneNumber)
        {
            var digits = NormalizePhoneDigits(phoneNumber);
            return digits.Length is 10 or 11 && digits.StartsWith('0') && digits[1] is '3' or '5' or '7' or '8' or '9';
        }

        private static bool HasAllowedExtension(string url, IEnumerable<string> allowedExtensions)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            var path = url.Split('?', '#')[0];
            return allowedExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
        }

        private static List<string> DeserializeStringList(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }

        private static bool RequiresTemporaryPassword(User user) =>
            !string.Equals(user.Provider, "Local", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(user.PasswordHash);

        private static string GenerateTemporaryPassword()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$";
            var bytes = RandomNumberGenerator.GetBytes(12);
            var builder = new StringBuilder(12);
            foreach (var b in bytes)
            {
                builder.Append(chars[b % chars.Length]);
            }

            return builder.ToString();
        }

        private static WorkingHoursDto MapWorkingHours(Cafe? cafe)
        {
            if (cafe?.WeekdayOpen is not { } weekdayOpen
                || cafe.WeekdayClose is not { } weekdayClose
                || cafe.WeekendOpen is not { } weekendOpen
                || cafe.WeekendClose is not { } weekendClose)
            {
                return new WorkingHoursDto();
            }

            return new WorkingHoursDto
            {
                WeekdayStart = weekdayOpen.ToString(@"hh\:mm"),
                WeekdayEnd = weekdayClose.ToString(@"hh\:mm"),
                WeekendStart = weekendOpen.ToString(@"hh\:mm"),
                WeekendEnd = weekendClose.ToString(@"hh\:mm")
            };
        }

        private async Task SendEmailSafeAsync(string to, string subject, string body)
        {
            try
            {
                await _emailService.SendEmailAsync(to, subject, body);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send cafe partner email to {Email}. Subject: {Subject}", to, subject);
            }
        }

        private static CafePartnerApplicationResponseDto MapApplicationDto(CafePartnerApplication application)
        {
            var cafe = application.CreatedCafe;
            return new CafePartnerApplicationResponseDto
            {
                Id = application.Id,
                CafeName = application.CafeName,
                Address = application.Address,
                Latitude = application.Latitude,
                Longitude = application.Longitude,
                PhoneNumber = application.PhoneNumber,
                RepresentativeEmail = application.RepresentativeEmail,
                BusinessLicense = application.BusinessLicense,
                BusinessLicenseImageUrl = application.BusinessLicenseImageUrl,
                ApplicationStatus = CafePartnerStatusMapper.ToApiApplicationStatus(application.Status),
                RejectionReason = application.RejectionReason,
                CreatedCafeId = application.CreatedCafeId ?? cafe?.Id,
                OperationalStatus = cafe?.PartnerOperationalStatus is { } op
                    ? CafePartnerStatusMapper.ToApiOperationalStatus(op)
                    : null,
                SubmittedByUserId = application.SubmittedByUserId,
                SubmittedByUsername = application.SubmittedByUser?.Username,
                ReviewedByAdminId = application.ReviewedByAdminId,
                ReviewedByAdminUsername = application.ReviewedByAdmin?.Username,
                CreatedManagerUserId = application.CreatedManagerUserId,
                SubmittedAt = application.SubmittedAt,
                UpdatedAt = application.UpdatedAt,
                ReviewedAt = application.ReviewedAt,
                ApprovedAt = application.ApprovedAt
            };
        }

        private static ManagerCafeProfileResponseDto MapManagerCafeProfile(CafePartnerApplication application, Cafe cafe)
        {
            var spaceUrls = DeserializeStringList(cafe.SpaceImageUrlsJson);
            var tableNames = DeserializeStringList(cafe.TableLayoutJson);
            var numberOfTables = cafe.NumberOfTables;
            var blockers = application.Status == CafePartnerApplicationStatus.Approved
                ? GetActivationBlockers(cafe)
                : new List<string>();

            if (cafe.PartnerOperationalStatus == CafePartnerOperationalStatus.Banned)
            {
                blockers.Add(ApiErrorMessages.CafePartner.CafeBannedBlocker);
            }

            return new ManagerCafeProfileResponseDto
            {
                CafeId = cafe.Id,
                ApplicationId = application.Id,
                Name = cafe.Name,
                Address = cafe.Address,
                Latitude = cafe.Latitude,
                Longitude = cafe.Longitude,
                PhoneNumber = cafe.PhoneNumber ?? application.PhoneNumber,
                WorkingHours = MapWorkingHours(cafe),
                NumberOfTables = numberOfTables,
                NumberOfPrivateRooms = cafe.NumberOfPrivateRooms,
                SpaceImageUrls = spaceUrls,
                NumberOfGamesOwned = cafe.NumberOfGamesOwned,
                PopularGamesList = cafe.PopularGamesList,
                HasGameMaster = cafe.HasGameMaster,
                BillingModel = CafePartnerStatusMapper.ToApiBillingModel(cafe.BillingModel),
                TableNames = tableNames,
                ApplicationStatus = CafePartnerStatusMapper.ToApiApplicationStatus(application.Status),
                OperationalStatus = cafe.PartnerOperationalStatus is { } operational
                    ? CafePartnerStatusMapper.ToApiOperationalStatus(operational)
                    : null,
                OperationalStatusReason = cafe.PartnerOperationalStatusReason,
                IsTableLayoutConfigured = tableNames.Count >= numberOfTables && numberOfTables > 0,
                CanActivate = application.Status == CafePartnerApplicationStatus.Approved &&
                              CafePartnerOperationalStatusHelper.CanManagerActivate(cafe.PartnerOperationalStatus) &&
                              blockers.Count == 0,
                CanReopen = application.Status == CafePartnerApplicationStatus.Approved &&
                            CafePartnerOperationalStatusHelper.CanManagerReopen(cafe.PartnerOperationalStatus) &&
                            blockers.Count == 0,
                ActivationBlockers = blockers,
                ApprovedAt = application.ApprovedAt,
                OperationalProfileUpdatedAt = cafe.OperationalProfileUpdatedAt
            };
        }
    }
}
