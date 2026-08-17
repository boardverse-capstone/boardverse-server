using BoardVerse.API.Authentication;
using BoardVerse.API.BackgroundServices;
using BoardVerse.API.Hubs;
using BoardVerse.Data;
using BoardVerse.Data.Repositories;
using BoardVerse.Core.Constants;
using BoardVerse.Core.IRepositories;
using Npgsql;
using BoardVerse.Services;
using BoardVerse.Services.Extensions;
using BoardVerse.Services.HostedServices;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using BoardVerse.Services.Services.Notifications;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using BoardVerse.Core.DTOs.Common;
using BoardVerse.Core.Json;
using BoardVerse.Core.Messages;
using BoardVerse.Core.Settings;
using System.Reflection;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Add Entity Framework Core
// Resolve connection string: prefer environment variables (DATABASE_URL or NEON_CONNECTION)
var defaultConn = builder.Configuration.GetConnectionString("DefaultConnection");
var envDatabaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL") ?? Environment.GetEnvironmentVariable("NEON_CONNECTION");
string resolvedConnectionString = defaultConn ?? string.Empty;
if (!string.IsNullOrWhiteSpace(envDatabaseUrl))
{
    // If the URL is in the postgres://user:pass@host:port/dbname form, convert to Npgsql connection string.
    if (envDatabaseUrl.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) || envDatabaseUrl.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
    {
        var uri = new Uri(envDatabaseUrl);
        var userInfo = uri.UserInfo.Split(':');
        if (userInfo.Length < 2)
        {
            throw new InvalidOperationException("Invalid database URL format: missing username or password");
        }
        var builderCs = new NpgsqlConnectionStringBuilder()
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = uri.AbsolutePath.Trim('/'),
            Username = userInfo[0],
            Password = userInfo[1],
            SslMode = SslMode.Require,
            TrustServerCertificate = true
        };

        resolvedConnectionString = builderCs.ToString();
    }
    else
    {
        // Assume it's already an acceptable Npgsql connection string
        resolvedConnectionString = envDatabaseUrl;
    }
}

builder.Services.AddDbContext<BoardVerseDbContext>(options =>
    BoardVerseDbContextOptions.UseBoardVersePostgreSql(options, resolvedConnectionString));

builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache(); // K-06: leaderboard caching (5-min TTL); cũng dùng bởi các adapter in-memory khác.

// Add Authentication
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var securityKey = jwtSettings["SecurityKey"] ?? throw new InvalidOperationException("JwtSettings:SecurityKey not configured");
var validIssuer = jwtSettings["ValidIssuer"] ?? throw new InvalidOperationException("JwtSettings:ValidIssuer not configured");
var validAudience = jwtSettings["ValidAudience"] ?? throw new InvalidOperationException("JwtSettings:ValidAudience not configured");

// H20: In Production, refuse to start if JWT SecurityKey is the placeholder or < 32 chars.
// Operators must override via env var JwtSettings__SecurityKey=<random 32+ chars>.
if (!builder.Environment.IsDevelopment())
{
    if (securityKey.StartsWith("REPLACE", StringComparison.Ordinal)
        || securityKey.Length < 32)
    {
        throw new InvalidOperationException(
            "JwtSettings:SecurityKey is missing or placeholder in Production. " +
            "Set env var JwtSettings__SecurityKey to a random string ≥ 32 chars.");
    }
}

