using System.Text;
using System.Threading.RateLimiting;
using System.Data.Common;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dressfield.API.Middleware;
using Dressfield.Application.Interfaces;
using Dressfield.Core.Entities;
using Dressfield.Core.Interfaces;
using Dressfield.Infrastructure.Data;
using Dressfield.Infrastructure.Services;
using Dressfield.Application.DTOs;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Ensure static files root exists before middleware initialization.
var webRootPath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
Directory.CreateDirectory(Path.Combine(webRootPath, "uploads", "designs"));

// â”€â”€ Logging â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console()
    .WriteTo.File("logs/dressfield-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// â”€â”€ Resolve real client IP from Azure / reverse proxy (required for rate limiting) â”€â”€
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Trust all known proxies â€” Azure App Service manages this internally
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// â”€â”€ Request body size (20 MB â€” covers design image uploads) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = 20 * 1024 * 1024);

// â”€â”€ Database â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<DressfieldDbContext>(options =>
    options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 36))));
builder.Services.AddHealthChecks()
    .AddDbContextCheck<DressfieldDbContext>("database");

// â”€â”€ Identity â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredLength = 8;
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<DressfieldDbContext>()
    .AddDefaultTokenProviders();

// â”€â”€ JWT Authentication â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
// IMPORTANT: Jwt:Secret must NEVER be left as empty in production.
// Set it via Azure App Service â†’ Configuration â†’ Application settings: Jwt__Secret
var jwtSecret = builder.Configuration["Jwt:Secret"];
if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Length < 32)
    throw new InvalidOperationException(
        "Jwt:Secret is missing or too short (min 32 chars). " +
        "Set it via Azure environment variable Jwt__Secret. " +
        "For local dev, add it to appsettings.Development.json.");

var jwtIssuer   = builder.Configuration["Jwt:Issuer"]!;
var jwtAudience = builder.Configuration["Jwt:Audience"]!;

builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme    = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = jwtIssuer,
            ValidAudience            = jwtAudience,
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew                = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

// â”€â”€ CORS â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
                      ?? ["http://localhost:3000",
                          "https://dressfield-ga8o-git-main-dressfield.vercel.app",
                          "https://dressfield.ge"];
        policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
    });
});

// â”€â”€ Rate Limiting â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
builder.Services.AddRateLimiter(options =>
{
    // Auth endpoints â€” 10 requests per minute per IP (prevents brute-force)
    options.AddFixedWindowLimiter("auth", o =>
    {
        o.Window              = TimeSpan.FromMinutes(1);
        o.PermitLimit         = 10;
        o.QueueLimit          = 0;
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });

    // Order creation â€” 20 requests per minute per IP (prevents order flooding)
    options.AddFixedWindowLimiter("orders", o =>
    {
        o.Window      = TimeSpan.FromMinutes(1);
        o.PermitLimit = 20;
        o.QueueLimit  = 0;
    });

    options.AddFixedWindowLimiter("upload", o =>
    {
        o.Window      = TimeSpan.FromMinutes(1);
        o.PermitLimit = 12;
        o.QueueLimit  = 0;
    });

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// â”€â”€ Application Services â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
var smtpHost = builder.Configuration["Smtp:Host"];
if (string.IsNullOrWhiteSpace(smtpHost))
{
    builder.Services.AddScoped<IEmailService, DevEmailService>();
    Log.Warning("Smtp:Host is not configured. Using DevEmailService (emails are logged only).");
}
else
{
    builder.Services.AddScoped<IEmailService, SmtpEmailService>();
    Log.Information("Using SmtpEmailService for outbound emails.");
}

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<ICustomOrderService, CustomOrderService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<ICartService, CartService>();
builder.Services.AddScoped<IAdminDashboardService, AdminDashboardService>();
builder.Services.AddHostedService<EmailOutboxWorker>();

// Storage service â€” Azure Blob in production, local filesystem only in development
var azureConnectionString = builder.Configuration["AzureStorage:ConnectionString"];
if (string.IsNullOrWhiteSpace(azureConnectionString))
{
    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "AzureStorage:ConnectionString is required outside development. " +
            "Set it via Azure environment variable AzureStorage__ConnectionString.");
    }

    builder.Services.AddScoped<IStorageService, LocalStorageService>();
    Log.Warning("AzureStorage:ConnectionString is not configured. Using LocalStorageService (development only).");
}
else
{
    builder.Services.AddScoped<IStorageService, AzureBlobStorageService>();
    Log.Information("Using AzureBlobStorageService for uploads.");
}

var clamEnabled = builder.Configuration.GetValue<bool>("Security:ClamAv:Enabled");
if (clamEnabled)
{
    var clamHost = builder.Configuration["Security:ClamAv:Host"];
    if (string.IsNullOrWhiteSpace(clamHost))
    {
        throw new InvalidOperationException(
            "Security:ClamAv:Host must be set when malware scanning is enabled.");
    }

    builder.Services.AddScoped<IFileSecurityScanner, ClamAvFileSecurityScanner>();
    Log.Information("ClamAV file scanning is enabled.");
}
else
{
    builder.Services.AddScoped<IFileSecurityScanner, NoOpFileSecurityScanner>();
    if (!builder.Environment.IsDevelopment())
    {
        Log.Warning("ClamAV scanning is disabled. Set Security:ClamAv:Enabled=true in production.");
    }
}

