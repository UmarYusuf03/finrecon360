using finrecon360_backend.Authorization;
using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Auth;
using finrecon360_backend.Models;
using finrecon360_backend.Services;
using finrecon360_backend.Options;
using finrecon360_backend.Services.BankAccounts;
using finrecon360_backend.Services.Transactions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;

LoadDotEnv(Path.Combine(Directory.GetCurrentDirectory(), ".env"));


static void LoadDotEnv(string filePath)
{
    if (!File.Exists(filePath))
    {
        return;
    }

    foreach (var line in File.ReadAllLines(filePath))
    {
        var trimmed = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
        {
            continue;
        }

        var separatorIndex = trimmed.IndexOf('=');
        if (separatorIndex <= 0)
        {
            continue;
        }

        var key = trimmed.Substring(0, separatorIndex).Trim();
        var value = trimmed.Substring(separatorIndex + 1).Trim();

        if ((value.StartsWith('"') && value.EndsWith('"')) || (value.StartsWith("'") && value.EndsWith("'")))
        {
            value = value.Substring(1, value.Length - 2);
        }

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}

var builder = WebApplication.CreateBuilder(args);

// 1. Bind JWT settings
builder.Services.Configure<JwtSettings>(
    builder.Configuration.GetSection("Jwt"));

// 2. Add DbContext (SQL Server)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("DefaultConnection is not configured.");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

// 3. Register JWT token service
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();

builder.Services.Configure<BrevoOptions>(options =>
{
    options.ApiKey = builder.Configuration["BREVO_API_KEY"] ?? string.Empty;
    options.SenderEmail = builder.Configuration["BREVO_SENDER_EMAIL"] ?? string.Empty;
    options.SenderName = builder.Configuration["BREVO_SENDER_NAME"] ?? string.Empty;
    options.TemplateIdMagicLinkVerify = builder.Configuration.GetValue<long>("BREVO_TEMPLATE_ID_MAGICLINK_VERIFY");
    var inviteTemplate = builder.Configuration.GetValue<long?>("BREVO_TEMPLATE_ID_MAGICLINK_INVITE");
    options.TemplateIdMagicLinkInvite = inviteTemplate is > 0 ? inviteTemplate : null;
    options.TemplateIdMagicLinkReset = builder.Configuration.GetValue<long>("BREVO_TEMPLATE_ID_MAGICLINK_RESET");
    var changeTemplate = builder.Configuration.GetValue<long?>("BREVO_TEMPLATE_ID_MAGICLINK_CHANGE");
    options.TemplateIdMagicLinkChange = changeTemplate is > 0 ? changeTemplate : null;
});

builder.Services.Configure<MagicLinkOptions>(options =>
{
    options.ExpiresMinutes = builder.Configuration.GetValue<int>("MAGICLINK_EXPIRES_MINUTES", 10);
    options.MaxAttempts = builder.Configuration.GetValue<int>("MAGICLINK_MAX_ATTEMPTS", 5);
    options.ResendCooldownSeconds = builder.Configuration.GetValue<int>("MAGICLINK_RESEND_COOLDOWN_SECONDS", 60);
    options.FrontendBaseUrl = builder.Configuration["FRONTEND_BASE_URL"] ?? string.Empty;
});

builder.Services.Configure<TenantProvisioningOptions>(options =>
{
    options.DefaultConnectionString = builder.Configuration["TENANT_DB_TEMPLATE"]
        ?? builder.Configuration["TENANT_DB_DEFAULT"];
});

builder.Services.Configure<PayHereOptions>(options =>
{
    options.MerchantId = builder.Configuration["PAYHERE_MERCHANT_ID"] ?? string.Empty;
    options.MerchantSecret = builder.Configuration["PAYHERE_MERCHANT_SECRET"] ?? string.Empty;
    options.CheckoutBaseUrl = builder.Configuration["PAYHERE_CHECKOUT_BASE_URL"] ?? "https://sandbox.payhere.lk/pay/checkout";
    options.ReturnUrl = builder.Configuration["PAYHERE_RETURN_URL"] ?? string.Empty;
    options.CancelUrl = builder.Configuration["PAYHERE_CANCEL_URL"] ?? string.Empty;
    options.NotifyUrl = builder.Configuration["PAYHERE_NOTIFY_URL"] ?? string.Empty;
    options.Currency = builder.Configuration["PAYHERE_CURRENCY"] ?? "LKR";
});

builder.Services.Configure<OnboardingTokenOptions>(options =>
{
    options.Issuer = builder.Configuration["ONBOARDING_TOKEN_ISSUER"] ?? "finrecon360";
    options.Audience = builder.Configuration["ONBOARDING_TOKEN_AUDIENCE"] ?? "onboarding";
    options.ExpiresMinutes = builder.Configuration.GetValue<int>("ONBOARDING_TOKEN_EXPIRES_MINUTES", 20);
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IUserContext, UserContext>();
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<IMagicLinkService, MagicLinkService>();
builder.Services.AddScoped<IAuditLogger, AuditLogger>();
builder.Services.AddSingleton<IPasswordHasher, Sha256PasswordHasher>();
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<ITenantProvisioner, DefaultTenantProvisioner>();
builder.Services.AddScoped<ITenantDbProtector, TenantDbProtector>();
builder.Services.AddScoped<ITenantDbResolver, TenantDbResolver>();
builder.Services.AddScoped<ITenantDbContextFactory, TenantDbContextFactory>();
builder.Services.AddSingleton<ITenantSchemaMigrator, SqlServerTenantSchemaMigrator>();
builder.Services.AddScoped<ITenantUserDirectoryService, TenantUserDirectoryService>();
builder.Services.AddScoped<ISystemEnforcementService, SystemEnforcementService>();
builder.Services.AddScoped<IOnboardingTokenService, OnboardingTokenService>();
builder.Services.AddScoped<IOnboardingMagicLinkService, OnboardingMagicLinkService>();
builder.Services.AddScoped<IPayHereCheckoutService, PayHereCheckoutService>();
builder.Services.AddScoped<IPaymentCheckoutService, PaymentCheckoutService>();
builder.Services.AddScoped<IImportFileParser, ImportFileParser>();
builder.Services.AddScoped<IImportNormalizationService, ImportNormalizationService>();
builder.Services.AddSingleton<IReconciliationOrchestrator, ReconciliationOrchestrator>();
builder.Services.AddScoped<IReconciliationExecutionService, ReconciliationExecutionService>();
builder.Services.AddScoped<BankAccountService>();
builder.Services.AddScoped<TransactionService>();

builder.Services.AddDataProtection()
    .SetApplicationName("finrecon360-backend");

builder.Services.AddControllers();

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value!.Errors.Select(e => e.ErrorMessage).ToArray());

        return new BadRequestObjectResult(new { message = "Validation failed.", errors });
    };
});

