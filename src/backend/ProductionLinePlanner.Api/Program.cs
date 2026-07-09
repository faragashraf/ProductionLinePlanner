using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
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
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Application.Requests;
using ProductionLinePlanner.Api.Security;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure;
using ProductionLinePlanner.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);
var allowedCorsOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>()?
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Select(origin => origin.Trim())
    .Where(origin => builder.Environment.IsDevelopment() || origin.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
    .ToArray() ?? Array.Empty<string>();
var allowedMethods = builder.Configuration
    .GetSection("Cors:AllowedMethods")
    .Get<string[]>() ?? ["GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"];
var allowedHeaders = builder.Configuration
    .GetSection("Cors:AllowedHeaders")
    .Get<string[]>() ?? ["Accept", "Content-Type", "Authorization", "X-Requested-With"];
var corsAllowCredentials = builder.Configuration.GetValue("Cors:AllowCredentials", false);
var rateLimitWindowSeconds = Math.Max(15, builder.Configuration.GetValue("Security:RateLimit:WindowSeconds", 60));
var rateLimitPermitLimit = Math.Max(1, builder.Configuration.GetValue("Security:RateLimit:PermitLimit", 120));
const string SecurityCorsPolicy = "ProductionLinePlannerCors";
const string TempStatusScheduled = "Scheduled";
const string TempStatusActive = "Active";
const string TempStatusCancelled = "Cancelled";
const string TimelineActionCreate = "Create";
const string TimelineActionUpdate = "Update";
const string TimelineActionCancel = "Cancel";
var jwtSection = builder.Configuration.GetSection("Authentication:Jwt");
var jwtIssuer = jwtSection["Issuer"] ?? "ProductionLinePlanner.Api";
var jwtAudience = jwtSection["Audience"] ?? "ProductionLinePlanner.WebClient";
var jwtAccessTokenMinutes = Math.Max(15, builder.Configuration.GetValue("Authentication:Jwt:AccessTokenMinutes", 45));
var jwtRefreshTokenDays = Math.Max(1, builder.Configuration.GetValue("Authentication:Jwt:RefreshTokenDays", 14));
var jwtSigningKey = jwtSection["SigningKey"];
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

builder.Services.AddCors(options =>
{
    options.AddPolicy(
        SecurityCorsPolicy,
        policy =>
        {
            if (allowedCorsOrigins.Length > 0)
            {
                policy.WithOrigins(allowedCorsOrigins);
            }

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
        });
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var partitionKey = context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rateLimitPermitLimit,
                QueueLimit = 0,
                Window = TimeSpan.FromSeconds(rateLimitWindowSeconds),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            });
    });
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
builder.Services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();

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
    });

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
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandler("/api/error");

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseCors(SecurityCorsPolicy);
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] =
        "camera=(), microphone=(), geolocation=(), payment=(), usb=(), magnetometer=(), gyroscope=()";
    context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains; preload";
    await next();
});
app.UseAuthentication();
app.UseAuthorization();

var factoriesApi = app.MapGroup("/api/factories").RequireAuthorization("Admin");
var productionLinesApi = app.MapGroup("/api/production-lines").RequireAuthorization("Admin");
var mainStagesApi = app.MapGroup("/api/main-stages").RequireAuthorization("Admin");
var subStagesApi = app.MapGroup("/api/sub-stages").RequireAuthorization("Admin");
var workersApi = app.MapGroup("/api/workers").RequireAuthorization("Admin");
var assignmentsApi = app.MapGroup("/api/assignments").RequireAuthorization("Admin");
var attendanceApi = app.MapGroup("/api/attendance").RequireAuthorization();
var notificationsApi = app.MapGroup("/api/notifications").RequireAuthorization("Admin");
var readinessApi = app.MapGroup("/api/readiness").RequireAuthorization("Admin");

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
        UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Unauthorized", "UnauthorizedAccess"),
        System.ComponentModel.DataAnnotations.ValidationException => (StatusCodes.Status400BadRequest, "Validation Failed", "ValidationError"),
        KeyNotFoundException => (StatusCodes.Status404NotFound, "Not Found", "ResourceNotFound"),
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
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var email = request.Email?.Trim();
    var password = request.Password?.Trim();

    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
    {
        return ApiResponse.Failure("ValidationError", "Email and password are required.");
    }

    var user = await dbContext.AppUsers
        .Include(x => x.Roles)
        .AsNoTracking()
        .FirstOrDefaultAsync(x => x.IsActive && x.Email == email, cancellationToken);

    if (user is null)
    {
        return ApiResponse.Failure("InvalidCredentials", "Invalid email or password.", 401);
    }

    if (passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password) is not (PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded))
    {
        return ApiResponse.Failure("InvalidCredentials", "Invalid email or password.", 401);
    }

    var now = DateTime.UtcNow;
    var expiresAt = now.AddMinutes(jwtAccessTokenMinutes);
    var refreshToken = AuthTokenService.GenerateRefreshToken();
    var refreshTokenHash = AuthTokenService.HashRefreshToken(refreshToken);
    var refreshTokenExpiresAt = now.AddDays(jwtRefreshTokenDays);

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
    await dbContext.SaveChangesAsync(cancellationToken);

    AssignmentHelpers.AddAuditLog(
        dbContext,
        user.Id,
        AuditActionType.Create,
        nameof(AppUser),
        user.Id.ToString(),
        before: null,
        after: new { Event = "AuthLogin", user.Email },
        httpContext: httpContext);
    var roles = user.Roles
        .Select(role => role.Role.ToString())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(role => role)
        .ToArray();

    var response = new AuthLoginResponse
    {
        AccessToken = accessToken,
        RefreshToken = refreshToken,
        ExpiresAt = expiresAt,
        UserId = user.Id,
        Roles = roles,
        Permissions = Array.Empty<string>()
    };

    return Results.Ok(ApiResponse.Success(response));
})
    .WithTags("Auth")
    .WithName("AuthLogin");

authApi.MapGet("/me", async (
    ICurrentUserService currentUserService,
    AppDbContext dbContext,
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
        .Select(role => role.Role.ToString())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(role => role)
        .ToArray();

    var permissions = AuthTokenService.ResolvePermissionsForRoles(roles);
    return Results.Ok(ApiResponse.Success(new CurrentUserResponse
    {
        Id = user.Id,
        FullName = user.FullName,
        Email = user.Email,
        Roles = roles,
        Permissions = permissions
    }));
})
    .RequireAuthorization()
    .WithTags("Auth")
    .WithName("AuthMe");

authApi.MapPost("/refresh", async (
    RefreshTokenRequest request,
    AppDbContext dbContext,
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
        if (!storedToken.IsRevoked)
        {
            storedToken.Revoke(now, "Expired");
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return ApiResponse.Failure("InvalidToken", "Invalid or expired refresh token.", 401);
    }

    var user = storedToken.AppUser;
    var roles = user.Roles
        .Select(role => role.Role.ToString())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(role => role)
        .ToArray();

    var accessTokenExpiresAt = now.AddMinutes(jwtAccessTokenMinutes);
    var accessToken = AuthTokenService.CreateAccessToken(user, now, accessTokenExpiresAt, jwtIssuer, jwtAudience, jwtKey);

    var newRefreshToken = AuthTokenService.GenerateRefreshToken();
    var newRefreshTokenHash = AuthTokenService.HashRefreshToken(newRefreshToken);
    var newRefreshTokenExpiresAt = now.AddDays(jwtRefreshTokenDays);

    storedToken.Revoke(now, "Rotated");
    storedToken.MarkAsUsed(now);
    dbContext.RefreshTokens.Add(new RefreshToken(
        id: Guid.NewGuid(),
        appUserId: user.Id,
        tokenHash: newRefreshTokenHash,
        expiresAtUtc: newRefreshTokenExpiresAt,
        createdAtUtc: now));

    AssignmentHelpers.AddAuditLog(
        dbContext,
        user.Id,
        AuditActionType.Update,
        nameof(RefreshToken),
        storedToken.Id.ToString(),
        before: storedToken,
        after: new { eventType = "AuthRefresh", storedToken.Id, replacedBy = newRefreshTokenHash[..8] },
        httpContext);

    await dbContext.SaveChangesAsync(cancellationToken);

    var response = new AuthLoginResponse
    {
        AccessToken = accessToken,
        RefreshToken = newRefreshToken,
        ExpiresAt = accessTokenExpiresAt,
        UserId = user.Id,
        Roles = roles,
        Permissions = AuthTokenService.ResolvePermissionsForRoles(roles)
    };

    return Results.Ok(ApiResponse.Success(response));
})
    .WithTags("Auth")
    .WithName("AuthRefresh");

authApi.MapPost("/logout", async (
    LogoutRequest request,
    AppDbContext dbContext,
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
        await dbContext.SaveChangesAsync(cancellationToken);

        AssignmentHelpers.AddAuditLog(
            dbContext,
            storedToken.AppUserId,
            AuditActionType.Revoke,
            nameof(RefreshToken),
            storedToken.Id.ToString(),
            before: storedToken,
            after: new { eventType = "AuthLogout" },
            httpContext);
    }

    return Results.Ok(ApiResponse.Success(new { revoked = true }));
})
    .WithTags("Auth")
    .WithName("AuthLogout");