// Firebase settings — allow override via env FIREBASE_CREDENTIALS_JSON (production).
// Override pattern keeps credentials.json out of source control.
// Production runtime (Render) sets:
//   FIREBASE__ENABLED = true
//   FIREBASE__CREDENTIALS_JSON = "<full JSON content as one line>"
// ASP.NET Core env var binding uses "__" as section separator.
var firebaseCredentialsFromEnv = Environment.GetEnvironmentVariable("FIREBASE_CREDENTIALS_JSON");
builder.Services.Configure<FirebaseSettings>(options =>
{
    var section = builder.Configuration.GetSection(FirebaseSettings.SectionName);
    section.Bind(options);
    if (!string.IsNullOrWhiteSpace(firebaseCredentialsFromEnv))
    {
        options.CredentialsJson = firebaseCredentialsFromEnv;
    }
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(securityKey)),
            ValidateIssuer = true,
            ValidIssuer = validIssuer,
            ValidateAudience = true,
            ValidAudience = validAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        options.Events = JwtBearerEventHandlers.Create();

        // SignalR WebSocket clients (browser @microsoft/signalr, mobile clients) cannot set custom
        // headers on the WebSocket handshake, so they pass the JWT via the `access_token` query
        // string. Lift it into the bearer pipeline for hub paths only; REST endpoints keep the
        // default Authorization-header behaviour.
        options.Events.OnMessageReceived = context =>
        {
            var path = context.HttpContext.Request.Path;
            if (!path.StartsWithSegments("/hubs"))
            {
                return Task.CompletedTask;
            }

            var accessToken = context.Request.Query["access_token"];
            if (string.IsNullOrEmpty(accessToken))
            {
                return Task.CompletedTask;
            }

            // Reject obviously malformed tokens early to avoid passing garbage downstream.
            var token = accessToken.ToString();
            var parts = token.Split('.');
            if (parts.Length != 3)
            {
                return Task.CompletedTask;
            }

            context.Token = token;
            return Task.CompletedTask;
        };
    });

// Add Authorization
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdmin", policy => policy.RequireRole("Admin"));
    options.AddPolicy("RequireManagerOrStaff", policy => policy.RequireRole("Manager", "CafeStaff"));

    // H-M15: Default-deny authentication is now applied via [Authorize] on BaseApiController.
    // Không dùng FallbackPolicy ở đây vì chặn cả public endpoint (BoardGame, Cafe, MasterGame, Health) → 401.
    // Default-deny chỉ apply cho controller kế thừa BaseApiController; controller con gắn [AllowAnonymous] nếu public.
});

// Distributed cache: Redis when REDIS_URL/config is set (Render/prod), in-memory otherwise (local dev)
builder.Services.AddBoardVerseRedis(builder.Configuration);