builder.Services.AddHttpClient<IEmailSender, BrevoEmailSender>(client =>
{
    client.BaseAddress = new Uri("https://api.brevo.com/v3/");
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    var adminPermitLimit = builder.Configuration.GetValue<int>("RATE_LIMIT_ADMIN_PER_MINUTE", 120);
    options.AddPolicy("admin", context =>
    {
        var key = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.User?.FindFirst(ClaimTypes.Email)?.Value
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = adminPermitLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });
    });
    options.AddPolicy("auth-link", context =>
    {
        var key = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.User?.FindFirst(ClaimTypes.Email)?.Value
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 3,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });
    });
    options.AddPolicy("auth-confirm", context =>
    {
        var key = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.User?.FindFirst(ClaimTypes.Email)?.Value
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });
    });
    options.AddPolicy("me", context =>
    {
        var key = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.User?.FindFirst(ClaimTypes.Email)?.Value
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });
    });
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
    {
        var origin = builder.Configuration["FRONTEND_BASE_URL"] ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(origin))
        {
            policy.WithOrigins(origin.TrimEnd('/'))
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});

// 4. Authentication & Authorization
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSection["Key"];
var jwtIssuer = jwtSection["Issuer"];
var jwtAudience = jwtSection["Audience"];
var isTesting = builder.Environment.IsEnvironment("Testing");

if (string.IsNullOrWhiteSpace(jwtKey))
{
    if (isTesting)
    {
        jwtKey = "test-signing-key-should-be-long-32chars";
    }
    else
    {
        throw new InvalidOperationException("Jwt:Key is not configured.");
    }
}

if (string.IsNullOrWhiteSpace(jwtIssuer))
{
    if (isTesting)
    {
        jwtIssuer = "test-issuer";
    }
    else
    {
        throw new InvalidOperationException("Jwt:Issuer is not configured.");
    }
}

if (string.IsNullOrWhiteSpace(jwtAudience))
{
    if (isTesting)
    {
        jwtAudience = "test-audience";
    }
    else
    {
        throw new InvalidOperationException("Jwt:Audience is not configured.");
    }
}

/// <summary>
/// WHY: Configures the JWT authentication scheme with token validation parameters.
/// During request processing, JwtBearerEvents.OnTokenValidated enforces that the token's subject (user ID) belongs
/// to an active, non-suspended user. Tenant-specific access rules are enforced downstream in controllers/handlers
/// using the PermissionHandler, allowing decoupling of global user status checks from tenant-scoped permissions.
/// </summary>
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = !isTesting,
            ValidateAudience = !isTesting,
            ValidateIssuerSigningKey = !isTesting,
            ValidateLifetime = true,
            ValidIssuer = isTesting ? null : jwtIssuer,
            ValidAudience = isTesting ? null : jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromSeconds(30) // small tolerance
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var userIdValue = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub)
                    ?? context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!Guid.TryParse(userIdValue, out var userId))
                {
                    context.Fail("Invalid token subject.");
                    return;
                }

                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId);
                if (user == null || !user.IsActive || user.Status == UserStatus.Suspended || user.Status == UserStatus.Banned)
                {
                    context.Fail("User is not active.");
                    return;
                }

                // Tenant-specific checks are enforced in endpoint authorization logic.
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();

var app = builder.Build();

app.UseCors("frontend");