factoriesApi.MapGet("", async (
    AppDbContext dbContext,
    CancellationToken cancellationToken,
    bool? isActive = true,
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
    .WithName("GetFactories");

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
    .WithName("GetFactory");

factoriesApi.MapPost("", async (
    CreateFactoryRequest request,
    AppDbContext dbContext,
    ICurrentUserService currentUserService,
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
    await dbContext.SaveChangesAsync(cancellationToken);
    AssignmentHelpers.AddAuditLog(
        dbContext,
        actorUserId.Value,
        AuditActionType.Create,
        nameof(Factory),
        entity.Id.ToString(),
        before: null,
        after: new { entity.Id, entity.Name, entity.Code, entity.Location, entity.IsActive },
        httpContext);

    return Results.Created($"/api/factories/{entity.Id}", ApiResponse.Success(new FactoryDto
    {
        Id = entity.Id,
        Name = entity.Name,
        Code = entity.Code,
        Location = entity.Location,
        IsActive = entity.IsActive
    }));
})
    .RequireAuthorization("SuperAdmin")
    .WithTags("Factories")
    .WithName("CreateFactory");

factoriesApi.MapPatch("/{factoryId:guid}", async (
    Guid factoryId,
    UpdateFactoryRequest request,
    AppDbContext dbContext,
    ICurrentUserService currentUserService,
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

    if (request.Name is null && request.Location is null && request.IsActive is null)
    {
        return ApiResponse.Failure("ValidationError", "No updatable fields were provided.");
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
    await dbContext.SaveChangesAsync(cancellationToken);
    AssignmentHelpers.AddAuditLog(
        dbContext,
        actorUserId.Value,
        AuditActionType.Update,
        nameof(Factory),
        entity.Id.ToString(),
        before: beforeFactory,
        after: new { entity.Id, entity.Name, entity.Code, entity.Location, entity.IsActive },
        httpContext);

    return Results.Ok(ApiResponse.Success(new FactoryDto
    {
        Id = entity.Id,
        Name = entity.Name,
        Code = entity.Code,
        Location = entity.Location,
        IsActive = entity.IsActive
    }));
})
    .RequireAuthorization("SuperAdmin")
    .WithTags("Factories")
    .WithName("UpdateFactory");

factoriesApi.MapDelete("/{factoryId:guid}", async (
    Guid factoryId,
    AppDbContext dbContext,
    ICurrentUserService currentUserService,
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

    var beforeFactory = new { entity.Id, entity.Name, entity.Code, entity.Location, entity.IsActive };
    dbContext.Entry(entity).Property(nameof(Factory.IsActive)).CurrentValue = false;
    dbContext.Entry(entity).Property(nameof(Factory.UpdatedAtUtc)).CurrentValue = DateTime.UtcNow;
    await dbContext.SaveChangesAsync(cancellationToken);
    AssignmentHelpers.AddAuditLog(
        dbContext,
        actorUserId.Value,
        AuditActionType.Delete,
        nameof(Factory),
        entity.Id.ToString(),
        before: beforeFactory,
        after: new { entity.Id, entity.Name, entity.Code, entity.Location, entity.IsActive },
        httpContext);

    return Results.NoContent();
})
    .RequireAuthorization("SuperAdmin")
    .WithTags("Factories")
    .WithName("DeleteFactory");

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
            Name = x.Name,
            LineCode = x.LineCode,
            SequenceOrder = x.SequenceOrder,
            IsActive = x.IsActive
        })
        .ToArrayAsync(cancellationToken);

    return Results.Ok(new { success = true, data = new { items = entities, totalCount, pageNumber = page, pageSize } });
})
    .WithTags("ProductionLines")
    .WithName("GetProductionLinesByFactory");

productionLinesApi.MapGet("/{lineId:guid}", async (
    Guid lineId,
    AppDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var entity = await dbContext.ProductionLines
        .AsNoTracking()
        .FirstOrDefaultAsync(x => x.Id == lineId && x.IsActive, cancellationToken);

    if (entity is null)
    {
        return ApiResponse.Failure("NotFound", "Production line not found.", statusCode: 404);
    }

    return Results.Ok(ApiResponse.Success(new ProductionLineDto
    {
        Id = entity.Id,
        FactoryId = entity.FactoryId,
        Name = entity.Name,
        LineCode = entity.LineCode,
        SequenceOrder = entity.SequenceOrder,
        IsActive = entity.IsActive
    }));
})
    .WithTags("ProductionLines")
    .WithName("GetProductionLine");

productionLinesApi.MapPost("", async (
    CreateProductionLineRequest request,
    AppDbContext dbContext,
    ICurrentUserService currentUserService,
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
        isActive: request.IsActive);

    dbContext.ProductionLines.Add(entity);
    await dbContext.SaveChangesAsync(cancellationToken);
    AssignmentHelpers.AddAuditLog(
        dbContext,
        actorUserId.Value,
        AuditActionType.Create,
        nameof(ProductionLine),
        entity.Id.ToString(),
        before: null,
        after: new { entity.Id, entity.FactoryId, entity.Name, entity.LineCode, entity.SequenceOrder, entity.IsActive },
        httpContext);

    return Results.Created($"/api/production-lines/{entity.Id}", ApiResponse.Success(new ProductionLineDto
    {
        Id = entity.Id,
        FactoryId = entity.FactoryId,
        Name = entity.Name,
        LineCode = entity.LineCode,
        SequenceOrder = entity.SequenceOrder,
        IsActive = entity.IsActive
    }));
})
    .WithTags("ProductionLines")
    .WithName("CreateProductionLine");

productionLinesApi.MapPatch("/{lineId:guid}", async (
    Guid lineId,
    UpdateProductionLineRequest request,
    AppDbContext dbContext,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var entity = await dbContext.ProductionLines.FirstOrDefaultAsync(x => x.Id == lineId && x.IsActive, cancellationToken);
    if (entity is null)
    {
        return ApiResponse.Failure("NotFound", "Production line not found.", statusCode: 404);
    }

    if (request.Name is null && request.LineCode is null && request.SequenceOrder is null && request.IsActive is null)
    {
        return ApiResponse.Failure("ValidationError", "No updatable fields were provided.");
    }

    var updatedAt = DateTime.UtcNow;
    var hasChanges = false;
    var entry = dbContext.Entry(entity);
    if (request.Name is { } name && !string.IsNullOrWhiteSpace(name))
    {
        entry.Property(nameof(ProductionLine.Name)).CurrentValue = name.Trim();
        hasChanges = true;
    }
    else if (request.Name is not null && string.IsNullOrWhiteSpace(request.Name))
    {
        return ApiResponse.Failure("ValidationError", "Name cannot be empty.");
    }

    if (request.LineCode is not null)
    {
        var normalizedLineCode = request.LineCode.Trim();
        if (string.IsNullOrWhiteSpace(normalizedLineCode))
        {
            return ApiResponse.Failure("ValidationError", "LineCode cannot be empty.");
        }

        if (entity.LineCode != normalizedLineCode)
        {
            var conflict = await dbContext.ProductionLines.AnyAsync(
                x => x.Id != lineId && x.FactoryId == entity.FactoryId && x.LineCode == normalizedLineCode,
                cancellationToken);
            if (conflict)
            {
                return ApiResponse.Failure("Conflict", "LineCode must be unique within the factory.", 409);
            }
        }

        entry.Property(nameof(ProductionLine.LineCode)).CurrentValue = normalizedLineCode;
        hasChanges = true;
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

    var beforeProductionLine = new { entity.Id, entity.FactoryId, entity.Name, entity.LineCode, entity.SequenceOrder, entity.IsActive };
    entry.Property(nameof(ProductionLine.UpdatedAtUtc)).CurrentValue = updatedAt;
    await dbContext.SaveChangesAsync(cancellationToken);
    AssignmentHelpers.AddAuditLog(
        dbContext,
        actorUserId.Value,
        AuditActionType.Update,
        nameof(ProductionLine),
        entity.Id.ToString(),
        before: beforeProductionLine,
        after: new { entity.Id, entity.FactoryId, entity.Name, entity.LineCode, entity.SequenceOrder, entity.IsActive },
        httpContext);

    return Results.Ok(ApiResponse.Success(new ProductionLineDto
    {
        Id = entity.Id,
        FactoryId = entity.FactoryId,
        Name = entity.Name,
        LineCode = entity.LineCode,
        SequenceOrder = entity.SequenceOrder,
        IsActive = entity.IsActive
    }));
})
    .WithTags("ProductionLines")
    .WithName("UpdateProductionLine");

productionLinesApi.MapDelete("/{lineId:guid}", async (
    Guid lineId,
    AppDbContext dbContext,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var entity = await dbContext.ProductionLines.FirstOrDefaultAsync(x => x.Id == lineId && x.IsActive, cancellationToken);
    if (entity is null)
    {
        return ApiResponse.Failure("NotFound", "Production line not found.", 404);
    }

    var beforeProductionLine = new { entity.Id, entity.FactoryId, entity.Name, entity.LineCode, entity.SequenceOrder, entity.IsActive };
    dbContext.Entry(entity).Property(nameof(ProductionLine.IsActive)).CurrentValue = false;
    dbContext.Entry(entity).Property(nameof(ProductionLine.UpdatedAtUtc)).CurrentValue = DateTime.UtcNow;
    await dbContext.SaveChangesAsync(cancellationToken);
    AssignmentHelpers.AddAuditLog(
        dbContext,
        actorUserId.Value,
        AuditActionType.Delete,
        nameof(ProductionLine),
        entity.Id.ToString(),
        before: beforeProductionLine,
        after: new { entity.Id, entity.FactoryId, entity.Name, entity.LineCode, entity.SequenceOrder, entity.IsActive },
        httpContext);

    return Results.NoContent();
})
    .RequireAuthorization("SuperAdmin")
    .WithTags("ProductionLines")
    .WithName("DeleteProductionLine");

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

mainStagesApi.MapGet("/{mainStageId:guid}", async (
    Guid mainStageId,
    AppDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var entity = await dbContext.MainStages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == mainStageId && x.IsActive, cancellationToken);
    if (entity is null)
    {
        return ApiResponse.Failure("NotFound", "Main stage not found.", 404);
    }

    return Results.Ok(ApiResponse.Success(new MainStageDto
    {
        Id = entity.Id,
        ProductionLineId = entity.ProductionLineId,
        Name = entity.Name,
        SequenceOrder = entity.SequenceOrder,
        IsCritical = entity.IsCritical,
        IsActive = entity.IsActive
    }));
})
    .WithTags("MainStages")
    .WithName("GetMainStage");

mainStagesApi.MapPost("", async (
    CreateMainStageRequest request,
    AppDbContext dbContext,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    if (request.ProductionLineId == Guid.Empty)
    {
        return ApiResponse.Failure("ValidationError", "ProductionLineId is required.");
    }

    if (string.IsNullOrWhiteSpace(request.Name))
    {
        return ApiResponse.Failure("ValidationError", "Name is required.");
    }

    if (request.SequenceOrder < 0)
    {
        return ApiResponse.Failure("ValidationError", "SequenceOrder must be zero or greater.");
    }

    var lineExists = await dbContext.ProductionLines.AnyAsync(x => x.Id == request.ProductionLineId && x.IsActive, cancellationToken);
    if (!lineExists)
    {
        return ApiResponse.Failure("ValidationError", "ProductionLineId does not exist.", 404);
    }

    var hasConflict = await dbContext.MainStages.AnyAsync(
        x => x.ProductionLineId == request.ProductionLineId && x.SequenceOrder == request.SequenceOrder && x.IsActive,
        cancellationToken);
    if (hasConflict)
    {
        return ApiResponse.Failure("Conflict", "SequenceOrder must be unique for this production line.", statusCode: 409);
    }

    var entity = new MainStage(
        id: Guid.NewGuid(),
        productionLineId: request.ProductionLineId,
        name: request.Name,
        isCritical: request.IsCritical,
        sequenceOrder: request.SequenceOrder,
        isActive: request.IsActive);

    dbContext.MainStages.Add(entity);
    await dbContext.SaveChangesAsync(cancellationToken);
    AssignmentHelpers.AddAuditLog(
        dbContext,
        actorUserId.Value,
        AuditActionType.Create,
        nameof(MainStage),
        entity.Id.ToString(),
        before: null,
        after: new { entity.Id, entity.ProductionLineId, entity.Name, entity.IsCritical, entity.SequenceOrder, entity.IsActive },
        httpContext);

    return Results.Created($"/api/main-stages/{entity.Id}", ApiResponse.Success(new MainStageDto
    {
        Id = entity.Id,
        ProductionLineId = entity.ProductionLineId,
        Name = entity.Name,
        SequenceOrder = entity.SequenceOrder,
        IsCritical = entity.IsCritical,
        IsActive = entity.IsActive
    }));
})
    .WithTags("MainStages")
    .WithName("CreateMainStage");

