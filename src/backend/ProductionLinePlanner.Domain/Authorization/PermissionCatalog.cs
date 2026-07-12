namespace ProductionLinePlanner.Domain.Authorization;

public static class PermissionCatalog
{
    /// Product-controlled canonical catalog in code.
    /// Not persisted as a separate entity/table; only Permissions are stored in DB.
    public sealed record PermissionCatalogEntry(
        string Name,
        string Capability,
        string? DescriptionAr,
        string? DescriptionEn,
        bool IsCritical = false);

    private static readonly PermissionCatalogEntry[] Entries =
    [
        new("workers.view", "workers", "عرض بيانات العمال", "View worker records", false),
        new("workers.manage", "workers", "إدارة بيانات العمال", "Manage worker records", true),
        new("workers.export", "workers", "تصدير بيانات العمال", "Export workers", false),

        new("departments.view", "departments", "عرض بيانات الأقسام", "View departments", false),
        new("departments.manage", "departments", "إدارة الأقسام", "Manage departments", true),

        new("attendance.view", "attendance", "عرض سجل الحضور", "View attendance", false),
        new("attendance.sync", "attendance", "مزامنة الحضور", "Synchronize attendance", true),

        new(FactoryStructurePermissions.View, "factory-structure", "عرض المصانع وخطوط الإنتاج", "View factories and production lines", false),
        new(FactoryStructurePermissions.Manage, "factory-structure", "إدارة المصانع وخطوط الإنتاج", "Manage factories and production lines", true),

        new("assignments.view", "assignments", "عرض التعيينات", "View assignments", false),
        new("assignments.manage", "assignments", "إدارة التعيينات", "Manage assignments", true),

        new("compensation.view", "compensation", "عرض التعويضات", "View compensation", false),
        new("compensation.manage", "compensation", "إدارة التعويضات", "Manage compensation", true),
        new("compensation.import", "compensation", "استيراد التعويضات", "Import compensation", true),
        new("compensation.export", "compensation", "تصدير التعويضات", "Export compensation", true),

        new("stages.view", "stages", "عرض المراحل", "View stages", false),
        new("stages.manage", "stages", "إدارة المراحل", "Manage stages", true),
        new("stages.import", "stages", "استيراد مراحل", "Import stages", true),
        new("stages.export", "stages", "تصدير المراحل", "Export stages", true),

        new("models.view", "models", "عرض نماذج المنتجات", "View production models", false),
        new("models.manage", "models", "إدارة نماذج المنتجات", "Manage production models", true),

        new("production.view", "production", "عرض بيانات الإنتاج", "View production", false),
        new("production.record", "production", "تسجيل نتائج الإنتاج", "Record production", true),
        new("production.approve", "production", "الموافقة على الإنتاج", "Approve production", true),

        new("users.view", "users", "عرض المستخدمين", "View users", false),
        new("users.manage", "users", "إدارة المستخدمين", "Manage users", true),

        new("roles.view", "roles", "عرض الأدوار", "View roles", false),
        new("roles.manage", "roles", "إدارة الأدوار", "Manage roles", true),

        new("permissions.assign", "permissions", "إدارة التعريفات الممنوحة", "Assign permission overrides", true),
        new("audit.view", "audit", "عرض سجل المراجعة", "View audit logs", true)
    ];

    public static IReadOnlyList<PermissionCatalogEntry> All => Entries;

    public static IReadOnlyList<PermissionCatalogEntry> ByCapability(string capability) =>
        [.. Entries.Where(x => x.Capability.Equals(capability, StringComparison.OrdinalIgnoreCase))];

    public static bool IsKnown(string permissionName) =>
        !string.IsNullOrWhiteSpace(permissionName) &&
        Entries.Any(x => string.Equals(x.Name, permissionName, StringComparison.OrdinalIgnoreCase));
}
