using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using ProductionLinePlanner.Domain.Authorization;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Authorization;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Tests;

[Collection("PermissionSeedService")]
public sealed class PermissionSeedServiceTests
{
    [Fact]
    public async Task Reconciliation_makes_system_role_permissions_match_the_catalog_without_touching_custom_data()
    {
        await using var fixture = await PermissionSeedFixture.CreateAsync();

        await fixture.SeedAsync();

        var adminPermissionNames = await fixture.GetRolePermissionNamesAsync(fixture.AdminRole.Id);
        var expectedAdminPermissions = PermissionSeedService.GetPermissionsForRole(UserRole.Admin)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(expectedAdminPermissions, adminPermissionNames.OrderBy(name => name));
        Assert.Contains("workers.manage", adminPermissionNames);
        Assert.Contains("production.approve", adminPermissionNames);
        Assert.DoesNotContain("compensation.manage", adminPermissionNames);
        Assert.True((await fixture.GetRoleAsync(fixture.AdminRole.Id)).IsSystemRole);

        Assert.Equal(["compensation.manage"], await fixture.GetRolePermissionNamesAsync(fixture.CustomRole.Id));
        Assert.Single(await fixture.GetUserOverridesAsync());
        Assert.Equal(2, await fixture.GetAssignedRoleCountAsync());
    }

    [Fact]
    public async Task Reconciliation_keeps_inactive_authoritative_permissions_inactive_and_is_idempotent()
    {
        await using var fixture = await PermissionSeedFixture.CreateAsync();

        await fixture.SeedAsync();
        var before = await fixture.GetRolePermissionNamesAsync(fixture.AdminRole.Id);

        await fixture.SeedAsync(force: true);
        var after = await fixture.GetRolePermissionNamesAsync(fixture.AdminRole.Id);

        Assert.Equal(before.OrderBy(name => name), after.OrderBy(name => name));
        Assert.False(await fixture.IsPermissionActiveAsync("workers.view"));
        Assert.Contains("workers.view", after);

        var superAdminPermissionNames = await fixture.GetRolePermissionNamesAsync(fixture.SuperAdminRoleId);
        Assert.Equal(
            PermissionCatalog.All.Select(permission => permission.Name).OrderBy(name => name),
            superAdminPermissionNames.OrderBy(name => name));
        Assert.Contains("production.approve", superAdminPermissionNames);
    }

    [CollectionDefinition("PermissionSeedService", DisableParallelization = true)]
    public sealed class PermissionSeedServiceCollection
    {
    }

    private sealed class PermissionSeedFixture : IAsyncDisposable
    {
        private static readonly FieldInfo SeededOnceField = typeof(PermissionSeedService)
            .GetField("_seededOnce", BindingFlags.Static | BindingFlags.NonPublic)!;

        private readonly AppDbContext _dbContext;
        private readonly PermissionSeedService _service;
        private readonly AppUser _user;

        private PermissionSeedFixture(
            AppDbContext dbContext,
            PermissionSeedService service,
            AppRole adminRole,
            AppRole customRole,
            AppUser user)
        {
            _dbContext = dbContext;
            _service = service;
            AdminRole = adminRole;
            CustomRole = customRole;
            _user = user;
        }

        public AppRole AdminRole { get; }
        public AppRole CustomRole { get; }
        public Guid SuperAdminRoleId { get; private set; }

        public static async Task<PermissionSeedFixture> CreateAsync()
        {
            SeededOnceField.SetValue(null, false);
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            var dbContext = new AppDbContext(options);

            var adminRole = new AppRole(Guid.NewGuid(), UserRole.Admin, "Admin", isSystemRole: false);
            var customRole = new AppRole(Guid.NewGuid(), "Legacy custom role");
            var user = new AppUser(Guid.NewGuid(), "Seed test", "seed@example.com", "hash");
            user.AssignRole(adminRole);
            user.AssignRole(customRole);

            var inactiveOfficialPermission = new Permission(
                Guid.NewGuid(),
                "workers.view",
                "workers",
                isActive: false);
            var legacyExtraPermission = new Permission(
                Guid.NewGuid(),
                "compensation.manage",
                "compensation");

            dbContext.AddRange(adminRole, customRole, user, inactiveOfficialPermission, legacyExtraPermission);
            dbContext.RolePermissions.AddRange(
                new RolePermission(adminRole.Id, legacyExtraPermission.Id),
                new RolePermission(customRole.Id, legacyExtraPermission.Id));
            dbContext.UserPermissionOverrides.Add(
                new UserPermissionOverride(user.Id, legacyExtraPermission.Id, PermissionEffect.Grant));
            await dbContext.SaveChangesAsync();

            return new PermissionSeedFixture(
                dbContext,
                new PermissionSeedService(dbContext, NullLogger<PermissionSeedService>.Instance),
                adminRole,
                customRole,
                user);
        }

        public async Task SeedAsync(bool force = false)
        {
            if (force)
            {
                SeededOnceField.SetValue(null, false);
            }

            await _service.EnsureSeedAsync();
            SuperAdminRoleId = await _dbContext.AppRoles
                .Where(role => role.Role == UserRole.SuperAdmin)
                .Select(role => role.Id)
                .SingleAsync();
        }

        public Task<List<string>> GetRolePermissionNamesAsync(Guid roleId) =>
            (from assignment in _dbContext.RolePermissions
             join permission in _dbContext.Permissions on assignment.PermissionId equals permission.Id
             where assignment.AppRoleId == roleId
             select permission.Name).ToListAsync();

        public Task<AppRole> GetRoleAsync(Guid roleId) => _dbContext.AppRoles.SingleAsync(role => role.Id == roleId);

        public Task<List<UserPermissionOverride>> GetUserOverridesAsync() =>
            _dbContext.UserPermissionOverrides.Where(overrideEntry => overrideEntry.AppUserId == _user.Id).ToListAsync();

        public Task<int> GetAssignedRoleCountAsync() =>
            _dbContext.AppUsers.Where(user => user.Id == _user.Id).SelectMany(user => user.Roles).CountAsync();

        public Task<bool> IsPermissionActiveAsync(string permissionName) =>
            _dbContext.Permissions.Where(permission => permission.Name == permissionName).Select(permission => permission.IsActive).SingleAsync();

        public async ValueTask DisposeAsync()
        {
            SeededOnceField.SetValue(null, false);
            await _dbContext.DisposeAsync();
        }
    }
}