mainStagesApi.MapPatch("/{mainStageId:guid}", async (
    Guid mainStageId,
    UpdateMainStageRequest request,
    AppDbContext dbContext,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var entity = await dbContext.MainStages.FirstOrDefaultAsync(x => x.Id == mainStageId && x.IsActive, cancellationToken);
    if (entity is null)
    {
        return ApiResponse.Failure("NotFound", "Main stage not found.", 404);
    }

    if (request.Name is null && request.IsCritical is null && request.SequenceOrder is null && request.IsActive is null)
    {
        return ApiResponse.Failure("ValidationError", "No updatable fields were provided.");
    }

    var updatedAt = DateTime.UtcNow;
    var hasChanges = false;
    if (request.Name is { } name && !string.IsNullOrWhiteSpace(name))
    {
        entity.Rename(name, updatedAt);
        hasChanges = true;
    }
    else if (request.Name is not null && string.IsNullOrWhiteSpace(request.Name))
    {
        return ApiResponse.Failure("ValidationError", "Name cannot be empty.");
    }

    if (request.IsCritical is not null)
    {
        dbContext.Entry(entity).Property(nameof(MainStage.IsCritical)).CurrentValue = request.IsCritical.Value;
        hasChanges = true;
    }

    if (request.SequenceOrder is not null)
    {
        if (request.SequenceOrder.Value < 0)
        {
            return ApiResponse.Failure("ValidationError", "SequenceOrder must be zero or greater.");
        }

        if (entity.SequenceOrder != request.SequenceOrder.Value)
        {
            var sequenceConflict = await dbContext.MainStages.AnyAsync(
                x => x.Id != mainStageId && x.ProductionLineId == entity.ProductionLineId && x.SequenceOrder == request.SequenceOrder.Value,
                cancellationToken);
            if (sequenceConflict)
            {
                return ApiResponse.Failure("Conflict", "SequenceOrder must be unique within this production line.", 409);
            }

            dbContext.Entry(entity).Property(nameof(MainStage.SequenceOrder)).CurrentValue = request.SequenceOrder.Value;
            hasChanges = true;
        }
    }

    if (request.IsActive is not null)
    {
        if (entity.IsActive != request.IsActive.Value)
        {
            dbContext.Entry(entity).Property(nameof(MainStage.IsActive)).CurrentValue = request.IsActive.Value;
            hasChanges = true;
        }
    }

    if (!hasChanges)
    {
        return ApiResponse.Failure("ValidationError", "No valid changes detected.");
    }

    var beforeMainStage = new { entity.Id, entity.ProductionLineId, entity.Name, entity.IsCritical, entity.SequenceOrder, entity.IsActive };
    dbContext.Entry(entity).Property(nameof(MainStage.UpdatedAtUtc)).CurrentValue = updatedAt;
    await dbContext.SaveChangesAsync(cancellationToken);
    AssignmentHelpers.AddAuditLog(
        dbContext,
        actorUserId.Value,
        AuditActionType.Update,
        nameof(MainStage),
        entity.Id.ToString(),
        before: beforeMainStage,
        after: new { entity.Id, entity.ProductionLineId, entity.Name, entity.IsCritical, entity.SequenceOrder, entity.IsActive },
        httpContext);

    return Results.Ok(ApiResponse.Success(new MainStageDto
    {
        Id = entity.Id,
        ProductionLineId = entity.ProductionLineId,
        Name = entity.Name,
        SequenceOrder = entity.SequenceOrder,
        IsCritical = entity.IsCritical,
        IsActive = entity.IsActive
    }));
})
    .WithTags("MainStages")
    .WithName("UpdateMainStage");

mainStagesApi.MapDelete("/{mainStageId:guid}", async (
    Guid mainStageId,
    AppDbContext dbContext,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var entity = await dbContext.MainStages.FirstOrDefaultAsync(x => x.Id == mainStageId && x.IsActive, cancellationToken);
    if (entity is null)
    {
        return ApiResponse.Failure("NotFound", "Main stage not found.", 404);
    }

    var beforeMainStage = new { entity.Id, entity.ProductionLineId, entity.Name, entity.IsCritical, entity.SequenceOrder, entity.IsActive };
    dbContext.Entry(entity).Property(nameof(MainStage.IsActive)).CurrentValue = false;
    dbContext.Entry(entity).Property(nameof(MainStage.UpdatedAtUtc)).CurrentValue = DateTime.UtcNow;
    await dbContext.SaveChangesAsync(cancellationToken);
    AssignmentHelpers.AddAuditLog(
        dbContext,
        actorUserId.Value,
        AuditActionType.Delete,
        nameof(MainStage),
        entity.Id.ToString(),
        before: beforeMainStage,
        after: new { entity.Id, entity.ProductionLineId, entity.Name, entity.IsCritical, entity.SequenceOrder, entity.IsActive },
        httpContext);

    return Results.NoContent();
})
    .RequireAuthorization("SuperAdmin")
    .WithTags("MainStages")
    .WithName("DeleteMainStage");

mainStagesApi.MapGet("/{mainStageId:guid}/sub-stages", async (
    AppDbContext dbContext,
    Guid mainStageId,
    CancellationToken cancellationToken,
    bool includeInactive = false,
    int page = 1,
    int pageSize = 50) =>
{
    if (page < 1 || pageSize < 1 || pageSize > 200)
    {
        return ApiResponse.Failure("ValidationError", "page and pageSize must be positive, pageSize max 200.");
    }

    var mainStageExists = await dbContext.MainStages.AnyAsync(x => x.Id == mainStageId && x.IsActive, cancellationToken);
    if (!mainStageExists)
    {
        return ApiResponse.Failure("NotFound", "Main stage not found.", 404);
    }

    var query = dbContext.SubStages.AsNoTracking().Where(x => x.MainStageId == mainStageId);
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
        .Select(x => new SubStageDto
        {
            Id = x.Id,
            MainStageId = x.MainStageId,
            Name = x.Name,
            Capacity = x.Capacity,
            SequenceOrder = x.SequenceOrder,
            IsActive = x.IsActive
        })
        .ToArrayAsync(cancellationToken);

    return Results.Ok(new { success = true, data = new { items = entities, totalCount, pageNumber = page, pageSize } });
})
    .WithTags("SubStages")
    .WithName("GetSubStagesByMainStage");

subStagesApi.MapGet("/{subStageId:guid}", async (
    Guid subStageId,
    AppDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var entity = await dbContext.SubStages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == subStageId && x.IsActive, cancellationToken);
    if (entity is null)
    {
        return ApiResponse.Failure("NotFound", "Sub stage not found.", 404);
    }

    return Results.Ok(ApiResponse.Success(new SubStageDto
    {
        Id = entity.Id,
        MainStageId = entity.MainStageId,
        Name = entity.Name,
        Capacity = entity.Capacity,
        SequenceOrder = entity.SequenceOrder,
        IsActive = entity.IsActive
    }));
})
    .WithTags("SubStages")
    .WithName("GetSubStage");

subStagesApi.MapPost("", async (
    CreateSubStageRequest request,
    AppDbContext dbContext,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    if (request.MainStageId == Guid.Empty)
    {
        return ApiResponse.Failure("ValidationError", "MainStageId is required.");
    }

    if (string.IsNullOrWhiteSpace(request.Name))
    {
        return ApiResponse.Failure("ValidationError", "Name is required.");
    }

    if (request.Capacity < 0)
    {
        return ApiResponse.Failure("ValidationError", "Capacity must be zero or greater.");
    }

    if (request.SequenceOrder < 0)
    {
        return ApiResponse.Failure("ValidationError", "SequenceOrder must be zero or greater.");
    }

    var mainStageExists = await dbContext.MainStages.AnyAsync(x => x.Id == request.MainStageId && x.IsActive, cancellationToken);
    if (!mainStageExists)
    {
        return ApiResponse.Failure("ValidationError", "MainStageId does not exist.", 404);
    }

    var hasConflict = await dbContext.SubStages.AnyAsync(
        x => x.MainStageId == request.MainStageId && x.SequenceOrder == request.SequenceOrder && x.IsActive,
        cancellationToken);
    if (hasConflict)
    {
        return ApiResponse.Failure("Conflict", "SequenceOrder must be unique within this main stage.", 409);
    }

    var entity = new SubStage(
        id: Guid.NewGuid(),
        mainStageId: request.MainStageId,
        name: request.Name,
        capacity: request.Capacity,
        sequenceOrder: request.SequenceOrder,
        isActive: request.IsActive);

    dbContext.SubStages.Add(entity);
    await dbContext.SaveChangesAsync(cancellationToken);
    AssignmentHelpers.AddAuditLog(
        dbContext,
        actorUserId.Value,
        AuditActionType.Create,
        nameof(SubStage),
        entity.Id.ToString(),
        before: null,
        after: new { entity.Id, entity.MainStageId, entity.Name, entity.Capacity, entity.SequenceOrder, entity.IsActive },
        httpContext);

    return Results.Created($"/api/sub-stages/{entity.Id}", ApiResponse.Success(new SubStageDto
    {
        Id = entity.Id,
        MainStageId = entity.MainStageId,
        Name = entity.Name,
        Capacity = entity.Capacity,
        SequenceOrder = entity.SequenceOrder,
        IsActive = entity.IsActive
    }));
})
    .WithTags("SubStages")
    .WithName("CreateSubStage");

