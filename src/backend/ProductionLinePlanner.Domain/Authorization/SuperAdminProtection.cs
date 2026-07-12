namespace ProductionLinePlanner.Domain.Authorization;

public static class SuperAdminProtection
{
    public static bool WouldRemoveLastActiveSuperAdmin(
        bool targetIsActiveSuperAdmin,
        int otherActiveSuperAdminCount) =>
        targetIsActiveSuperAdmin && otherActiveSuperAdminCount == 0;
}