builder.Services.AddBoardVerseEmail(builder.Configuration);
builder.Services.AddBoardVerseBgg(builder.Configuration);
builder.Services.AddBoardVerseGeocoding(builder.Configuration); // Nominatim reverse-geocode cho PlayerLocationDto
builder.Services.AddBoardVersePayment();
// Reservation flow (BR §XXI-A.2..21A.6) — Phase 1 wallet đã đăng ký ở AddBoardVersePayment.
builder.Services.AddScoped<IReservationRepository, ReservationRepository>();
builder.Services.AddScoped<ISeatInventoryRepository, SeatInventoryRepository>();
builder.Services.AddScoped<IGameInventoryRepository, GameInventoryRepository>();
builder.Services.AddScoped<ICafeConfigRepository, CafeConfigRepository>();
builder.Services.AddScoped<IOutboxRepository, OutboxRepository>();
builder.Services.AddSingleton<IOutboxEventPublisher, RealOutboxPublisher>();
builder.Services.AddScoped<DepositCalculator>();
builder.Services.AddScoped<EligibilityValidator>();
builder.Services.AddScoped<IReservationService, ReservationService>();
builder.Services.AddScoped<IReservationExtensionService, ReservationExtensionService>();
builder.Services.AddScoped<IWalkInWindowRepository, WalkInWindowRepository>();
builder.Services.AddScoped<IWalkInBookingRepository, WalkInBookingRepository>();
builder.Services.AddScoped<IWalkInService, WalkInService>();
builder.Services.AddScoped<RefundCalculationService>();
builder.Services.AddScoped<IKarmaShortPlayRecordRepository, KarmaShortPlayRecordRepository>();
builder.Services.AddScoped<IPlayerKarmaService, PlayerKarmaService>();
builder.Services.AddScoped<IKarmaService, KarmaService>();
builder.Services.AddScoped<ICafeScheduleOverrideRepository, CafeScheduleOverrideRepository>();
builder.Services.AddScoped<IScheduleResolver, CafeScheduleResolver>();
builder.Services.AddScoped<ICafeScheduleService, CafeScheduleService>();
builder.Services.AddScoped<ITimeSlotService, TimeSlotService>();
builder.Services.AddScoped<IAuthRepository, AuthRepository>();
builder.Services.AddScoped<IUserProfileRepository, UserProfileRepository>();
builder.Services.AddScoped<IUserManagementRepository, UserManagementRepository>();
builder.Services.AddScoped<IHealthRepository, HealthRepository>();
builder.Services.AddScoped<IGameTemplateRepository, GameTemplateRepository>();
builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
builder.Services.AddScoped<IGameComponentTemplateRepository, GameComponentTemplateRepository>();
builder.Services.AddScoped<ICafeRepository, CafeRepository>();
builder.Services.AddScoped<ICafeTableRepository, CafeTableRepository>();
builder.Services.AddScoped<ICafeInventoryRepository, CafeInventoryRepository>();
builder.Services.AddScoped<ICafePosRepository, CafePosRepository>();
builder.Services.AddScoped<ILobbyRepository, LobbyRepository>();
builder.Services.AddScoped<IPosCheckInTokenRepository, PosCheckInTokenRepository>();
builder.Services.AddScoped<IPlayerCheckInService, PlayerCheckInService>();
builder.Services.AddScoped<IActiveSessionRepository, ActiveSessionRepository>();
builder.Services.AddScoped<IKarmaRatingRepository, KarmaRatingRepository>();
builder.Services.AddScoped<IMatchResultRepository, MatchResultRepository>();
builder.Services.AddScoped<IAdminModerationRepository, AdminModerationRepository>();
builder.Services.AddScoped<IPlayerAlertRepository, PlayerAlertRepository>(); // R-01
builder.Services.AddScoped<IPlayerRiskScoreRepository, PlayerRiskScoreRepository>(); // BR-RISK-01
builder.Services.AddScoped<IPaymentWebhookAuditRepository, PaymentWebhookAuditRepository>(); // GAP-10
builder.Services.AddScoped<ISystemConfigurationRepository, SystemConfigurationRepository>();
builder.Services.AddScoped<ICafePartnerApplicationRepository, CafePartnerApplicationRepository>();
builder.Services.AddScoped<IBookingDepositRepository, BookingDepositRepository>();
builder.Services.AddScoped<IBookingRepository, BookingRepository>();
builder.Services.AddScoped<ITransactionRepository, TransactionRepository>();
builder.Services.AddScoped<ICafeSettlementRepository, CafeSettlementRepository>();
        builder.Services.AddScoped<ISettlementService, SettlementService>();
        builder.Services.AddScoped<ICafeShiftRepository, CafeShiftRepository>();
        builder.Services.AddScoped<ICafeShiftService, CafeShiftService>();
builder.Services.AddScoped<IBookingNoShowVoteRepository, BookingNoShowVoteRepository>();
builder.Services.AddScoped<IBookingRatingRepository, BookingRatingRepository>();
builder.Services.AddScoped<ICafeBookingService, CafeBookingService>();
builder.Services.AddScoped<IBookingRatingService, BookingRatingService>();
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<LegacyBookingCleanupService>(); // Cleanup stale legacy Booking rows
builder.Services.AddSingleton<LegacyBookingCleanupMetricsStore>(); // GAP-10: persist metrics across scopes
builder.Services.Configure<BoardVerse.Core.Settings.LegacyBookingSettings>(
    builder.Configuration.GetSection(BoardVerse.Core.Settings.LegacyBookingSettings.SectionName));

