namespace ProductionLinePlanner.Domain.Notifications;

public static class NotificationEventKeys
{
    public const string WorkerCreated = nameof(WorkerCreated);
    public const string WorkerUpdated = nameof(WorkerUpdated);
    public const string AssignmentChanged = nameof(AssignmentChanged);
    public const string DailyProductionApproved = nameof(DailyProductionApproved);
    public const string AttendanceSyncFailed = nameof(AttendanceSyncFailed);
    public const string WorkerCheckedIn = nameof(WorkerCheckedIn);
    public const string WorkerCheckedOut = nameof(WorkerCheckedOut);
}

public static class NotificationEventCatalog
{
    private static readonly NotificationEventDefinition[] Entries =
    [
        Create(
            NotificationEventKeys.WorkerCreated,
            "تم إنشاء عامل",
            NotificationSeverity.Information,
            soundEnabled: false,
            "تم إنشاء عامل",
            "تم إنشاء العامل {WorkerName} بواسطة {ActorName} في {FactoryName}.",
            ["WorkerName", "ActorName", "FactoryName"]),
        Create(
            NotificationEventKeys.WorkerUpdated,
            "تم تحديث عامل",
            NotificationSeverity.Information,
            soundEnabled: false,
            "تم تحديث عامل",
            "تم تحديث العامل {WorkerName} بواسطة {ActorName} في {FactoryName}.",
            ["WorkerName", "ActorName", "FactoryName"]),
        Create(
            NotificationEventKeys.AssignmentChanged,
            "تم تغيير التسكين",
            NotificationSeverity.Warning,
            soundEnabled: true,
            "تم تغيير التسكين",
            "تم تسكين العامل {WorkerName} في {LineName} داخل {FactoryName} بواسطة {ActorName}.",
            ["WorkerName", "ActorName", "LineName", "FactoryName"]),
        Create(
            NotificationEventKeys.DailyProductionApproved,
            "تم اعتماد الإنتاج اليومي",
            NotificationSeverity.Success,
            soundEnabled: false,
            "تم اعتماد الإنتاج اليومي",
            "تم اعتماد الإنتاج اليومي لـ {LineName} في {FactoryName} بواسطة {ActorName}.",
            ["ActorName", "LineName", "FactoryName"]),
        Create(
            NotificationEventKeys.AttendanceSyncFailed,
            "فشلت مزامنة الحضور",
            NotificationSeverity.Critical,
            soundEnabled: true,
            "فشلت مزامنة الحضور",
            "فشلت مزامنة الحضور في {FactoryName}. تتطلب المراجعة بواسطة {ActorName}.",
            ["ActorName", "FactoryName"]),
        CreateAttendance(
            NotificationEventKeys.WorkerCheckedIn,
            "تسجيل حضور عامل",
            "حضور عامل",
            "سجل العامل {WorkerName} — رقم {EmployeeCode} — الحضور الساعة {AttendanceTime}. {AssignmentText}"),
        CreateAttendance(
            NotificationEventKeys.WorkerCheckedOut,
            "تسجيل انصراف عامل",
            "انصراف عامل",
            "سجل العامل {WorkerName} — رقم {EmployeeCode} — الانصراف الساعة {AttendanceTime}. {AssignmentText}")
    ];

    public static IReadOnlyList<NotificationEventDefinition> All => Entries;

    public static NotificationEventDefinition? Find(string? eventKey) =>
        string.IsNullOrWhiteSpace(eventKey)
            ? null
            : Entries.FirstOrDefault(entry =>
                entry.Key.Equals(eventKey.Trim(), StringComparison.OrdinalIgnoreCase));

    public static bool IsKnown(string? eventKey) => Find(eventKey) is not null;

    private static NotificationEventDefinition Create(
        string key,
        string displayName,
        NotificationSeverity severity,
        bool soundEnabled,
        string titleTemplate,
        string messageTemplate,
        IReadOnlyCollection<string> allowedTokens)
    {
        var defaultPolicy = new NotificationPolicyDefinition(
            key,
            IsEnabled: false,
            severity,
            new NotificationSoundPolicy(soundEnabled),
            new NotificationToastPolicy(Enabled: true),
            new NotificationInboxPolicy(Enabled: true),
            new NotificationBrowserPolicy(Enabled: false),
            titleTemplate,
            messageTemplate,
            RecipientRules: []);

        return new NotificationEventDefinition(key, displayName, allowedTokens, defaultPolicy);
    }

    private static NotificationEventDefinition CreateAttendance(
        string key,
        string displayName,
        string titleTemplate,
        string messageTemplate)
    {
        var defaultPolicy = new NotificationPolicyDefinition(
            key,
            IsEnabled: true,
            NotificationSeverity.Information,
            new NotificationSoundPolicy(Enabled: true),
            new NotificationToastPolicy(Enabled: true),
            new NotificationInboxPolicy(Enabled: true),
            new NotificationBrowserPolicy(Enabled: true),
            titleTemplate,
            messageTemplate,
            [new NotificationRecipientRule(NotificationRecipientKind.AllActiveUsers)]);

        return new NotificationEventDefinition(
            key,
            displayName,
            ["WorkerName", "EmployeeCode", "AttendanceTime", "AssignmentText"],
            defaultPolicy);
    }
}
