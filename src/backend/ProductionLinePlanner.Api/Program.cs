using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Application.Requests;
using ProductionLinePlanner.Application.Realtime;
using ProductionLinePlanner.Application.Notifications;
using ProductionLinePlanner.Application.Workers;
using ProductionLinePlanner.Api.Security;
using ProductionLinePlanner.Api.Authorization;
using ProductionLinePlanner.Api.Bootstrap;
using ProductionLinePlanner.Api.Database;
using ProductionLinePlanner.Api.Diagnostics;
using ProductionLinePlanner.Api.Endpoints;
using ProductionLinePlanner.Api.Realtime;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Domain.Authorization;
using ProductionLinePlanner.Infrastructure;
using ProductionLinePlanner.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);
var isEfDesignTime = AppDomain.CurrentDomain.GetAssemblies()
    .Any(assembly => string.Equals(assembly.GetName().Name, "Microsoft.EntityFrameworkCore.Design", StringComparison.Ordinal));
var allowedCorsOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>()?
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Select(origin => origin.Trim())
    .Where(origin => builder.Environment.IsDevelopment()
        || origin.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || (builder.Configuration.GetValue("Cors:AllowInsecureHttpOrigins", false)
            && origin.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray() ?? Array.Empty<string>();
var allowedMethods = builder.Configuration
    .GetSection("Cors:AllowedMethods")
    .Get<string[]>() ?? ["GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"];
var allowedHeaders = builder.Configuration
    .GetSection("Cors:AllowedHeaders")
    .Get<string[]>() ?? ["Accept", "Content-Type", "Authorization", "X-Requested-With", "X-SignalR-User-Agent", ManufacturingRealtimeHeaders.CorrelationId];
var corsAllowCredentials = builder.Configuration.GetValue("Cors:AllowCredentials", false);
var enableHsts = builder.Configuration.GetValue("Hosting:EnableHsts", !builder.Environment.IsDevelopment());
var enableHttpsRedirection = builder.Configuration.GetValue("Hosting:EnableHttpsRedirection", true);
var rateLimitWindowSeconds = Math.Max(15, builder.Configuration.GetValue("Security:RateLimit:WindowSeconds", 60));
var rateLimitPermitLimit = Math.Max(1, builder.Configuration.GetValue("Security:RateLimit:PermitLimit", 120));
var criticalProductionPermitLimit = Math.Max(1, builder.Configuration.GetValue("Security:RateLimit:CriticalProductionPermitLimit", rateLimitPermitLimit));
var workerPhotoPermitLimit = Math.Max(1, builder.Configuration.GetValue("Security:RateLimit:WorkerPhotoPermitLimit", 120));
var workerPhotoWritePermitLimit = Math.Max(1, builder.Configuration.GetValue("Security:RateLimit:WorkerPhotoWritePermitLimit", 20));
var normalReadPermitLimit = Math.Max(1, builder.Configuration.GetValue("Security:RateLimit:NormalReadPermitLimit", 240));
const string SecurityCorsPolicy = "ProductionLinePlannerCors";
const string SecurityBootstrapEndpoint = "/api/admin/bootstrap";
var jwtSection = builder.Configuration.GetSection("Authentication:Jwt");
var jwtIssuer = jwtSection["Issuer"] ?? "ProductionLinePlanner.Api";
var jwtAudience = jwtSection["Audience"] ?? "ProductionLinePlanner.WebClient";
var jwtAccessTokenMinutes = Math.Max(15, builder.Configuration.GetValue("Authentication:Jwt:AccessTokenMinutes", 45));
var jwtRefreshTokenDays = Math.Max(1, builder.Configuration.GetValue("Authentication:Jwt:RefreshTokenDays", 14));
var jwtSigningKey = jwtSection["SigningKey"];
var bootstrapSecret = builder.Configuration["Bootstrap:Secret"];
if (string.IsNullOrWhiteSpace(jwtSigningKey) ||
    jwtSigningKey.Contains("REPLACE_WITH_", StringComparison.OrdinalIgnoreCase) ||
    jwtSigningKey.Contains("USER_SECRET", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException("Authentication:Jwt:SigningKey must be configured via configuration and cannot be a placeholder.");
}

if (Encoding.UTF8.GetByteCount(jwtSigningKey) < 64)
{
    throw new InvalidOperationException("Authentication:Jwt:SigningKey must be at least 64 bytes. Configure it via appsettings or environment variables.");
}

var jwtKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddOptions<DatabaseMigrationOptions>()
    .Bind(builder.Configuration.GetSection(DatabaseMigrationOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<DatabaseMigrationOptions>, DatabaseMigrationOptionsValidator>();

builder.Services.AddCors(options =>
{
    options.AddPolicy(
        SecurityCorsPolicy,
        policy =>
        {
            // Echo only the exact requesting origin when it is explicitly
            // configured. This keeps LAN and loopback development origins
            // distinct and leaves production restricted to configured HTTPS origins.
            policy.SetIsOriginAllowed(origin => allowedCorsOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase));

            if (allowedMethods.Length > 0)
            {
                policy.WithMethods(allowedMethods);
            }

            if (allowedHeaders.Length > 0)
            {
                policy.WithHeaders(allowedHeaders);
            }

            if (corsAllowCredentials)
            {
                policy.AllowCredentials();
            }
            else
            {
                policy.DisallowCredentials();
            }

            if (builder.Environment.IsDevelopment())
            {
                // Lets a locally hosted browser correlate the Network entry
                // with the Development-only endpoint routing evidence.
                policy.WithExposedHeaders(
                    PreviewRequestRoutingDiagnostics.RequestIdHeader,
                    PreviewRequestRoutingDiagnostics.EndpointHeader,
                    PreviewRequestRoutingDiagnostics.SelectedMethodsHeader,
                    PreviewRequestRoutingDiagnostics.CandidateMethodsHeader);
            }
        });
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(ApiRateLimitPolicies.CriticalProductionWrite, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetRateLimitPartitionKey(context),
            _ => FixedWindowOptions(criticalProductionPermitLimit, rateLimitWindowSeconds)));
    options.AddPolicy(ApiRateLimitPolicies.WorkerPhotoRead, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetRateLimitPartitionKey(context),
            _ => FixedWindowOptions(workerPhotoPermitLimit, rateLimitWindowSeconds)));
    options.AddPolicy(ApiRateLimitPolicies.WorkerPhotoWrite, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetRateLimitPartitionKey(context),
            _ => FixedWindowOptions(workerPhotoWritePermitLimit, rateLimitWindowSeconds)));
    options.AddPolicy(ApiRateLimitPolicies.NormalRead, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetRateLimitPartitionKey(context),
            _ => FixedWindowOptions(normalReadPermitLimit, rateLimitWindowSeconds)));
    options.OnRejected = async (context, _) =>
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too Many Requests",
            Detail = "Rate limit exceeded. Please retry later.",
            Instance = context.HttpContext.Request.Path,
            Type = "https://tools.ietf.org/html/rfc6585#section-4"
        };
        problem.Extensions["code"] = "RateLimitExceeded";
        problem.Extensions["retryAfterSeconds"] = rateLimitWindowSeconds;
        context.HttpContext.Response.Headers["Retry-After"] = rateLimitWindowSeconds.ToString();
        await context.HttpContext.Response.WriteAsJsonAsync(problem);
    };
});

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IManufacturingRealtimeCorrelationContext, HttpManufacturingRealtimeCorrelationContext>();
builder.Services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddSingleton<IUserPasswordHasher, UserPasswordHasher>();
builder.Services.AddSignalR(options => options.EnableDetailedErrors = false);
builder.Services.AddSingleton<IUserIdProvider, AuthenticatedUserIdProvider>();
builder.Services.AddScoped<INotificationLiveDispatcher, SignalRNotificationLiveDispatcher>();
builder.Services.AddScoped<IManufacturingDataChangePublisher, SignalRManufacturingDataChangePublisher>();
builder.Services.AddScoped<IStartupDatabaseMigrationExecutor, EfCoreStartupDatabaseMigrationExecutor>();
builder.Services.AddScoped<StartupDatabaseMigrationRunner>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = jwtKey,
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            RoleClaimType = ClaimTypes.Role
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var hubAccessToken = RealtimeAccessTokenResolver.Resolve(
                    context.HttpContext.Request.Path,
                    context.HttpContext.Request.Query);
                if (!string.IsNullOrWhiteSpace(hubAccessToken))
                {
                    context.Token = hubAccessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SuperAdmin", policy =>
    {
        policy.RequireRole(UserRole.SuperAdmin.ToString());
    });

    options.AddPolicy("Admin", policy =>
    {
        policy.RequireRole(UserRole.Admin.ToString(), UserRole.SuperAdmin.ToString());
    });

    options.AddPermissionPolicies();
});

var app = builder.Build();

if (ZkTimeWorkerSchemaInspectionCommand.IsRequested(args))
{
    try
    {
        await ZkTimeWorkerSchemaInspectionCommand.ExecuteAsync(app);
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"ZKTime worker schema inspection failed: {exception.Message}");
        Environment.ExitCode = 1;
    }
    await app.DisposeAsync();
    return;
}

if (WorkerActiveServiceSyncCommand.IsRequested(args))
{
    try
    {
        await WorkerActiveServiceSyncCommand.ExecuteAsync(app, args);
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"Active-service worker synchronization failed: {exception.Message}");
        Environment.ExitCode = 1;
    }
    await app.DisposeAsync();
    return;
}

if (PilotMasterDataBootstrapCommand.IsRequested(args))
{
    try
    {
        await PilotMasterDataBootstrapCommand.ExecuteAsync(app, args);
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"Pilot master-data command failed: {exception.Message}");
        Environment.ExitCode = 1;
    }
    await app.DisposeAsync();
    return;
}

if (!isEfDesignTime)
{
    await using var startupScope = app.Services.CreateAsyncScope();
    var migrationRunner = startupScope.ServiceProvider.GetRequiredService<StartupDatabaseMigrationRunner>();
    await migrationRunner.ApplyIfEnabledAsync(app.Lifetime.ApplicationStopping);

    var permissionSeedService = startupScope.ServiceProvider.GetRequiredService<IRolePermissionSeedService>();
    await permissionSeedService.EnsureSeedAsync();
    var notificationPolicyReconciler = startupScope.ServiceProvider.GetRequiredService<INotificationPolicyCatalogReconciler>();
    var notificationPolicyReconciliation = await notificationPolicyReconciler.EnsureDefaultsAsync();
    if (notificationPolicyReconciliation.IsFailure)
    {
        throw new InvalidOperationException(notificationPolicyReconciliation.Error?.Message ?? "Notification policy catalog reconciliation failed.");
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandler("/api/error");

if (enableHsts)
{
    app.UseHsts();
}

if (enableHttpsRedirection)
{
    app.UseHttpsRedirection();
}
app.UseCors(SecurityCorsPolicy);
app.UseAuthentication();
app.UsePreviewRequestRoutingDiagnostics();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] =
        "camera=(), microphone=(), geolocation=(), payment=(), usb=(), magnetometer=(), gyroscope=()";
    if (enableHsts)
    {
        context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains; preload";
    }
    await next();
});
app.UseAuthorization();

app.MapHub<NotificationsHub>(
        RealtimeEndpointPaths.NotificationsHub,
        options => options.CloseOnAuthenticationExpiration = true)
    .RequireAuthorization();

var factoriesApi = app.MapGroup("/api/factories").RequireAuthorization().RequireRateLimiting(ApiRateLimitPolicies.NormalRead);
var productionLinesApi = app.MapGroup("/api/production-lines").RequireAuthorization().RequireRateLimiting(ApiRateLimitPolicies.NormalRead);
var departmentsApi = app.MapGroup("/api/departments").RequireAuthorization().RequireRateLimiting(ApiRateLimitPolicies.NormalRead);
var stagesApi = app.MapGroup("/api/stages").RequireAuthorization().RequireRateLimiting(ApiRateLimitPolicies.NormalRead);
var mainStagesApi = app.MapGroup("/api/main-stages").RequireAuthorization().RequireRateLimiting(ApiRateLimitPolicies.NormalRead);
var subStagesApi = app.MapGroup("/api/sub-stages").RequireAuthorization().RequireRateLimiting(ApiRateLimitPolicies.NormalRead);
var workersApi = app.MapGroup("/api/workers").RequireAuthorization().RequireRateLimiting(ApiRateLimitPolicies.NormalRead);
var productModelsApi = app.MapGroup("/api/product-models").RequireAuthorization().RequireRateLimiting(ApiRateLimitPolicies.NormalRead);
var workerCompensationApi = app.MapGroup("/api/workers/{workerId:guid}/compensation").RequireAuthorization().RequireRateLimiting(ApiRateLimitPolicies.NormalRead);
var assignmentsApi = app.MapGroup("/api/assignments").RequireAuthorization().RequireRateLimiting(ApiRateLimitPolicies.CriticalProductionWrite);
var lineStaffingApi = app.MapGroup("/api/line-staffing").RequireAuthorization().RequireRateLimiting(ApiRateLimitPolicies.NormalRead);
var factoryStructureApi = app.MapGroup("/api/factory-structure").RequireAuthorization().RequireRateLimiting(ApiRateLimitPolicies.CriticalProductionWrite);
var attendanceApi = app.MapGroup("/api/attendance").RequireAuthorization().RequireRateLimiting(ApiRateLimitPolicies.NormalRead);
var attendanceDepartmentsApi = app.MapGroup("/api/attendance/departments").RequireAuthorization().RequireRateLimiting(ApiRateLimitPolicies.NormalRead);
var notificationsApi = app.MapGroup("/api/notifications").RequireAuthorization().RequireRateLimiting(ApiRateLimitPolicies.NormalRead);
var readinessApi = app.MapGroup("/api/readiness").RequireAuthorization().RequireRateLimiting(ApiRateLimitPolicies.NormalRead);