// Background Jobs

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserProfileService, UserProfileService>();
builder.Services.AddScoped<ILeaderboardService, LeaderboardService>();
builder.Services.AddScoped<IUserManagementService, UserManagementService>();
builder.Services.AddScoped<IHealthService, HealthService>();
builder.Services.AddScoped<IGameTemplateService, GameTemplateService>();
builder.Services.AddScoped<IBoardGameService, BoardGameService>();
builder.Services.AddScoped<ICafeService, CafeService>();
builder.Services.AddScoped<ICafeInventoryService, CafeInventoryService>();
builder.Services.AddScoped<ICafePosService, CafePosService>();
builder.Services.AddScoped<IReceiptService, ReceiptService>(); // P-01 & P-02
builder.Services.AddScoped<ILobbyService, LobbyService>();
builder.Services.AddScoped<IActiveSessionService, ActiveSessionService>();
builder.Services.AddScoped<IKarmaRatingService, KarmaRatingService>();
builder.Services.AddScoped<IMatchResultService, MatchResultService>();
builder.Services.AddScoped<IAdminModerationService, AdminModerationService>();
builder.Services.AddScoped<IAdminMasterCatalogService, AdminMasterCatalogService>();
builder.Services.AddScoped<SystemConfigurationService>();
builder.Services.AddScoped<ISystemConfigurationProvider>(sp => sp.GetRequiredService<SystemConfigurationService>());
builder.Services.AddScoped<IAdminSystemConfigurationService>(sp => sp.GetRequiredService<SystemConfigurationService>());
builder.Services.AddScoped<IKarmaConfigurationService, KarmaConfigurationService>();
builder.Services.AddScoped<ICoolingOffService, CoolingOffService>(); // BR-NEW-10: cooling-off detect/expire/escalate
builder.Services.AddScoped<IPlayerRiskQueryService, PlayerRiskQueryService>(); // BR-RISK-09: admin risk detail view
builder.Services.AddScoped<IPlayerRiskScoreService, PlayerRiskScoreService>(); // BR-RISK-01: hourly risk recompute
builder.Services.AddScoped<IPlayerAlertService, PlayerAlertService>(); // R-01: admin alert management
builder.Services.AddScoped<ILevelingService, LevelingService>();
builder.Services.AddScoped<ICafePartnerApplicationService, CafePartnerApplicationService>();
builder.Services.AddScoped<ISePayAccountRepository, SePayAccountRepository>();
builder.Services.AddScoped<ISePayAccountService, SePayAccountService>();
builder.Services.AddScoped<ITournamentRepository, TournamentRepository>();
builder.Services.AddScoped<ITournamentService, TournamentService>();
builder.Services.AddScoped<ITournamentWaitlistRepository, TournamentWaitlistRepository>();
builder.Services.AddScoped<ITournamentWaitlistService, TournamentWaitlistService>();
builder.Services.AddScoped<ITournamentSpectatorRepository, TournamentSpectatorRepository>();
builder.Services.AddScoped<ITournamentSpectatorService, TournamentSpectatorService>();
builder.Services.AddScoped<IAdminReportService, AdminReportService>();
builder.Services.AddScoped<IFriendshipRepository, FriendshipRepository>();
builder.Services.AddScoped<IFriendNoteRepository, FriendNoteRepository>();
builder.Services.AddScoped<IFriendReportRepository, FriendReportRepository>();
builder.Services.AddScoped<ILobbyMemberRepository, LobbyMemberRepository>();
builder.Services.AddScoped<LobbyInviteRepository>();
builder.Services.AddScoped<ILobbyInviteRepository>(sp => sp.GetRequiredService<LobbyInviteRepository>());
builder.Services.AddScoped<IFriendService, FriendService>();
builder.Services.AddScoped<IFriendNoteService, FriendNoteService>();
builder.Services.AddScoped<IFriendReportService, FriendReportService>();
builder.Services.AddScoped<ILobbyInviteService, LobbyInviteService>();
builder.Services.AddScoped<ILobbyMessageRepository, LobbyMessageRepository>();
builder.Services.AddScoped<ILobbyMessageService, LobbyMessageService>();
builder.Services.AddScoped<IDeviceTokenRepository, DeviceTokenRepository>();
builder.Services.AddScoped<IDeviceTokenService, DeviceTokenService>();
builder.Services.AddScoped<IPushNotificationService, FcmPushNotificationService>();

