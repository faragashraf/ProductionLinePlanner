using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Authorization;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Tests;

public sealed class IamDelegationPolicyTests
{
    [Fact]
    public async Task Super_admin_can_delegate_even_when_its_effective_permission_snapshot_is_incomplete()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new AppDbContext(options);

        var superAdminRole = new AppRole(Guid.NewGuid(), UserRole.SuperAdmin, UserRole.SuperAdmin.ToString(), isSystemRole: true);
        var actor = new AppUser(Guid.NewGuid(), "Super Admin", "super-admin@example.test", "hash");
        var target = new AppUser(Guid.NewGuid(), "Target", "target@example.test", "hash");
        actor.AssignRole(superAdminRole);
        db.AddRange(superAdminRole, actor, target);
        await db.SaveChangesAsync();

        var policy = new IamDelegationPolicy(db, new PermissionService(db));

        var decision = await policy.CanChangeDirectPermissionAsync(
            actor.Id,
            target.Id,
            "production.daily-drafts.approve",
            PermissionEffect.Grant,
            isRemoval: false);

        Assert.True(decision.Allowed);
    }

    [Fact]
    public async Task Super_admin_cannot_change_its_own_authorization()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new AppDbContext(options);

        var superAdminRole = new AppRole(Guid.NewGuid(), UserRole.SuperAdmin, UserRole.SuperAdmin.ToString(), isSystemRole: true);
        var actor = new AppUser(Guid.NewGuid(), "Super Admin", "super-admin@example.test", "hash");
        actor.AssignRole(superAdminRole);
        db.AddRange(superAdminRole, actor);
        await db.SaveChangesAsync();

        var policy = new IamDelegationPolicy(db, new PermissionService(db));

        var decision = await policy.CanChangeDirectPermissionAsync(
            actor.Id,
            actor.Id,
            "production.daily-drafts.approve",
            PermissionEffect.Grant,
            isRemoval: false);

        Assert.False(decision.Allowed);
        Assert.Equal("SelfPromotionForbidden", decision.Code);
    }
}