lineStaffingApi.MapGet("", async (
    Guid factoryId,
    Guid productionLineId,
    Guid productModelId,
    DateOnly staffingReferenceDate,
    ILineStaffingEngine lineStaffingEngine,
    CancellationToken cancellationToken) =>
{
    var result = await lineStaffingEngine.GetLineStaffingPlanAsync(
        factoryId,
        productionLineId,
        productModelId,
        staffingReferenceDate,
        cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(
            result.Error?.Code ?? "LineStaffingReadFailed",
            result.Error?.Message ?? "Unable to load the line staffing plan.",
            MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(ApiResponse.Success(result.Value!));
})
    .RequirePermission("factory-structure.view")
    .RequirePermission("models.view")
    .RequirePermission("workers.view")
    .RequirePermission("assignments.view")
    .WithTags("Line staffing")
    .WithName("GetLineStaffingPlan");

lineStaffingApi.MapGet("/stages/{subStageId:guid}", async (
    Guid factoryId,
    Guid productionLineId,
    Guid productModelId,
    Guid subStageId,
    DateOnly staffingReferenceDate,
    ILineStaffingEngine lineStaffingEngine,
    CancellationToken cancellationToken) =>
{
    var result = await lineStaffingEngine.GetLineStaffingStageRefreshAsync(
        factoryId,
        productionLineId,
        productModelId,
        subStageId,
        staffingReferenceDate,
        cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(
            result.Error?.Code ?? "LineStaffingStageReadFailed",
            result.Error?.Message ?? "Unable to load the selected staffing stage.",
            MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(ApiResponse.Success(result.Value!));
})
    .RequirePermission("factory-structure.view")
    .RequirePermission("models.view")
    .RequirePermission("workers.view")
    .RequirePermission("assignments.view")
    .WithTags("Line staffing")
    .WithName("GetLineStaffingStageRefresh");

lineStaffingApi.MapGet("/workers", async (
    DateOnly staffingReferenceDate,
    ILineStaffingEngine lineStaffingEngine,
    CancellationToken cancellationToken) =>
{
    var result = await lineStaffingEngine.GetActiveStaffingWorkersAsync(staffingReferenceDate, cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(
            result.Error?.Code ?? "LineStaffingWorkersReadFailed",
            result.Error?.Message ?? "Unable to load active staffing workers.",
            MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(ApiResponse.Success(result.Value!));
})
    .RequirePermission("workers.view")
    .RequirePermission("assignments.view")
    .WithTags("Line staffing")
    .WithName("GetActiveLineStaffingWorkers");

app.MapGet("/api/error", (HttpContext context) =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    if (exception is null)
    {
        return Results.Problem(
            title: "An unexpected error occurred.",
            statusCode: StatusCodes.Status500InternalServerError,
            instance: context.Request.Path,
            detail: "Unhandled error path was hit without exception details.");
    }

    var isDevelopment = app.Environment.IsDevelopment();
    var traceId = Activity.Current?.Id ?? context.TraceIdentifier;
    var (statusCode, title, code) = exception switch
    {
        BadHttpRequestException => (StatusCodes.Status400BadRequest, "Validation Failed", "InvalidRequestBody"),
        UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Unauthorized", "UnauthorizedAccess"),
        System.ComponentModel.DataAnnotations.ValidationException => (StatusCodes.Status400BadRequest, "Validation Failed", "ValidationError"),
        KeyNotFoundException => (StatusCodes.Status404NotFound, "Not Found", "ResourceNotFound"),
        ProductionConflictException => (StatusCodes.Status409Conflict, "Conflict", "ProductionConflict"),
        DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Conflict", "ConcurrencyConflict"),
        _ => (StatusCodes.Status500InternalServerError, "Internal Server Error", "UnhandledError")
    };

    var problem = new ProblemDetails
    {
        Status = statusCode,
        Title = title,
        Detail = isDevelopment ? exception.Message : "An unexpected error occurred.",
        Instance = context.Request.Path
    };
    problem.Extensions["traceId"] = traceId;
    problem.Extensions["code"] = code;
    return Results.Problem(problem);
})
    .WithTags("System")
    .WithName("Error");

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    timestampUtc = DateTime.UtcNow
}))
    .WithTags("System")
    .WithName("Health")
    .DisableRateLimiting();

app.MapGet("/", () => Results.Ok("ProductionLinePlanner API is running."))
    .WithTags("System")
    .WithName("Root");

app.MapGet("/api/identity/placeholder", () => Results.Ok(new
{
    message = "Authentication is currently a placeholder.",
    note = "JWT authentication handlers will be implemented in a future sprint."
}))
    .WithTags("Identity")
    .WithName("IdentityPlaceholder");

var authApi = app.MapGroup("/api/auth");

authApi.MapPost("/login", async (
    LoginRequest request,
    AppDbContext dbContext,
    IPasswordHasher<AppUser> passwordHasher,
    IPermissionService permissionService,
    IAuditEngine auditEngine,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var loginIdentifier = request.Email?.Trim();
    var password = request.Password;

    if (string.IsNullOrWhiteSpace(loginIdentifier) || string.IsNullOrWhiteSpace(password))
    {
        return ApiResponse.Failure("ValidationError", "Login identifier and password are required.");
    }

    var user = await AuthLoginVerifier.VerifyAsync(dbContext, passwordHasher, loginIdentifier, password, cancellationToken);

    if (user is null)
    {
        return ApiResponse.Failure("InvalidCredentials", "Invalid login identifier or password.", 401);
    }

    var now = DateTime.UtcNow;
    var expiresAt = now.AddMinutes(jwtAccessTokenMinutes);
    var refreshToken = AuthTokenService.GenerateRefreshToken();
    var refreshTokenHash = AuthTokenService.HashRefreshToken(refreshToken);
    var refreshTokenExpiresAt = now.AddDays(jwtRefreshTokenDays);

    await using var loginTransaction = dbContext.Database.IsRelational()
        ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
        : null;

    await dbContext.RefreshTokens
        .Where(rt => rt.AppUserId == user.Id && !rt.IsRevoked && rt.ExpiresAtUtc > now)
        .ExecuteUpdateAsync(setters => setters
            .SetProperty(rt => rt.IsRevoked, true)
            .SetProperty(rt => rt.RevokedAtUtc, now)
            .SetProperty(rt => rt.RevokedReason, "ReplacedByLogin"), cancellationToken);

    dbContext.RefreshTokens.Add(new RefreshToken(
        id: Guid.NewGuid(),
        appUserId: user.Id,
        tokenHash: refreshTokenHash,
        expiresAtUtc: refreshTokenExpiresAt,
        createdAtUtc: now));

    var accessToken = AuthTokenService.CreateAccessToken(user, now, expiresAt, jwtIssuer, jwtAudience, jwtKey);
    await auditEngine.RecordAsync(
        user.Id,
        AuditActionType.Create,
        nameof(AppUser),
        user.Id.ToString(),
        before: null,
        after: new { Event = "AuthLogin", user.Email },
        requestMeta: AuditRequestMetadata.From(httpContext),
        cancellationToken: cancellationToken);
    await dbContext.SaveChangesAsync(cancellationToken);
    if (loginTransaction is not null)
    {
        await loginTransaction.CommitAsync(cancellationToken);
    }
    var roles = user.Roles
        .Select(role => role.Name)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(role => role)
        .ToArray();

    var response = new AuthLoginResponse
    {
        AccessToken = accessToken,
        RefreshToken = refreshToken,
        ExpiresAt = expiresAt,
        UserId = user.Id,
        IsActive = user.IsActive,
        PermissionsVersion = user.UpdatedAtUtc,
        Roles = roles,
        Permissions = (await permissionService.GetEffectivePermissionsAsync(user.Id, cancellationToken)).ToArray()
    };

    return Results.Ok(ApiResponse.Success(response));
})
    .WithTags("Auth")
    .WithName("AuthLogin");

authApi.MapGet("/me", async (
    ICurrentUserService currentUserService,
    AppDbContext dbContext,
    IPermissionService permissionService,
    CancellationToken cancellationToken) =>
{
    if (!currentUserService.IsAuthenticated || currentUserService.UserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "Unauthorized.", 401);
    }

    var user = await dbContext.AppUsers
        .Include(x => x.Roles)
        .AsNoTracking()
        .FirstOrDefaultAsync(x => x.Id == currentUserService.UserId.Value, cancellationToken);

    if (user is null || !user.IsActive)
    {
        return ApiResponse.Failure("Unauthorized", "Unauthorized.", 401);
    }

    var roles = user.Roles
        .Select(role => role.Name)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(role => role)
        .ToArray();

    return Results.Ok(ApiResponse.Success(new CurrentUserResponse
    {
        Id = user.Id,
        FullName = user.FullName,
        Email = user.Email,
        IsActive = user.IsActive,
        PermissionsVersion = user.UpdatedAtUtc,
        Roles = roles,
        Permissions = (await permissionService.GetEffectivePermissionsAsync(user.Id, cancellationToken)).ToArray()
    }));
})
    .RequireAuthorization()
    .WithTags("Auth")
    .WithName("AuthMe");

if (app.Environment.IsDevelopment())
{
    var bootstrapApi = app.MapGroup(SecurityBootstrapEndpoint);

    bootstrapApi.MapPost("/super-admin", async (
        BootstrapSuperAdminRequest request,
        AppDbContext dbContext,
        IPasswordHasher<AppUser> passwordHasher,
        CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(request.FullName?.Trim()) ||
            string.IsNullOrWhiteSpace(request.Email?.Trim()) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return ApiResponse.Failure("ValidationError", "FullName, Login identifier, and Password are required.");
        }

        var bootstrapSecretValidation = ValidateBootstrapSecret(request.BootstrapSecret, bootstrapSecret);
        if (bootstrapSecretValidation is not null)
        {
            return bootstrapSecretValidation;
        }

        if (await dbContext.AppUsers.AnyAsync(cancellationToken))
        {
            return ApiResponse.Failure("BootstrapNotAllowed", "Bootstrap is only allowed when no AppUser exists.", 409);
        }

        var fullName = request.FullName.Trim();
        var email = AppUser.NormalizeLoginIdentifier(request.Email);

        if (await dbContext.AppRoles.AnyAsync(role => role.Role == UserRole.SuperAdmin, cancellationToken) is false)
        {
            dbContext.AppRoles.Add(new AppRole(
                id: Guid.NewGuid(),
                role: UserRole.SuperAdmin,
                name: UserRole.SuperAdmin.ToString(),
                description: "System bootstrap role.",
                isSystemRole: true,
                createdAtUtc: DateTime.UtcNow));
        }

        var userId = Guid.NewGuid();
        var passwordHash = passwordHasher.HashPassword(
            new AppUser(
                id: userId,
                fullName: fullName,
                email: email,
                passwordHash: "temporary-hash",
                createdAtUtc: DateTime.UtcNow),
            request.Password);

        var superAdminUser = new AppUser(
            id: userId,
            fullName: fullName,
            email: email,
            passwordHash: passwordHash,
            isActive: true,
            preferredLanguage: "en",
            createdAtUtc: DateTime.UtcNow);

        var superAdminRole = await dbContext.AppRoles
            .FirstOrDefaultAsync(role => role.Role == UserRole.SuperAdmin, cancellationToken);
        if (superAdminRole is null)
        {
            return ApiResponse.Failure("BootstrapRoleNotCreated", "Failed to create SuperAdmin role.");
        }

        superAdminUser.AssignRole(superAdminRole);
        dbContext.AppUsers.Add(superAdminUser);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Created($"{SecurityBootstrapEndpoint}/super-admin", ApiResponse.Success(new
        {
            id = superAdminUser.Id,
            fullName = superAdminUser.FullName,
            email = superAdminUser.Email,
            roles = new[] { UserRole.SuperAdmin.ToString() }
        }, "SuperAdmin bootstrap complete."));
    })
        .WithTags("Bootstrap")
        .WithName("BootstrapSuperAdmin");

    bootstrapApi.MapPost("/reset-super-admin-password", async (
        ResetSuperAdminPasswordRequest request,
        AppDbContext dbContext,
        IPasswordHasher<AppUser> passwordHasher,
        CancellationToken cancellationToken) =>
    {
        var email = request.Email?.Trim();
        var newPassword = request.NewPassword;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(newPassword))
        {
            return ApiResponse.Failure("ValidationError", "Login identifier and NewPassword are required.");
        }

        email = AppUser.NormalizeLoginIdentifier(email);

        var bootstrapSecretValidation = ValidateBootstrapSecret(request.BootstrapSecret, bootstrapSecret);
        if (bootstrapSecretValidation is not null)
        {
            return bootstrapSecretValidation;
        }

        var user = await dbContext.AppUsers
            .Include(x => x.Roles)
            .FirstOrDefaultAsync(x => x.IsActive && x.Email == email, cancellationToken);

        if (user is null || user.Roles.All(role => role.Role != UserRole.SuperAdmin))
        {
            return ApiResponse.Failure(
                "ResetNotAllowed",
                "Unable to reset password for the requested SuperAdmin user.",
                404);
        }

        var passwordHash = passwordHasher.HashPassword(user, newPassword);
        user.ChangePasswordHash(passwordHash);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(ApiResponse.Success(null, "SuperAdmin password reset complete."));
    })
        .WithTags("Bootstrap")
        .WithName("ResetSuperAdminPassword");
}