// Background Jobs for Lobby expiration — skip in Testing env (KarmaWindowJob interferes with integration tests)
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHostedService<LobbyTimeoutJob>();
    builder.Services.AddHostedService<KarmaWindowJob>();
    builder.Services.AddHostedService<KarmaWindowExpiryJob>(); // K-01: close karma window after 48h
    builder.Services.AddHostedService<BookingDepositExpiryJob>();
    builder.Services.AddHostedService<ReservationDeadlineJob>(); // GAP #17: deadline + cafe approval expiry + no-show
    builder.Services.AddHostedService<BvcTopUpExpiryJob>(); // BVC top-up pending expire sau 30 phút
    builder.Services.AddHostedService<SettlementRetryJob>();
    builder.Services.AddHostedService<TournamentExpiryJob>();
    builder.Services.AddHostedService<LobbyCleanupJob>();
    builder.Services.AddHostedService<TournamentReminderJob>();
    builder.Services.AddHostedService<TournamentNoShowDetectionJob>();
    builder.Services.AddHostedService<FriendRequestExpiryJob>();
    builder.Services.AddHostedService<LobbyInviteExpiryJob>(); // BR-LOBBY-INVITE-08: expire invite 24h
    builder.Services.AddHostedService<LobbyNotificationJob>(); // N-01: BR-NEW-13 milestone notifications
    builder.Services.AddHostedService<LobbyAtRiskWarningJob>(); // N-02: BR-NEW-14 at-risk warning
    builder.Services.AddHostedService<ReservationNoShowDetectionJob>(); // BR-CHECKIN-02: auto NoShow 30 phút grace
    // GAP-9: Conditional registration — skip Testing env để không phá integration test.
    if (!builder.Environment.IsEnvironment("Testing"))
    {
        builder.Services.AddHostedService<LegacyBookingCleanupJob>(); // Legacy Flow B: sweep stale PendingDeposit/Confirmed rows
    }
    builder.Services.AddHostedService<AutoReleaseExpiredSessionsJob>(); // BR-END-05: auto-release session khi staff quên end
    builder.Services.AddHostedService<WalkInWindowCleanupJob>(); // §4.4: auto-close expired WalkInWindows
    builder.Services.AddHostedService<CoolingOffJob>(); // BR-NEW-10: detect signals + expire cooling-off
    builder.Services.AddHostedService<RiskScoreRecomputeJob>(); // BR-RISK-01: hourly risk score recompute
    builder.Services.AddHostedService<SuspensionExpiryCheckJob>(); // BR-RISK-06: auto-unlock expired suspensions
    builder.Services.AddHostedService<AlertExpiryCleanupJob>(); // R-01: dismiss stale alerts after 30 days

    // BR §XXI-H.8: Reservation scheduler jobs (recruitmentDeadline, cafe approval 24h, no-show grace).
    builder.Services.AddReservationSchedulers();

    // BR-REQUIRED §17.5: Transactional Outbox publisher.
    builder.Services.AddHostedService<OutboxPublisherHostedService>();
}

// SignalR Hubs for real-time updates
builder.Services.AddSignalR();
builder.Services.AddScoped<ILobbyHubService, LobbyHubService>();
builder.Services.AddScoped<IPosHubService, PosHubService>();

builder.Services.AddControllers(options =>
{
    options.Filters.Add<BoardVerse.API.Filters.ValidateModelAttribute>();
})
.AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.JsonSerializerOptions.Converters.Add(new FlexibleDateOnlyJsonConverter());
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