if (!app.Environment.IsEnvironment("DesignTime") && !app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.IsRelational())
    {
        await db.Database.MigrateAsync();
    }
    await DbSeeder.SeedAsync(db);
}

if (app.Environment.IsEnvironment("DesignTime"))
{
    return;
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseHttpsRedirection();
}

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exceptionFeature = context.Features.Get<IExceptionHandlerFeature>();
        var loggerFactory = context.RequestServices.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("GlobalExceptionHandler");
        if (exceptionFeature?.Error is not null)
        {
            logger.LogError(exceptionFeature.Error, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        if (app.Environment.IsDevelopment())
        {
            await context.Response.WriteAsJsonAsync(new
            {
                message = "An unexpected error occurred.",
                detail = exceptionFeature?.Error?.Message,
                path = context.Request.Path.Value
            });
            return;
        }

        await context.Response.WriteAsJsonAsync(new { message = "An unexpected error occurred." });
    });
});

app.UseStatusCodePages(async context =>
{
    var status = context.HttpContext.Response.StatusCode;
    if (status >= 400 && context.HttpContext.Response.ContentLength is null)
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync($"{{\"message\":\"Request failed.\",\"statusCode\":{status}}}");
    }
});

app.UseRateLimiter();

// IMPORTANT: auth before endpoints
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

#region Auth Endpoints

app.MapPost("/api/auth/register", async (
    [FromBody] RegisterRequest request,
    AppDbContext db,
    IPasswordHasher passwordHasher,
    IMagicLinkService magicLinkService,
    IEmailSender emailSender,
    IAuditLogger auditLogger,
    IOptions<BrevoOptions> brevoOptions,
    IOptions<MagicLinkOptions> magicLinkOptions,
    HttpContext httpContext) =>
{
    if (await db.Users.AnyAsync(u => u.Email == request.Email))
    {
        return Results.BadRequest(new { message = "Email already registered." });
    }

    if (request.Password != request.ConfirmPassword)
    {
        return Results.BadRequest(new { message = "Passwords do not match." });
    }

    var user = new User
    {
        UserId = Guid.NewGuid(),
        Email = request.Email,
        FirstName = request.FirstName,
        LastName = request.LastName,
        Country = request.Country,
        Gender = request.Gender,
        DisplayName = $"{request.FirstName} {request.LastName}".Trim(),
        PasswordHash = passwordHasher.Hash(request.Password),
        CreatedAt = DateTime.UtcNow,
        EmailConfirmed = false,
        IsActive = true,
        Status = UserStatus.Active,
        UserType = UserType.GlobalPublic
    };

    db.Users.Add(user);
    await db.SaveChangesAsync();

    var token = await magicLinkService.CreateTokenAsync(user.Email, user.UserId, MagicLinkPurpose.EmailVerify, httpContext.Connection.RemoteIpAddress?.ToString());
    if (token != null)
    {
        var baseUrl = magicLinkOptions.Value.FrontendBaseUrl.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(baseUrl) && brevoOptions.Value.TemplateIdMagicLinkVerify > 0)
        {
            var magicLink = $"{baseUrl}/auth/magic-link?purpose={MagicLinkPurpose.EmailVerify}&token={token.Token}";
            var parameters = new Dictionary<string, object>
            {
                ["magicLink"] = magicLink,
                ["expiresInMinutes"] = magicLinkOptions.Value.ExpiresMinutes
            };
            await emailSender.SendTemplateAsync(user.Email, brevoOptions.Value.TemplateIdMagicLinkVerify, parameters);
        }
    }

    await auditLogger.LogAsync(user.UserId, "MagicLinkRequested", "AuthActionToken", null, "purpose=EmailVerify");

    return Results.Ok(new { message = "User registered successfully." });
});

app.MapPost("/api/auth/login", async (
    [FromBody] LoginRequest request,
    AppDbContext db,
    IJwtTokenService jwtTokenService,
    IPasswordHasher passwordHasher) =>
{
    var user = await db.Users
        .FirstOrDefaultAsync(u => u.Email == request.Email);

    if (user is null || !user.IsActive || user.Status != UserStatus.Active || !passwordHasher.Verify(request.Password, user.PasswordHash))
    {
        return Results.BadRequest(new { message = "Invalid email or password." });
    }

    var token = jwtTokenService.GenerateToken(user);

    return Results.Ok(new LoginResponse
    {
        Email = user.Email,
        FullName = $"{user.FirstName} {user.LastName}".Trim(),
        Token = token
    });
});

#endregion

#region Dashboard Endpoint (Protected)

app.MapGet("/api/dashboard/summary", () =>
{
    var summary = new DashboardSummary
    {
        TotalAccounts = 128,
        PendingReconciliations = 14,
        CompletedToday = 32,
        Alerts = 3,
        LastUpdatedUtc = DateTime.UtcNow
    };

    return Results.Ok(summary);
})
.RequireAuthorization($"{finrecon360_backend.Authorization.PermissionPolicyProvider.PolicyPrefix}ADMIN.DASHBOARD.VIEW");   // 🔒 tenant permission required

#endregion

app.Run();

public partial class Program { }