// Payment service â€” real BOG iPay in prod, mock in dev
var bogClientId = builder.Configuration["BogIPay:ClientId"];
if (string.IsNullOrWhiteSpace(bogClientId))
{
    builder.Services.AddScoped<IPaymentService, MockPaymentService>();
}
else
{
    builder.Services.AddHttpClient<BogIPayService>();
    builder.Services.AddScoped<IPaymentService, BogIPayService>();
}

builder.Services.AddValidatorsFromAssemblyContaining<Dressfield.Application.DTOs.RegisterRequest>();
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// â”€â”€ Middleware pipeline â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

// Must be first â€” resolve real client IP from Azure load balancer
app.UseForwardedHeaders();
app.UseExceptionHandler();

if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Dressfield API v1");
        c.RoutePrefix = string.Empty;
    });
}

if (!app.Environment.IsDevelopment())
{
    // Azure App Service terminates TLS at the edge, but enforce at app layer too
    app.UseHttpsRedirection();
}

// Security response headers (applied to every response)
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";          // No MIME sniffing
    headers["X-Frame-Options"]        = "DENY";             // No clickjacking
    headers["Referrer-Policy"]        = "strict-origin-when-cross-origin";
    headers["X-XSS-Protection"]       = "0";                // Disable legacy XSS auditor
    headers["Permissions-Policy"]     = "geolocation=(), camera=(), microphone=()";
    await next();
});

app.UseCors();
app.UseRateLimiter();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/api/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";

        var payload = new
        {
            status = report.Status.ToString(),
            timestamp = DateTime.UtcNow,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                durationMs = entry.Value.Duration.TotalMilliseconds
            })
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    },
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
});

static bool IsDatabaseUnavailable(Exception exception)
{
    Exception? current = exception;
    while (current is not null)
    {
        if (current is DbException or SocketException or TimeoutException)
            return true;

        current = current.InnerException;
    }

    return false;
}

// â”€â”€ Database seed (roles + first admin account) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
try
{
    using var scope       = app.Services.CreateScope();
    var db                = scope.ServiceProvider.GetRequiredService<DressfieldDbContext>();
    var passwordHasher    = scope.ServiceProvider.GetRequiredService<IPasswordHasher<ApplicationUser>>();
    var roleManager       = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager       = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    await db.Database.MigrateAsync();

    string[] roles = ["Admin", "Customer"];
    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }

    var adminEmail    = builder.Configuration["Admin:Email"] ?? "admin@dressfield.ge";
    var adminPassword = builder.Configuration["Admin:Password"];
    var resetExistingAdminPassword = builder.Configuration.GetValue<bool>("Admin:ResetExistingPassword");

    if (string.IsNullOrWhiteSpace(adminPassword))
    {
        if (app.Environment.IsDevelopment())
        {
            // Acceptable dev default â€” never reaches production
            adminPassword = "Admin123!@#";
            Log.Warning("Admin:Password not configured â€” using dev default. Never deploy this to production.");
        }
        else
        {
            // Hard fail in production â€” no fallback password ever
            throw new InvalidOperationException(
                "Admin:Password must be set in production via Azure environment variable Admin__Password.");
        }
    }

    var admin = await userManager.FindByEmailAsync(adminEmail);
    if (admin == null)
    {
        admin = new ApplicationUser
        {
            UserName       = adminEmail,
            Email          = adminEmail,
            FirstName      = "Admin",
            LastName       = "Dressfield",
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(admin, adminPassword);
        if (result.Succeeded)
        {
            if (!await userManager.IsInRoleAsync(admin, "Admin"))
                await userManager.AddToRoleAsync(admin, "Admin");
        }
        else
            Log.Error("Failed to create admin user: {Errors}", string.Join(", ", result.Errors.Select(e => e.Description)));
    }
    else
    {
        if (!await userManager.IsInRoleAsync(admin, "Admin"))
            await userManager.AddToRoleAsync(admin, "Admin");

        if (resetExistingAdminPassword)
        {
            admin.PasswordHash = passwordHasher.HashPassword(admin, adminPassword);
            admin.SecurityStamp = Guid.NewGuid().ToString();

            var result = await userManager.UpdateAsync(admin);
            if (!result.Succeeded)
                throw new InvalidOperationException(
                    $"Failed to persist existing admin password reset: {string.Join(", ", result.Errors.Select(e => e.Description))}");

            var activeRefreshTokens = await db.RefreshTokens
                .Where(token => token.UserId == admin.Id && !token.IsRevoked)
                .ToListAsync();

            foreach (var token in activeRefreshTokens)
                token.IsRevoked = true;

            await db.SaveChangesAsync();
            Log.Warning("Existing admin password was reset from configuration and active refresh tokens were revoked.");
        }
    }
}
catch (Exception ex) when (IsDatabaseUnavailable(ex))
{
    Log.Warning(ex, "Database unavailable during startup. Continuing without migration/seed.");
}
catch
{
    throw;
}

app.Run();