// [ApiController] auto-trả về ValidationProblemDetails (English) với shape { type, title, status, errors, traceId }.
// Bắt response đó NGAY TRƯỚC khi nó escape ra client → chuyển sang ApiResponse shape + message tiếng Việt
// từ ApiErrorMessages.Validation để UI parse được và người dùng hiểu được.
// Chi tiết field-level vẫn được giữ trong Data để FE/dev debug.
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
 options.InvalidModelStateResponseFactory = context =>
 {
 var errors = context.ModelState
 .Where(kvp => kvp.Value!.Errors.Count > 0)
 .ToDictionary(
 kvp => kvp.Key,
 kvp => kvp.Value!.Errors.Select(e => e.ErrorMessage).ToArray());

 var path = context.HttpContext.Request.Path.Value ?? string.Empty;
 var firstField = errors.Keys.FirstOrDefault() ?? string.Empty;
 var firstError = errors.Values.FirstOrDefault()?.FirstOrDefault() ?? string.Empty;

 // 1) Thử lookup message cụ thể cho Reservation flow fields (PreferredEndTime, TimeSlot, ...).
 // 2) Nếu không khớp domain đặc biệt nào → fallback FieldValidationFailed (generic per-field).
 // 3) Nếu không extract được field name → GenericValidationFailed.
 var specificMessage = ApiErrorMessages.Validation.GetReservationFieldMessage(firstField, firstError);
 var friendlyMessage = specificMessage
 ?? (string.IsNullOrEmpty(firstField)
 ? ApiErrorMessages.Validation.GenericValidationFailed
 : ApiErrorMessages.Validation.FieldValidationFailed(firstField, errors.Count));

 return new BadRequestObjectResult(new ApiResponse
 {
 StatusCode = StatusCodes.Status400BadRequest,
 Message = friendlyMessage,
 Data = new
 {
 fields = errors,
 path,
 },
 Timestamp = DateTime.UtcNow,
 Path = path,
 });
 };
});

// Add Rate Limiting
// L-01: 5 attempts per IP per 15 minutes for share code join brute-force protection
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            status = 429,
            message = BoardVerse.Core.Messages.ApiErrorMessages.LobbyInvite.ShareCodeRateLimitExceeded
        }, cancellationToken);
    };
    options.AddPolicy("ShareCodePolicy", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(15),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
});

// Add Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "BoardVerse API",
        Version = "v1",
        Description = "Authentication API for BoardVerse"
    });

    // Add JWT Bearer token support to Swagger
    var securityScheme = new OpenApiSecurityScheme
    {
        Name = "JWT Authentication",
        Description = "Enter JWT Bearer token",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    };

    options.AddSecurityDefinition("Bearer", securityScheme);

    var securityRequirement = new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] { }
        }
    };

    options.AddSecurityRequirement(securityRequirement);

    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
    }
});

