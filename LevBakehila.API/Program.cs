using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using LevBakehila.API.Json;
using LevBakehila.API.Middleware;
using LevBakehila.Core.Interfaces;
using LevBakehila.Core.Settings;
using LevBakehila.Data;
using LevBakehila.Data.Repositories;
using LevBakehila.Data.Seed;
using LevBakehila.Service.Mapping;
using LevBakehila.Service.Security;
using LevBakehila.Service.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using NLog;
using NLog.Web;

// NLog is started first so that even startup errors are written to the log
var startupLogger = LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ---------- Logging: ILogger<T> in code, NLog as the provider ----------
    builder.Logging.ClearProviders();
    builder.Host.UseNLog();

    // ---------- Configuration (secrets come from User Secrets) ----------
    string connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException(
            "Missing configuration 'ConnectionStrings:DefaultConnection'. Set it with: dotnet user-secrets set " +
            "\"ConnectionStrings:DefaultConnection\" \"Host=localhost;Port=5432;Database=LevBakehilaDb;Username=postgres;Password=<password>\" --project LevBakehila.API");

    var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>() ?? new JwtSettings();
    if (string.IsNullOrWhiteSpace(jwtSettings.Key) || jwtSettings.Key.Length < 32)
    {
        throw new InvalidOperationException(
            "Missing or too short configuration 'Jwt:Key' (at least 32 characters). " +
            "Set it with: dotnet user-secrets set \"Jwt:Key\" \"<random-secret>\" --project LevBakehila.API");
    }

    builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));

    // ---------- Dependency Injection ----------
    // DbContext is Scoped: one instance per HTTP request. It is not thread-safe and tracks entities,
    // so it must never be shared between requests. A Singleton that holds a Scoped dependency would
    // keep one DbContext alive for the whole application ("captive dependency") - wrong data and crashes.
    builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

    builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
    builder.Services.AddScoped<IUserRepository, UserRepository>();
    builder.Services.AddScoped<IActivityRepository, ActivityRepository>();
    builder.Services.AddScoped<IRegistrationRepository, RegistrationRepository>();
    builder.Services.AddScoped<ISkillRepository, SkillRepository>();

    builder.Services.AddScoped<IAuthService, AuthService>();
    builder.Services.AddScoped<IUserService, UserService>();
    builder.Services.AddScoped<IActivityService, ActivityService>();
    builder.Services.AddScoped<IRegistrationService, RegistrationService>();
    builder.Services.AddScoped<ISkillService, SkillService>();

    // AutoMapper: finds MappingProfile in the Service assembly. The configuration (IConfigurationProvider)
    // is a stateless Singleton; IMapper is Transient and safe to inject into the Scoped services.
    builder.Services.AddAutoMapper(typeof(MappingProfile));

    // Stateless, no dependencies -> safe as Singleton
    builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();

    // ---------- Controllers + JSON ----------
    builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            // Dates in and out of the API are UTC (ISO 8601 with "Z")
            options.JsonSerializerOptions.Converters.Add(new UtcDateTimeJsonConverter());
        })
        // [ApiController] still rejects an invalid ModelState with 400, but in the same ErrorResponse shape
        .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = ErrorResponse.FromInvalidModelState);

    // ---------- JWT Authentication ----------
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwtSettings.Issuer,
                ValidateAudience = true,
                ValidAudience = jwtSettings.Audience,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key)),
                RoleClaimType = ClaimTypes.Role,
                NameClaimType = ClaimTypes.Name,
                ClockSkew = TimeSpan.FromMinutes(1)
            };

            // 401 / 403 from the authentication layer use the same ErrorResponse JSON as everything else
            options.Events = new JwtBearerEvents
            {
                OnChallenge = context =>
                {
                    context.HandleResponse();
                    return ErrorResponse.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized, "יש להתחבר כדי לבצע פעולה זו.");
                },
                OnForbidden = context
                    => ErrorResponse.WriteAsync(context.HttpContext, StatusCodes.Status403Forbidden, "אין לך הרשאה לפעולה זו.")
            };
        });
    builder.Services.AddAuthorization();

    // ---------- CORS for the React client ----------
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
    builder.Services.AddCors(options => options.AddPolicy("ClientPolicy", policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .WithExposedHeaders(CorrelationIdMiddleware.HeaderName)));

    // ---------- Swagger with JWT "Authorize" button ----------
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo { Title = "LevBakehila API", Version = "v1" });

        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "הדביקי כאן את ה־JWT שהתקבל מ־POST /api/auth/login (ללא המילה Bearer)."
        });
        options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                },
                Array.Empty<string>()
            }
        });

        var xmlFile = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
        if (File.Exists(xmlFile))
        {
            options.IncludeXmlComments(xmlFile);
        }
    });

    var app = builder.Build();

    // ---------- Database: apply migrations + seed demo data (idempotent) ----------
    using (var scope = app.Services.CreateScope())
    {
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync();

        var seedSettings = builder.Configuration.GetSection(SeedSettings.SectionName).Get<SeedSettings>() ?? new SeedSettings();
        await DbSeeder.SeedAsync(
            dbContext,
            services.GetRequiredService<IPasswordHasher>(),
            seedSettings,
            services.GetRequiredService<ILogger<Program>>());
    }

    // ---------- HTTP pipeline (order matters) ----------
    app.UseMiddleware<ExceptionHandlingMiddleware>();   // 1. catches errors from everything below
    app.UseMiddleware<CorrelationIdMiddleware>();       // 2. CorrelationId + request logging

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseHttpsRedirection();
    app.UseCors("ClientPolicy");
    app.UseAuthentication();                            // 4. who are you?
    app.UseAuthorization();                             // 5. are you allowed?
    app.MapControllers();                               // 6. controllers

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    startupLogger.Error(ex, "Application stopped because of an exception during startup");
    throw;
}
finally
{
    LogManager.Shutdown();
}
