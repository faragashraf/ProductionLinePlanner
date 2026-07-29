namespace ProductionLinePlanner.Application.Notifications;

/// <summary>
/// Safe, server-owned notification navigation actions. Values are deliberately
/// finite so persisted notification metadata never becomes an arbitrary client route.
/// </summary>
public static class NotificationNavigationActions
{
    public const string OpenDailyAttendance = "OpenDailyAttendance";
}