// Add CORS
// Two policies:
//   "BoardVerseCors" - default cho REST API (giữ AllowAnyOrigin cho tương thích hiện tại).
//   "SignalRCors"    - explicit cho SignalR hubs: chỉ allow trusted origins + AllowCredentials + expose negotiate headers.
builder.Services.AddCors(options =>
{
    options.AddPolicy("BoardVerseCors", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });

    options.AddPolicy("SignalRCors", policy =>
    {
        // Trusted origins for SignalR negotiate. Hardcode dev origins; production origins
        // được bổ sung qua env var BoardVerse__SignalROrigins__0, _1, ... (comma-separated).
        var allowedOrigins = new List<string>
        {
            "http://localhost:3000",
            "http://127.0.0.1:3000",
            "http://localhost:5173",
            "http://127.0.0.1:5173",
            "http://localhost:5022"
        };

        var extraOrigins = builder.Configuration.GetSection("BoardVerse:SignalROrigins").Get<string[]>();
        if (extraOrigins is not null)
        {
            allowedOrigins.AddRange(extraOrigins.Where(o => !string.IsNullOrWhiteSpace(o)));
        }

        policy.WithOrigins(allowedOrigins.ToArray())
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials()
              .WithExposedHeaders(
                  "negotiate-version",
                  "signalr-connection-id",
                  "x-signalr-user-agent");
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    _ = scope.ServiceProvider.GetRequiredService<BoardVerseDbContext>();
    app.Logger.LogInformation("Database connection initialized. Schema migrations + seed must be run out-of-band.");
}

var redisInfo = app.Services.GetRequiredService<RedisCacheStartupInfo>();
RedisServiceExtensions.LogRedisCacheStartup(app.Logger, redisInfo);

var brevoSection = app.Configuration.GetSection(BrevoSettings.SectionName);
app.Logger.LogInformation(
    "Brevo startup: ApiKeySet={HasApiKey}, SenderEmail={SenderEmail}, ApiBaseUrl={ApiBaseUrl}",
    !string.IsNullOrWhiteSpace(brevoSection["ApiKey"]),
    string.IsNullOrWhiteSpace(brevoSection["SenderEmail"]) ? "(missing)" : brevoSection["SenderEmail"],
    string.IsNullOrWhiteSpace(brevoSection["ApiBaseUrl"]) ? "https://api.brevo.com (default)" : brevoSection["ApiBaseUrl"]);

var firebaseSection = app.Configuration.GetSection(FirebaseSettings.SectionName);
var firebaseEnabled = firebaseSection.GetValue<bool>("Enabled");
var firebaseCredentialsSet =
    !string.IsNullOrWhiteSpace(firebaseSection["CredentialsJson"])
    || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIREBASE_CREDENTIALS_JSON"));
app.Logger.LogInformation(
    "Firebase (FCM) startup: Enabled={Enabled}, ProjectId={ProjectId}, CredentialsSet={CredentialsSet}",
    firebaseEnabled,
    string.IsNullOrWhiteSpace(firebaseSection["ProjectId"]) ? "(missing)" : firebaseSection["ProjectId"],
    firebaseCredentialsSet);

// Configure the HTTP request pipeline.
var renderPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(renderPort))
{
    app.Urls.Clear();
    app.Urls.Add($"http://0.0.0.0:{renderPort}");
}
else if (app.Environment.IsDevelopment())
{
    // Disable HTTPS redirect in development
    app.Urls.Clear();
    app.Urls.Add("http://localhost:5022");
}

var enableSwaggerEnv = Environment.GetEnvironmentVariable("ENABLE_SWAGGER");
var enableSwagger = app.Environment.IsDevelopment() || string.Equals(enableSwaggerEnv, "true", StringComparison.OrdinalIgnoreCase);

if (enableSwagger)
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "BoardVerse API v1");
    });
}

app.UseCors("BoardVerseCors");

// Serve static HTML test pages from a dedicated wwwroot folder.
// P0-Fix-#1: KHÔNG mount thư mục CHA project (lộ appsettings.json, .git/, source code).
var wwwrootPath = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
if (Directory.Exists(wwwrootPath))
{
    app.UseDefaultFiles(new DefaultFilesOptions
    {
        FileProvider = new PhysicalFileProvider(wwwrootPath),
        DefaultFileNames = new List<string> { "index.html" }
    });
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(wwwrootPath),
        RequestPath = ""
    });
}

// Register exception middleware so every response uses the unified shape
app.UseMiddleware<BoardVerse.API.Middleware.ApiExceptionMiddleware>();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Map SignalR Hubs - apply SignalRCors policy to expose negotiate headers + allow credentials.
// Note: RequireCors must come BEFORE RequireAuthorization so preflight (OPTIONS) is handled
// by CORS middleware without hitting JwtBearer auth, otherwise the OPTIONS request fails
// with 401 and the browser drops the negotiate POST.
app.MapHub<LobbyHub>("/hubs/lobby")
    .RequireCors("SignalRCors");
app.MapHub<PosHub>("/hubs/pos")
    .RequireCors("SignalRCors");

app.Run();