authApi.MapPost("/refresh", async (
    RefreshTokenRequest request,
    AppDbContext dbContext,
    IPermissionService permissionService,
    IAuditEngine auditEngine,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var incomingToken = request.RefreshToken?.Trim();
    if (string.IsNullOrWhiteSpace(incomingToken))
    {
        return ApiResponse.Failure("ValidationError", "RefreshToken is required.");
    }

    var tokenHash = AuthTokenService.HashRefreshToken(incomingToken);
    var now = DateTime.UtcNow;

    var storedToken = await dbContext.RefreshTokens
        .Include(rt => rt.AppUser)
        .ThenInclude(user => user!.Roles)
        .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, cancellationToken);

    if (storedToken is null || storedToken.AppUser is null || !storedToken.AppUser.IsActive)
    {
        return ApiResponse.Failure("InvalidToken", "Invalid refresh token.", 401);
    }

    if (!storedToken.IsUsable(now))
    {
        if (storedToken.IsRevoked)
        {
            await using var reuseTransaction = dbContext.Database.IsRelational()
                ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
                : null;
            await dbContext.RefreshTokens
                .Where(token => token.AppUserId == storedToken.AppUserId && !token.IsRevoked)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(token => token.IsRevoked, true)
                    .SetProperty(token => token.RevokedAtUtc, now)
                    .SetProperty(token => token.RevokedReason, "RefreshTokenReuseDetected"), cancellationToken);
            await auditEngine.RecordAsync(
                storedToken.AppUserId,
                AuditActionType.Revoke,
                nameof(RefreshToken),
                storedToken.Id.ToString(),
                before: new { Result = "RefreshTokenReuseDetected" },
                after: new { Result = "SessionRevoked" },
                requestMeta: AuditRequestMetadata.From(httpContext),
                cancellationToken: cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (reuseTransaction is not null)
            {
                await reuseTransaction.CommitAsync(cancellationToken);
            }
        }
        if (!storedToken.IsRevoked)
        {
            storedToken.Revoke(now, "Expired");
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return ApiResponse.Failure("InvalidToken", "Invalid or expired refresh token.", 401);
    }

    var user = storedToken.AppUser;
    var roles = user.Roles
        .Select(role => role.Name)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(role => role)
        .ToArray();

    var accessTokenExpiresAt = now.AddMinutes(jwtAccessTokenMinutes);
    var accessToken = AuthTokenService.CreateAccessToken(user, now, accessTokenExpiresAt, jwtIssuer, jwtAudience, jwtKey);

    var newRefreshToken = AuthTokenService.GenerateRefreshToken();
    var newRefreshTokenHash = AuthTokenService.HashRefreshToken(newRefreshToken);
    var newRefreshTokenExpiresAt = now.AddDays(jwtRefreshTokenDays);

    await using var rotationTransaction = dbContext.Database.IsRelational()
        ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
        : null;

    var rotationSucceeded = await dbContext.RefreshTokens
        .Where(token => token.Id == storedToken.Id && !token.IsRevoked && token.ExpiresAtUtc > now)
        .ExecuteUpdateAsync(setters => setters
            .SetProperty(token => token.IsRevoked, true)
            .SetProperty(token => token.RevokedAtUtc, now)
            .SetProperty(token => token.RevokedReason, "Rotated")
            .SetProperty(token => token.LastUsedAtUtc, now), cancellationToken);
    if (rotationSucceeded != 1)
    {
        await dbContext.RefreshTokens
            .Where(token => token.AppUserId == user.Id && !token.IsRevoked)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(token => token.IsRevoked, true)
                .SetProperty(token => token.RevokedAtUtc, now)
                .SetProperty(token => token.RevokedReason, "RefreshTokenReuseDetected"), cancellationToken);
        await auditEngine.RecordAsync(
            user.Id,
            AuditActionType.Revoke,
            nameof(RefreshToken),
            storedToken.Id.ToString(),
            before: new { Result = "ConcurrentRefreshReuseDetected" },
            after: new { Result = "SessionRevoked" },
            requestMeta: AuditRequestMetadata.From(httpContext),
            cancellationToken: cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (rotationTransaction is not null)
        {
            await rotationTransaction.CommitAsync(cancellationToken);
        }
        return ApiResponse.Failure("InvalidToken", "Invalid refresh token.", 401);
    }

    dbContext.RefreshTokens.Add(new RefreshToken(
        id: Guid.NewGuid(),
        appUserId: user.Id,
        tokenHash: newRefreshTokenHash,
        expiresAtUtc: newRefreshTokenExpiresAt,
        createdAtUtc: now));

    await auditEngine.RecordAsync(
        user.Id,
        AuditActionType.Update,
        nameof(RefreshToken),
        storedToken.Id.ToString(),
        before: storedToken,
        after: new { eventType = "AuthRefresh", storedToken.Id, replacedBy = newRefreshTokenHash[..8] },
        requestMeta: AuditRequestMetadata.From(httpContext),
        cancellationToken: cancellationToken);

    await dbContext.SaveChangesAsync(cancellationToken);
    if (rotationTransaction is not null)
    {
        await rotationTransaction.CommitAsync(cancellationToken);
    }

    var response = new AuthLoginResponse
    {
        AccessToken = accessToken,
        RefreshToken = newRefreshToken,
        ExpiresAt = accessTokenExpiresAt,
        UserId = user.Id,
        IsActive = user.IsActive,
        PermissionsVersion = user.UpdatedAtUtc,
        Roles = roles,
        Permissions = (await permissionService.GetEffectivePermissionsAsync(user.Id, cancellationToken)).ToArray()
    };

    return Results.Ok(ApiResponse.Success(response));
})
    .WithTags("Auth")
    .WithName("AuthRefresh");

authApi.MapPost("/logout", async (
    LogoutRequest request,
    AppDbContext dbContext,
    IAuditEngine auditEngine,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var incomingToken = request.RefreshToken?.Trim();
    if (string.IsNullOrWhiteSpace(incomingToken))
    {
        return ApiResponse.Failure("ValidationError", "RefreshToken is required.");
    }

    var tokenHash = AuthTokenService.HashRefreshToken(incomingToken);
    var now = DateTime.UtcNow;

    var storedToken = await dbContext.RefreshTokens
        .Include(x => x.AppUser)
        .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, cancellationToken);

    if (storedToken is null)
    {
        return Results.Ok(ApiResponse.Success(new { revoked = false }));
    }

    if (!storedToken.IsRevoked)
    {
        storedToken.Revoke(now, "Logout");
        await auditEngine.RecordAsync(
            storedToken.AppUserId,
            AuditActionType.Revoke,
            nameof(RefreshToken),
            storedToken.Id.ToString(),
            before: storedToken,
            after: new { eventType = "AuthLogout" },
            requestMeta: AuditRequestMetadata.From(httpContext),
            cancellationToken: cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    return Results.Ok(ApiResponse.Success(new { revoked = true }));
})
    .WithTags("Auth")
    .WithName("AuthLogout");

app.MapIamAdminEndpoints();
app.MapNotificationPolicyAdminEndpoints();
factoriesApi.MapGet("", async (
    AppDbContext dbContext,
    CancellationToken cancellationToken,
    bool? isActive = null,
    int page = 1,
    int pageSize = 50) =>
{
    if (page < 1 || pageSize < 1 || pageSize > 200)
    {
        return ApiResponse.Failure("ValidationError", "page and pageSize must be positive, pageSize max 200.");
    }

    var query = dbContext.Factories.AsNoTracking();
    if (isActive.HasValue)
    {
        query = query.Where(x => x.IsActive == isActive.Value);
    }

    var totalCount = await query.CountAsync(cancellationToken);
    var entities = await query
        .OrderBy(x => x.Code)
        .ThenBy(x => x.Name)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(x => new FactoryDto
        {
            Id = x.Id,
            Name = x.Name,
            Code = x.Code,
            Location = x.Location,
            IsActive = x.IsActive
        })
        .ToArrayAsync(cancellationToken);

    return Results.Ok(new { success = true, data = new { items = entities, totalCount, pageNumber = page, pageSize } });
})
    .WithTags("Factories")
    .WithName("GetFactories")
    .RequirePermission(FactoryStructurePermissions.ForHttpMethod("GET"));

factoriesApi.MapGet("/{factoryId:guid}", async (
    Guid factoryId,
    AppDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var entity = await dbContext.Factories
        .AsNoTracking()
        .FirstOrDefaultAsync(x => x.Id == factoryId && x.IsActive, cancellationToken);

    if (entity is null)
    {
        return ApiResponse.Failure("NotFound", "Factory not found.", statusCode: 404);
    }

    return Results.Ok(ApiResponse.Success(new FactoryDto
    {
        Id = entity.Id,
        Name = entity.Name,
        Code = entity.Code,
        Location = entity.Location,
        IsActive = entity.IsActive
    }));
})
    .WithTags("Factories")
    .WithName("GetFactory")
    .RequirePermission(FactoryStructurePermissions.ForHttpMethod("GET"));

factoriesApi.MapPost("", async (
    CreateFactoryRequest request,
    AppDbContext dbContext,
    ICurrentUserService currentUserService,
    IAuditEngine auditEngine,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    if (string.IsNullOrWhiteSpace(request.Name))
    {
        return ApiResponse.Failure("ValidationError", "Name is required.");
    }

    var code = request.Code?.Trim();
    if (string.IsNullOrWhiteSpace(code))
    {
        return ApiResponse.Failure("ValidationError", "Code is required.");
    }

    var hasConflict = await dbContext.Factories.AnyAsync(
        x => x.Code == code,
        cancellationToken);

    if (hasConflict)
    {
        return ApiResponse.Failure("Conflict", "A factory with this code already exists.", statusCode: 409);
    }

    var entity = new Factory(
        id: Guid.NewGuid(),
        name: request.Name,
        code: code,
        location: request.Location?.Trim(),
        isActive: request.IsActive);

    dbContext.Factories.Add(entity);
    await auditEngine.RecordAsync(
        actorUserId.Value,
        AuditActionType.Create,
        nameof(Factory),
        entity.Id.ToString(),
        before: null,
        after: new { entity.Id, entity.Name, entity.Code, entity.Location, entity.IsActive },
        requestMeta: AuditRequestMetadata.From(httpContext),
        cancellationToken: cancellationToken);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/factories/{entity.Id}", ApiResponse.Success(new FactoryDto
    {
        Id = entity.Id,
        Name = entity.Name,
        Code = entity.Code,
        Location = entity.Location,
        IsActive = entity.IsActive
    }));
})
    .WithTags("Factories")
    .WithName("CreateFactory")
    .RequirePermission(FactoryStructurePermissions.ForHttpMethod("POST"));

factoriesApi.MapPatch("/{factoryId:guid}", async (
    Guid factoryId,
    UpdateFactoryRequest request,
    AppDbContext dbContext,
    ICurrentUserService currentUserService,
    IAuditEngine auditEngine,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var entity = await dbContext.Factories.FirstOrDefaultAsync(x => x.Id == factoryId, cancellationToken);
    if (entity is null)
    {
        return ApiResponse.Failure("NotFound", "Factory not found.", statusCode: 404);
    }

    if (request.Name is null && request.Location is null && request.IsActive is null && request.Code is null)
    {
        return ApiResponse.Failure("ValidationError", "No updatable fields were provided.");
    }

    if (request.Code is not null)
    {
        var normalizedCode = request.Code.Trim();
        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            return ApiResponse.Failure("ValidationError", "Code cannot be empty.");
        }

        if (!string.Equals(entity.Code, normalizedCode, StringComparison.Ordinal))
        {
            return ApiResponse.Failure("ValidationError", "لا يمكن تعديل الكود بعد إنشاء السجل.");
        }
    }

    var updatedAt = DateTime.UtcNow;
    var entry = dbContext.Entry(entity);
    var changed = false;

    if (request.Name is { } name && !string.IsNullOrWhiteSpace(name))
    {
        entry.Property(nameof(Factory.Name)).CurrentValue = name.Trim();
        changed = true;
    }
    else if (request.Name is not null && string.IsNullOrWhiteSpace(request.Name))
    {
        return ApiResponse.Failure("ValidationError", "Name cannot be empty.");
    }

    if (request.Location is not null)
    {
        var trimmedLocation = request.Location.Trim();
        if (string.IsNullOrWhiteSpace(trimmedLocation))
        {
            return ApiResponse.Failure("ValidationError", "Location cannot be empty.");
        }

        entry.Property(nameof(Factory.Location)).CurrentValue = trimmedLocation;
        changed = true;
    }

    if (request.IsActive is not null && entity.IsActive != request.IsActive.Value)
    {
        entry.Property(nameof(Factory.IsActive)).CurrentValue = request.IsActive.Value;
        changed = true;
    }

    if (!changed)
    {
        return ApiResponse.Failure("ValidationError", "No valid changes detected.");
    }

    var beforeFactory = new { entity.Id, entity.Name, entity.Code, entity.Location, entity.IsActive };
    entry.Property(nameof(Factory.UpdatedAtUtc)).CurrentValue = updatedAt;
    await auditEngine.RecordAsync(
        actorUserId.Value,
        AuditActionType.Update,
        nameof(Factory),
        entity.Id.ToString(),
        before: beforeFactory,
        after: new { entity.Id, entity.Name, entity.Code, entity.Location, entity.IsActive },
        requestMeta: AuditRequestMetadata.From(httpContext),
        cancellationToken: cancellationToken);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Ok(ApiResponse.Success(new FactoryDto
    {
        Id = entity.Id,
        Name = entity.Name,
        Code = entity.Code,
        Location = entity.Location,
        IsActive = entity.IsActive
    }));
})
    .WithTags("Factories")
    .WithName("UpdateFactory")
    .RequirePermission(FactoryStructurePermissions.ForHttpMethod("PATCH"));

factoriesApi.MapDelete("/{factoryId:guid}", async (
    Guid factoryId,
    AppDbContext dbContext,
    ICurrentUserService currentUserService,
    IAuditEngine auditEngine,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var entity = await dbContext.Factories.FirstOrDefaultAsync(x => x.Id == factoryId && x.IsActive, cancellationToken);
    if (entity is null)
    {
        return ApiResponse.Failure("NotFound", "Factory not found.", statusCode: 404);
    }

    if (await dbContext.Departments.AnyAsync(x => x.FactoryId == factoryId, cancellationToken)
        || await dbContext.ProductionLines.AnyAsync(x => x.FactoryId == factoryId, cancellationToken))
    {
        return ApiResponse.Failure("Conflict", "لا يمكن حذف المصنع لوجود أقسام أو خطوط إنتاج مرتبطة به.", 409);
    }

    var beforeFactory = new { entity.Id, entity.Name, entity.Code, entity.Location, entity.IsActive };
    await auditEngine.RecordAsync(
        actorUserId.Value,
        AuditActionType.Delete,
        nameof(Factory),
        entity.Id.ToString(),
        before: beforeFactory,
        after: null,
        requestMeta: AuditRequestMetadata.From(httpContext),
        cancellationToken: cancellationToken);
    dbContext.Factories.Remove(entity);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.NoContent();
})
    .WithTags("Factories")
    .WithName("DeleteFactory")
    .RequirePermission(FactoryStructurePermissions.ForHttpMethod("DELETE"));

factoriesApi.MapGet("/{factoryId:guid}/production-lines", async (
    AppDbContext dbContext,
    Guid factoryId,
    CancellationToken cancellationToken,
    bool includeInactive = false,
    int page = 1,
    int pageSize = 50) =>
{
    if (page < 1 || pageSize < 1 || pageSize > 200)
    {
        return ApiResponse.Failure("ValidationError", "page and pageSize must be positive, pageSize max 200.");
    }

    var factoryExists = await dbContext.Factories.AnyAsync(x => x.Id == factoryId && x.IsActive, cancellationToken);
    if (!factoryExists)
    {
        return ApiResponse.Failure("NotFound", "Factory not found.", statusCode: 404);
    }

    var query = dbContext.ProductionLines
        .AsNoTracking()
        .Where(x => x.FactoryId == factoryId);

    if (!includeInactive)
    {
        query = query.Where(x => x.IsActive);
    }

    var totalCount = await query.CountAsync(cancellationToken);
    var entities = await query
        .OrderBy(x => x.SequenceOrder)
        .ThenBy(x => x.Name)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(x => new ProductionLineDto
        {
            Id = x.Id,
            FactoryId = x.FactoryId,
            DepartmentId = x.DepartmentId,
            DepartmentCode = x.Department == null ? null : x.Department.Code,
            DepartmentNameAr = x.Department == null ? null : x.Department.NameAr,
            Name = x.Name,
            LineCode = x.LineCode,
            SequenceOrder = x.SequenceOrder,
            IsActive = x.IsActive
        })
        .ToArrayAsync(cancellationToken);

    return Results.Ok(new { success = true, data = new { items = entities, totalCount, pageNumber = page, pageSize } });
})
    .WithTags("ProductionLines")
    .WithName("GetProductionLinesByFactory")
    .RequirePermission(FactoryStructurePermissions.ForHttpMethod("GET"));