subStagesApi.MapPatch("/{subStageId:guid}", async (
    Guid subStageId,
    UpdateSubStageRequest request,
    AppDbContext dbContext,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var entity = await dbContext.SubStages.FirstOrDefaultAsync(x => x.Id == subStageId && x.IsActive, cancellationToken);
    if (entity is null)
    {
        return ApiResponse.Failure("NotFound", "Sub stage not found.", 404);
    }

    if (request.Name is null && request.Capacity is null && request.SequenceOrder is null && request.IsActive is null)
    {
        return ApiResponse.Failure("ValidationError", "No updatable fields were provided.");
    }

    var updatedAt = DateTime.UtcNow;
    var hasChanges = false;

    if (request.Name is { } name && !string.IsNullOrWhiteSpace(name))
    {
        dbContext.Entry(entity).Property(nameof(SubStage.Name)).CurrentValue = name.Trim();
        hasChanges = true;
    }
    else if (request.Name is not null && string.IsNullOrWhiteSpace(request.Name))
    {
        return ApiResponse.Failure("ValidationError", "Name cannot be empty.");
    }

    if (request.Capacity is not null)
    {
        try
        {
            entity.UpdateCapacity(request.Capacity.Value, updatedAt);
            hasChanges = true;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return ApiResponse.Failure("ValidationError", ex.Message);
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
            var sequenceConflict = await dbContext.SubStages.AnyAsync(
                x => x.Id != subStageId && x.MainStageId == entity.MainStageId && x.SequenceOrder == request.SequenceOrder.Value,
                cancellationToken);
            if (sequenceConflict)
            {
                return ApiResponse.Failure("Conflict", "SequenceOrder must be unique within this main stage.", 409);
            }

            dbContext.Entry(entity).Property(nameof(SubStage.SequenceOrder)).CurrentValue = request.SequenceOrder.Value;
            hasChanges = true;
        }
    }

    if (request.IsActive is not null)
    {
        if (entity.IsActive != request.IsActive.Value)
        {
            dbContext.Entry(entity).Property(nameof(SubStage.IsActive)).CurrentValue = request.IsActive.Value;
            hasChanges = true;
        }
    }

    if (!hasChanges)
    {
        return ApiResponse.Failure("ValidationError", "No valid changes detected.");
    }

    var beforeSubStage = new { entity.Id, entity.MainStageId, entity.Name, entity.Capacity, entity.SequenceOrder, entity.IsActive };
    dbContext.Entry(entity).Property(nameof(SubStage.UpdatedAtUtc)).CurrentValue = updatedAt;
    await dbContext.SaveChangesAsync(cancellationToken);
    AssignmentHelpers.AddAuditLog(
        dbContext,
        actorUserId.Value,
        AuditActionType.Update,
        nameof(SubStage),
        entity.Id.ToString(),
        before: beforeSubStage,
        after: new { entity.Id, entity.MainStageId, entity.Name, entity.Capacity, entity.SequenceOrder, entity.IsActive },
        httpContext);

    return Results.Ok(ApiResponse.Success(new SubStageDto
    {
        Id = entity.Id,
        MainStageId = entity.MainStageId,
        Name = entity.Name,
        Capacity = entity.Capacity,
        SequenceOrder = entity.SequenceOrder,
        IsActive = entity.IsActive
    }));
})
    .WithTags("SubStages")
    .WithName("UpdateSubStage");

subStagesApi.MapDelete("/{subStageId:guid}", async (
    Guid subStageId,
    AppDbContext dbContext,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var entity = await dbContext.SubStages.FirstOrDefaultAsync(x => x.Id == subStageId && x.IsActive, cancellationToken);
    if (entity is null)
    {
        return ApiResponse.Failure("NotFound", "Sub stage not found.", 404);
    }

    var beforeSubStage = new { entity.Id, entity.MainStageId, entity.Name, entity.Capacity, entity.SequenceOrder, entity.IsActive };
    dbContext.Entry(entity).Property(nameof(SubStage.IsActive)).CurrentValue = false;
    dbContext.Entry(entity).Property(nameof(SubStage.UpdatedAtUtc)).CurrentValue = DateTime.UtcNow;
    await dbContext.SaveChangesAsync(cancellationToken);
    AssignmentHelpers.AddAuditLog(
        dbContext,
        actorUserId.Value,
        AuditActionType.Delete,
        nameof(SubStage),
        entity.Id.ToString(),
        before: beforeSubStage,
        after: new { entity.Id, entity.MainStageId, entity.Name, entity.Capacity, entity.SequenceOrder, entity.IsActive },
        httpContext);

    return Results.NoContent();
})
    .RequireAuthorization("SuperAdmin")
    .WithTags("SubStages")
    .WithName("DeleteSubStage");

workersApi.MapGet("", async (
    AppDbContext dbContext,
    string? search,
    CancellationToken cancellationToken,
    bool? isActive = true,
    int page = 1,
    int pageSize = 50) =>
{
    if (page < 1 || pageSize < 1 || pageSize > 200)
    {
        return ApiResponse.Failure("ValidationError", "page and pageSize must be positive, pageSize max 200.");
    }

    var searchPattern = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%";
    var query = dbContext.Workers.AsNoTracking();

    if (isActive.HasValue)
    {
        query = query.Where(x => x.IsActive == isActive.Value);
    }

    if (searchPattern is not null)
    {
        query = query.Where(x => EF.Functions.Like(x.EmployeeCode, searchPattern) || EF.Functions.Like(x.FullName, searchPattern));
    }

    var totalCount = await query.CountAsync(cancellationToken);
    var entities = await query
        .OrderBy(x => x.FullName)
        .ThenBy(x => x.EmployeeCode)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToArrayAsync(cancellationToken);

    var workerIds = entities.Select(x => x.Id).ToArray();
    var activeDefaultAssignments = new List<(Guid WorkerId, DateTime AssignedAt, Guid Id, Guid SubStageId)>();
    if (workerIds.Length > 0)
    {
        activeDefaultAssignments = (await dbContext.WorkerDefaultAssignments
                .AsNoTracking()
                .Where(x => workerIds.Contains(x.WorkerId) && x.IsActive)
                .Select(x => new { x.WorkerId, x.AssignedAt, x.Id, x.SubStageId })
                .ToListAsync(cancellationToken))
            .Select(x => (x.WorkerId, x.AssignedAt, x.Id, x.SubStageId))
            .ToList();
    }

    var defaultSubStageByWorker = activeDefaultAssignments
        .OrderByDescending(x => x.AssignedAt)
        .ThenByDescending(x => x.Id)
        .GroupBy(x => x.WorkerId)
        .ToDictionary(g => g.Key, g => (Guid?)g.First().SubStageId);

    var dtos = entities.Select(x => new WorkerDto
    {
        Id = x.Id,
        EmployeeCode = x.EmployeeCode,
        FullName = x.FullName,
        AttendanceUserId = x.AttendanceUserId,
        BadgeNumber = x.BadgeNumber,
        Phone = x.Phone,
        IsActive = x.IsActive,
        DefaultSubStageId = defaultSubStageByWorker.GetValueOrDefault(x.Id)
    }).ToArray();

    return Results.Ok(new { success = true, data = new { items = dtos, totalCount, pageNumber = page, pageSize } });
})
    .WithTags("Workers")
    .WithName("GetWorkers");

workersApi.MapGet("/{workerId:guid}", async (
    Guid workerId,
    AppDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var entity = await dbContext.Workers
        .AsNoTracking()
        .FirstOrDefaultAsync(x => x.Id == workerId && x.IsActive, cancellationToken);
    if (entity is null)
    {
        return ApiResponse.Failure("NotFound", "Worker not found.", 404);
    }

    var defaultSubStageId = await dbContext.WorkerDefaultAssignments
        .AsNoTracking()
        .Where(x => x.WorkerId == workerId && x.IsActive)
        .OrderByDescending(x => x.AssignedAt)
        .ThenByDescending(x => x.Id)
        .Select(x => (Guid?)x.SubStageId)
        .FirstOrDefaultAsync(cancellationToken);

    return Results.Ok(ApiResponse.Success(new WorkerDto
    {
        Id = entity.Id,
        EmployeeCode = entity.EmployeeCode,
        FullName = entity.FullName,
        AttendanceUserId = entity.AttendanceUserId,
        BadgeNumber = entity.BadgeNumber,
        Phone = entity.Phone,
        IsActive = entity.IsActive,
        DefaultSubStageId = defaultSubStageId
    }));
})
    .WithTags("Workers")
    .WithName("GetWorker");

workersApi.MapPost("", async (
    CreateWorkerRequest request,
    AppDbContext dbContext,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    if (string.IsNullOrWhiteSpace(request.EmployeeCode))
    {
        return ApiResponse.Failure("ValidationError", "EmployeeCode is required.");
    }

    if (string.IsNullOrWhiteSpace(request.FullName))
    {
        return ApiResponse.Failure("ValidationError", "FullName is required.");
    }

    var employeeCode = request.EmployeeCode.Trim();
    var hasConflict = await dbContext.Workers.AnyAsync(x => x.EmployeeCode == employeeCode, cancellationToken);
    if (hasConflict)
    {
        return ApiResponse.Failure("Conflict", "EmployeeCode must be unique.", 409);
    }

    var entity = new Worker(
        id: Guid.NewGuid(),
        employeeCode: employeeCode,
        fullName: request.FullName,
        phone: request.Phone,
        attendanceUserId: request.AttendanceUserId,
        badgeNumber: request.BadgeNumber,
        isActive: request.IsActive);

    dbContext.Workers.Add(entity);
    await dbContext.SaveChangesAsync(cancellationToken);
    AssignmentHelpers.AddAuditLog(
        dbContext,
        actorUserId.Value,
        AuditActionType.Create,
        nameof(Worker),
        entity.Id.ToString(),
        before: null,
        after: new { entity.Id, entity.EmployeeCode, entity.FullName, entity.AttendanceUserId, entity.BadgeNumber, entity.Phone, entity.IsActive },
        httpContext);

    var defaultSubStageId = await dbContext.WorkerDefaultAssignments
        .AsNoTracking()
        .Where(x => x.WorkerId == entity.Id && x.IsActive)
        .OrderByDescending(x => x.AssignedAt)
        .ThenByDescending(x => x.Id)
        .Select(x => (Guid?)x.SubStageId)
        .FirstOrDefaultAsync(cancellationToken);

    return Results.Created($"/api/workers/{entity.Id}", ApiResponse.Success(new WorkerDto
    {
        Id = entity.Id,
        EmployeeCode = entity.EmployeeCode,
        FullName = entity.FullName,
        AttendanceUserId = entity.AttendanceUserId,
        BadgeNumber = entity.BadgeNumber,
        Phone = entity.Phone,
        IsActive = entity.IsActive,
        DefaultSubStageId = defaultSubStageId
    }));
})
    .WithTags("Workers")
    .WithName("CreateWorker");

