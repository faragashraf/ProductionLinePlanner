using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Authorization;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Tests;

public sealed class PermissionServiceTests
{
    [Fact]
    public async Task Role_grant_is_effective_once()
    {
        await using var fixture = await PermissionFixture.CreateAsync();
        fixture.GrantRolePermission("workers.view");
        await fixture.SaveAsync();

        var permissions = await fixture.Service.GetEffectivePermissionsAsync(fixture.User.Id);

        Assert.Equal(["workers.view"], permissions);
    }

    [Fact]
    public async Task Direct_grant_adds_a_permission()
    {
        await using var fixture = await PermissionFixture.CreateAsync();
        fixture.GrantUserPermission("assignments.manage");
        await fixture.SaveAsync();

        var permissions = await fixture.Service.GetEffectivePermissionsAsync(fixture.User.Id);

        Assert.Contains("assignments.manage", permissions);
    }

    [Fact]
    public async Task Direct_deny_overrides_a_role_grant()
    {
        await using var fixture = await PermissionFixture.CreateAsync();
        fixture.GrantRolePermission("workers.view");
        fixture.DenyUserPermission("workers.view");
        await fixture.SaveAsync();

        var permissions = await fixture.Service.GetEffectivePermissionsAsync(fixture.User.Id);

        Assert.DoesNotContain("workers.view", permissions);
    }

    [Fact]
    public async Task Updated_direct_override_changes_a_grant_to_a_deny()
    {
        await using var fixture = await PermissionFixture.CreateAsync();
        var overrideEntry = fixture.GrantUserPermission("workers.view");
        overrideEntry.UpdateEffect(PermissionEffect.Deny);
        await fixture.SaveAsync();

        var permissions = await fixture.Service.GetEffectivePermissionsAsync(fixture.User.Id);

        Assert.DoesNotContain("workers.view", permissions);
    }

    [Fact]
    public async Task Inactive_permissions_and_unknown_catalog_entries_are_excluded()
    {
        await using var fixture = await PermissionFixture.CreateAsync();
        fixture.GrantRolePermission("workers.view", isActive: false);
        fixture.GrantRolePermission("unknown.capability");
        await fixture.SaveAsync();

        var permissions = await fixture.Service.GetEffectivePermissionsAsync(fixture.User.Id);

        Assert.Empty(permissions);
    }

    [Fact]
    public async Task Duplicate_grant_sources_produce_a_unique_result()
    {
        await using var fixture = await PermissionFixture.CreateAsync();
        fixture.GrantRolePermission("workers.view");
        fixture.GrantUserPermission("workers.view");
        await fixture.SaveAsync();

        var permissions = await fixture.Service.GetEffectivePermissionsAsync(fixture.User.Id);

        Assert.Equal(1, permissions.Count(permission => permission == "workers.view"));
    }

    [Fact]
    public async Task Inactive_user_has_no_effective_permissions()
    {
        await using var fixture = await PermissionFixture.CreateAsync(isActive: false);
        fixture.GrantRolePermission("workers.view");
        await fixture.SaveAsync();

        var permissions = await fixture.Service.GetEffectivePermissionsAsync(fixture.User.Id);

        Assert.Empty(permissions);
    }

    private sealed class PermissionFixture : IAsyncDisposable
    {
        private readonly AppDbContext _db;
        private readonly Dictionary<string, Permission> _permissions = new(StringComparer.OrdinalIgnoreCase);

        private PermissionFixture(AppDbContext db, AppUser user, AppRole role)
        {
            _db = db;
            User = user;
            Role = role;
            Service = new PermissionService(db);
        }

        public AppUser User { get; }
        public AppRole Role { get; }
        public PermissionService Service { get; }

        public static async Task<PermissionFixture> CreateAsync(bool isActive = true)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;
            var db = new AppDbContext(options);
            var user = new AppUser(Guid.NewGuid(), "Test User", "test@example.com", "hash", isActive: isActive);
            var role = new AppRole(Guid.NewGuid(), UserRole.Planner, "Planner");
            user.AssignRole(role);
            db.AddRange(user, role);
            await db.SaveChangesAsync();
            return new PermissionFixture(db, user, role);
        }

        public RolePermission GrantRolePermission(string name, bool isActive = true)
        {
            var permission = GetPermission(name, isActive);
            var assignment = new RolePermission(Role.Id, permission.Id);
            _db.RolePermissions.Add(assignment);
            return assignment;
        }

        public UserPermissionOverride GrantUserPermission(string name) => AddOverride(name, PermissionEffect.Grant);

        public UserPermissionOverride DenyUserPermission(string name) => AddOverride(name, PermissionEffect.Deny);

        public Task SaveAsync() => _db.SaveChangesAsync();

        public ValueTask DisposeAsync() => _db.DisposeAsync();

        private UserPermissionOverride AddOverride(string name, PermissionEffect effect)
        {
            var permission = GetPermission(name, true);
            var entry = new UserPermissionOverride(User.Id, permission.Id, effect);
            _db.UserPermissionOverrides.Add(entry);
            return entry;
        }

        private Permission GetPermission(string name, bool isActive)
        {
            if (_permissions.TryGetValue(name, out var existing))
            {
                return existing;
            }

            var capability = name.Split('.', 2)[0];
            var permission = new Permission(Guid.NewGuid(), name, capability, isActive: isActive);
            _permissions.Add(name, permission);
            _db.Permissions.Add(permission);
            return permission;
        }
    }
}
