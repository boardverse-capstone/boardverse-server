using BoardVerse.API.Authentication;
using BoardVerse.API.BackgroundServices;
using BoardVerse.API.Hubs;
using BoardVerse.Data;
using BoardVerse.Data.Repositories;
using BoardVerse.Core.IRepositories;
using Npgsql;
using BoardVerse.Services.Extensions;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using BoardVerse.Core.DTOs.Common;
using BoardVerse.Core.Json;
using BoardVerse.Core.Settings;
using BoardVerse.API.Infrastructure;
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

// Add Authentication
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var securityKey = jwtSettings["SecurityKey"] ?? throw new InvalidOperationException("JwtSettings:SecurityKey not configured");
var validIssuer = jwtSettings["ValidIssuer"] ?? throw new InvalidOperationException("JwtSettings:ValidIssuer not configured");
var validAudience = jwtSettings["ValidAudience"] ?? throw new InvalidOperationException("JwtSettings:ValidAudience not configured");

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
    });

// Add Authorization
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdmin", policy => policy.RequireRole("Admin"));
    options.AddPolicy("RequireManagerOrStaff", policy => policy.RequireRole("Manager", "CafeStaff"));
});

// Distributed cache: Redis when REDIS_URL/config is set (Render/prod), in-memory otherwise (local dev)
builder.Services.AddBoardVerseRedis(builder.Configuration);

builder.Services.AddBoardVerseEmail(builder.Configuration);
builder.Services.AddBoardVerseBgg(builder.Configuration);
builder.Services.AddBoardVersePayment();
builder.Services.AddScoped<IAuthRepository, AuthRepository>();
builder.Services.AddScoped<IUserProfileRepository, UserProfileRepository>();
builder.Services.AddScoped<IUserManagementRepository, UserManagementRepository>();
builder.Services.AddScoped<IHealthRepository, HealthRepository>();
builder.Services.AddScoped<IGameTemplateRepository, GameTemplateRepository>();
builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
builder.Services.AddScoped<IGameComponentTemplateRepository, GameComponentTemplateRepository>();
builder.Services.AddScoped<ICafeRepository, CafeRepository>();
builder.Services.AddScoped<ICafeInventoryRepository, CafeInventoryRepository>();
builder.Services.AddScoped<ICafePosRepository, CafePosRepository>();
builder.Services.AddScoped<ILobbyRepository, LobbyRepository>();
builder.Services.AddScoped<IActiveSessionRepository, ActiveSessionRepository>();
builder.Services.AddScoped<IKarmaRatingRepository, KarmaRatingRepository>();
builder.Services.AddScoped<IMatchResultRepository, MatchResultRepository>();
builder.Services.AddScoped<IAdminModerationRepository, AdminModerationRepository>();
builder.Services.AddScoped<ISystemConfigurationRepository, SystemConfigurationRepository>();
builder.Services.AddScoped<ICafePartnerApplicationRepository, CafePartnerApplicationRepository>();
builder.Services.AddScoped<IPaymentMasterAccountRepository, PaymentMasterAccountRepository>();
builder.Services.AddScoped<IBookingDepositRepository, BookingDepositRepository>();
builder.Services.AddScoped<ITransactionRepository, TransactionRepository>();
builder.Services.AddScoped<ICafeSettlementRepository, CafeSettlementRepository>();
builder.Services.AddScoped<ISettlementService, SettlementService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserProfileService, UserProfileService>();
builder.Services.AddScoped<IUserManagementService, UserManagementService>();
builder.Services.AddScoped<IHealthService, HealthService>();
builder.Services.AddScoped<IGameTemplateService, GameTemplateService>();
builder.Services.AddScoped<IBoardGameService, BoardGameService>();
builder.Services.AddScoped<ICafeService, CafeService>();
builder.Services.AddScoped<ICafeInventoryService, CafeInventoryService>();
builder.Services.AddScoped<ICafePosService, CafePosService>();
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
builder.Services.AddScoped<ICafePartnerApplicationService, CafePartnerApplicationService>();
builder.Services.AddScoped<ISePayAccountRepository, SePayAccountRepository>();
builder.Services.AddScoped<ISePayAccountService, SePayAccountService>();
builder.Services.AddScoped<ITournamentRepository, TournamentRepository>();
builder.Services.AddScoped<ITournamentService, TournamentService>();
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