workersApi.MapPatch("/{workerId:guid}", async (
    Guid workerId,
    UpdateWorkerRequest request,
    AppDbContext dbContext,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var entity = await dbContext.Workers.FirstOrDefaultAsync(x => x.Id == workerId && x.IsActive, cancellationToken);
    if (entity is null)
    {
        return ApiResponse.Failure("NotFound", "Worker not found.", 404);
    }

    if (request.FullName is null && request.Phone is null && request.AttendanceUserId is null &&
        request.BadgeNumber is null && request.IsActive is null)
    {
        return ApiResponse.Failure("ValidationError", "No updatable fields were provided.");
    }

    var updatedAt = DateTime.UtcNow;
    var hasChanges = false;

    if (request.FullName is { } fullName && !string.IsNullOrWhiteSpace(fullName))
    {
        entity.UpdateName(fullName, updatedAt);
        hasChanges = true;
    }
    else if (request.FullName is not null && string.IsNullOrWhiteSpace(request.FullName))
    {
        return ApiResponse.Failure("ValidationError", "FullName cannot be empty.");
    }

    if (request.AttendanceUserId is not null)
    {
        var trimmedAttendanceUserId = request.AttendanceUserId.Trim();
        if (string.IsNullOrWhiteSpace(trimmedAttendanceUserId))
        {
            return ApiResponse.Failure("ValidationError", "AttendanceUserId cannot be empty.");
        }

        if (entity.AttendanceUserId != trimmedAttendanceUserId)
        {
            dbContext.Entry(entity).Property(nameof(Worker.AttendanceUserId)).CurrentValue = trimmedAttendanceUserId;
            hasChanges = true;
        }
    }

    if (request.Phone is not null)
    {
        var trimmedPhone = request.Phone.Trim();
        if (string.IsNullOrWhiteSpace(trimmedPhone))
        {
            return ApiResponse.Failure("ValidationError", "Phone cannot be empty.");
        }

        if (entity.Phone != trimmedPhone)
        {
            dbContext.Entry(entity).Property(nameof(Worker.Phone)).CurrentValue = trimmedPhone;
            hasChanges = true;
        }
    }

    if (request.BadgeNumber is not null)
    {
        var trimmedBadgeNumber = request.BadgeNumber.Trim();
        if (string.IsNullOrWhiteSpace(trimmedBadgeNumber))
        {
            return ApiResponse.Failure("ValidationError", "BadgeNumber cannot be empty.");
        }

        if (entity.BadgeNumber != trimmedBadgeNumber)
        {
            dbContext.Entry(entity).Property(nameof(Worker.BadgeNumber)).CurrentValue = trimmedBadgeNumber;
            hasChanges = true;
        }
    }

    if (request.IsActive is not null)
    {
        if (entity.IsActive != request.IsActive.Value)
        {
            dbContext.Entry(entity).Property(nameof(Worker.IsActive)).CurrentValue = request.IsActive.Value;
            hasChanges = true;
        }
    }

    if (!hasChanges)
    {
        return ApiResponse.Failure("ValidationError", "No valid changes detected.");
    }

    var beforeWorker = new { entity.Id, entity.EmployeeCode, entity.FullName, entity.AttendanceUserId, entity.BadgeNumber, entity.Phone, entity.IsActive };
    dbContext.Entry(entity).Property(nameof(Worker.UpdatedAtUtc)).CurrentValue = updatedAt;
    await dbContext.SaveChangesAsync(cancellationToken);
    AssignmentHelpers.AddAuditLog(
        dbContext,
        actorUserId.Value,
        AuditActionType.Update,
        nameof(Worker),
        entity.Id.ToString(),
        before: beforeWorker,
        after: new { entity.Id, entity.EmployeeCode, entity.FullName, entity.AttendanceUserId, entity.BadgeNumber, entity.Phone, entity.IsActive },
        httpContext);

    var defaultSubStageId = await dbContext.WorkerDefaultAssignments
        .AsNoTracking()
        .Where(x => x.WorkerId == workerId && x.IsActive)
        .OrderByDescending(x => x.AssignedAt)
        .ThenByDescending(x => x.Id)
        .Select(x => (Guid?)x.SubStageId)
        .FirstOrDefaultAsync(cancellationToken);

    return Results.Ok(ApiResponse.Success(new WorkerDto
    {
        Id = entity.Id,
        EmployeeCode = entity.EmployeeCode,
        FullName = entity.FullName,
        AttendanceUserId = entity.AttendanceUserId,
        BadgeNumber = entity.BadgeNumber,
        Phone = entity.Phone,
        IsActive = entity.IsActive,
        DefaultSubStageId = defaultSubStageId
    }));
})
    .WithTags("Workers")
    .WithName("UpdateWorker");

workersApi.MapDelete("/{workerId:guid}", async (
    Guid workerId,
    AppDbContext dbContext,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var entity = await dbContext.Workers.FirstOrDefaultAsync(x => x.Id == workerId && x.IsActive, cancellationToken);
    if (entity is null)
    {
        return ApiResponse.Failure("NotFound", "Worker not found.", 404);
    }

    var beforeWorker = new { entity.Id, entity.EmployeeCode, entity.FullName, entity.AttendanceUserId, entity.BadgeNumber, entity.Phone, entity.IsActive };
    dbContext.Entry(entity).Property(nameof(Worker.IsActive)).CurrentValue = false;
    dbContext.Entry(entity).Property(nameof(Worker.UpdatedAtUtc)).CurrentValue = DateTime.UtcNow;
    await dbContext.SaveChangesAsync(cancellationToken);
    AssignmentHelpers.AddAuditLog(
        dbContext,
        actorUserId.Value,
        AuditActionType.Delete,
        nameof(Worker),
        entity.Id.ToString(),
        before: beforeWorker,
        after: new { entity.Id, entity.EmployeeCode, entity.FullName, entity.AttendanceUserId, entity.BadgeNumber, entity.Phone, entity.IsActive },
        httpContext);

    return Results.NoContent();
})
    .RequireAuthorization("SuperAdmin")
    .WithTags("Workers")
    .WithName("DeleteWorker");

workersApi.MapGet("/{workerId:guid}/current-assignment", async (
    Guid workerId,
    AppDbContext dbContext,
    CancellationToken cancellationToken,
    DateTime? asOfUtc) =>
{
    var workerExists = await dbContext.Workers
        .AsNoTracking()
        .AnyAsync(x => x.Id == workerId && x.IsActive, cancellationToken);

    if (!workerExists)
    {
        return ApiResponse.Failure("NotFound", "Worker not found.", 404);
    }

    var asOf = asOfUtc ?? DateTime.UtcNow;
    var assignments = await AssignmentHelpers.ResolveCurrentAssignmentsAsync(dbContext, [workerId], asOf, cancellationToken);
    var assignment = assignments.GetValueOrDefault(workerId);

    var dto = new CurrentWorkerAssignmentDto
    {
        WorkerId = workerId,
        EffectiveSubStageId = assignment?.EffectiveSubStageId,
        AssignmentType = assignment?.AssignmentType,
        StartedAtUtc = assignment?.StartsAtUtc,
        EndsAtUtc = assignment?.EndsAtUtc,
        FromSubStageId = assignment?.FromSubStageId,
        ToSubStageId = assignment?.ToSubStageId,
        ReplacementForWorkerId = assignment?.ReplacementForWorkerId
    };

    return Results.Ok(ApiResponse.Success(dto));
})
    .WithTags("Workers")
    .WithName("GetWorkerCurrentAssignment");

assignmentsApi.MapPost("/default", async (
    CreateDefaultAssignmentRequest request,
    AppDbContext dbContext,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    if (request.WorkerId == Guid.Empty)
    {
        return ApiResponse.Failure("ValidationError", "WorkerId is required.");
    }

    if (request.SubStageId == Guid.Empty)
    {
        return ApiResponse.Failure("ValidationError", "SubStageId is required.");
    }

    var now = DateTime.UtcNow;

    var worker = await dbContext.Workers
        .AsNoTracking()
        .FirstOrDefaultAsync(x => x.Id == request.WorkerId, cancellationToken);

    if (worker is null || !worker.IsActive)
    {
        return ApiResponse.Failure("NotFound", "Worker not found or inactive.", 404);
    }

    var subStage = await dbContext.SubStages
        .AsNoTracking()
        .FirstOrDefaultAsync(x => x.Id == request.SubStageId, cancellationToken);

    if (subStage is null || !subStage.IsActive)
    {
        return ApiResponse.Failure("NotFound", "SubStage not found or inactive.", 404);
    }

    var currentDefaults = await dbContext.WorkerDefaultAssignments
        .Where(x => x.WorkerId == request.WorkerId && x.IsActive)
        .OrderByDescending(x => x.AssignedAt)
        .ThenByDescending(x => x.Id)
        .ToListAsync(cancellationToken);

    var currentDefault = currentDefaults.FirstOrDefault();

    if (currentDefault is not null && currentDefaults.Count > 1)
    {
        foreach (var duplicate in currentDefaults.Where(x => x.Id != currentDefault.Id))
        {
            dbContext.Entry(duplicate).Property(nameof(WorkerDefaultAssignment.IsActive)).CurrentValue = false;
            dbContext.Entry(duplicate).Property(nameof(WorkerDefaultAssignment.UpdatedAtUtc)).CurrentValue = now;
        }
    }

    if (currentDefault is not null && currentDefault.SubStageId == request.SubStageId)
    {
        AssignmentHelpers.AddAuditLog(
            dbContext,
            actorUserId.Value,
            AuditActionType.Update,
            nameof(WorkerDefaultAssignment),
            currentDefault.Id.ToString(),
            before: currentDefault,
            httpContext);

        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(ApiResponse.Success(new
        {
            assignmentId = currentDefault.Id,
            workerId = currentDefault.WorkerId,
            subStageId = currentDefault.SubStageId,
            assignmentType = AssignmentType.Default.ToString(),
            startsAt = currentDefault.AssignedAt
        }));
    }

    var currentDefaultForTimeline = currentDefault;
    var previousSubStageId = currentDefaultForTimeline?.SubStageId;
    if (currentDefault is not null)
    {
        dbContext.Entry(currentDefault).Property(nameof(WorkerDefaultAssignment.IsActive)).CurrentValue = false;
        dbContext.Entry(currentDefault).Property(nameof(WorkerDefaultAssignment.UpdatedAtUtc)).CurrentValue = now;
    }

    var assignment = new WorkerDefaultAssignment(
        id: Guid.NewGuid(),
        workerId: request.WorkerId,
        subStageId: request.SubStageId,
        assignedByUserId: actorUserId.Value,
        assignedAtUtc: now,
        reason: request.Reason,
        isActive: true,
        createdAtUtc: now);

    dbContext.WorkerDefaultAssignments.Add(assignment);
    dbContext.AssignmentTimelineEntries.Add(new AssignmentTimelineEntry(
        id: Guid.NewGuid(),
        workerId: request.WorkerId,
        fromSubStageId: previousSubStageId,
        toSubStageId: request.SubStageId,
        assignmentType: AssignmentType.Default.ToString(),
        actionType: currentDefault is null ? TimelineActionCreate : TimelineActionUpdate,
        reason: request.Reason,
        startAtUtc: now,
        endAtUtc: null,
        performedByUserId: actorUserId.Value,
        isAutomatic: false,
        relatedTemporaryAssignmentId: null,
        replacementForWorkerId: null,
        createdAtUtc: now));

    AssignmentHelpers.AddAuditLog(dbContext, actorUserId.Value, AuditActionType.Create, nameof(WorkerDefaultAssignment), assignment.Id.ToString(), before: assignment, httpContext: httpContext);

    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/assignments/default/{assignment.Id}", ApiResponse.Success(new
    {
        assignmentId = assignment.Id,
        workerId = assignment.WorkerId,
        subStageId = assignment.SubStageId,
        assignmentType = AssignmentType.Default.ToString(),
        startsAt = assignment.AssignedAt
    }));
})
    .WithTags("Assignments")
    .WithName("CreateOrUpdateDefaultAssignment");