productionLinesApi.MapGet("", async (
    AppDbContext dbContext,
    CancellationToken cancellationToken,
    Guid? factoryId = null,
    Guid? departmentId = null,
    bool includeInactive = false,
    int page = 1,
    int pageSize = 50) =>
{
    if (page < 1 || pageSize < 1 || pageSize > 200)
    {
        return ApiResponse.Failure("ValidationError", "page and pageSize must be positive, pageSize max 200.");
    }

    var query = dbContext.ProductionLines.AsNoTracking();
    if (factoryId.HasValue) query = query.Where(x => x.FactoryId == factoryId.Value);
    if (departmentId.HasValue) query = query.Where(x => x.DepartmentId == departmentId.Value);
    if (!includeInactive) query = query.Where(x => x.IsActive);
    var totalCount = await query.CountAsync(cancellationToken);
    var items = await query.OrderBy(x => x.SequenceOrder).ThenBy(x => x.Name).Skip((page - 1) * pageSize).Take(pageSize).Select(x => new ProductionLineDto
    {
        Id = x.Id, FactoryId = x.FactoryId, DepartmentId = x.DepartmentId, DepartmentCode = x.Department == null ? null : x.Department.Code, DepartmentNameAr = x.Department == null ? null : x.Department.NameAr, Name = x.Name, LineCode = x.LineCode, SequenceOrder = x.SequenceOrder, IsActive = x.IsActive
    }).ToArrayAsync(cancellationToken);
    return Results.Ok(new { success = true, data = new { items, totalCount, pageNumber = page, pageSize } });
})
    .WithTags("ProductionLines")
    .WithName("GetProductionLines")
    .RequirePermission(FactoryStructurePermissions.ForHttpMethod("GET"));

productionLinesApi.MapGet("/{lineId:guid}", async (
    Guid lineId,
    AppDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var entity = await dbContext.ProductionLines
        .AsNoTracking()
        .Include(x => x.Department)
        .FirstOrDefaultAsync(x => x.Id == lineId && x.IsActive, cancellationToken);

    if (entity is null)
    {
        return ApiResponse.Failure("NotFound", "Production line not found.", statusCode: 404);
    }

    return Results.Ok(ApiResponse.Success(new ProductionLineDto
    {
        Id = entity.Id,
        FactoryId = entity.FactoryId,
        DepartmentId = entity.DepartmentId,
        DepartmentCode = entity.Department?.Code,
        DepartmentNameAr = entity.Department?.NameAr,
        Name = entity.Name,
        LineCode = entity.LineCode,
        SequenceOrder = entity.SequenceOrder,
        IsActive = entity.IsActive
    }));
})
    .WithTags("ProductionLines")
    .WithName("GetProductionLine")
    .RequirePermission(FactoryStructurePermissions.ForHttpMethod("GET"));

productionLinesApi.MapPost("", async (
    CreateProductionLineRequest request,
    AppDbContext dbContext,
    ICurrentUserService currentUserService,
    IAuditEngine auditEngine,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    if (request.FactoryId == Guid.Empty)
    {
        return ApiResponse.Failure("ValidationError", "FactoryId is required.");
    }

    if (request.DepartmentId is null || request.DepartmentId == Guid.Empty)
    {
        return ApiResponse.Failure("ValidationError", "DepartmentId is required when creating a production line.");
    }

    if (string.IsNullOrWhiteSpace(request.Name))
    {
        return ApiResponse.Failure("ValidationError", "Name is required.");
    }

    if (request.SequenceOrder < 0)
    {
        return ApiResponse.Failure("ValidationError", "SequenceOrder must be zero or greater.");
    }

    var factoryExists = await dbContext.Factories.AnyAsync(x => x.Id == request.FactoryId && x.IsActive, cancellationToken);
    if (!factoryExists)
    {
        return ApiResponse.Failure("ValidationError", "FactoryId does not exist.", 404);
    }

    var department = await dbContext.Departments.FirstOrDefaultAsync(
        x => x.Id == request.DepartmentId.Value && x.FactoryId == request.FactoryId && x.IsActive,
        cancellationToken);
    if (department is null)
    {
        return ApiResponse.Failure("ValidationError", "DepartmentId must reference an active department in the same factory.", 404);
    }

    var lineCode = string.IsNullOrWhiteSpace(request.LineCode) ? null : request.LineCode.Trim();
    if (lineCode is not null)
    {
        var hasDuplicateLineCode = await dbContext.ProductionLines.AnyAsync(
            x => x.FactoryId == request.FactoryId && x.LineCode == lineCode,
            cancellationToken);
        if (hasDuplicateLineCode)
        {
            return ApiResponse.Failure("Conflict", "LineCode must be unique within the factory.", statusCode: 409);
        }
    }

    var entity = new ProductionLine(
        id: Guid.NewGuid(),
        factoryId: request.FactoryId,
        name: request.Name,
        lineCode: lineCode,
        sequenceOrder: request.SequenceOrder,
        departmentId: request.DepartmentId,
        isActive: request.IsActive);

    dbContext.ProductionLines.Add(entity);
    await auditEngine.RecordAsync(
        actorUserId.Value,
        AuditActionType.Create,
        nameof(ProductionLine),
        entity.Id.ToString(),
        before: null,
        after: new { entity.Id, entity.FactoryId, entity.DepartmentId, entity.Name, entity.LineCode, entity.SequenceOrder, entity.IsActive },
        requestMeta: AuditRequestMetadata.From(httpContext),
        cancellationToken: cancellationToken);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/production-lines/{entity.Id}", ApiResponse.Success(new ProductionLineDto
    {
        Id = entity.Id,
        FactoryId = entity.FactoryId,
        DepartmentId = entity.DepartmentId,
        DepartmentCode = department.Code,
        DepartmentNameAr = department.NameAr,
        Name = entity.Name,
        LineCode = entity.LineCode,
        SequenceOrder = entity.SequenceOrder,
        IsActive = entity.IsActive
    }));
})
    .WithTags("ProductionLines")
    .WithName("CreateProductionLine")
    .RequirePermission(FactoryStructurePermissions.ForHttpMethod("POST"));

productionLinesApi.MapPatch("/{lineId:guid}", async (
    Guid lineId,
    UpdateProductionLineRequest request,
    AppDbContext dbContext,
    ICurrentUserService currentUserService,
    IAuditEngine auditEngine,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var entity = await dbContext.ProductionLines.FirstOrDefaultAsync(x => x.Id == lineId, cancellationToken);
    if (entity is null)
    {
        return ApiResponse.Failure("NotFound", "Production line not found.", statusCode: 404);
    }

    var beforeProductionLine = new { entity.Id, entity.FactoryId, entity.DepartmentId, entity.Name, entity.LineCode, entity.SequenceOrder, entity.IsActive };

    if (request.Name is null && request.DepartmentId is null && request.LineCode is null && request.SequenceOrder is null && request.IsActive is null)
    {
        return ApiResponse.Failure("ValidationError", "No updatable fields were provided.");
    }

    var updatedAt = DateTime.UtcNow;
    var hasChanges = false;
    var entry = dbContext.Entry(entity);
    Department? selectedDepartment = null;
    if (request.Name is { } name && !string.IsNullOrWhiteSpace(name))
    {
        entry.Property(nameof(ProductionLine.Name)).CurrentValue = name.Trim();
        hasChanges = true;
    }
    else if (request.Name is not null && string.IsNullOrWhiteSpace(request.Name))
    {
        return ApiResponse.Failure("ValidationError", "Name cannot be empty.");
    }

    if (request.DepartmentId is not null)
    {
        if (request.DepartmentId.Value == Guid.Empty)
        {
            return ApiResponse.Failure("ValidationError", "DepartmentId cannot be empty.");
        }

        selectedDepartment = await dbContext.Departments.FirstOrDefaultAsync(
            x => x.Id == request.DepartmentId.Value && x.FactoryId == entity.FactoryId && x.IsActive,
            cancellationToken);
        if (selectedDepartment is null)
        {
            return ApiResponse.Failure("ValidationError", "DepartmentId must reference an active department in the same factory.", 404);
        }

        if (entity.DepartmentId != selectedDepartment.Id)
        {
            entity.SetDepartment(selectedDepartment.Id, updatedAt);
            hasChanges = true;
        }
    }

    if (request.LineCode is not null)
    {
        var normalizedLineCode = request.LineCode.Trim();
        if (string.IsNullOrWhiteSpace(normalizedLineCode))
        {
            return ApiResponse.Failure("ValidationError", "LineCode cannot be empty.");
        }

        if (!string.Equals(entity.LineCode, normalizedLineCode, StringComparison.Ordinal))
        {
            return ApiResponse.Failure("ValidationError", "لا يمكن تعديل الكود بعد إنشاء السجل.");
        }
    }

    if (request.SequenceOrder is not null)
    {
        if (request.SequenceOrder.Value < 0)
        {
            return ApiResponse.Failure("ValidationError", "SequenceOrder must be zero or greater.");
        }

        if (entity.SequenceOrder != request.SequenceOrder.Value)
        {
            entity.SetSequenceOrder(request.SequenceOrder.Value, updatedAt);
            hasChanges = true;
        }
    }

    if (request.IsActive is not null)
    {
        if (entity.IsActive != request.IsActive.Value)
        {
            entry.Property(nameof(ProductionLine.IsActive)).CurrentValue = request.IsActive.Value;
            hasChanges = true;
        }
    }

    if (!hasChanges)
    {
        return ApiResponse.Failure("ValidationError", "No valid changes detected.");
    }

    entry.Property(nameof(ProductionLine.UpdatedAtUtc)).CurrentValue = updatedAt;
    await auditEngine.RecordAsync(
        actorUserId.Value,
        AuditActionType.Update,
        nameof(ProductionLine),
        entity.Id.ToString(),
        before: beforeProductionLine,
        after: new { entity.Id, entity.FactoryId, entity.DepartmentId, entity.Name, entity.LineCode, entity.SequenceOrder, entity.IsActive },
        requestMeta: AuditRequestMetadata.From(httpContext),
        cancellationToken: cancellationToken);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Ok(ApiResponse.Success(new ProductionLineDto
    {
        Id = entity.Id,
        FactoryId = entity.FactoryId,
        DepartmentId = entity.DepartmentId,
        DepartmentCode = selectedDepartment?.Code ?? await dbContext.Departments.Where(x => x.Id == entity.DepartmentId).Select(x => x.Code).FirstOrDefaultAsync(cancellationToken),
        DepartmentNameAr = selectedDepartment?.NameAr ?? await dbContext.Departments.Where(x => x.Id == entity.DepartmentId).Select(x => x.NameAr).FirstOrDefaultAsync(cancellationToken),
        Name = entity.Name,
        LineCode = entity.LineCode,
        SequenceOrder = entity.SequenceOrder,
        IsActive = entity.IsActive
    }));
})
    .WithTags("ProductionLines")
    .WithName("UpdateProductionLine")
    .RequirePermission(FactoryStructurePermissions.ForHttpMethod("PATCH"));

productionLinesApi.MapDelete("/{lineId:guid}", async (
    Guid lineId,
    AppDbContext dbContext,
    ICurrentUserService currentUserService,
    IAuditEngine auditEngine,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var entity = await dbContext.ProductionLines.FirstOrDefaultAsync(x => x.Id == lineId, cancellationToken);
    if (entity is null)
    {
        return ApiResponse.Failure("NotFound", "Production line not found.", 404);
    }

    if (await dbContext.MainStages.AnyAsync(x => x.ProductionLineId == lineId, cancellationToken)
        || await dbContext.SubStages.AnyAsync(x => x.ProductionLineId == lineId, cancellationToken)
        || await dbContext.ProductionOrders.AnyAsync(x => x.ProductionLineId == lineId, cancellationToken))
    {
        return ApiResponse.Failure("Conflict", "لا يمكن حذف خط الإنتاج لوجود مراحل أو أوامر إنتاج أو علاقات تشغيلية مرتبطة به.", 409);
    }

    var beforeProductionLine = new { entity.Id, entity.FactoryId, entity.Name, entity.LineCode, entity.SequenceOrder, entity.IsActive };
    await auditEngine.RecordAsync(
        actorUserId.Value,
        AuditActionType.Delete,
        nameof(ProductionLine),
        entity.Id.ToString(),
        before: beforeProductionLine,
        after: null,
        requestMeta: AuditRequestMetadata.From(httpContext),
        cancellationToken: cancellationToken);
    dbContext.ProductionLines.Remove(entity);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.NoContent();
})
    .WithTags("ProductionLines")
    .WithName("DeleteProductionLine")
    .RequirePermission(FactoryStructurePermissions.ForHttpMethod("DELETE"));

productionLinesApi.MapGet("/{productionLineId:guid}/main-stages", async (
    AppDbContext dbContext,
    Guid productionLineId,
    CancellationToken cancellationToken,
    bool includeInactive = false,
    int page = 1,
    int pageSize = 50) =>
{
    if (page < 1 || pageSize < 1 || pageSize > 200)
    {
        return ApiResponse.Failure("ValidationError", "page and pageSize must be positive, pageSize max 200.");
    }

    var lineExists = await dbContext.ProductionLines.AnyAsync(x => x.Id == productionLineId && x.IsActive, cancellationToken);
    if (!lineExists)
    {
        return ApiResponse.Failure("NotFound", "Production line not found.", 404);
    }

    var query = dbContext.MainStages.AsNoTracking().Where(x => x.ProductionLineId == productionLineId);
    if (!includeInactive)
    {
        query = query.Where(x => x.IsActive);
    }

    var totalCount = await query.CountAsync(cancellationToken);
    var entities = await query
        .OrderBy(x => x.SequenceOrder)
        .ThenBy(x => x.Name)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(x => new MainStageDto
        {
            Id = x.Id,
            ProductionLineId = x.ProductionLineId,
            Name = x.Name,
            SequenceOrder = x.SequenceOrder,
            IsCritical = x.IsCritical,
            IsActive = x.IsActive
        })
        .ToArrayAsync(cancellationToken);

    return Results.Ok(new { success = true, data = new { items = entities, totalCount, pageNumber = page, pageSize } });
})
    .WithTags("MainStages")
    .WithName("GetMainStagesByLine");