// Background Jobs for Lobby expiration — skip in Testing env (KarmaWindowJob interferes with integration tests)
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHostedService<LobbyTimeoutJob>();
    builder.Services.AddHostedService<KarmaWindowJob>();
    builder.Services.AddHostedService<BookingDepositExpiryJob>();
    builder.Services.AddHostedService<SettlementRetryJob>();
    builder.Services.AddHostedService<TournamentExpiryJob>();
    builder.Services.AddHostedService<LobbyCleanupJob>();
    builder.Services.AddHostedService<TournamentReminderJob>();
    builder.Services.AddHostedService<TournamentNoShowDetectionJob>();
    builder.Services.AddHostedService<FriendRequestExpiryJob>();
}

// SignalR Hubs for real-time updates
builder.Services.AddSignalR();
builder.Services.AddScoped<ILobbyHubService, LobbyHubService>();

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

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.SuppressModelStateInvalidFilter = true;
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

// Add CORS if needed
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BoardVerseDbContext>();

    // Fix QrUrl column width for SePay full checkout URLs (can exceed 500 chars)
    try
    {
        await db.Database.ExecuteSqlRawAsync($@"
            ALTER TABLE ""BookingDeposits"" ALTER COLUMN ""QrUrl"" TYPE varchar(2000)");
        app.Logger.LogInformation("BookingDeposits.QrUrl column extended to varchar(2000)");
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Could not alter QrUrl column (may already be wide enough)");
    }

    // Gap 3 fix: add RetryCount + NextRetryAt to CafeSettlements for SettlementRetryJob
    try
    {
        await db.Database.ExecuteSqlRawAsync($@"
            ALTER TABLE ""CafeSettlements""
                ADD COLUMN IF NOT EXISTS ""RetryCount"" integer NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS ""NextRetryAt"" timestamp with time zone NULL");
        app.Logger.LogInformation("CafeSettlements.RetryCount + NextRetryAt columns ensured.");
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Could not alter CafeSettlements retry columns (may already exist)");
    }

    // Gap 6 fix: make CafeTableId + CafeInventoryBoxId nullable on ActiveSessions
    try
    {
        await db.Database.ExecuteSqlRawAsync($@"
            ALTER TABLE ""ActiveSessions""
                ALTER COLUMN ""CafeTableId"" DROP NOT NULL,
                ALTER COLUMN ""CafeInventoryBoxId"" DROP NOT NULL");
        app.Logger.LogInformation("ActiveSessions.CafeTableId + CafeInventoryBoxId made nullable.");
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Could not alter ActiveSessions FK columns (may already be nullable)");
    }

    var inventoryRepo = scope.ServiceProvider.GetRequiredService<ICafeInventoryRepository>();
    await inventoryRepo.BackfillMissingInventoryBoxesAsync();
    app.Logger.LogInformation("Inventory box backfill completed.");

    // SePay Master Account cần được tạo thủ công qua Admin API: POST /api/sepay-accounts

    // PaymentTestSeed disabled - tests use their own bootstrapper with unique data
    // await PaymentTestSeed.SeedAsync(app.Services);
}

var redisInfo = app.Services.GetRequiredService<RedisCacheStartupInfo>();
RedisServiceExtensions.LogRedisCacheStartup(app.Logger, redisInfo);

var brevoSection = app.Configuration.GetSection(BrevoSettings.SectionName);
app.Logger.LogInformation(
    "Brevo startup: ApiKeySet={HasApiKey}, SenderEmail={SenderEmail}, ApiBaseUrl={ApiBaseUrl}",
    !string.IsNullOrWhiteSpace(brevoSection["ApiKey"]),
    string.IsNullOrWhiteSpace(brevoSection["SenderEmail"]) ? "(missing)" : brevoSection["SenderEmail"],
    string.IsNullOrWhiteSpace(brevoSection["ApiBaseUrl"]) ? "https://api.brevo.com (default)" : brevoSection["ApiBaseUrl"]);

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

app.UseCors("AllowAll");

// Serve static HTML test pages
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.Combine(app.Environment.ContentRootPath, "..")),
    RequestPath = "/test"
});

// Register exception middleware so every response uses the unified shape
app.UseMiddleware<BoardVerse.API.Middleware.ApiExceptionMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Map SignalR Hubs
app.MapHub<LobbyHub>("/hubs/lobby");

app.Run();