assignmentsApi.MapPost("/temporary", async (
    CreateTemporaryAssignmentRequest request,
    AppDbContext dbContext,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    if (request.WorkerId == Guid.Empty)
    {
        return ApiResponse.Failure("ValidationError", "WorkerId is required.");
    }

    if (request.FromSubStageId == Guid.Empty || request.ToSubStageId == Guid.Empty)
    {
        return ApiResponse.Failure("ValidationError", "FromSubStageId and ToSubStageId are required.");
    }

    if (string.IsNullOrWhiteSpace(request.Reason))
    {
        return ApiResponse.Failure("ValidationError", "Reason is required.");
    }

    if (request.StartAtUtc >= request.EndAtUtc)
    {
        return ApiResponse.Failure("ValidationError", "EndAtUtc must be after StartAtUtc.");
    }

    var now = DateTime.UtcNow;

    var worker = await dbContext.Workers
        .AsNoTracking()
        .FirstOrDefaultAsync(x => x.Id == request.WorkerId, cancellationToken);

    if (worker is null || !worker.IsActive)
    {
        return ApiResponse.Failure("NotFound", "Worker not found or inactive.", 404);
    }

    if (!await dbContext.SubStages.AnyAsync(x => x.Id == request.FromSubStageId && x.IsActive, cancellationToken))
    {
        return ApiResponse.Failure("ValidationError", "FromSubStage is invalid or inactive.", 404);
    }

    if (!await dbContext.SubStages.AnyAsync(x => x.Id == request.ToSubStageId && x.IsActive, cancellationToken))
    {
        return ApiResponse.Failure("ValidationError", "ToSubStage is invalid or inactive.", 404);
    }

    var hasConflict = await dbContext.WorkerTemporaryAssignments.AnyAsync(x =>
        x.WorkerId == request.WorkerId &&
        (x.Status == TempStatusScheduled || x.Status == TempStatusActive) &&
        x.StartAtUtc < request.EndAtUtc &&
        x.EndAtUtc > request.StartAtUtc,
        cancellationToken);

    if (hasConflict)
    {
        return ApiResponse.Failure("Conflict", "Worker has overlapping temporary assignment.", 409);
    }

    var status = request.StartAtUtc <= now
        ? TempStatusActive
        : TempStatusScheduled;

    var entity = new WorkerTemporaryAssignment(
        id: Guid.NewGuid(),
        workerId: request.WorkerId,
        fromSubStageId: request.FromSubStageId,
        toSubStageId: request.ToSubStageId,
        startAtUtc: request.StartAtUtc,
        endAtUtc: request.EndAtUtc,
        assignedByUserId: actorUserId.Value,
        reason: request.Reason,
        replacementForWorkerId: null,
        status: status,
        createdAtUtc: now);

    dbContext.WorkerTemporaryAssignments.Add(entity);
    dbContext.AssignmentTimelineEntries.Add(new AssignmentTimelineEntry(
        id: Guid.NewGuid(),
        workerId: request.WorkerId,
        fromSubStageId: request.FromSubStageId,
        toSubStageId: request.ToSubStageId,
        assignmentType: AssignmentType.Temporary.ToString(),
        actionType: TimelineActionCreate,
        reason: request.Reason,
        startAtUtc: request.StartAtUtc,
        endAtUtc: request.EndAtUtc,
        performedByUserId: actorUserId.Value,
        isAutomatic: false,
        relatedTemporaryAssignmentId: null,
        replacementForWorkerId: null,
        createdAtUtc: now));

    AssignmentHelpers.AddAuditLog(dbContext, actorUserId.Value, AuditActionType.Create, nameof(WorkerTemporaryAssignment), entity.Id.ToString(), before: entity, httpContext: httpContext);

    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/assignments/temporary/{entity.Id}", ApiResponse.Success(new
    {
        assignmentId = entity.Id,
        workerId = entity.WorkerId,
        fromSubStageId = entity.FromSubStageId,
        toSubStageId = entity.ToSubStageId,
        assignmentType = entity.AssignmentType.ToString(),
        status = entity.Status,
        startAtUtc = entity.StartAtUtc,
        endAtUtc = entity.EndAtUtc
    }));
})
    .WithTags("Assignments")
    .WithName("CreateTemporaryAssignment");

assignmentsApi.MapPost("/replacement", async (
    CreateReplacementAssignmentRequest request,
    AppDbContext dbContext,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    if (request.ReplacementWorkerId == Guid.Empty || request.ReplacedWorkerId == Guid.Empty || request.SubStageId == Guid.Empty)
    {
        return ApiResponse.Failure("ValidationError", "ReplacementWorkerId, ReplacedWorkerId and SubStageId are required.");
    }

    if (request.ReplacementWorkerId == request.ReplacedWorkerId)
    {
        return ApiResponse.Failure("ValidationError", "Replacement worker must differ from replaced worker.");
    }

    if (string.IsNullOrWhiteSpace(request.Reason))
    {
        return ApiResponse.Failure("ValidationError", "Reason is required.");
    }

    if (request.StartAtUtc >= request.EndAtUtc)
    {
        return ApiResponse.Failure("ValidationError", "EndAtUtc must be after StartAtUtc.");
    }

    var now = DateTime.UtcNow;

    var replacementWorker = await dbContext.Workers
        .AsNoTracking()
        .FirstOrDefaultAsync(x => x.Id == request.ReplacementWorkerId, cancellationToken);

    if (replacementWorker is null || !replacementWorker.IsActive)
    {
        return ApiResponse.Failure("NotFound", "Replacement worker not found or inactive.", 404);
    }

    var replacedWorker = await dbContext.Workers
        .AsNoTracking()
        .FirstOrDefaultAsync(x => x.Id == request.ReplacedWorkerId, cancellationToken);

    if (replacedWorker is null || !replacedWorker.IsActive)
    {
        return ApiResponse.Failure("NotFound", "Replaced worker not found or inactive.", 404);
    }

    // TODO Attendance Integration: verify ReplacedWorkerId is truly absent at assignment time using attendance records.
    if (!await dbContext.SubStages.AnyAsync(x => x.Id == request.SubStageId && x.IsActive, cancellationToken))
    {
        return ApiResponse.Failure("ValidationError", "SubStage not found or inactive.", 404);
    }

    var replacedWorkerDefault = await dbContext.WorkerDefaultAssignments
        .AsNoTracking()
        .Where(x => x.WorkerId == request.ReplacedWorkerId && x.IsActive)
        .OrderByDescending(x => x.AssignedAt)
        .ThenByDescending(x => x.Id)
        .FirstOrDefaultAsync(cancellationToken);

    if (replacedWorkerDefault is not null && replacedWorkerDefault.SubStageId != request.SubStageId)
    {
        return ApiResponse.Failure("ValidationError", "Replaced worker default assignment is in a different sub-stage.");
    }

    var conflict = await dbContext.WorkerTemporaryAssignments.AnyAsync(x =>
        x.WorkerId == request.ReplacementWorkerId &&
        (x.Status == TempStatusScheduled || x.Status == TempStatusActive) &&
        x.StartAtUtc < request.EndAtUtc &&
        x.EndAtUtc > request.StartAtUtc,
        cancellationToken);

    if (conflict)
    {
        return ApiResponse.Failure("Conflict", "Replacement worker already has overlapping temporary assignment.", 409);
    }

    var status = request.StartAtUtc <= now
        ? TempStatusActive
        : TempStatusScheduled;

    var fromSubStageId = replacedWorkerDefault?.SubStageId ?? request.SubStageId;

    var entity = new WorkerTemporaryAssignment(
        id: Guid.NewGuid(),
        workerId: request.ReplacementWorkerId,
        fromSubStageId: fromSubStageId,
        toSubStageId: request.SubStageId,
        startAtUtc: request.StartAtUtc,
        endAtUtc: request.EndAtUtc,
        assignedByUserId: actorUserId.Value,
        reason: request.Reason,
        replacementForWorkerId: request.ReplacedWorkerId,
        status: status,
        createdAtUtc: now);

    dbContext.WorkerTemporaryAssignments.Add(entity);
    dbContext.AssignmentTimelineEntries.Add(new AssignmentTimelineEntry(
        id: Guid.NewGuid(),
        workerId: request.ReplacementWorkerId,
        fromSubStageId: fromSubStageId,
        toSubStageId: request.SubStageId,
        assignmentType: AssignmentType.Replacement.ToString(),
        actionType: TimelineActionCreate,
        reason: request.Reason,
        startAtUtc: request.StartAtUtc,
        endAtUtc: request.EndAtUtc,
        performedByUserId: actorUserId.Value,
        isAutomatic: false,
        relatedTemporaryAssignmentId: null,
        replacementForWorkerId: request.ReplacedWorkerId,
        createdAtUtc: now));

    AssignmentHelpers.AddAuditLog(dbContext, actorUserId.Value, AuditActionType.Create, nameof(WorkerTemporaryAssignment), entity.Id.ToString(), before: entity, httpContext: httpContext);

    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/assignments/temporary/{entity.Id}", ApiResponse.Success(new
    {
        assignmentId = entity.Id,
        workerId = entity.WorkerId,
        replacementForWorkerId = entity.ReplacementForWorkerId,
        fromSubStageId = entity.FromSubStageId,
        toSubStageId = entity.ToSubStageId,
        assignmentType = entity.AssignmentType.ToString(),
        status = entity.Status,
        startAtUtc = entity.StartAtUtc,
        endAtUtc = entity.EndAtUtc
    }));
})
    .WithTags("Assignments")
    .WithName("CreateReplacementAssignment");

assignmentsApi.MapDelete("/temporary/{assignmentId:guid}", async (
    Guid assignmentId,
    AppDbContext dbContext,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var assignment = await dbContext.WorkerTemporaryAssignments
        .FirstOrDefaultAsync(
            x => x.Id == assignmentId &&
                 (x.Status == TempStatusScheduled || x.Status == TempStatusActive),
            cancellationToken);

    if (assignment is null)
    {
        return ApiResponse.Failure("NotFound", "Temporary assignment not found.", 404);
    }

    var now = DateTime.UtcNow;
    dbContext.Entry(assignment).Property(nameof(WorkerTemporaryAssignment.Status)).CurrentValue = TempStatusCancelled;
    dbContext.Entry(assignment).Property(nameof(WorkerTemporaryAssignment.EndAtUtc)).CurrentValue = now;
    dbContext.Entry(assignment).Property(nameof(WorkerTemporaryAssignment.UpdatedAtUtc)).CurrentValue = now;

    dbContext.AssignmentTimelineEntries.Add(new AssignmentTimelineEntry(
        id: Guid.NewGuid(),
        workerId: assignment.WorkerId,
        fromSubStageId: assignment.FromSubStageId,
        toSubStageId: assignment.ToSubStageId,
        assignmentType: assignment.AssignmentType.ToString(),
        actionType: TimelineActionCancel,
        reason: assignment.Reason,
        startAtUtc: assignment.StartAtUtc,
        endAtUtc: now,
        performedByUserId: actorUserId.Value,
        isAutomatic: false,
        relatedTemporaryAssignmentId: assignment.Id,
        replacementForWorkerId: assignment.ReplacementForWorkerId,
        createdAtUtc: now));

    AssignmentHelpers.AddAuditLog(dbContext, actorUserId.Value, AuditActionType.Cancel, nameof(WorkerTemporaryAssignment), assignment.Id.ToString(), before: assignment, httpContext: httpContext);

    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Ok(ApiResponse.Success(new
    {
        assignmentId = assignment.Id,
        cancelledAt = now,
        status = TempStatusCancelled
    }));
})
    .WithTags("Assignments")
    .WithName("CancelTemporaryAssignment");