mainStagesApi.MapGet("/{mainStageId:guid}/sub-stages", async (
    IProductionStageCatalogService stageCatalogService,
    Guid mainStageId,
    CancellationToken cancellationToken,
    string? search = null,
    bool? isActive = true,
    bool includeInactive = false,
    int page = 1,
    int pageSize = 50) =>
{
    var result = await stageCatalogService.GetSubStagesAsync(mainStageId, search, includeInactive ? null : isActive, page, pageSize, cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(new { success = true, data = new { items = result.Value!, totalCount = result.TotalCount, pageNumber = result.PageNumber, pageSize = result.PageSize } });
})
    .WithTags("SubStages")
    .WithName("GetSubStagesByMainStage")
    .RequirePermission("stages.view");

mainStagesApi.MapGet("", async (
    IProductionStageCatalogService stageCatalogService,
    Guid? productionLineId,
    CancellationToken cancellationToken,
    string? search = null,
    bool? isActive = true,
    int page = 1,
    int pageSize = 50) =>
{
    var result = await stageCatalogService.GetMainStagesAsync(productionLineId, search, isActive, page, pageSize, cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(new { success = true, data = new { items = result.Value!, totalCount = result.TotalCount, pageNumber = result.PageNumber, pageSize = result.PageSize } });
})
    .WithTags("MainStages")
    .WithName("GetMainStages")
    .RequirePermission("stages.view");

mainStagesApi.MapGet("/{mainStageId:guid}", async (
    Guid mainStageId,
    IProductionStageCatalogService stageCatalogService,
    CancellationToken cancellationToken) =>
{
    var result = await stageCatalogService.GetMainStageAsync(mainStageId, cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(ApiResponse.Success(result.Value!));
})
    .WithTags("MainStages")
    .WithName("GetMainStage")
    .RequirePermission("stages.view");

mainStagesApi.MapPost("", async (
    CreateMainStageRequest request,
    IProductionStageCatalogService stageCatalogService,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var requestMeta = $"{httpContext.Request.Method} {httpContext.Request.Path}";
    var result = await stageCatalogService.CreateMainStageAsync(
        request.ProductionLineId,
        request.Name,
        request.IsCritical,
        request.SequenceOrder,
        request.IsActive,
        actorUserId.Value,
        requestMeta,
        cancellationToken);

    if (result.IsFailure)
    {
        return ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Created($"/api/main-stages/{result.Value!.Id}", ApiResponse.Success(result.Value));
})
    .WithTags("MainStages")
    .WithName("CreateMainStage")
    .RequirePermission("stages.manage");

mainStagesApi.MapPatch("/{mainStageId:guid}", async (
    Guid mainStageId,
    UpdateMainStageRequest request,
    IProductionStageCatalogService stageCatalogService,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var requestMeta = $"{httpContext.Request.Method} {httpContext.Request.Path}";
    var result = await stageCatalogService.UpdateMainStageAsync(
        mainStageId,
        request.Name,
        request.IsCritical,
        request.SequenceOrder,
        request.IsActive,
        actorUserId.Value,
        requestMeta,
        cancellationToken);

    if (result.IsFailure)
    {
        return ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(ApiResponse.Success(result.Value!));
})
    .WithTags("MainStages")
    .WithName("UpdateMainStage")
    .RequirePermission("stages.manage");

mainStagesApi.MapDelete("/{mainStageId:guid}", async (
    Guid mainStageId,
    IProductionStageCatalogService stageCatalogService,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var requestMeta = $"{httpContext.Request.Method} {httpContext.Request.Path}";
    var result = await stageCatalogService.DeactivateMainStageAsync(mainStageId, actorUserId.Value, requestMeta, cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code));
    }

    return Results.NoContent();
})
    .WithTags("MainStages")
    .WithName("DeleteMainStage")
    .RequirePermission("stages.manage");

subStagesApi.MapGet("", async (
    IProductionStageCatalogService stageCatalogService,
    Guid? mainStageId,
    CancellationToken cancellationToken,
    string? search = null,
    bool? isActive = true,
    int page = 1,
    int pageSize = 50) =>
{
    var result = await stageCatalogService.GetSubStagesAsync(mainStageId, search, isActive, page, pageSize, cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(new { success = true, data = new { items = result.Value!, totalCount = result.TotalCount, pageNumber = result.PageNumber, pageSize = result.PageSize } });
})
    .WithTags("SubStages")
    .WithName("GetSubStages")
    .RequirePermission("stages.view");

subStagesApi.MapGet("/{subStageId:guid}", async (
    Guid subStageId,
    IProductionStageCatalogService stageCatalogService,
    CancellationToken cancellationToken) =>
{
    var result = await stageCatalogService.GetSubStageAsync(subStageId, cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(ApiResponse.Success(result.Value!));
})
    .WithTags("SubStages")
    .WithName("GetSubStage")
    .RequirePermission("stages.view");

subStagesApi.MapPost("", async (
    CreateSubStageRequest request,
    IProductionStageCatalogService stageCatalogService,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var requestMeta = $"{httpContext.Request.Method} {httpContext.Request.Path}";
    var result = await stageCatalogService.CreateSubStageAsync(
        request.MainStageId,
        request.Code,
        request.Name,
        request.DefaultOrder,
        request.Capacity,
        request.IsActive,
        actorUserId.Value,
        requestMeta,
        cancellationToken);

    if (result.IsFailure)
    {
        return ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Created($"/api/sub-stages/{result.Value!.Id}", ApiResponse.Success(result.Value));
})
    .WithTags("SubStages")
    .WithName("CreateSubStage")
    .RequirePermission("stages.manage");

subStagesApi.MapPatch("/{subStageId:guid}", async (
    Guid subStageId,
    UpdateSubStageRequest request,
    IProductionStageCatalogService stageCatalogService,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var requestMeta = $"{httpContext.Request.Method} {httpContext.Request.Path}";
    var result = await stageCatalogService.UpdateSubStageAsync(
        subStageId,
        request.Code,
        request.Name,
        request.DefaultOrder,
        request.Capacity,
        request.IsActive,
        actorUserId.Value,
        requestMeta,
        cancellationToken);

    if (result.IsFailure)
    {
        return ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(ApiResponse.Success(result.Value!));
})
    .WithTags("SubStages")
    .WithName("UpdateSubStage")
    .RequirePermission("stages.manage");

subStagesApi.MapDelete("/{subStageId:guid}", async (
    Guid subStageId,
    IProductionStageCatalogService stageCatalogService,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var requestMeta = $"{httpContext.Request.Method} {httpContext.Request.Path}";
    var result = await stageCatalogService.DeactivateSubStageAsync(subStageId, actorUserId.Value, requestMeta, cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code));
    }

    return Results.NoContent();
})
    .WithTags("SubStages")
    .WithName("DeleteSubStage")
    .RequirePermission("stages.manage");

stagesApi.MapGet("", async (
    IProductionStageCatalogService stageCatalogService,
    Guid? factoryId,
    Guid? departmentId,
    Guid? productionLineId,
    CancellationToken cancellationToken,
    string? name = null,
    string? code = null,
    bool? isActive = true,
    bool includeInactive = false,
    int page = 1,
    int pageSize = 50) =>
{
    var result = await stageCatalogService.GetOperationalStagesAsync(factoryId, departmentId, productionLineId, name, code, includeInactive ? null : isActive, page, pageSize, cancellationToken);
    return result.IsFailure
        ? ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code))
        : Results.Ok(new { success = true, data = new { items = result.Value!, totalCount = result.TotalCount, pageNumber = result.PageNumber, pageSize = result.PageSize } });
})
    .WithTags("Stages")
    .WithName("GetOperationalStages")
    .RequirePermission("stages.view");

stagesApi.MapGet("/{stageId:guid}", async (Guid stageId, IProductionStageCatalogService stageCatalogService, CancellationToken cancellationToken) =>
{
    var result = await stageCatalogService.GetSubStageAsync(stageId, cancellationToken);
    return result.IsFailure
        ? ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code))
        : Results.Ok(ApiResponse.Success(result.Value!));
})
    .WithTags("Stages")
    .WithName("GetOperationalStage")
    .RequirePermission("stages.view");

stagesApi.MapGet("/{stageId:guid}/dependencies", async (Guid stageId, IProductionStageCatalogService stageCatalogService, CancellationToken cancellationToken) =>
{
    var result = await stageCatalogService.GetSubStageDependenciesAsync(stageId, cancellationToken);
    return result.IsFailure
        ? ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code))
        : Results.Ok(ApiResponse.Success(result.Value!));
})
    .WithTags("Stages")
    .WithName("GetOperationalStageDependencies")
    .RequirePermission("stages.view");

stagesApi.MapPost("", async (
    CreateOperationalStageRequest request,
    IProductionStageCatalogService stageCatalogService,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    if (currentUserService.UserId is not { } actorUserId) return ApiResponse.Failure("Unauthorized", "User context is required.");
    var result = await stageCatalogService.CreateOperationalStageAsync(request.ProductionLineId, request.Name, request.Capacity, request.IsActive, actorUserId, AuditRequestMetadata.From(httpContext), cancellationToken);
    return result.IsFailure
        ? ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code))
        : Results.Created($"/api/stages/{result.Value!.Id}", ApiResponse.Success(result.Value));
})
    .WithTags("Stages")
    .WithName("CreateOperationalStage")
    .RequirePermission("stages.manage");

stagesApi.MapPatch("/{stageId:guid}", async (
    Guid stageId,
    UpdateSubStageRequest request,
    IProductionStageCatalogService stageCatalogService,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    if (currentUserService.UserId is not { } actorUserId) return ApiResponse.Failure("Unauthorized", "User context is required.");
    var result = await stageCatalogService.UpdateSubStageAsync(stageId, request.Code, request.Name, request.DefaultOrder, request.Capacity, request.IsActive, actorUserId, AuditRequestMetadata.From(httpContext), cancellationToken);
    return result.IsFailure
        ? ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code))
        : Results.Ok(ApiResponse.Success(result.Value!));
})
    .WithTags("Stages")
    .WithName("UpdateOperationalStage")
    .RequirePermission("stages.manage");

stagesApi.MapPost("/{stageId:guid}/deactivate", async (
    Guid stageId,
    IProductionStageCatalogService stageCatalogService,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    if (currentUserService.UserId is not { } actorUserId) return ApiResponse.Failure("Unauthorized", "User context is required.");
    var result = await stageCatalogService.DeactivateSubStageAsync(stageId, actorUserId, AuditRequestMetadata.From(httpContext), cancellationToken);
    return result.IsFailure
        ? ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code))
        : Results.Ok(ApiResponse.Success(result.Value!));
})
    .WithTags("Stages")
    .WithName("DeactivateOperationalStage")
    .RequirePermission("stages.manage");

stagesApi.MapDelete("/{stageId:guid}", async (
    Guid stageId,
    IProductionStageCatalogService stageCatalogService,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    if (currentUserService.UserId is not { } actorUserId) return ApiResponse.Failure("Unauthorized", "User context is required.");
    var result = await stageCatalogService.DeleteSubStageAsync(stageId, actorUserId, AuditRequestMetadata.From(httpContext), cancellationToken);
    return result.IsFailure
        ? ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code))
        : Results.NoContent();
})
    .WithTags("Stages")
    .WithName("DeleteOperationalStage")
    .RequirePermission("stages.delete");

workersApi.MapGet("", async (
    IEmployeeMasterDataService employeeService,
    CancellationToken cancellationToken,
    string? search = null,
    bool? isActive = null,
    int page = 1,
    int pageSize = 50) =>
{
    var result = await employeeService.GetWorkersAsync(search, isActive, page, pageSize, cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code));
    }

    var value = result.Value ?? [];
    return Results.Ok(new
    {
        success = true,
        data = new
        {
            items = value,
            totalCount = result.TotalCount,
            pageNumber = result.PageNumber,
            pageSize = result.PageSize
        }
    });
})
    .RequirePermission("workers.view")
    .WithTags("Workers")
    .WithName("GetWorkers");

workersApi.MapWorkerPhotoEndpoints();
workersApi.MapWorkerSyncEndpoints();

workersApi.MapGet("/{workerId:guid}", async (
    Guid workerId,
    IEmployeeMasterDataService employeeService,
    CancellationToken cancellationToken) =>
{
    var result = await employeeService.GetWorkerAsync(workerId, cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(ApiResponse.Success(result.Value!));
})
    .RequirePermission("workers.view")
    .WithTags("Workers")
    .WithName("GetWorker");

workersApi.MapPatch("/{workerId:guid}", async (
    Guid workerId,
    UpdateWorkerRequest request,
    IEmployeeMasterDataService employeeService,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var requestMeta = $"{httpContext.Request.Method} {httpContext.Request.Path}";
    var result = await employeeService.UpdateMasterIdentityAsync(
        workerId,
        request,
        actorUserId.Value,
        requestMeta,
        cancellationToken);

    if (result.IsFailure)
    {
        return ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(ApiResponse.Success(result.Value!));
})
    .RequirePermission("workers.manage")
    .WithTags("Workers")
    .WithName("UpdateWorker");

workersApi.MapPatch("/{workerId:guid}/employment-status", async (
    Guid workerId,
    SetWorkerEmploymentStatusRequest request,
    IEmployeeMasterDataService employeeService,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var requestMeta = $"{httpContext.Request.Method} {httpContext.Request.Path}";
    var result = await employeeService.SetEmploymentStatusAsync(
        workerId,
        request,
        actorUserId.Value,
        requestMeta,
        cancellationToken);

    if (result.IsFailure)
    {
        return ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(ApiResponse.Success(result.Value!));
})
    .RequirePermission("workers.manage")
    .WithTags("Workers")
    .WithName("SetWorkerEmploymentStatus");

workersApi.MapGet("/{workerId:guid}/current-assignment", async (
    Guid workerId,
    IAssignmentEngine assignmentEngine,
    CancellationToken cancellationToken,
    DateTime? asOfUtc = null) =>
{
    var result = await assignmentEngine.GetCurrentAssignmentAsync(workerId, asOfUtc, cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(
            result.Error?.Code ?? "ValidationError",
            result.Error?.Message ?? "Validation failed.",
            MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(ApiResponse.Success(result.Value!));
})
    .RequirePermission("workers.view")
    .WithTags("Workers")
    .WithName("GetWorkerCurrentAssignment");

departmentsApi.MapGet("", async (
    IDepartmentCatalogService departmentCatalog,
    Guid? factoryId,
    CancellationToken cancellationToken,
    string? search = null,
    bool? isActive = true,
    bool includeInactive = false,
    int page = 1,
    int pageSize = 50) =>
{
    var result = await departmentCatalog.GetDepartmentsAsync(factoryId, search, includeInactive ? null : isActive, page, pageSize, cancellationToken);
    if (result.IsFailure) return ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code));
    return Results.Ok(new { success = true, data = new { items = result.Value!, totalCount = result.TotalCount, pageNumber = result.PageNumber, pageSize = result.PageSize } });
})
    .RequirePermission("departments.view")
    .WithTags("Departments")
    .WithName("GetLocalDepartments");

departmentsApi.MapGet("/{departmentId:guid}", async (Guid departmentId, IDepartmentCatalogService departmentCatalog, CancellationToken cancellationToken) =>
{
    var result = await departmentCatalog.GetDepartmentAsync(departmentId, cancellationToken);
    return result.IsFailure
        ? ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code))
        : Results.Ok(ApiResponse.Success(result.Value!));
})
    .RequirePermission("departments.view")
    .WithTags("Departments")
    .WithName("GetLocalDepartment");

departmentsApi.MapPost("", async (
    CreateDepartmentRequest request,
    IDepartmentCatalogService departmentCatalog,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    if (currentUserService.UserId is not { } actorUserId) return ApiResponse.Failure("Unauthorized", "User context is required.");
    var result = await departmentCatalog.CreateAsync(request.FactoryId, request.Code, request.NameAr, request.NameEn, request.SequenceOrder, request.IsActive, actorUserId, AuditRequestMetadata.From(httpContext), cancellationToken);
    return result.IsFailure
        ? ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code))
        : Results.Created($"/api/departments/{result.Value!.Id}", ApiResponse.Success(result.Value));
})
    .RequirePermission("departments.manage")
    .WithTags("Departments")
    .WithName("CreateLocalDepartment");

