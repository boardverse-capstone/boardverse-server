using BoardVerse.API.Authentication;
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
using System.Reflection;

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
builder.Services.AddScoped<IAuthRepository, AuthRepository>();
builder.Services.AddScoped<IUserProfileRepository, UserProfileRepository>();
builder.Services.AddScoped<IUserManagementRepository, UserManagementRepository>();
builder.Services.AddScoped<IHealthRepository, HealthRepository>();
builder.Services.AddScoped<IGameTemplateRepository, GameTemplateRepository>();
builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
builder.Services.AddScoped<ICafeRepository, CafeRepository>();
builder.Services.AddScoped<ICafeInventoryRepository, CafeInventoryRepository>();
builder.Services.AddScoped<ICafePosRepository, CafePosRepository>();
builder.Services.AddScoped<IKarmaRatingRepository, KarmaRatingRepository>();
builder.Services.AddScoped<IMatchResultRepository, MatchResultRepository>();
builder.Services.AddScoped<IAdminModerationRepository, AdminModerationRepository>();
builder.Services.AddScoped<ISystemConfigurationRepository, SystemConfigurationRepository>();
builder.Services.AddScoped<ICafePartnerApplicationRepository, CafePartnerApplicationRepository>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserProfileService, UserProfileService>();
builder.Services.AddScoped<IUserManagementService, UserManagementService>();
builder.Services.AddScoped<IHealthService, HealthService>();
builder.Services.AddScoped<IGameTemplateService, GameTemplateService>();
builder.Services.AddScoped<IBoardGameService, BoardGameService>();
builder.Services.AddScoped<ICafeService, CafeService>();
builder.Services.AddScoped<ICafeInventoryService, CafeInventoryService>();
builder.Services.AddScoped<ICafePosService, CafePosService>();
builder.Services.AddScoped<IKarmaRatingService, KarmaRatingService>();
builder.Services.AddScoped<IMatchResultService, MatchResultService>();
builder.Services.AddScoped<IAdminModerationService, AdminModerationService>();
builder.Services.AddScoped<SystemConfigurationService>();
builder.Services.AddScoped<ISystemConfigurationProvider>(sp => sp.GetRequiredService<SystemConfigurationService>());
builder.Services.AddScoped<IAdminSystemConfigurationService>(sp => sp.GetRequiredService<SystemConfigurationService>());
builder.Services.AddScoped<IKarmaConfigurationService, KarmaConfigurationService>();
builder.Services.AddScoped<ICafePartnerApplicationService, CafePartnerApplicationService>();
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
    await GameSchemaBootstrapper.EnsureUserAndCafeTablesAsync(db);
    await GameSchemaBootstrapper.EnsureGameTablesAsync(db);
    await GameSchemaBootstrapper.EnsureInventoryTablesAsync(db);
    await GameSchemaBootstrapper.EnsureInventoryBoxBackfillAsync(db);
    await GameSchemaBootstrapper.EnsureLobbyAndKarmaRatingTablesAsync(db);
    await GameSchemaBootstrapper.EnsureMatchResultTablesAsync(db);
    await GameSchemaBootstrapper.EnsureUserModerationColumnsAsync(db);
    await GameSchemaBootstrapper.EnsureKarmaLogAndSystemConfigTablesAsync(db);
    app.Logger.LogInformation("Database schema bootstrap completed.");
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

// Register exception middleware so every response uses the unified shape
app.UseMiddleware<BoardVerse.API.Middleware.ApiExceptionMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
