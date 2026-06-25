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
            var workingHours = ParseWorkingHours(request.WorkingHours);
            var now = DateTime.UtcNow;

            var application = new CafePartnerApplication
            {
                Id = Guid.NewGuid(),
                CafeName = request.CafeName.Trim(),
                Address = request.Address.Trim(),
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                Hotline = NormalizePhoneDigits(request.Hotline),
                RepresentativeEmail = email,
                BusinessLicense = request.BusinessLicense.Trim().ToUpperInvariant(),
                BusinessLicenseImageUrl = request.BusinessLicenseImageUrl.Trim(),
                WeekdayOpen = workingHours.WeekdayOpen,
                WeekdayClose = workingHours.WeekdayClose,
                WeekendOpen = workingHours.WeekendOpen,
                WeekendClose = workingHours.WeekendClose,
                BillingModel = CafePartnerBillingModel.ByHour,
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

            return MapToDto(await GetApplicationOrThrowAsync(application.Id));
        }

        public async Task<CafePartnerApplicationResponseDto> GetByIdAsync(Guid id) =>
            MapToDto(await GetApplicationOrThrowAsync(id));

        public async Task<PaginatedResponse<CafePartnerApplicationResponseDto>> GetAllForAdminAsync(
            AdminCafePartnerApplicationQueryDto query)
        {
            var result = await _applicationRepository.GetPagedAsync(query);
            return new PaginatedResponse<CafePartnerApplicationResponseDto>
            {
                Data = result.Data.Select(MapToDto).ToList(),
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
                    PhoneNumber = application.Hotline,
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
                existingUser.PhoneNumber = application.Hotline;
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
                PhoneNumber = application.Hotline,
                Description = null,
                ManagerId = managerUser.Id,
                CreatedAt = DateTime.UtcNow,
                IsActive = false,
                PartnerOperationalStatus = CafePartnerOperationalStatus.DataBlank,
                WeekdayOpen = application.WeekdayOpen,
                WeekdayClose = application.WeekdayClose,
                WeekendOpen = application.WeekendOpen,
                WeekendClose = application.WeekendClose
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
                Application = MapToDto(await GetApplicationOrThrowAsync(id)),
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

            return MapToDto(await GetApplicationOrThrowAsync(id));
        }

        public async Task<CafePartnerApplicationResponseDto> GetMyPartnerProfileAsync(Guid managerUserId)
        {
            var application = await GetApprovedApplicationForManagerOrThrowAsync(managerUserId);
            return MapToDto(application);
        }

        public async Task<CafePartnerApplicationResponseDto> UpdateOperationalProfileAsync(
            Guid managerUserId,
            UpdateOperationalProfileRequestDto request)
        {
            ValidatePhase2Request(request);

            var application = await GetApprovedApplicationForManagerOrThrowAsync(managerUserId);
            EnsureOperationalStateAllowsEdit(application);

            application.NumberOfTables = request.NumberOfTables;
            application.NumberOfPrivateRooms = request.NumberOfPrivateRooms;
            application.SpaceImageUrlsJson = JsonSerializer.Serialize(
                request.SpaceImageUrls.Select(u => u.Trim()).Where(u => !string.IsNullOrWhiteSpace(u)).ToList());
            application.NumberOfGamesOwned = request.NumberOfGamesOwned;
            application.PopularGamesList = request.PopularGamesList.Trim();
            application.HasGameMaster = request.HasGameMaster;
            application.BillingModel = request.BillingModel;
            var existingTableNames = DeserializeStringList(application.TableLayoutJson);
            application.TableLayoutJson = JsonSerializer.Serialize(
                CafePartnerTableLayoutHelper.ResolveTableNames(
                    request.NumberOfTables,
                    request.TableNames,
                    existingTableNames));
            application.OperationalProfileUpdatedAt = DateTime.UtcNow;
            application.UpdatedAt = DateTime.UtcNow;

            if (application.CreatedCafe != null)
            {
                application.CreatedCafe.Description = application.PopularGamesList;
                application.CreatedCafe.UpdatedAt = DateTime.UtcNow;

                var tableNames = DeserializeStringList(application.TableLayoutJson);
                await _applicationRepository.SyncCafeTablesAsync(application.CreatedCafe.Id, tableNames);
            }

            await _applicationRepository.SaveChangesAsync();
            return MapToDto(await GetApprovedApplicationForManagerOrThrowAsync(managerUserId));
        }

        public async Task<CafePartnerApplicationResponseDto> ActivateAsync(Guid managerUserId)
        {
            var application = await GetApprovedApplicationForManagerOrThrowAsync(managerUserId);
            var cafe = application.CreatedCafe
                ?? throw new CafePartnerApplicationInvalidStatusException(ApiErrorMessages.CafePartner.LinkedCafeMissing);

            if (!CafePartnerOperationalStatusHelper.CanManagerActivate(cafe.PartnerOperationalStatus))
            {
                throw new CafePartnerApplicationInvalidStatusException(
                    cafe.PartnerOperationalStatus == CafePartnerOperationalStatus.Banned
                        ? ApiErrorMessages.CafePartner.CafeBannedByAdmin
                        : cafe.PartnerOperationalStatus == CafePartnerOperationalStatus.Inactive
                            ? ApiErrorMessages.CafePartner.CafePermanentlyClosed
                            : ApiErrorMessages.CafePartner.OnlyDataBlankCafesCanBeActivated);
            }

            var blockers = GetActivationBlockers(application);
            if (blockers.Count > 0)
            {
                throw new CafePartnerActivationRequirementsNotMetException(
                    ApiErrorMessages.CafePartner.ActivationRequirementsNotMet(blockers));
            }

            var tableNames = DeserializeStringList(application.TableLayoutJson);
            await _applicationRepository.SyncCafeTablesAsync(cafe.Id, tableNames);

            cafe.IsActive = true;
            cafe.PartnerOperationalStatus = CafePartnerOperationalStatus.Active;
            cafe.PartnerOperationalStatusReason = null;
            cafe.PartnerOperationalStatusChangedAt = DateTime.UtcNow;
            cafe.UpdatedAt = DateTime.UtcNow;
            application.UpdatedAt = DateTime.UtcNow;

            await _applicationRepository.SaveChangesAsync();

            await SendEmailSafeAsync(
                application.RepresentativeEmail,
                ApiEmailMessages.CafePartner.CafeActivatedSubject,
                ApiEmailMessages.CafePartner.CafeActivatedBody(application.CafeName));

            return MapToDto(await GetApprovedApplicationForManagerOrThrowAsync(managerUserId));
        }

        public async Task<CafePartnerApplicationResponseDto> DeactivateAsync(Guid managerUserId)
        {
            var application = await GetApprovedApplicationForManagerOrThrowAsync(managerUserId);
            var cafe = application.CreatedCafe
                ?? throw new CafePartnerApplicationInvalidStatusException(ApiErrorMessages.CafePartner.LinkedCafeMissing);

            if (!CafePartnerOperationalStatusHelper.CanManagerPause(cafe.PartnerOperationalStatus))
            {
                throw new CafePartnerApplicationInvalidStatusException(
                    cafe.PartnerOperationalStatus == CafePartnerOperationalStatus.Banned
                        ? ApiErrorMessages.CafePartner.CafeBannedByAdmin
                        : cafe.PartnerOperationalStatus == CafePartnerOperationalStatus.Inactive
                            ? ApiErrorMessages.CafePartner.CafePermanentlyClosed
                            : ApiErrorMessages.CafePartner.OnlyActiveCafesCanBePaused);
            }

            if (await _applicationRepository.HasActiveBookingsAsync(cafe.Id))
            {
                throw new CafePartnerApplicationInvalidStatusException(
                    ApiErrorMessages.CafePartner.CannotPauseWithActiveSessions);
            }

            cafe.IsActive = false;
            cafe.PartnerOperationalStatus = CafePartnerOperationalStatus.DataBlank;
            cafe.PartnerOperationalStatusReason = null;
            cafe.PartnerOperationalStatusChangedAt = DateTime.UtcNow;
            cafe.UpdatedAt = DateTime.UtcNow;
            application.UpdatedAt = DateTime.UtcNow;

            await _applicationRepository.SaveChangesAsync();
            return MapToDto(await GetApprovedApplicationForManagerOrThrowAsync(managerUserId));
        }

        public async Task<CafePartnerApplicationResponseDto> ClosePermanentlyAsync(Guid managerUserId)
        {
            var application = await GetApprovedApplicationForManagerOrThrowAsync(managerUserId);
            var cafe = application.CreatedCafe
                ?? throw new CafePartnerApplicationInvalidStatusException(ApiErrorMessages.CafePartner.LinkedCafeMissing);

            if (!CafePartnerOperationalStatusHelper.CanManagerClosePermanently(cafe.PartnerOperationalStatus))
            {
                throw new CafePartnerApplicationInvalidStatusException(
                    cafe.PartnerOperationalStatus == CafePartnerOperationalStatus.Banned
                        ? ApiErrorMessages.CafePartner.CafeBannedByAdmin
                        : ApiErrorMessages.CafePartner.CafePermanentlyClosed);
            }

            if (await _applicationRepository.HasActiveBookingsAsync(cafe.Id))
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
            application.UpdatedAt = utcNow;

            await _applicationRepository.SaveChangesAsync();
            return MapToDto(await GetApprovedApplicationForManagerOrThrowAsync(managerUserId));
        }

        private async Task<CafePartnerApplication> GetApplicationOrThrowAsync(Guid id) =>
            await _applicationRepository.GetByIdAsync(id)
            ?? throw new CafePartnerApplicationNotFoundException(ApiErrorMessages.CafePartner.ApplicationNotFound(id));

        private async Task<CafePartnerApplication> GetApprovedApplicationForManagerOrThrowAsync(Guid managerUserId) =>
            await _applicationRepository.GetApprovedByManagerUserIdAsync(managerUserId)
            ?? throw new CafePartnerApplicationNotFoundException(ApiErrorMessages.CafePartner.ApplicationNotFoundForManager);

        private static void EnsureOperationalStateAllowsEdit(CafePartnerApplication application)
        {
            var cafe = application.CreatedCafe;
            if (cafe == null)
                return;

            if (CafePartnerOperationalStatusHelper.IsTerminal(cafe.PartnerOperationalStatus))
            {
                throw new CafePartnerApplicationInvalidStatusException(
                    cafe.PartnerOperationalStatus == CafePartnerOperationalStatus.Banned
                        ? ApiErrorMessages.CafePartner.CafeBannedByAdmin
                        : ApiErrorMessages.CafePartner.CafePermanentlyClosed);
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

            if (!IsValidVnHotline(request.Hotline))
            {
                throw new BadRequestException(ApiErrorMessages.CafePartner.HotlineInvalid);
            }

            if (!Regex.IsMatch(request.BusinessLicense.Trim(), @"^[a-zA-Z0-9\-]+$"))
            {
                throw new BadRequestException(ApiErrorMessages.CafePartner.BusinessLicenseAlphanumeric);
            }

            if (!HasAllowedExtension(request.BusinessLicenseImageUrl, LicenseExtensions))
            {
                throw new BadRequestException(ApiErrorMessages.CafePartner.BusinessLicenseImageFormatInvalid);
            }

            ParseWorkingHours(request.WorkingHours);

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
        }

        private static List<string> GetActivationBlockers(CafePartnerApplication application)
        {
            var blockers = new List<string>();

            if (application.NumberOfTables < CafePartnerActivationRules.MinPublicTables)
            {
                blockers.Add(ApiErrorMessages.CafePartner.MinPublicTablesRequired(CafePartnerActivationRules.MinPublicTables));
            }

            if (application.NumberOfGamesOwned < CafePartnerActivationRules.MinGamesOwned)
            {
                blockers.Add(ApiErrorMessages.CafePartner.MinGamesOwnedRequired(CafePartnerActivationRules.MinGamesOwned));
            }

            var spaceUrls = DeserializeStringList(application.SpaceImageUrlsJson);
            if (spaceUrls.Count < CafePartnerActivationRules.MinSpaceImages ||
                spaceUrls.Any(u => !HasAllowedExtension(u, SpaceImageExtensions)))
            {
                blockers.Add(ApiErrorMessages.CafePartner.MinSpaceImagesActivationRequired(CafePartnerActivationRules.MinSpaceImages));
            }

            var tableNames = DeserializeStringList(application.TableLayoutJson);
            if (tableNames.Count < application.NumberOfTables)
            {
                blockers.Add(ApiErrorMessages.CafePartner.TableLayoutRequired);
            }

            if (string.IsNullOrWhiteSpace(application.PopularGamesList))
            {
                blockers.Add(ApiErrorMessages.CafePartner.PopularGamesListRequired);
            }

            if (!application.Latitude.HasValue || !application.Longitude.HasValue)
            {
                blockers.Add(ApiErrorMessages.CafePartner.GpsLocationRequiredBeforeActivation);
            }

            return blockers;
        }

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

        private static bool IsValidVnHotline(string hotline)
        {
            var digits = NormalizePhoneDigits(hotline);
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

        private CafePartnerApplicationResponseDto MapToDto(CafePartnerApplication application)
        {
            var spaceUrls = DeserializeStringList(application.SpaceImageUrlsJson);
            var tableNames = DeserializeStringList(application.TableLayoutJson);
            var blockers = application.Status == CafePartnerApplicationStatus.Approved
                ? GetActivationBlockers(application)
                : new List<string>();

            if (application.CreatedCafe?.PartnerOperationalStatus == CafePartnerOperationalStatus.Inactive)
            {
                blockers.Add(ApiErrorMessages.CafePartner.CafePermanentlyClosedBlocker);
            }
            else if (application.CreatedCafe?.PartnerOperationalStatus == CafePartnerOperationalStatus.Banned)
            {
                blockers.Add(ApiErrorMessages.CafePartner.CafeBannedBlocker);
            }

            return new CafePartnerApplicationResponseDto
            {
                Id = application.Id,
                CafeName = application.CafeName,
                Address = application.Address,
                Latitude = application.Latitude,
                Longitude = application.Longitude,
                Hotline = application.Hotline,
                RepresentativeEmail = application.RepresentativeEmail,
                WorkingHours = new WorkingHoursDto
                {
                    WeekdayStart = application.WeekdayOpen.ToString(@"hh\:mm"),
                    WeekdayEnd = application.WeekdayClose.ToString(@"hh\:mm"),
                    WeekendStart = application.WeekendOpen.ToString(@"hh\:mm"),
                    WeekendEnd = application.WeekendClose.ToString(@"hh\:mm")
                },
                BusinessLicense = application.BusinessLicense,
                BusinessLicenseImageUrl = application.BusinessLicenseImageUrl,
                NumberOfTables = application.NumberOfTables,
                NumberOfPrivateRooms = application.NumberOfPrivateRooms,
                SpaceImageUrls = spaceUrls,
                NumberOfGamesOwned = application.NumberOfGamesOwned,
                PopularGamesList = application.PopularGamesList,
                HasGameMaster = application.HasGameMaster,
                BillingModel = CafePartnerStatusMapper.ToApiBillingModel(application.BillingModel),
                TableNames = tableNames,
                ApplicationStatus = CafePartnerStatusMapper.ToApiApplicationStatus(application.Status),
                OperationalStatus = application.CreatedCafe?.PartnerOperationalStatus is { } op
                    ? CafePartnerStatusMapper.ToApiOperationalStatus(op)
                    : null,
                OperationalStatusReason = application.CreatedCafe?.PartnerOperationalStatusReason,
                RejectionReason = application.RejectionReason,
                IsTableLayoutConfigured = tableNames.Count >= application.NumberOfTables && application.NumberOfTables > 0,
                CanActivate = application.Status == CafePartnerApplicationStatus.Approved &&
                              CafePartnerOperationalStatusHelper.CanManagerActivate(application.CreatedCafe?.PartnerOperationalStatus) &&
                              blockers.Count == 0,
                ActivationBlockers = blockers,
                SubmittedByUserId = application.SubmittedByUserId,
                SubmittedByUsername = application.SubmittedByUser?.Username,
                ReviewedByAdminId = application.ReviewedByAdminId,
                ReviewedByAdminUsername = application.ReviewedByAdmin?.Username,
                CreatedManagerUserId = application.CreatedManagerUserId,
                CreatedCafeId = application.CreatedCafeId,
                SubmittedAt = application.SubmittedAt,
                UpdatedAt = application.UpdatedAt,
                ReviewedAt = application.ReviewedAt,
                ApprovedAt = application.ApprovedAt,
                OperationalProfileUpdatedAt = application.OperationalProfileUpdatedAt
            };
        }
    }
}