departmentsApi.MapPatch("/{departmentId:guid}", async (
    Guid departmentId,
    UpdateDepartmentRequest request,
    IDepartmentCatalogService departmentCatalog,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    if (currentUserService.UserId is not { } actorUserId) return ApiResponse.Failure("Unauthorized", "User context is required.");
    var result = await departmentCatalog.UpdateAsync(departmentId, request.Code, request.NameAr, request.NameEn, request.SequenceOrder, request.IsActive, actorUserId, AuditRequestMetadata.From(httpContext), cancellationToken);
    return result.IsFailure
        ? ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code))
        : Results.Ok(ApiResponse.Success(result.Value!));
})
    .RequirePermission("departments.manage")
    .WithTags("Departments")
    .WithName("UpdateLocalDepartment");

departmentsApi.MapDelete("/{departmentId:guid}", async (
    Guid departmentId,
    IDepartmentCatalogService departmentCatalog,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    if (currentUserService.UserId is not { } actorUserId) return ApiResponse.Failure("Unauthorized", "User context is required.");
    var result = await departmentCatalog.DeleteAsync(departmentId, actorUserId, AuditRequestMetadata.From(httpContext), cancellationToken);
    return result.IsFailure
        ? ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code))
        : Results.NoContent();
})
    .RequirePermission("departments.manage")
    .WithTags("Departments")
    .WithName("DeleteLocalDepartment");

attendanceDepartmentsApi.MapGet("", async (
    IDepartmentAdministrationService departmentService,
    CancellationToken cancellationToken) =>
{
    var result = await departmentService.GetDepartmentsAsync(cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(new
    {
        success = true,
        data = new
        {
            items = result.Value ?? Array.Empty<AttendanceDepartmentRecord>()
        }
    });
})
    .RequirePermission("departments.view")
    .WithTags("Departments")
    .WithName("GetDepartments");

attendanceDepartmentsApi.MapPost("", async (
    CreateAttendanceDepartmentRequest request,
    IDepartmentAdministrationService departmentService,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var requestMeta = $"{httpContext.Request.Method} {httpContext.Request.Path}";
    var result = await departmentService.CreateDepartmentAsync(request.Name, actorUserId.Value, requestMeta, cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Created($"/api/attendance/departments/{result.Value!.DepartmentId}", ApiResponse.Success(result.Value));
})
    .RequirePermission("departments.manage")
    .WithTags("Departments")
    .WithName("CreateDepartment");

attendanceDepartmentsApi.MapPatch("/{departmentId:int}", async (
    int departmentId,
    UpdateAttendanceDepartmentRequest request,
    IDepartmentAdministrationService departmentService,
    ICurrentUserService currentUserService,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var result = await departmentService.UpdateDepartmentNameAsync(departmentId, request.Name, actorUserId.Value, cancellationToken: cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code));
    }

    return Results.NoContent();
})
    .RequirePermission("departments.manage")
    .WithTags("Departments")
    .WithName("UpdateDepartment");

attendanceDepartmentsApi.MapPost("/{departmentId:int}/move-worker", async (
    int departmentId,
    MoveWorkerToDepartmentRequest request,
    IDepartmentAdministrationService departmentService,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var requestMeta = $"{httpContext.Request.Method} {httpContext.Request.Path}";
    var result = await departmentService.MoveWorkerToDepartmentAsync(
        request.WorkerId,
        departmentId,
        actorUserId.Value,
        requestMeta,
        cancellationToken);

    if (result.IsFailure)
    {
        return ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code));
    }

    return Results.NoContent();
})
    .RequirePermission("departments.manage")
    .WithTags("Departments")
    .WithName("MoveWorkerToDepartment");

attendanceDepartmentsApi.MapDelete("/{departmentId:int}", async (
    int departmentId,
    IDepartmentAdministrationService departmentService,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var requestMeta = $"{httpContext.Request.Method} {httpContext.Request.Path}";
    var result = await departmentService.DeleteDepartmentAsync(departmentId, actorUserId.Value, requestMeta, cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code));
    }

    return Results.NoContent();
})
    .RequirePermission("departments.manage")
    .WithTags("Departments")
    .WithName("DeleteDepartment");

