using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Requests;
using ProductionLinePlanner.Api.Security;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Authorization;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Tests;

public sealed class UserManagementCreateEditTests
{
    [Theory]
    [InlineData("admin")]
    [InlineData("ashraf")]
    [InlineData("factory.manager")]
    [InlineData("user_102")]
    public async Task Create_accepts_non_email_login_identifiers_with_the_minimum_required_fields(string identifier)
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Service.CreateAsync(fixture.ActorId, new AdminUserCreateRequest
        {
            FullName = "Factory Manager",
            Email = identifier,
            Password = "secret-value",
            RoleIds = [fixture.Role.Id]
        }, "test");

        Assert.True(result.Succeeded);
        Assert.Equal(201, result.StatusCode);
        Assert.Equal(identifier, result.User!.Email);
        Assert.Single(result.User.RoleIds);
        var stored = await fixture.Db.AppUsers.Include(user => user.Roles).SingleAsync(user => user.Id == result.User.Id);
        Assert.Equal("hashed:secret-value", stored.PasswordHash);
        Assert.Single(stored.Roles);
        Assert.Single(fixture.Audit.Calls);
        Assert.Equal(AuditActionType.Create, fixture.Audit.Calls[0]);
    }

    [Fact]
    public async Task Create_trims_and_normalizes_the_login_identifier_consistently()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Service.CreateAsync(fixture.ActorId, new AdminUserCreateRequest
        {
            FullName = "  Ashraf Farag  ",
            Email = "  Factory.Manager  ",
            Password = "secret",
            RoleIds = [fixture.Role.Id]
        }, null);

        Assert.Equal("Ashraf Farag", result.User!.FullName);
        Assert.Equal("factory.manager", result.User.Email);
    }

    [Fact]
    public async Task Create_rejects_duplicate_login_identifier_after_normalization()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Db.AppUsers.Add(new AppUser(Guid.NewGuid(), "Existing", "factory.manager", "hash"));
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Service.CreateAsync(fixture.ActorId, new AdminUserCreateRequest
        {
            FullName = "Duplicate",
            Email = "  FACTORY.MANAGER ",
            Password = "secret",
            RoleIds = [fixture.Role.Id]
        }, null);

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("DuplicateLoginIdentifier", result.Code);
    }

    [Theory]
    [InlineData("", "login", "password")]
    [InlineData("Name", "   ", "password")]
    [InlineData("Name", "login", "")]
    public async Task Create_rejects_missing_required_values(string fullName, string identifier, string password)
    {
        await using var fixture = await Fixture.CreateAsync();
        var result = await fixture.Service.CreateAsync(fixture.ActorId, new AdminUserCreateRequest
        {
            FullName = fullName,
            Email = identifier,
            Password = password,
            RoleIds = [fixture.Role.Id]
        }, null);

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal("ValidationError", result.Code);
    }

    [Fact]
    public async Task Create_requires_at_least_one_role()
    {
        await using var fixture = await Fixture.CreateAsync();
        var result = await fixture.Service.CreateAsync(fixture.ActorId, new AdminUserCreateRequest
        {
            FullName = "No Role",
            Email = "no.role",
            Password = "password",
            RoleIds = []
        }, null);

        Assert.False(result.Succeeded);
        Assert.Equal("ValidationError", result.Code);
    }

    [Fact]
    public async Task Update_changes_manageable_fields_without_accepting_or_requiring_a_password()
    {
        await using var fixture = await Fixture.CreateAsync();
        var user = new AppUser(Guid.NewGuid(), "Before", "before", "original-hash");
        user.AssignRole(fixture.Role);
        fixture.Db.AppUsers.Add(user);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Service.UpdateAsync(fixture.ActorId, user.Id, new AdminUserUpdateRequest
        {
            FullName = "After",
            Email = " AFTER.USER ",
            IsActive = false,
            RoleIds = [fixture.Role.Id]
        }, "test");

        Assert.True(result.Succeeded);
        Assert.Equal("After", result.User!.FullName);
        Assert.Equal("after.user", result.User.Email);
        Assert.False(result.User.IsActive);
        Assert.Equal("original-hash", (await fixture.Db.AppUsers.SingleAsync(candidate => candidate.Id == user.Id)).PasswordHash);
        Assert.DoesNotContain(typeof(AdminUserUpdateRequest).GetProperties(), property => property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Update_rejects_a_duplicate_login_identifier()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = new AppUser(Guid.NewGuid(), "First", "first", "hash");
        first.AssignRole(fixture.Role);
        var second = new AppUser(Guid.NewGuid(), "Second", "second", "hash");
        second.AssignRole(fixture.Role);
        fixture.Db.AppUsers.AddRange(first, second);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Service.UpdateAsync(fixture.ActorId, second.Id, new AdminUserUpdateRequest
        {
            FullName = second.FullName,
            Email = " FIRST ",
            IsActive = true,
            RoleIds = [fixture.Role.Id]
        }, null);

        Assert.False(result.Succeeded);
        Assert.Equal("DuplicateLoginIdentifier", result.Code);
    }

    [Fact]
    public async Task Login_verifier_authenticates_a_trimmed_text_username()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using var db = new AppDbContext(options);
        var hasher = new PasswordHasher<AppUser>();
        var user = new AppUser(Guid.NewGuid(), "Operator", "factory.manager", "temporary");
        user.ChangePasswordHash(hasher.HashPassword(user, "correct-horse"));
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();

        var authenticated = await AuthLoginVerifier.VerifyAsync(db, hasher, "  FACTORY.MANAGER ", "correct-horse");

        Assert.NotNull(authenticated);
        Assert.Equal(user.Id, authenticated.Id);
    }

    [Fact]
    public void Public_user_contracts_do_not_expose_password_hash_or_refresh_tokens()
    {
        var json = JsonSerializer.Serialize(new ProductionLinePlanner.Application.DTOs.AdminUserDetailsDto());
        Assert.DoesNotContain("Password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RefreshToken", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_and_update_endpoints_require_users_manage_authorization()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddScoped<AppDbContext>(_ => null!);
        builder.Services.AddScoped<IPermissionService>(_ => null!);
        builder.Services.AddScoped<IIamDelegationPolicy>(_ => null!);
        builder.Services.AddScoped<ICurrentUserService>(_ => null!);
        builder.Services.AddScoped<IAuditEngine>(_ => null!);
        builder.Services.AddScoped<IIamAuthorizationService>(_ => null!);
        builder.Services.AddScoped<IUserManagementService>(_ => null!);
        var app = builder.Build();
        app.MapIamAdminEndpoints();

        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
        AssertPolicy(endpoints, "/api/admin/users", "POST", "Permission:users.manage");
        AssertPolicy(endpoints, "/api/admin/users/role-options", "GET", "Permission:users.manage");
        AssertPolicy(endpoints, "/api/admin/users/{userId:guid}", "PUT", "Permission:users.manage");
        AssertPolicy(endpoints, "/api/admin/users/{userId:guid}", "GET", "Permission:users.view");
        await app.DisposeAsync();
    }

    private static void AssertPolicy(RouteEndpoint[] endpoints, string route, string method, string policy)
    {
        var endpoint = endpoints.Single(candidate =>
            candidate.RoutePattern.RawText == route &&
            candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method) == true);
        Assert.Contains(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(), authorization => authorization.Policy == policy);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(AppDbContext db, AppRole role, RecordingAudit audit)
        {
            Db = db;
            Role = role;
            Audit = audit;
            Service = new UserManagementService(db, new FakePasswordHasher(), new PermissiveDelegationPolicy(), audit);
        }

        public Guid ActorId { get; } = Guid.NewGuid();
        public AppDbContext Db { get; }
        public AppRole Role { get; }
        public RecordingAudit Audit { get; }
        public UserManagementService Service { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
            var db = new AppDbContext(options);
            var role = new AppRole(Guid.NewGuid(), UserRole.Admin, "Admin", isSystemRole: true);
            db.AppRoles.Add(role);
            await db.SaveChangesAsync();
            return new Fixture(db, role, new RecordingAudit());
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class FakePasswordHasher : IUserPasswordHasher
    {
        public string HashPassword(AppUser user, string password) => $"hashed:{password}";
    }

    private sealed class PermissiveDelegationPolicy : IIamDelegationPolicy
    {
        public Task<DelegationDecision> CanAssignRoleAsync(Guid actorUserId, Guid targetUserId, AppRole role, CancellationToken cancellationToken = default) => Task.FromResult(DelegationDecision.Permit());
        public Task<DelegationDecision> CanChangeDirectPermissionAsync(Guid actorUserId, Guid targetUserId, string permissionName, PermissionEffect effect, bool isRemoval, CancellationToken cancellationToken = default) => Task.FromResult(DelegationDecision.Permit());
        public Task<DelegationDecision> CanManageRolePermissionsAsync(Guid actorUserId, IEnumerable<string> permissionNames, CancellationToken cancellationToken = default) => Task.FromResult(DelegationDecision.Permit());
    }

    private sealed class RecordingAudit : IAuditEngine
    {
        public List<AuditActionType> Calls { get; } = [];

        public Task<Result> RecordAsync(Guid actorUserId, AuditActionType actionType, string entityType, string entityId, object? before = null, object? after = null, string? requestMeta = null, CancellationToken cancellationToken = default)
        {
            Calls.Add(actionType);
            return Task.FromResult(Result.Success());
        }
    }
}
