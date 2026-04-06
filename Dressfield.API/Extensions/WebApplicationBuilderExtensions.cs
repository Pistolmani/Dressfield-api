using System.Text;
using System.Threading.RateLimiting;
using Dressfield.Application.Interfaces;
using Dressfield.Core.Entities;
using Dressfield.Core.Interfaces;
using Dressfield.Infrastructure.Data;
using Dressfield.Infrastructure.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;

namespace Dressfield.API.Extensions;

public static class WebApplicationBuilderExtensions
{
    public static WebApplicationBuilder AddDressfieldLogging(this WebApplicationBuilder builder)
    {
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(builder.Configuration)
            .WriteTo.Console()
            .WriteTo.File("logs/dressfield-.log", rollingInterval: RollingInterval.Day)
            .CreateLogger();

        builder.Host.UseSerilog();
        return builder;
    }

    public static WebApplicationBuilder AddDressfieldProxying(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
        });

        return builder;
    }

    public static WebApplicationBuilder AddDressfieldRequestLimits(this WebApplicationBuilder builder)
    {
        builder.WebHost.ConfigureKestrel(options =>
            options.Limits.MaxRequestBodySize = 20 * 1024 * 1024);

        return builder;
    }

    public static WebApplicationBuilder AddDressfieldPersistence(this WebApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
        builder.Services.AddDbContext<DressfieldDbContext>(options =>
            options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 36))));

        builder.Services.AddHealthChecks()
            .AddDbContextCheck<DressfieldDbContext>("database");

        return builder;
    }

    public static WebApplicationBuilder AddDressfieldIdentityAndAuth(this WebApplicationBuilder builder)
    {
        builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
            {
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequiredLength = 8;
                options.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<DressfieldDbContext>()
            .AddDefaultTokenProviders();

        var jwtSecret = builder.Configuration["Jwt:Secret"];
        if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Length < 32)
        {
            throw new InvalidOperationException(
                "Jwt:Secret is missing or too short (min 32 chars). " +
                "Set it via Azure environment variable Jwt__Secret. " +
                "For local dev, add it to appsettings.Development.json.");
        }

        var jwtIssuer = builder.Configuration["Jwt:Issuer"]!;
        var jwtAudience = builder.Configuration["Jwt:Audience"]!;

        builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtIssuer,
                    ValidAudience = jwtAudience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
                    ClockSkew = TimeSpan.Zero
                };
            });

        builder.Services.AddAuthorization();
        return builder;
    }

    public static WebApplicationBuilder AddDressfieldCors(this WebApplicationBuilder builder)
    {
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
                              ?? ["http://localhost:3000"];
                policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
            });
        });

        return builder;
    }

    public static WebApplicationBuilder AddDressfieldRateLimiting(this WebApplicationBuilder builder)
    {
        builder.Services.AddRateLimiter(options =>
        {
            options.AddPolicy("auth", context =>
            {
                var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                return RateLimitPartition.GetFixedWindowLimiter(ipAddress, _ => new FixedWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromMinutes(1),
                    PermitLimit = 10,
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                });
            });

            options.AddPolicy("orders", context =>
            {
                var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                return RateLimitPartition.GetFixedWindowLimiter(ipAddress, _ => new FixedWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromMinutes(1),
                    PermitLimit = 20,
                    QueueLimit = 0
                });
            });

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        });

        return builder;
    }

    public static WebApplicationBuilder AddDressfieldApplicationServices(this WebApplicationBuilder builder)
    {
        var smtpHost = builder.Configuration["Smtp:Host"];
        if (string.IsNullOrWhiteSpace(smtpHost))
        {
            builder.Services.AddScoped<IEmailService, DevEmailService>();
        }
        else
        {
            builder.Services.AddScoped<IEmailService, SmtpEmailService>();
        }

        builder.Services.AddScoped<IAuthService, AuthService>();
        builder.Services.AddScoped<IProductService, ProductService>();
        builder.Services.AddScoped<ICustomOrderService, CustomOrderService>();
        builder.Services.AddScoped<IOrderService, OrderService>();
        builder.Services.AddScoped<IPromoCodeService, PromoCodeService>();

        var azureConnectionString = builder.Configuration["AzureStorage:ConnectionString"];
        if (string.IsNullOrWhiteSpace(azureConnectionString))
        {
            builder.Services.AddScoped<IStorageService, LocalStorageService>();
        }
        else
        {
            builder.Services.AddScoped<IStorageService, AzureBlobStorageService>();
        }

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

        builder.Services.AddHostedService<EmailOutboxWorker>();
        return builder;
    }

    public static WebApplicationBuilder AddDressfieldApi(this WebApplicationBuilder builder)
    {
        builder.Services.AddValidatorsFromAssemblyContaining<Dressfield.Application.DTOs.RegisterRequest>();
        builder.Services.AddControllers();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        return builder;
    }
}