assignmentsApi.MapGet("/{workerId:guid}/timeline", async (
    Guid workerId,
    AppDbContext dbContext,
    CancellationToken cancellationToken,
    int page = 1,
    int pageSize = 50,
    DateTime? fromDate = null,
    DateTime? toDate = null) =>
{
    if (page < 1 || pageSize < 1 || pageSize > 200)
    {
        return ApiResponse.Failure("ValidationError", "page and pageSize must be positive, pageSize max 200.");
    }

    var query = dbContext.AssignmentTimelineEntries
        .AsNoTracking()
        .Where(x => x.WorkerId == workerId);

    if (fromDate.HasValue)
    {
        query = query.Where(x => x.StartAtUtc >= fromDate.Value);
    }

    if (toDate.HasValue)
    {
        query = query.Where(x => x.StartAtUtc <= toDate.Value);
    }

    var totalCount = await query.CountAsync(cancellationToken);

    var entries = await query
        .OrderByDescending(x => x.CreatedAtUtc)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(x => new AssignmentTimelineDto
        {
            Id = x.Id,
            WorkerId = x.WorkerId,
            FromSubStageId = x.FromSubStageId,
            ToSubStageId = x.ToSubStageId,
            AssignmentType = x.AssignmentType,
            ActionType = x.ActionType,
            Reason = x.Reason,
            StartAtUtc = x.StartAtUtc,
            EndAtUtc = x.EndAtUtc,
            PerformedByUserId = x.PerformedByUserId,
            IsAutomatic = x.IsAutomatic,
            RelatedTemporaryAssignmentId = x.RelatedTemporaryAssignmentId,
            ReplacementForWorkerId = x.ReplacementForWorkerId,
            CreatedAtUtc = x.CreatedAtUtc
        })
        .ToArrayAsync(cancellationToken);

    return Results.Ok(new
    {
        success = true,
        data = new
        {
            items = entries,
            totalCount,
            pageNumber = page,
            pageSize
        }
    });
})
    .WithTags("Assignments")
    .WithName("GetWorkerAssignmentTimeline");

assignmentsApi.MapGet("/sub-stages/{subStageId:guid}/workers", async (
    Guid subStageId,
    AppDbContext dbContext,
    CancellationToken cancellationToken,
    DateTime? asOfUtc = null) =>
{
    if (!await dbContext.SubStages.AnyAsync(x => x.Id == subStageId, cancellationToken))
    {
        return ApiResponse.Failure("NotFound", "SubStage not found.", 404);
    }

    var asOf = asOfUtc ?? DateTime.UtcNow;
    var workers = await dbContext.Workers.AsNoTracking().Where(x => x.IsActive).Select(x => new { x.Id, x.FullName, x.EmployeeCode }).ToListAsync(cancellationToken);
    if (workers.Count == 0)
    {
        return Results.Ok(ApiResponse.Success(new
        {
            subStageId,
            items = Array.Empty<SubStageCurrentWorkerDto>()
        }));
    }

    var assignments = await AssignmentHelpers.ResolveCurrentAssignmentsAsync(
        dbContext,
        workers.Select(x => x.Id).ToList(),
        asOf,
        cancellationToken);

    var items = workers
        .Where(x => assignments.TryGetValue(x.Id, out var assignment) && assignment.EffectiveSubStageId == subStageId)
        .Select(x =>
        {
            var assignment = assignments[x.Id];
            return new SubStageCurrentWorkerDto
            {
                WorkerId = x.Id,
                FullName = x.FullName,
                EmployeeCode = x.EmployeeCode,
                AssignmentType = assignment.AssignmentType ?? AssignmentType.Default,
                FromSubStageId = assignment.FromSubStageId,
                ReplacementForWorkerId = assignment.ReplacementForWorkerId
            };
        })
        .OrderBy(x => x.FullName)
        .ToArray();

    return Results.Ok(ApiResponse.Success(new
    {
        subStageId,
        workersCount = items.Length,
        items
    }));
})
    .WithTags("Assignments")
    .WithName("GetWorkersInSubStage");

notificationsApi.MapGet("", async (
    AppDbContext dbContext,
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

    if (page < 1 || pageSize < 1 || pageSize > 200)
    {
        return ApiResponse.Failure("ValidationError", "page and pageSize must be positive, pageSize max 200.");
    }

    var query = dbContext.Notifications
        .AsNoTracking()
        .Where(x => x.RecipientUserId == recipientUserId);

    if (isRead.HasValue)
    {
        query = query.Where(x => x.IsRead == isRead.Value);
    }

    var totalCount = await query.CountAsync(cancellationToken);
    var notifications = await query
        .OrderByDescending(x => x.CreatedAtUtc)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(x => new NotificationDto
        {
            Id = x.Id,
            RecipientUserId = x.RecipientUserId,
            SenderUserId = x.SenderUserId,
            Title = x.Title,
            Message = x.Message,
            Status = x.Status,
            IsRead = x.IsRead,
            RelatedWorkerId = x.RelatedWorkerId,
            RelatedEntityType = x.RelatedEntityType,
            RelatedEntityId = x.RelatedEntityId,
            CreatedAtUtc = x.CreatedAtUtc,
            ReadAtUtc = x.ReadAtUtc
        })
        .ToArrayAsync(cancellationToken);

    return Results.Ok(new
    {
        success = true,
        data = new
        {
            items = notifications,
            totalCount,
            pageNumber = page,
            pageSize
        }
    });
})
    .WithTags("Notifications")
    .WithName("GetNotifications");

