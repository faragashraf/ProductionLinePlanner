using ProductionLinePlanner.Domain.Authorization;

namespace ProductionLinePlanner.Tests;

public sealed class SuperAdminProtectionTests
{
    [Fact]
    public void Last_active_super_admin_cannot_be_disabled()
    {
        Assert.True(SuperAdminProtection.WouldRemoveLastActiveSuperAdmin(true, 0));
    }

    [Fact]
    public void Super_admin_role_can_be_removed_when_another_active_super_admin_exists()
    {
        Assert.False(SuperAdminProtection.WouldRemoveLastActiveSuperAdmin(true, 1));
    }
}