productModelsApi.MapGet("", async (
    IProductModelService productModelService,
    CancellationToken cancellationToken,
    string? search = null,
    bool? isActive = true,
    bool includeInactive = false,
    int page = 1,
    int pageSize = 50) =>
{
    var result = await productModelService.GetModelsAsync(search, includeInactive ? null : isActive, page, pageSize, cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(new
    {
        success = true,
        data = new
        {
            items = result.Value ?? Array.Empty<ProductModelDto>(),
            totalCount = result.TotalCount,
            pageNumber = result.PageNumber,
            pageSize = result.PageSize
        }
    });
})
    .RequirePermission("models.view")
    .WithTags("ProductModels")
    .WithName("GetProductModels");

productModelsApi.MapGet("/{modelId:guid}", async (
    Guid modelId,
    IProductModelService productModelService,
    CancellationToken cancellationToken) =>
{
    var result = await productModelService.GetModelAsync(modelId, cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(ApiResponse.Success(result.Value!));
})
    .RequirePermission("models.view")
    .WithTags("ProductModels")
    .WithName("GetProductModel");

productModelsApi.MapPost("", async (
    CreateProductModelRequest request,
    IProductModelService productModelService,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var requestMeta = $"{httpContext.Request.Method} {httpContext.Request.Path}";
    var result = await productModelService.CreateModelAsync(
        request,
        actorUserId.Value,
        requestMeta,
        cancellationToken);

    if (result.IsFailure)
    {
        return ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Created($"/api/product-models/{result.Value!.Id}", ApiResponse.Success(result.Value));
})
    .RequirePermission("models.manage")
    .WithTags("ProductModels")
    .WithName("CreateProductModel");

productModelsApi.MapPatch("/{modelId:guid}", async (
    Guid modelId,
    UpdateProductModelRequest request,
    IProductModelService productModelService,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var requestMeta = $"{httpContext.Request.Method} {httpContext.Request.Path}";
    var result = await productModelService.UpdateModelAsync(
        modelId,
        request,
        actorUserId.Value,
        requestMeta,
        cancellationToken);

    if (result.IsFailure)
    {
        return ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(ApiResponse.Success(result.Value!));
})
    .RequirePermission("models.manage")
    .WithTags("ProductModels")
    .WithName("UpdateProductModel");

productModelsApi.MapPatch("/{modelId:guid}/activation", async (
    Guid modelId,
    bool isActive,
    IProductModelService productModelService,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var requestMeta = $"{httpContext.Request.Method} {httpContext.Request.Path}";
    var result = await productModelService.SetModelActivationAsync(modelId, isActive, actorUserId.Value, requestMeta, cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code));
    }

    return Results.NoContent();
})
    .RequirePermission("models.manage")
    .WithTags("ProductModels")
    .WithName("SetModelActivation");

productModelsApi.MapGet("/{modelId:guid}/stages", async (
    Guid modelId,
    IProductModelService productModelService,
    CancellationToken cancellationToken) =>
{
    var result = await productModelService.GetModelStagesAsync(modelId, cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(new { success = true, data = result.Value ?? Array.Empty<ProductModelStageDto>() });
})
    .RequirePermission("models.view")
    .WithTags("ProductModels")
    .WithName("GetProductModelStages");

productModelsApi.MapPost("/{modelId:guid}/stages", async (
    Guid modelId,
    UpsertProductModelStageRequest request,
    IProductModelService productModelService,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    if (request.InvalidCompensationMode is not null)
    {
        return ApiResponse.Failure("ValidationError", request.InvalidCompensationMode, StatusCodes.Status400BadRequest);
    }

    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var requestMeta = $"{httpContext.Request.Method} {httpContext.Request.Path}";
    var result = await productModelService.AddModelStageAsync(
        modelId,
        request,
        actorUserId.Value,
        requestMeta,
        cancellationToken);

    if (result.IsFailure)
    {
        return ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Created($"/api/product-models/{modelId}/stages/{result.Value!.Id}", ApiResponse.Success(result.Value));
})
    .RequirePermission("models.manage")
    .WithTags("ProductModels")
    .WithName("AddProductModelStage");

productModelsApi.MapPatch("/{modelId:guid}/stages/{modelStageId:guid}", async (
    Guid modelId,
    Guid modelStageId,
    UpsertProductModelStageRequest request,
    IProductModelService productModelService,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    if (request.InvalidCompensationMode is not null)
    {
        return ApiResponse.Failure("ValidationError", request.InvalidCompensationMode, StatusCodes.Status400BadRequest);
    }

    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var requestMeta = $"{httpContext.Request.Method} {httpContext.Request.Path}";
    var result = await productModelService.UpdateModelStageAsync(
        modelId,
        modelStageId,
        request,
        actorUserId.Value,
        requestMeta,
        cancellationToken);

    if (result.IsFailure)
    {
        return ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(ApiResponse.Success(result.Value!));
})
    .RequirePermission("models.manage")
    .WithTags("ProductModels")
    .WithName("UpdateProductModelStage");

productModelsApi.MapDelete("/{modelId:guid}/stages/{modelStageId:guid}", async (
    Guid modelId,
    Guid modelStageId,
    IProductModelService productModelService,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var requestMeta = $"{httpContext.Request.Method} {httpContext.Request.Path}";
    var result = await productModelService.DeactivateModelStageAsync(
        modelId,
        modelStageId,
        actorUserId.Value,
        requestMeta,
        cancellationToken);

    if (result.IsFailure)
    {
        return ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code));
    }

    return Results.NoContent();
})
    .RequirePermission("models.manage")
    .WithTags("ProductModels")
    .WithName("DeactivateProductModelStage");

productModelsApi.MapPost("/{modelId:guid}/stages/copy", async (
    Guid modelId,
    CopyProductModelStagesRequest request,
    IProductModelService productModelService,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var requestMeta = $"{httpContext.Request.Method} {httpContext.Request.Path}";
    var result = await productModelService.CopyModelStagesAsync(
        modelId,
        request,
        actorUserId.Value,
        requestMeta,
        cancellationToken);

    if (result.IsFailure)
    {
        return ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code));
    }

    return Results.NoContent();
})
    .RequirePermission("models.manage")
    .WithTags("ProductModels")
    .WithName("CopyModelStages");

workerCompensationApi.MapGet("/current", async (
    Guid workerId,
    DateTime? asOfUtc,
    IWorkerCompensationService workerCompensationService,
    CancellationToken cancellationToken) =>
{
    var result = asOfUtc.HasValue
        ? await workerCompensationService.GetCurrentSalaryAsync(workerId, asOfUtc.Value, cancellationToken)
        : await workerCompensationService.GetCurrentSalaryAsync(workerId, cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(ApiResponse.Success(result.Value!));
})
    .RequirePermission("compensation.view")
    .WithTags("WorkerCompensation")
    .WithName("GetCurrentSalary");

workerCompensationApi.MapGet("/history", async (
    Guid workerId,
    IWorkerCompensationService workerCompensationService,
    CancellationToken cancellationToken) =>
{
    var result = await workerCompensationService.GetSalaryHistoryAsync(workerId, cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(new
    {
        success = true,
        data = new { items = result.Value ?? Array.Empty<WorkerSalaryHistoryDto>() }
    });
})
    .RequirePermission("compensation.view")
    .WithTags("WorkerCompensation")
    .WithName("GetSalaryHistory");

workerCompensationApi.MapPost("/current", async (
    Guid workerId,
    SetWorkerSalaryRequest request,
    IWorkerCompensationService workerCompensationService,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var requestMeta = $"{httpContext.Request.Method} {httpContext.Request.Path}";
    var result = await workerCompensationService.SetSalaryAsync(
        workerId,
        request,
        actorUserId.Value,
        requestMeta,
        cancellationToken);

    if (result.IsFailure)
    {
        return ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(ApiResponse.Success(result.Value!));
})
    .RequirePermission("compensation.manage")
    .WithTags("WorkerCompensation")
    .WithName("SetWorkerSalary");

workerCompensationApi.MapPost("/historical", async (
    Guid workerId,
    SetWorkerSalaryHistoryRequest request,
    IWorkerCompensationService workerCompensationService,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var requestMeta = $"{httpContext.Request.Method} {httpContext.Request.Path}";
    var result = await workerCompensationService.AddHistoricalSalaryAsync(
        workerId,
        request,
        actorUserId.Value,
        requestMeta,
        cancellationToken);

    if (result.IsFailure)
    {
        return ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.", MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(ApiResponse.Success(result.Value!));
})
    .RequirePermission("compensation.manage")
    .WithTags("WorkerCompensation")
    .WithName("AddHistoricalSalary");

assignmentsApi.MapPost("/default", async (
    CreateDefaultAssignmentRequest request,
    IAssignmentEngine assignmentEngine,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var requestMeta = $"{httpContext.Request.Method} {httpContext.Request.Path}";
    var clientIp = httpContext.Connection.RemoteIpAddress?.ToString();
    if (!string.IsNullOrWhiteSpace(clientIp))
    {
        requestMeta = $"{requestMeta} from {clientIp}";
    }

    if (!string.IsNullOrWhiteSpace(currentUserService.UserName))
    {
        requestMeta = $"{requestMeta} by {currentUserService.UserName}";
    }

    var result = await assignmentEngine.CreateOrUpdateDefaultAssignmentAsync(request, actorUserId.Value, requestMeta, cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(
            result.Error?.Code ?? "ValidationError",
            result.Error?.Message ?? "Validation failed.",
            MapFailureStatusCode(result.Error?.Code));
    }

    var value = result.Value!;
    var response = new
    {
        assignmentId = value.AssignmentId,
        workerId = value.WorkerId,
        subStageId = value.SubStageId,
        assignmentType = value.AssignmentType,
        startsAt = value.StartsAtUtc
    };

    return value.IsCreated
        ? Results.Created($"/api/assignments/default/{value.AssignmentId}", ApiResponse.Success(response))
        : Results.Ok(ApiResponse.Success(response));
})
    .RequirePermission("assignments.manage")
    .WithTags("Assignments")
    .WithName("CreateOrUpdateDefaultAssignment");

assignmentsApi.MapPut("/default/stages/{subStageId:guid}", async (
    Guid subStageId,
    UpdateStageDefaultAssignmentsRequest request,
    IAssignmentEngine assignmentEngine,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var result = await assignmentEngine.UpdateStageDefaultAssignmentsAsync(
        subStageId,
        request.WorkerIds,
        actorUserId.Value,
        $"{httpContext.Request.Method} {httpContext.Request.Path}",
        cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(
            result.Error?.Code ?? "ValidationError",
            result.Error?.Message ?? "Validation failed.",
            MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(ApiResponse.Success(result.Value!));
})
    .RequirePermission("assignments.manage")
    .WithTags("Assignments")
    .WithName("UpdateStageDefaultAssignments");

assignmentsApi.MapDelete("/default/{workerId:guid}", async (
    Guid workerId,
    Guid subStageId,
    string reason,
    IAssignmentEngine assignmentEngine,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var result = await assignmentEngine.RemoveDefaultAssignmentAsync(
        workerId,
        subStageId,
        reason,
        actorUserId.Value,
        $"{httpContext.Request.Method} {httpContext.Request.Path}",
        cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(
            result.Error?.Code ?? "ValidationError",
            result.Error?.Message ?? "Validation failed.",
            MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(ApiResponse.Success(new
    {
        assignmentId = result.Value!.AssignmentId,
        workerId = result.Value.WorkerId,
        subStageId = result.Value.SubStageId,
        assignmentType = result.Value.AssignmentType
    }));
})
    .RequirePermission("assignments.manage")
    .WithTags("Assignments")
    .WithName("RemoveDefaultAssignment");

assignmentsApi.MapPost("/temporary", () =>
    ApiResponse.Failure("FeatureDisabled", "التسكين غير الدائم متوقف حاليًا. استخدم التسكين الدائم فقط.", StatusCodes.Status409Conflict))
    .RequirePermission("assignments.manage")
    .WithTags("Assignments")
    .WithName("CreateTemporaryAssignment");

assignmentsApi.MapPost("/replacement", () =>
    ApiResponse.Failure("FeatureDisabled", "التسكين غير الدائم متوقف حاليًا. استخدم التسكين الدائم فقط.", StatusCodes.Status409Conflict))
    .RequirePermission("assignments.manage")
    .WithTags("Assignments")
    .WithName("CreateReplacementAssignment");

assignmentsApi.MapPost("/move", () =>
    ApiResponse.Failure("FeatureDisabled", "النقل عبر هذه الواجهة متوقف حاليًا. استخدم التسكين الدائم فقط.", StatusCodes.Status409Conflict))
    .RequirePermission("assignments.manage")
    .WithTags("Assignments")
    .WithName("MoveCurrentAssignment");

assignmentsApi.MapGet("/recommendations", async (
    Guid subStageId,
    IAssignmentRecommendationEngine recommendationEngine,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    int topCandidates = 10,
    CancellationToken cancellationToken = default) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var requestMeta = $"{httpContext.Request.Method} {httpContext.Request.Path}";
    var clientIp = httpContext.Connection.RemoteIpAddress?.ToString();
    if (!string.IsNullOrWhiteSpace(clientIp))
    {
        requestMeta = $"{requestMeta} from {clientIp}";
    }

    if (!string.IsNullOrWhiteSpace(currentUserService.UserName))
    {
        requestMeta = $"{requestMeta} by {currentUserService.UserName}";
    }

    var result = await recommendationEngine.GetRecommendationsAsync(
        subStageId,
        actorUserId.Value,
        requestMeta,
        topCandidates,
        cancellationToken);

    if (result.IsFailure)
    {
        return ApiResponse.Failure(
            result.Error?.Code ?? "ValidationError",
            result.Error?.Message ?? "Validation failed.",
            MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(ApiResponse.Success(result.Value!));
})
    .RequirePermission("assignments.view")
    .WithTags("Assignments")
    .WithName("GetAssignmentRecommendations");

assignmentsApi.MapDelete("/temporary/{assignmentId:guid}", () =>
    ApiResponse.Failure("FeatureDisabled", "إدارة التسكين غير الدائم متوقفة حاليًا.", StatusCodes.Status409Conflict))
    .RequirePermission("assignments.manage")
    .WithTags("Assignments")
    .WithName("CancelTemporaryAssignment");

assignmentsApi.MapGet("/{workerId:guid}/timeline", async (
    Guid workerId,
    IAssignmentEngine assignmentEngine,
    CancellationToken cancellationToken,
    int page = 1,
    int pageSize = 50,
    DateTime? fromDate = null,
    DateTime? toDate = null) =>
{
    var result = await assignmentEngine.GetWorkerTimelineAsync(
        workerId,
        page,
        pageSize,
        fromDate,
        toDate,
        cancellationToken);

    if (result.IsFailure)
    {
        return ApiResponse.Failure(
            result.Error?.Code ?? "ValidationError",
            result.Error?.Message ?? "Validation failed.",
            MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(new
    {
        success = true,
        data = new
        {
            items = result.Value!.Items.ToArray(),
            totalCount = result.Value.TotalCount,
            pageNumber = result.Value.PageNumber,
            pageSize = result.Value.PageSize
        }
    });
})
    .RequirePermission("assignments.view")
    .WithTags("Assignments")
    .WithName("GetWorkerAssignmentTimeline");

assignmentsApi.MapGet("/sub-stages/{subStageId:guid}/workers", async (
    Guid subStageId,
    IAssignmentEngine assignmentEngine,
    CancellationToken cancellationToken,
    DateTime? asOfUtc = null) =>
{
    var result = await assignmentEngine.GetSubStageWorkersAsync(subStageId, asOfUtc, cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(
            result.Error?.Code ?? "ValidationError",
            result.Error?.Message ?? "Validation failed.",
            MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(ApiResponse.Success(result.Value!));
})
    .RequirePermission("assignments.view")
    .WithTags("Assignments")
    .WithName("GetWorkersInSubStage");

assignmentsApi.MapGet("/sub-stages/{subStageId:guid}/worker-context", async (
    Guid subStageId,
    DateOnly productionDate,
    AppDbContext dbContext,
    IAssignmentEngine assignmentEngine,
    IAttendanceEngine attendanceEngine,
    ICairoTimeZoneProvider cairoTimeZoneProvider,
    CancellationToken cancellationToken) =>
{
    var subStageExists = await dbContext.SubStages
        .AsNoTracking()
        .AnyAsync(x => x.Id == subStageId && x.IsActive, cancellationToken);
    if (!subStageExists)
    {
        return ApiResponse.Failure("NotFound", "SubStage not found or inactive.", 404);
    }

    var workers = await dbContext.Workers
        .AsNoTracking()
        .Where(x => x.IsActive && x.EmploymentStatus == EmploymentStatus.Active)
        .OrderBy(x => x.EmployeeCode)
        .Select(x => new { x.Id, x.EmployeeCode, x.FullName, x.PhotoReference, x.LocalDepartmentName })
        .ToArrayAsync(cancellationToken);
    var localEndOfProductionDate = productionDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified).AddDays(1);
    var asOfUtc = TimeZoneInfo.ConvertTimeToUtc(localEndOfProductionDate, cairoTimeZoneProvider.TimeZone).AddTicks(-1);
    var assignments = await assignmentEngine.ResolveCurrentAssignmentsAsync(workers.Select(x => x.Id), asOfUtc, cancellationToken);
    if (assignments.IsFailure)
    {
        return ApiResponse.Failure(assignments.Error?.Code ?? "AssignmentReadFailed", assignments.Error?.Message ?? "Unable to load worker assignments.", MapFailureStatusCode(assignments.Error?.Code));
    }

    var attendance = await attendanceEngine.GetLatestAttendanceStatusByWorkerAsync(workers.Select(x => x.Id), asOfUtc, cancellationToken);
    if (attendance.IsFailure)
    {
        return ApiResponse.Failure(attendance.Error?.Code ?? "AttendanceReadFailed", attendance.Error?.Message ?? "Unable to load attendance state.", MapFailureStatusCode(attendance.Error?.Code));
    }

    var items = workers.Select(worker =>
    {
        assignments.Value!.TryGetValue(worker.Id, out var assignment);
        attendance.Value!.TryGetValue(worker.Id, out var attendanceState);
        var attendanceStatus = attendanceState?.Status ?? AttendanceStatus.Unassigned;
        var isPresent = attendanceStatus is AttendanceStatus.Present or AttendanceStatus.Late;
        var effectiveSubStageId = assignment?.EffectiveSubStageId;
        var attendanceEvidence = attendanceState is null
            ? "NoAttendanceData"
            : isPresent
                ? "ActualCheckInFound"
                : attendanceStatus == AttendanceStatus.Absent && string.Equals(attendanceState.SourceRawId, "sync-no-source", StringComparison.OrdinalIgnoreCase)
                    ? "NoSourceCheckIn"
                    : attendanceStatus == AttendanceStatus.Absent
                        ? "ConfirmedAbsent"
                        : "NoAttendanceData";
        return new
        {
            workerId = worker.Id,
            employeeCode = worker.EmployeeCode,
            fullName = worker.FullName,
            photoReference = worker.PhotoReference,
            hasPhoto = !string.IsNullOrWhiteSpace(worker.PhotoReference),
            departmentName = worker.LocalDepartmentName,
            attendanceStatus = attendanceStatus.ToString(),
            attendanceTimeUtc = attendanceState?.AttendanceTimeUtc,
            attendanceSource = attendanceState?.Source,
            attendanceEvidence,
            hasAttendanceData = attendanceState is not null,
            actualCheckInFound = isPresent,
            assignmentId = assignment?.AssignmentId,
            assignmentType = assignment?.AssignmentType?.ToString(),
            assignmentStartsAtUtc = assignment?.StartsAtUtc,
            assignmentEndsAtUtc = assignment?.EndsAtUtc,
            effectiveSubStageId,
            isAvailable = isPresent && (!effectiveSubStageId.HasValue || effectiveSubStageId.Value == subStageId)
        };
    }).ToArray();

    return Results.Ok(ApiResponse.Success(new
    {
        subStageId,
        productionDate,
        activeServiceWorkersCount = workers.Length,
        workersWithAttendanceDataCount = items.Count(x => x.hasAttendanceData),
        actualCheckInWorkersCount = items.Count(x => x.actualCheckInFound),
        noSourceCheckInWorkersCount = items.Count(x => x.attendanceEvidence == "NoSourceCheckIn"),
        currentWorkers = items.Where(x => x.effectiveSubStageId == subStageId).ToArray(),
        // Keep the attendance source read-only.  The client needs all workers confirmed
        // present (including workers currently effective on another stage) so an
        // authorized manager can make a deliberate, time-bound move instead of
        // treating a current assignment as an invisible exclusion.
        presentWorkers = items.Where(x => x.attendanceStatus is "Present" or "Late").ToArray(),
        availableWorkers = items.Where(x => x.isAvailable).ToArray(),
        unavailableWorkersCount = items.Count(x => !x.isAvailable)
    }));
})
    .RequirePermission("assignments.view")
    .WithTags("Assignments")
    .WithName("GetSubStageWorkerContext");

factoryStructureApi.MapGet("/delete-eligibility", async (
    AppDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var factories = await dbContext.Factories.AsNoTracking()
        .Select(factory => new
        {
            entityId = factory.Id,
            canDelete = !dbContext.Departments.Any(department => department.FactoryId == factory.Id)
                && !dbContext.ProductionLines.Any(line => line.FactoryId == factory.Id),
            deleteBlockReason = dbContext.Departments.Any(department => department.FactoryId == factory.Id)
                || dbContext.ProductionLines.Any(line => line.FactoryId == factory.Id)
                    ? "المصنع مرتبط بأقسام أو خطوط إنتاج."
                    : null
        })
        .ToArrayAsync(cancellationToken);
    var departments = await dbContext.Departments.AsNoTracking()
        .Select(department => new
        {
            entityId = department.Id,
            canDelete = !dbContext.ProductionLines.Any(line => line.DepartmentId == department.Id),
            deleteBlockReason = dbContext.ProductionLines.Any(line => line.DepartmentId == department.Id)
                ? "القسم مرتبط بخطوط إنتاج."
                : null
        })
        .ToArrayAsync(cancellationToken);
    var lines = await dbContext.ProductionLines.AsNoTracking()
        .Select(line => new
        {
            entityId = line.Id,
            canDelete = !dbContext.MainStages.Any(stage => stage.ProductionLineId == line.Id)
                && !dbContext.SubStages.Any(stage => stage.ProductionLineId == line.Id)
                && !dbContext.ProductionOrders.Any(order => order.ProductionLineId == line.Id),
            deleteBlockReason = dbContext.MainStages.Any(stage => stage.ProductionLineId == line.Id)
                || dbContext.SubStages.Any(stage => stage.ProductionLineId == line.Id)
                || dbContext.ProductionOrders.Any(order => order.ProductionLineId == line.Id)
                    ? "خط الإنتاج مرتبط بمراحل أو أوامر إنتاج أو بيانات تشغيلية."
                    : null
        })
        .ToArrayAsync(cancellationToken);

    return Results.Ok(ApiResponse.Success(new { factories, departments, lines }));
})
    .RequirePermission(FactoryStructurePermissions.View)
    .WithTags("FactoryStructure")
    .WithName("GetFactoryStructureDeleteEligibility");

factoryStructureApi.MapGet("/sub-stages/{subStageId:guid}/workers", async (
    Guid subStageId,
    IAssignmentEngine assignmentEngine,
    CancellationToken cancellationToken,
    DateTime? asOfUtc = null) =>
{
    var result = await assignmentEngine.GetSubStageWorkersAsync(subStageId, asOfUtc, cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(
            result.Error?.Code ?? "ValidationError",
            result.Error?.Message ?? "Validation failed.",
            MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(ApiResponse.Success(result.Value!));
})
    .RequirePermission(FactoryStructurePermissions.View)
    .WithTags("FactoryStructure")
    .WithName("GetFactoryStructureSubStageWorkers");

factoryStructureApi.MapGet("/sub-stages/staffing-coverage", async (
    IAssignmentEngine assignmentEngine,
    CancellationToken cancellationToken) =>
{
    var result = await assignmentEngine.GetActiveSubStageAssignmentCoverageAsync(cancellationToken: cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(
            result.Error?.Code ?? "FactoryStructureCoverageReadFailed",
            result.Error?.Message ?? "Unable to load sub-stage staffing coverage.",
            MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(ApiResponse.Success(result.Value!));
})
    .RequirePermission(FactoryStructurePermissions.View)
    .RequirePermission("stages.view")
    .WithTags("FactoryStructure")
    .WithName("GetFactoryStructureSubStageStaffingCoverage");

factoryStructureApi.MapGet("/sub-stages/attendance-summary", async (
    IReadinessEngine readinessEngine,
    CancellationToken cancellationToken) =>
{
    var result = await readinessEngine.GetActiveSubStageAttendanceSummariesAsync(cancellationToken: cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(
            result.Error?.Code ?? "FactoryStructureAttendanceReadFailed",
            result.Error?.Message ?? "Unable to load sub-stage attendance summaries.",
            MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(ApiResponse.Success(result.Value!));
})
    .RequirePermission(FactoryStructurePermissions.View)
    .RequirePermission("stages.view")
    .RequirePermission("attendance.view")
    .WithTags("FactoryStructure")
    .WithName("GetFactoryStructureSubStageAttendanceSummary");

factoryStructureApi.MapGet("/sub-stages/{subStageId:guid}/eligible-workers", async (
    Guid subStageId,
    AppDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var subStageExists = await dbContext.SubStages
        .AsNoTracking()
        .AnyAsync(x => x.Id == subStageId && x.IsActive, cancellationToken);

    if (!subStageExists)
    {
        return ApiResponse.Failure("NotFound", "SubStage not found or inactive.", 404);
    }

    var workers = await dbContext.Workers
        .AsNoTracking()
        .Where(x => x.IsActive && x.EmploymentStatus == EmploymentStatus.Active)
        .OrderBy(x => x.EmployeeCode)
        .Select(x => new
        {
            id = x.Id,
            code = x.EmployeeCode,
            fullName = x.FullName,
            state = "جاهز",
            phone = x.Phone
        })
        .ToArrayAsync(cancellationToken);

    return Results.Ok(ApiResponse.Success(new
    {
        items = workers,
        totalCount = workers.Length,
        pageNumber = 1,
        pageSize = workers.Length
    }));
})
    .RequirePermission(FactoryStructurePermissions.View)
    .WithTags("FactoryStructure")
    .WithName("GetFactoryStructureEligibleWorkers");

factoryStructureApi.MapPost("/assignments/default", async (
    CreateDefaultAssignmentRequest request,
    IAssignmentEngine assignmentEngine,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var requestMeta = $"{httpContext.Request.Method} {httpContext.Request.Path}";
    var result = await assignmentEngine.CreateOrUpdateDefaultAssignmentAsync(request, actorUserId.Value, requestMeta, cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(
            result.Error?.Code ?? "ValidationError",
            result.Error?.Message ?? "Validation failed.",
            MapFailureStatusCode(result.Error?.Code));
    }

    var value = result.Value!;
    var response = new
    {
        assignmentId = value.AssignmentId,
        workerId = value.WorkerId,
        subStageId = value.SubStageId,
        assignmentType = value.AssignmentType,
        startsAt = value.StartsAtUtc
    };

    return value.IsCreated
        ? Results.Created($"/api/factory-structure/assignments/default/{value.AssignmentId}", ApiResponse.Success(response))
        : Results.Ok(ApiResponse.Success(response));
})
    .RequirePermission(FactoryStructurePermissions.Manage)
    .WithTags("FactoryStructure")
    .WithName("CreateOrUpdateFactoryStructureDefaultAssignment");

notificationsApi.MapGet("", async (
    INotificationEngine notificationEngine,
    ICurrentUserService currentUserService,
    CancellationToken cancellationToken,
    bool? isRead = null,
    int page = 1,
    int pageSize = 50) =>
{
    var recipientUserId = currentUserService.UserId;
    if (recipientUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var result = await notificationEngine.GetNotificationsAsync(
        recipientUserId.Value,
        isRead,
        page,
        pageSize,
        cancellationToken);

    if (result.IsFailure)
    {
        return ApiResponse.Failure(
            result.Error?.Code ?? "ValidationError",
            result.Error?.Message ?? "Validation failed.",
            MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(new
    {
        success = true,
        data = new
        {
            items = result.Value!.Items.ToArray(),
            totalCount = result.Value.TotalCount,
            pageNumber = result.Value.PageNumber,
            pageSize
        }
    });
})
    .WithTags("Notifications")
    .WithName("GetNotifications");

notificationsApi.MapGet("/unread-count", async (
    INotificationEngine notificationEngine,
    ICurrentUserService currentUserService,
    CancellationToken cancellationToken) =>
{
    var recipientUserId = currentUserService.UserId;
    if (recipientUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var result = await notificationEngine.GetUnreadCountAsync(recipientUserId.Value, cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(
            result.Error?.Code ?? "ValidationError",
            result.Error?.Message ?? "Validation failed.",
            MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(ApiResponse.Success(new { unreadCount = result.Value }));
})
    .WithTags("Notifications")
    .WithName("GetUnreadNotificationCount");

notificationsApi.MapPatch("/{notificationId:guid}/read", async (
    Guid notificationId,
    INotificationEngine notificationEngine,
    ICurrentUserService currentUserService,
    CancellationToken cancellationToken) =>
{
    var recipientUserId = currentUserService.UserId;
    if (recipientUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var result = await notificationEngine.MarkNotificationReadAsync(recipientUserId.Value, notificationId, null, cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(
            result.Error?.Code ?? "ValidationError",
            result.Error?.Message ?? "Validation failed.",
            MapFailureStatusCode(result.Error?.Code));
    }

    var value = result.Value!;
    return Results.Ok(ApiResponse.Success(new
    {
        value.Id,
        value.IsRead,
        value.ReadAtUtc
    }));
})
    .WithTags("Notifications")
    .WithName("ReadNotification");

notificationsApi.MapPatch("/read-all", async (
    INotificationEngine notificationEngine,
    ICurrentUserService currentUserService,
    CancellationToken cancellationToken,
    DateTime? beforeDateUtc = null) =>
{
    var recipientUserId = currentUserService.UserId;
    if (recipientUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var result = await notificationEngine.MarkAllAsReadAsync(recipientUserId.Value, beforeDateUtc, cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(
            result.Error?.Code ?? "ValidationError",
            result.Error?.Message ?? "Validation failed.",
            MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(ApiResponse.Success(new { updatedCount = result.Value }));
})
    .WithTags("Notifications")
    .WithName("ReadAllNotifications");

attendanceApi.MapPost("/sync/today", async (
    IAttendanceEngine attendanceEngine,
    CancellationToken cancellationToken) =>
{
    var result = await attendanceEngine.SyncTodayAsync(cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(
            result.Error?.Code ?? "AttendanceSyncFailed",
            result.Error?.Message ?? "Unable to sync attendance data.",
            MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(ApiResponse.Success(result.Value));
})
    .RequirePermission("attendance.sync")
    .WithTags("Attendance")
    .WithName("SyncAttendanceToday");

attendanceApi.MapAttendanceWorkforceEndpoints();

attendanceApi.MapGet("/today", async (
    IAttendanceEngine attendanceEngine,
    DateTime? dateUtc = null,
    Guid? factoryId = null,
    Guid? lineId = null,
    CancellationToken cancellationToken = default) =>
{
    var result = await attendanceEngine.GetTodayAttendanceAsync(
        factoryId,
        lineId,
        dateUtc,
        cancellationToken);

    if (result.IsFailure)
    {
        return ApiResponse.Failure(
            result.Error?.Code ?? "AttendanceReadFailed",
            result.Error?.Message ?? "Unable to load today's attendance.",
        MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(ApiResponse.Success(new
    {
        date = (dateUtc ?? DateTime.UtcNow).Date,
        items = result.Value ?? Array.Empty<AttendanceWorkerStateDto>()
    }));
})
    .RequirePermission("attendance.view")
    .WithTags("Attendance")
    .WithName("GetTodayAttendance");

attendanceApi.MapGet("/workers/{workerId:guid}", async (
    Guid workerId,
    IAttendanceEngine attendanceEngine,
    DateTime? fromDateUtc = null,
    DateTime? toDateUtc = null,
    CancellationToken cancellationToken = default) =>
{
    var result = await attendanceEngine.GetWorkerAttendanceAsync(
        workerId,
        fromDateUtc,
        toDateUtc,
        cancellationToken);

    if (result.IsFailure)
    {
        return ApiResponse.Failure(
            result.Error?.Code ?? "AttendanceReadFailed",
            result.Error?.Message ?? "Unable to load worker attendance.",
        MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(ApiResponse.Success(new
    {
        workerId,
        dateFromUtc = fromDateUtc,
        dateToUtc = toDateUtc,
        items = result.Value ?? Array.Empty<AttendanceRecordDto>()
    }));
})
    .RequirePermission("attendance.view")
    .WithTags("Attendance")
    .WithName("GetWorkerAttendance");

attendanceApi.MapGet("/stages/{subStageId:guid}", async (
    Guid subStageId,
    IAttendanceEngine attendanceEngine,
    DateTime? dateUtc = null,
    CancellationToken cancellationToken = default) =>
{
    var result = await attendanceEngine.GetSubStageAttendanceAsync(subStageId, dateUtc, cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(
            result.Error?.Code ?? "AttendanceReadFailed",
            result.Error?.Message ?? "Unable to load stage attendance.",
            MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(ApiResponse.Success(result.Value));
})
    .RequirePermission("attendance.view")
    .WithTags("Attendance")
    .WithName("GetSubStageAttendance");

readinessApi.MapGet("/factory", async (
    IReadinessEngine readinessEngine,
    CancellationToken cancellationToken,
    DateTime? asOfUtc = null) =>
{
    var result = await readinessEngine.GetFactoryReadinessAsync(asOfUtc, cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(
            result.Error?.Code ?? "ValidationError",
            result.Error?.Message ?? "Validation failed.",
            MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(ApiResponse.Success(result.Value!));
})
    .WithTags("Readiness")
    .WithName("GetFactoryReadiness");

readinessApi.MapGet("/production-lines", async (
    IReadinessEngine readinessEngine,
    CancellationToken cancellationToken,
    DateTime? asOfUtc = null) =>
{
    var result = await readinessEngine.GetProductionLinesReadinessAsync(asOfUtc, cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(
            result.Error?.Code ?? "ValidationError",
            result.Error?.Message ?? "Validation failed.",
            MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(ApiResponse.Success(result.Value!));
})
    .WithTags("Readiness")
    .WithName("GetProductionLinesReadiness");

readinessApi.MapGet("/sub-stages/{subStageId:guid}", async (
    Guid subStageId,
    IReadinessEngine readinessEngine,
    CancellationToken cancellationToken,
    DateTime? asOfUtc = null) =>
{
    var result = await readinessEngine.GetSubStageReadinessAsync(subStageId, asOfUtc, cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(
            result.Error?.Code ?? "ValidationError",
            result.Error?.Message ?? "Validation failed.",
            MapFailureStatusCode(result.Error?.Code));
    }

    return Results.Ok(ApiResponse.Success(result.Value!));
})
    .WithTags("Readiness")
    .WithName("GetSubStageReadiness");

static int MapFailureStatusCode(string? code)
{
    return code switch
    {
        "ValidationError" => StatusCodes.Status400BadRequest,
        "NotFound" => StatusCodes.Status404NotFound,
        "Unauthorized" or "InvalidToken" or "InvalidCredentials" => StatusCodes.Status401Unauthorized,
        "Conflict" or "IdentityConflict" or "SourceObservedOnly" or "ExternalSourceReadOnly" => StatusCodes.Status409Conflict,
        "AttendanceSyncInProgress" => StatusCodes.Status409Conflict,
        "BootstrapNotAllowed" => StatusCodes.Status409Conflict,
        "Forbidden" => StatusCodes.Status403Forbidden,
        "AttendanceSyncTimeout" or "AttendanceSourceTimeout" => StatusCodes.Status504GatewayTimeout,
        "AttendanceSourceError" or "AttendanceSyncCancelled" => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError
    };
}

static IResult? ValidateBootstrapSecret(string? requestBootstrapSecret, string? configuredBootstrapSecret)
{
    var incomingSecret = requestBootstrapSecret?.Trim();
    if (string.IsNullOrWhiteSpace(incomingSecret))
    {
        return ApiResponse.Failure("ValidationError", "BootstrapSecret is required.");
    }

    var configuredSecret = configuredBootstrapSecret?.Trim();
    if (string.IsNullOrWhiteSpace(configuredSecret) ||
        configuredSecret.Contains("REPLACE_WITH", StringComparison.OrdinalIgnoreCase) ||
        configuredSecret.Contains("USER_SECRET", StringComparison.OrdinalIgnoreCase))
    {
        return ApiResponse.Failure("ConfigurationError", "Bootstrap secret is not configured.");
    }

    var incomingSecretBytes = Encoding.UTF8.GetBytes(incomingSecret);
    var configuredSecretBytes = Encoding.UTF8.GetBytes(configuredSecret);
    if (incomingSecretBytes.Length != configuredSecretBytes.Length ||
        !CryptographicOperations.FixedTimeEquals(incomingSecretBytes, configuredSecretBytes))
    {
        return ApiResponse.Failure("Unauthorized", "Invalid bootstrap secret.", 401);
    }

    return null;
}

static string GetRateLimitPartitionKey(HttpContext context)
{
    var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
    return !string.IsNullOrWhiteSpace(userId)
        ? $"user:{userId}"
        : $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "anonymous"}";
}

static FixedWindowRateLimiterOptions FixedWindowOptions(int permitLimit, int windowSeconds) => new()
{
    PermitLimit = permitLimit,
    QueueLimit = 0,
    Window = TimeSpan.FromSeconds(windowSeconds),
    QueueProcessingOrder = QueueProcessingOrder.OldestFirst
};

ProductionLinePlanner.Api.Endpoints.ProductionCostRecordingEndpoints.MapProductionCostRecordingEndpoints(app);
ProductionLinePlanner.Api.Endpoints.ProductionQuantitiesReportEndpoints.MapProductionQuantitiesReportEndpoints(app);
ProductionLinePlanner.Api.Endpoints.ProductionFinancialReportEndpoints.MapProductionFinancialReportEndpoints(app);

app.Run();