notificationsApi.MapGet("/unread-count", async (
    AppDbContext dbContext,
    ICurrentUserService currentUserService,
    CancellationToken cancellationToken) =>
{
    var recipientUserId = currentUserService.UserId;
    if (recipientUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var unreadCount = await dbContext.Notifications
        .AsNoTracking()
        .CountAsync(x => x.RecipientUserId == recipientUserId && !x.IsRead, cancellationToken);

    return Results.Ok(ApiResponse.Success(new { unreadCount }));
})
    .WithTags("Notifications")
    .WithName("GetUnreadNotificationCount");

notificationsApi.MapPatch("/{notificationId:guid}/read", async (
    Guid notificationId,
    AppDbContext dbContext,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var recipientUserId = currentUserService.UserId;
    if (recipientUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var notification = await dbContext.Notifications
        .FirstOrDefaultAsync(x => x.Id == notificationId && x.RecipientUserId == recipientUserId, cancellationToken);

    if (notification is null)
    {
        return ApiResponse.Failure("NotFound", "Notification not found.", 404);
    }

    var now = DateTime.UtcNow;
    if (!notification.IsRead)
    {
        notification.MarkAsRead(now);
        await dbContext.SaveChangesAsync(cancellationToken);
        AssignmentHelpers.AddAuditLog(dbContext, recipientUserId.Value, AuditActionType.Update, nameof(Notification), notification.Id.ToString(), before: notification, httpContext: httpContext);
    }

    return Results.Ok(ApiResponse.Success(new
    {
        notification.Id,
        notification.IsRead,
        notification.ReadAtUtc
    }));
})
    .WithTags("Notifications")
    .WithName("ReadNotification");

notificationsApi.MapPatch("/read-all", async (
    AppDbContext dbContext,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken,
    DateTime? beforeDateUtc = null) =>
{
    var recipientUserId = currentUserService.UserId;
    if (recipientUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var query = dbContext.Notifications
        .Where(x => x.RecipientUserId == recipientUserId.Value && !x.IsRead);

    if (beforeDateUtc.HasValue)
    {
        query = query.Where(x => x.CreatedAtUtc <= beforeDateUtc.Value);
    }

    var now = DateTime.UtcNow;
    var updatedCount = await query.ExecuteUpdateAsync(setters => setters
        .SetProperty(x => x.IsRead, true)
        .SetProperty(x => x.Status, NotificationStatus.Read)
        .SetProperty(x => x.ReadAtUtc, now), cancellationToken);

    AssignmentHelpers.AddAuditLog(dbContext, recipientUserId.Value, AuditActionType.Update, nameof(Notification), "read-all", before: new { recipientUserId = recipientUserId.Value }, httpContext: httpContext);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Ok(ApiResponse.Success(new { updatedCount }));
})
    .WithTags("Notifications")
    .WithName("ReadAllNotifications");

attendanceApi.MapPost("/sync/today", async (
    IAttendanceSyncService attendanceSyncService,
    CancellationToken cancellationToken) =>
{
    var result = await attendanceSyncService.SyncTodayAsync(cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(
            result.Error?.Code ?? "AttendanceSyncFailed",
            result.Error?.Message ?? "Unable to sync attendance data.",
            result.Error?.Code is "NotFound" or "ValidationError" ? 400 : 500);
    }

    return Results.Ok(ApiResponse.Success(result.Value));
})
    .RequireAuthorization("Admin")
    .WithTags("Attendance")
    .WithName("SyncAttendanceToday");

attendanceApi.MapGet("/today", async (
    IAttendanceReadService attendanceReadService,
    DateTime? dateUtc = null,
    Guid? factoryId = null,
    Guid? lineId = null,
    CancellationToken cancellationToken = default) =>
{
    var result = await attendanceReadService.GetTodayAttendanceAsync(
        factoryId,
        lineId,
        dateUtc,
        cancellationToken);

    if (result.IsFailure)
    {
        return ApiResponse.Failure(
            result.Error?.Code ?? "AttendanceReadFailed",
            result.Error?.Message ?? "Unable to load today's attendance.",
            result.Error?.Code is "NotFound" ? 404 : 400);
    }

    return Results.Ok(ApiResponse.Success(new
    {
        date = (dateUtc ?? DateTime.UtcNow).Date,
        items = result.Value ?? Array.Empty<AttendanceWorkerStateDto>()
    }));
})
    .WithTags("Attendance")
    .WithName("GetTodayAttendance");

attendanceApi.MapGet("/workers/{workerId:guid}", async (
    Guid workerId,
    IAttendanceReadService attendanceReadService,
    DateTime? fromDateUtc = null,
    DateTime? toDateUtc = null,
    CancellationToken cancellationToken = default) =>
{
    var result = await attendanceReadService.GetWorkerAttendanceAsync(
        workerId,
        fromDateUtc,
        toDateUtc,
        cancellationToken);

    if (result.IsFailure)
    {
        return ApiResponse.Failure(
            result.Error?.Code ?? "AttendanceReadFailed",
            result.Error?.Message ?? "Unable to load worker attendance.",
            result.Error?.Code is "NotFound" ? 404 : 400);
    }

    return Results.Ok(ApiResponse.Success(new
    {
        workerId,
        dateFromUtc = fromDateUtc,
        dateToUtc = toDateUtc,
        items = result.Value ?? Array.Empty<AttendanceRecordDto>()
    }));
})
    .WithTags("Attendance")
    .WithName("GetWorkerAttendance");

attendanceApi.MapGet("/stages/{subStageId:guid}", async (
    Guid subStageId,
    IAttendanceReadService attendanceReadService,
    DateTime? dateUtc = null,
    CancellationToken cancellationToken = default) =>
{
    var result = await attendanceReadService.GetSubStageAttendanceAsync(subStageId, dateUtc, cancellationToken);
    if (result.IsFailure)
    {
        return ApiResponse.Failure(
            result.Error?.Code ?? "AttendanceReadFailed",
            result.Error?.Message ?? "Unable to load stage attendance.",
            result.Error?.Code is "NotFound" ? 404 : 400);
    }

    return Results.Ok(ApiResponse.Success(result.Value));
})
    .WithTags("Attendance")
    .WithName("GetSubStageAttendance");

readinessApi.MapGet("/factory", async (
    AppDbContext dbContext,
    CancellationToken cancellationToken,
    DateTime? asOfUtc = null) =>
{
    var asOf = asOfUtc ?? DateTime.UtcNow;
    var now = asOf;

    var activeSubStages = await (from ss in dbContext.SubStages.AsNoTracking()
                                join ms in dbContext.MainStages.AsNoTracking() on ss.MainStageId equals ms.Id
                                join pl in dbContext.ProductionLines.AsNoTracking() on ms.ProductionLineId equals pl.Id
                                where ss.IsActive && ms.IsActive && pl.IsActive
                                select new
                                {
                                    ss.Id,
                                    ss.Capacity
                                })
        .ToListAsync(cancellationToken);

    var requiredWorkers = activeSubStages.Sum(x => x.Capacity);

    var activeWorkerIds = await dbContext.Workers
        .AsNoTracking()
        .Where(x => x.IsActive)
        .Select(x => x.Id)
        .ToListAsync(cancellationToken);

    var assignments = await AssignmentHelpers.ResolveCurrentAssignmentsAsync(dbContext, activeWorkerIds, now, cancellationToken);
    var assignmentsInActiveSubStages = assignments
        .Where(x => x.Value.EffectiveSubStageId.HasValue && activeSubStages.Any(ss => ss.Id == x.Value.EffectiveSubStageId))
        .ToList();

    var attendanceByWorker = await AssignmentHelpers.GetLatestAttendanceStatusByWorkerAsync(
        dbContext,
        assignmentsInActiveSubStages.Select(x => x.Key).ToArray(),
        cancellationToken,
        asOf);

    var assignedWorkers = assignmentsInActiveSubStages.Count;
    var present = 0;
    var late = 0;
    var absent = 0;
    var unassignedFromAttendance = 0;

    foreach (var entry in assignmentsInActiveSubStages)
    {
        if (!attendanceByWorker.TryGetValue(entry.Key, out var status))
        {
            unassignedFromAttendance++;
            continue;
        }

        if (status == AttendanceStatus.Present)
        {
            present++;
        }
        else if (status == AttendanceStatus.Late)
        {
            late++;
        }
        else if (status == AttendanceStatus.Absent)
        {
            absent++;
        }
        else
        {
            unassignedFromAttendance++;
        }
    }

    var unassignedWorkers = Math.Max(0, requiredWorkers - assignedWorkers) + unassignedFromAttendance;
    var readyCount = attendanceByWorker.Count == 0 ? assignedWorkers : present;
    var readinessPercent = StageReadinessSnapshot.CalculateReadinessPercent(requiredWorkers, readyCount, late, absent, unassignedWorkers);

    return Results.Ok(ApiResponse.Success(new StageReadinessDto
    {
        ScopeType = "Factory",
        ScopeEntityId = Guid.Empty,
        RequiredWorkers = requiredWorkers,
        AssignedWorkers = assignedWorkers,
        PresentWorkers = present,
        LateWorkers = late,
        AbsentWorkers = absent,
        UnassignedWorkers = unassignedWorkers,
        ReadinessPercent = readinessPercent,
        Status = StageReadinessSnapshot.ReadinessFromPercent(readinessPercent),
        CalculatedAtUtc = now
    }));
})
    .WithTags("Readiness")
    .WithName("GetFactoryReadiness");

readinessApi.MapGet("/production-lines", async (
    AppDbContext dbContext,
    CancellationToken cancellationToken,
    DateTime? asOfUtc = null) =>
{
    var asOf = asOfUtc ?? DateTime.UtcNow;
    var now = asOf;

    var lineItems = await (from line in dbContext.ProductionLines.AsNoTracking()
                          join mainStage in dbContext.MainStages.AsNoTracking() on line.Id equals mainStage.ProductionLineId
                          join subStage in dbContext.SubStages.AsNoTracking() on mainStage.Id equals subStage.MainStageId
                          where line.IsActive && mainStage.IsActive && subStage.IsActive
                          group subStage by new { line.Id, line.Name } into g
                          select new
                          {
                              ProductionLineId = g.Key.Id,
                              LineName = g.Key.Name,
                              RequiredWorkers = g.Sum(x => x.Capacity),
                              SubStageIds = g.Select(x => x.Id).ToArray()
                          })
        .ToListAsync(cancellationToken);

    var activeWorkerIds = await dbContext.Workers.AsNoTracking().Where(x => x.IsActive).Select(x => x.Id).ToListAsync(cancellationToken);
    var assignments = await AssignmentHelpers.ResolveCurrentAssignmentsAsync(dbContext, activeWorkerIds, now, cancellationToken);
    var attendanceByWorker = await AssignmentHelpers.GetLatestAttendanceStatusByWorkerAsync(
        dbContext,
        assignments.Select(x => x.Key).ToArray(),
        cancellationToken,
        asOf);

    var readinessItems = lineItems
        .Select(item =>
        {
            var assignmentsInLine = assignments
                .Where(x => x.Value.EffectiveSubStageId is not null && item.SubStageIds.Contains(x.Value.EffectiveSubStageId.Value))
                .ToList();

            var assigned = assignmentsInLine.Count;
            var present = 0;
            var late = 0;
            var absent = 0;
            var unassignedFromAttendance = 0;

            foreach (var assignment in assignmentsInLine)
            {
                if (!attendanceByWorker.TryGetValue(assignment.Key, out var status))
                {
                    unassignedFromAttendance++;
                    continue;
                }

                if (status == AttendanceStatus.Present)
                {
                    present++;
                }
                else if (status == AttendanceStatus.Late)
                {
                    late++;
                }
                else if (status == AttendanceStatus.Absent)
                {
                    absent++;
                }
                else
                {
                    unassignedFromAttendance++;
                }
            }

            var unassignedWorkers = Math.Max(0, item.RequiredWorkers - assigned) + unassignedFromAttendance;
            var readyCount = attendanceByWorker.Count == 0 ? assigned : present;
            var readinessPercent = StageReadinessSnapshot.CalculateReadinessPercent(item.RequiredWorkers, readyCount, late, absent, unassignedWorkers);

            return new
            {
                scopeType = "ProductionLine",
                scopeEntityId = item.ProductionLineId,
                lineName = item.LineName,
                requiredWorkers = item.RequiredWorkers,
                assignedWorkers = assigned,
                presentWorkers = present,
                lateWorkers = late,
                absentWorkers = absent,
                unassignedWorkers,
                readinessPercent,
                status = StageReadinessSnapshot.ReadinessFromPercent(readinessPercent)
            };
        })
        .ToList();

    var requiredWorkers = lineItems.Sum(x => x.RequiredWorkers);
    var assignedWorkers = readinessItems.Sum(x => (int)x.assignedWorkers);
    var presentWorkers = readinessItems.Sum(x => (int)x.presentWorkers);
    var lateWorkers = readinessItems.Sum(x => (int)x.lateWorkers);
    var absentWorkers = readinessItems.Sum(x => (int)x.absentWorkers);
    var unassignedWorkers = readinessItems.Sum(x => (int)x.unassignedWorkers);
    var readinessPercent = StageReadinessSnapshot.CalculateReadinessPercent(requiredWorkers, presentWorkers, lateWorkers, absentWorkers, unassignedWorkers);

    return Results.Ok(ApiResponse.Success(new
    {
        scopeType = "ProductionLines",
        scopeEntityId = Guid.Empty,
        requiredWorkers,
        assignedWorkers,
        presentWorkers,
        lateWorkers,
        absentWorkers,
        unassignedWorkers,
        readinessPercent,
        status = StageReadinessSnapshot.ReadinessFromPercent(readinessPercent),
        calculatedAtUtc = now,
        items = readinessItems
    }));
})
    .WithTags("Readiness")
    .WithName("GetProductionLinesReadiness");

readinessApi.MapGet("/sub-stages/{subStageId:guid}", async (
    Guid subStageId,
    AppDbContext dbContext,
    CancellationToken cancellationToken,
    DateTime? asOfUtc = null) =>
{
    var subStage = await dbContext.SubStages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == subStageId, cancellationToken);
    if (subStage is null)
    {
        return ApiResponse.Failure("NotFound", "SubStage not found.", 404);
    }

    var asOf = asOfUtc ?? DateTime.UtcNow;
    var now = asOf;

    var activeWorkerIds = await dbContext.Workers.AsNoTracking().Where(x => x.IsActive).Select(x => x.Id).ToListAsync(cancellationToken);
    var assignments = await AssignmentHelpers.ResolveCurrentAssignmentsAsync(dbContext, activeWorkerIds, now, cancellationToken);
    var matchingAssignments = assignments.Where(x => x.Value.EffectiveSubStageId == subStageId).ToList();
    var attendanceByWorker = await AssignmentHelpers.GetLatestAttendanceStatusByWorkerAsync(
        dbContext,
        matchingAssignments.Select(x => x.Key).ToArray(),
        cancellationToken,
        asOf);

    var present = 0;
    var late = 0;
    var absent = 0;
    var unassignedFromAttendance = 0;

    foreach (var assignment in matchingAssignments)
    {
        if (!attendanceByWorker.TryGetValue(assignment.Key, out var status))
        {
            unassignedFromAttendance++;
            continue;
        }

        if (status == AttendanceStatus.Present)
        {
            present++;
        }
        else if (status == AttendanceStatus.Late)
        {
            late++;
        }
        else if (status == AttendanceStatus.Absent)
        {
            absent++;
        }
        else
        {
            unassignedFromAttendance++;
        }
    }

    var assignedWorkers = matchingAssignments.Count;
    var requiredWorkers = subStage.Capacity;
    var unassignedWorkers = Math.Max(0, requiredWorkers - assignedWorkers) + unassignedFromAttendance;
    var readinessPercent = StageReadinessSnapshot.CalculateReadinessPercent(requiredWorkers, present, late, absent, unassignedWorkers);

    return Results.Ok(ApiResponse.Success(new StageReadinessDto
    {
        ScopeType = "SubStage",
        ScopeEntityId = subStageId,
        RequiredWorkers = requiredWorkers,
        AssignedWorkers = assignedWorkers,
        PresentWorkers = present,
        LateWorkers = late,
        AbsentWorkers = absent,
        UnassignedWorkers = unassignedWorkers,
        ReadinessPercent = readinessPercent,
        Status = StageReadinessSnapshot.ReadinessFromPercent(readinessPercent),
        CalculatedAtUtc = now
    }));
})
    .WithTags("Readiness")
    .WithName("GetSubStageReadiness");

app.Run();
