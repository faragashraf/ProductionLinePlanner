/*
    Dayoub attendance pipeline diagnostics
    SQL Server 2016 compatible, SELECT-only, production-safe.

    Data flow covered by this report:
      ZKTime USERINFO/CHECKINOUT
        -> dbo.ZkWorkerSyncInbox / dbo.ZkAttendanceSyncInbox
        -> dbo.Workers / dbo.AttendanceRecords
        -> dbo.AttendanceNotificationEvents / dbo.Notifications

    CHECKTIME is stored in the staging inbox as Egypt-local time. Dayoub attendance
    timestamps are UTC. The operational-day boundary below matches the application's
    AttendanceSourceOptions default and the installed staging contract (05:00 local).
*/

SET NOCOUNT ON;
SET LOCK_TIMEOUT 15000;
SET DEADLOCK_PRIORITY LOW;

DECLARE @BadgeNumber NVARCHAR(20) = N''; -- Required: exact ZK BADGENUMBER.
DECLARE @DaysBack INT = 7;               -- Required: positive lookback window.

IF NULLIF(LTRIM(RTRIM(@BadgeNumber)), N'') IS NULL
BEGIN
    PRINT N'ERROR: Set @BadgeNumber before running Attendance-Diagnostics.sql.';
    RETURN;
END;

IF @DaysBack IS NULL OR @DaysBack < 1 OR @DaysBack > 366
BEGIN
    PRINT N'ERROR: @DaysBack must be between 1 and 366.';
    RETURN;
END;

SET @BadgeNumber = LTRIM(RTRIM(@BadgeNumber));

DECLARE @WorkdayBoundaryTime TIME(0) = '05:00:00';
DECLARE @WorkdayBoundarySeconds INT = DATEDIFF(SECOND, CONVERT(TIME(0), '00:00:00'), @WorkdayBoundaryTime);
DECLARE @DelayWarningMinutes INT = 15;
DECLARE @NowUtc DATETIME2(7) = SYSUTCDATETIME();
DECLARE @NowLocal DATETIME2(7) = CONVERT(DATETIME2(7), SYSUTCDATETIME() AT TIME ZONE 'UTC' AT TIME ZONE 'Egypt Standard Time');
DECLARE @FromLocal DATETIME2(7);
DECLARE @FromUtc DATETIME2(7);
DECLARE @FromOperationalDate DATE;

SET @FromLocal = DATEADD(DAY, -@DaysBack, @NowLocal);
SET @FromUtc = CONVERT(DATETIME2(7), @FromLocal AT TIME ZONE 'Egypt Standard Time' AT TIME ZONE 'UTC');
SET @FromOperationalDate = CONVERT(DATE, DATEADD(SECOND, -@WorkdayBoundarySeconds, @FromLocal));

PRINT N'=====================================================';
PRINT N'Attendance Diagnostics Context';
PRINT N'=====================================================';

SELECT
    DB_NAME() AS DayoubDatabase,
    @@SERVERNAME AS SqlServer,
    @BadgeNumber AS BadgeNumber,
    @DaysBack AS DaysBack,
    @FromLocal AS WindowStartEgyptLocal,
    @NowLocal AS WindowEndEgyptLocal,
    @FromUtc AS WindowStartUtc,
    @NowUtc AS WindowEndUtc,
    @WorkdayBoundaryTime AS OperationalDayBoundaryEgyptLocal,
    @DelayWarningMinutes AS DelayWarningMinutes,
    N'ZK staging CHECKTIME is Egypt-local; Dayoub AttendanceTimeUtc is UTC.' AS TimeSemantics;

PRINT N'=====================================================';
PRINT N'Schema Readiness';
PRINT N'=====================================================';

SELECT ObjectName, ObjectType, IsAvailable
FROM
(
    SELECT N'dbo.Workers' AS ObjectName, N'Application table' AS ObjectType,
        CONVERT(BIT, CASE WHEN OBJECT_ID(N'dbo.Workers', N'U') IS NULL THEN 0 ELSE 1 END) AS IsAvailable
    UNION ALL SELECT N'dbo.Departments', N'Application table', CONVERT(BIT, CASE WHEN OBJECT_ID(N'dbo.Departments', N'U') IS NULL THEN 0 ELSE 1 END)
    UNION ALL SELECT N'dbo.WorkerDefaultAssignments', N'Application table', CONVERT(BIT, CASE WHEN OBJECT_ID(N'dbo.WorkerDefaultAssignments', N'U') IS NULL THEN 0 ELSE 1 END)
    UNION ALL SELECT N'dbo.ProductionLines', N'Application table', CONVERT(BIT, CASE WHEN OBJECT_ID(N'dbo.ProductionLines', N'U') IS NULL THEN 0 ELSE 1 END)
    UNION ALL SELECT N'dbo.SubStages', N'Application table', CONVERT(BIT, CASE WHEN OBJECT_ID(N'dbo.SubStages', N'U') IS NULL THEN 0 ELSE 1 END)
    UNION ALL SELECT N'dbo.AttendanceRecords', N'Application table', CONVERT(BIT, CASE WHEN OBJECT_ID(N'dbo.AttendanceRecords', N'U') IS NULL THEN 0 ELSE 1 END)
    UNION ALL SELECT N'dbo.AttendanceSyncStates', N'Application table', CONVERT(BIT, CASE WHEN OBJECT_ID(N'dbo.AttendanceSyncStates', N'U') IS NULL THEN 0 ELSE 1 END)
    UNION ALL SELECT N'dbo.AttendanceNotificationEvents', N'Application outbox', CONVERT(BIT, CASE WHEN OBJECT_ID(N'dbo.AttendanceNotificationEvents', N'U') IS NULL THEN 0 ELSE 1 END)
    UNION ALL SELECT N'dbo.Notifications', N'Application table', CONVERT(BIT, CASE WHEN OBJECT_ID(N'dbo.Notifications', N'U') IS NULL THEN 0 ELSE 1 END)
    UNION ALL SELECT N'dbo.NotificationPolicies', N'Application table', CONVERT(BIT, CASE WHEN OBJECT_ID(N'dbo.NotificationPolicies', N'U') IS NULL THEN 0 ELSE 1 END)
    UNION ALL SELECT N'dbo.ZkWorkerSyncInbox', N'ZK durable staging', CONVERT(BIT, CASE WHEN OBJECT_ID(N'dbo.ZkWorkerSyncInbox', N'U') IS NULL THEN 0 ELSE 1 END)
    UNION ALL SELECT N'dbo.ZkAttendanceSyncInbox', N'ZK durable staging', CONVERT(BIT, CASE WHEN OBJECT_ID(N'dbo.ZkAttendanceSyncInbox', N'U') IS NULL THEN 0 ELSE 1 END)
    UNION ALL SELECT N'dbo.ZkSyncState', N'ZK durable staging', CONVERT(BIT, CASE WHEN OBJECT_ID(N'dbo.ZkSyncState', N'U') IS NULL THEN 0 ELSE 1 END)
    UNION ALL SELECT N'dbo.ZkSyncRuns', N'ZK durable staging', CONVERT(BIT, CASE WHEN OBJECT_ID(N'dbo.ZkSyncRuns', N'U') IS NULL THEN 0 ELSE 1 END)
) AS Objects
ORDER BY ObjectType, ObjectName;

IF OBJECT_ID(N'dbo.Workers', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Departments', N'U') IS NULL
   OR OBJECT_ID(N'dbo.WorkerDefaultAssignments', N'U') IS NULL
   OR OBJECT_ID(N'dbo.ProductionLines', N'U') IS NULL
   OR OBJECT_ID(N'dbo.SubStages', N'U') IS NULL
   OR OBJECT_ID(N'dbo.AttendanceRecords', N'U') IS NULL
   OR OBJECT_ID(N'dbo.AttendanceSyncStates', N'U') IS NULL
   OR OBJECT_ID(N'dbo.AttendanceNotificationEvents', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Notifications', N'U') IS NULL
   OR OBJECT_ID(N'dbo.NotificationPolicies', N'U') IS NULL
   OR OBJECT_ID(N'dbo.ZkWorkerSyncInbox', N'U') IS NULL
   OR OBJECT_ID(N'dbo.ZkAttendanceSyncInbox', N'U') IS NULL
   OR OBJECT_ID(N'dbo.ZkSyncState', N'U') IS NULL
   OR OBJECT_ID(N'dbo.ZkSyncRuns', N'U') IS NULL
BEGIN
    PRINT N'ERROR: One or more required project tables are unavailable. Review Schema Readiness; detailed diagnostics stopped safely.';
    RETURN;
END;

DECLARE @ZkUserCount INT = 0;
DECLARE @ZkUserId INT = NULL;
DECLARE @ZkDefaultDepartmentId INT = NULL;
DECLARE @ZkIsCurrentWorker BIT = NULL;
DECLARE @WorkerCount INT = 0;
DECLARE @WorkerId UNIQUEIDENTIFIER = NULL;
DECLARE @WorkerIsActive BIT = NULL;
DECLARE @WorkerEmploymentStatus INT = NULL;
DECLARE @WorkerAttendanceUserId NVARCHAR(120) = NULL;
DECLARE @ZkPunchCount BIGINT = 0;
DECLARE @ZkCheckInCount BIGINT = 0;
DECLARE @ZkCheckOutCount BIGINT = 0;
DECLARE @ZkInvalidPunchCount BIGINT = 0;
DECLARE @ZkOperationalDayCount INT = 0;
DECLARE @DayoubAttendanceCount BIGINT = 0;
DECLARE @DayoubOperationalDayCount INT = 0;
DECLARE @MissingAttendanceDayCount INT = 0;
DECLARE @ProcessedOrphanCount INT = 0;
DECLARE @AttendanceInboxPending BIGINT = 0;
DECLARE @AttendanceInboxProcessing BIGINT = 0;
DECLARE @AttendanceInboxProcessed BIGINT = 0;
DECLARE @AttendanceInboxSkipped BIGINT = 0;
DECLARE @AttendanceInboxFailed BIGINT = 0;
DECLARE @WorkerInboxPending BIGINT = 0;
DECLARE @WorkerInboxProcessing BIGINT = 0;
DECLARE @WorkerInboxFailed BIGINT = 0;
DECLARE @ExpectedNotificationEventCount INT = 0;
DECLARE @AttendanceNotificationEventCount INT = 0;
DECLARE @PublishedNotificationCount INT = 0;
DECLARE @AttendancePolicyCount INT = 0;
DECLARE @EnabledAttendancePolicyCount INT = 0;
DECLARE @AttendanceSyncStateCount INT = 0;
DECLARE @MissingAttendanceSyncStateDayCount INT = 0;
DECLARE @AttendanceSyncFailureCount INT = 0;
DECLARE @ZkFailedRunCount INT = 0;
DECLARE @LastZkAttendanceLocal DATETIME2(7) = NULL;
DECLARE @LastZkAttendanceUtc DATETIME2(7) = NULL;
DECLARE @LastDayoubAttendanceUtc DATETIME2(7) = NULL;
DECLARE @LastDayoubPersistedAtUtc DATETIME2(7) = NULL;
DECLARE @LastAttendanceSyncSuccessUtc DATETIME2(7) = NULL;
DECLARE @LastZkRunCompletedUtc DATETIME2(7) = NULL;
DECLARE @LastZkRunStatus VARCHAR(16) = NULL;
DECLARE @AttendanceDelayMinutes INT = NULL;

SELECT @ZkUserCount = COUNT(*)
FROM dbo.ZkWorkerSyncInbox AS Inbox
WHERE LTRIM(RTRIM(Inbox.BadgeNumber)) = @BadgeNumber;

SELECT TOP (1)
    @ZkUserId = Inbox.SourceUserId,
    @ZkDefaultDepartmentId = Inbox.SourceDefaultDepartmentId,
    @ZkIsCurrentWorker = Inbox.IsCurrentWorker
FROM dbo.ZkWorkerSyncInbox AS Inbox
WHERE LTRIM(RTRIM(Inbox.BadgeNumber)) = @BadgeNumber
ORDER BY Inbox.LastSeenAtUtc DESC, Inbox.SourceUserId;

SELECT @WorkerCount = COUNT(*)
FROM dbo.Workers AS Worker
WHERE LTRIM(RTRIM(Worker.BadgeNumber)) = @BadgeNumber
   OR (@ZkUserId IS NOT NULL AND LTRIM(RTRIM(Worker.AttendanceUserId)) = CONVERT(NVARCHAR(20), @ZkUserId));

SELECT TOP (1)
    @WorkerId = Worker.Id,
    @WorkerIsActive = Worker.IsActive,
    @WorkerEmploymentStatus = Worker.EmploymentStatus,
    @WorkerAttendanceUserId = Worker.AttendanceUserId
FROM dbo.Workers AS Worker
WHERE LTRIM(RTRIM(Worker.BadgeNumber)) = @BadgeNumber
   OR (@ZkUserId IS NOT NULL AND LTRIM(RTRIM(Worker.AttendanceUserId)) = CONVERT(NVARCHAR(20), @ZkUserId))
ORDER BY
    CASE WHEN LTRIM(RTRIM(Worker.BadgeNumber)) = @BadgeNumber THEN 0 ELSE 1 END,
    Worker.UpdatedAtUtc DESC,
    Worker.Id;

SELECT
    @ZkPunchCount = COUNT_BIG(*),
    @ZkCheckInCount = COALESCE(SUM(CONVERT(BIGINT, CASE WHEN UPPER(LTRIM(RTRIM(Inbox.SourceCheckType))) = N'I' THEN 1 ELSE 0 END)), 0),
    @ZkCheckOutCount = COALESCE(SUM(CONVERT(BIGINT, CASE WHEN UPPER(LTRIM(RTRIM(Inbox.SourceCheckType))) = N'O' THEN 1 ELSE 0 END)), 0),
    @ZkInvalidPunchCount = COALESCE(SUM(CONVERT(BIGINT, CASE WHEN UPPER(LTRIM(RTRIM(Inbox.SourceCheckType))) NOT IN (N'I', N'O') THEN 1 ELSE 0 END)), 0),
    @LastZkAttendanceLocal = MAX(Inbox.SourceCheckTimeLocal)
FROM dbo.ZkAttendanceSyncInbox AS Inbox
WHERE (Inbox.SourceUserId = @ZkUserId OR LTRIM(RTRIM(Inbox.BadgeNumber)) = @BadgeNumber)
  AND Inbox.SourceCheckTimeLocal >= @FromLocal
  AND Inbox.SourceCheckTimeLocal <= @NowLocal;

IF @LastZkAttendanceLocal IS NOT NULL
    SET @LastZkAttendanceUtc = CONVERT(DATETIME2(7), @LastZkAttendanceLocal AT TIME ZONE 'Egypt Standard Time' AT TIME ZONE 'UTC');

SELECT @ZkOperationalDayCount = COUNT(DISTINCT CONVERT(DATE, DATEADD(SECOND, -@WorkdayBoundarySeconds, Inbox.SourceCheckTimeLocal)))
FROM dbo.ZkAttendanceSyncInbox AS Inbox
WHERE (Inbox.SourceUserId = @ZkUserId OR LTRIM(RTRIM(Inbox.BadgeNumber)) = @BadgeNumber)
  AND Inbox.SourceCheckTimeLocal >= @FromLocal
  AND Inbox.SourceCheckTimeLocal <= @NowLocal
  AND UPPER(LTRIM(RTRIM(Inbox.SourceCheckType))) = N'I';

SELECT
    @DayoubAttendanceCount = COUNT_BIG(*),
    @LastDayoubAttendanceUtc = MAX(COALESCE(
        TRY_CONVERT(DATETIME2(7), JSON_VALUE(Record.SourcePayload, N'$.LastOutUtc')),
        TRY_CONVERT(DATETIME2(7), JSON_VALUE(Record.SourcePayload, N'$.FirstInUtc')),
        Record.AttendanceTimeUtc)),
    @LastDayoubPersistedAtUtc = MAX(Record.CreatedAtUtc)
FROM dbo.AttendanceRecords AS Record
WHERE (Record.WorkerId = @WorkerId
       OR LTRIM(RTRIM(Record.BadgeNumber)) = @BadgeNumber
       OR (@ZkUserId IS NOT NULL AND LTRIM(RTRIM(Record.AttendanceUserId)) = CONVERT(NVARCHAR(20), @ZkUserId)))
  AND Record.AttendanceTimeUtc >= @FromUtc
  AND Record.AttendanceTimeUtc <= @NowUtc;

SELECT @DayoubOperationalDayCount = COUNT(DISTINCT
    CONVERT(DATE, DATEADD(SECOND, -@WorkdayBoundarySeconds,
        CONVERT(DATETIME2(7), Record.AttendanceTimeUtc AT TIME ZONE 'UTC' AT TIME ZONE 'Egypt Standard Time'))))
FROM dbo.AttendanceRecords AS Record
WHERE (Record.WorkerId = @WorkerId
       OR LTRIM(RTRIM(Record.BadgeNumber)) = @BadgeNumber
       OR (@ZkUserId IS NOT NULL AND LTRIM(RTRIM(Record.AttendanceUserId)) = CONVERT(NVARCHAR(20), @ZkUserId)))
  AND Record.AttendanceTimeUtc >= @FromUtc
  AND Record.AttendanceTimeUtc <= @NowUtc;

SELECT @AttendanceInboxPending = COUNT_BIG(*)
FROM dbo.ZkAttendanceSyncInbox
WHERE (SourceUserId = @ZkUserId OR LTRIM(RTRIM(BadgeNumber)) = @BadgeNumber)
  AND SourceCheckTimeLocal >= @FromLocal AND ProcessingStatus = 'Pending';

SELECT @AttendanceInboxProcessing = COUNT_BIG(*)
FROM dbo.ZkAttendanceSyncInbox
WHERE (SourceUserId = @ZkUserId OR LTRIM(RTRIM(BadgeNumber)) = @BadgeNumber)
  AND SourceCheckTimeLocal >= @FromLocal AND ProcessingStatus = 'Processing';

SELECT @AttendanceInboxProcessed = COUNT_BIG(*)
FROM dbo.ZkAttendanceSyncInbox
WHERE (SourceUserId = @ZkUserId OR LTRIM(RTRIM(BadgeNumber)) = @BadgeNumber)
  AND SourceCheckTimeLocal >= @FromLocal AND ProcessingStatus = 'Processed';

SELECT @AttendanceInboxSkipped = COUNT_BIG(*)
FROM dbo.ZkAttendanceSyncInbox
WHERE (SourceUserId = @ZkUserId OR LTRIM(RTRIM(BadgeNumber)) = @BadgeNumber)
  AND SourceCheckTimeLocal >= @FromLocal AND ProcessingStatus = 'Skipped';

SELECT @AttendanceInboxFailed = COUNT_BIG(*)
FROM dbo.ZkAttendanceSyncInbox
WHERE (SourceUserId = @ZkUserId OR LTRIM(RTRIM(BadgeNumber)) = @BadgeNumber)
  AND SourceCheckTimeLocal >= @FromLocal AND ProcessingStatus = 'Failed';

SELECT @WorkerInboxPending = COUNT_BIG(*)
FROM dbo.ZkWorkerSyncInbox
WHERE (SourceUserId = @ZkUserId OR LTRIM(RTRIM(BadgeNumber)) = @BadgeNumber)
  AND ProcessingStatus = 'Pending';

SELECT @WorkerInboxProcessing = COUNT_BIG(*)
FROM dbo.ZkWorkerSyncInbox
WHERE (SourceUserId = @ZkUserId OR LTRIM(RTRIM(BadgeNumber)) = @BadgeNumber)
  AND ProcessingStatus = 'Processing';

SELECT @WorkerInboxFailed = COUNT_BIG(*)
FROM dbo.ZkWorkerSyncInbox
WHERE (SourceUserId = @ZkUserId OR LTRIM(RTRIM(BadgeNumber)) = @BadgeNumber)
  AND ProcessingStatus = 'Failed';

SELECT @ExpectedNotificationEventCount = COALESCE(SUM(
    CASE WHEN Record.AttendanceStatus IN (N'Present', N'Late') THEN
        CASE WHEN JSON_VALUE(Record.SourcePayload, N'$.LastOutUtc') IS NULL THEN 1 ELSE 2 END
    ELSE 0 END), 0)
FROM dbo.AttendanceRecords AS Record
WHERE (Record.WorkerId = @WorkerId
       OR LTRIM(RTRIM(Record.BadgeNumber)) = @BadgeNumber
       OR (@ZkUserId IS NOT NULL AND LTRIM(RTRIM(Record.AttendanceUserId)) = CONVERT(NVARCHAR(20), @ZkUserId)))
  AND Record.AttendanceTimeUtc >= @FromUtc
  AND Record.AttendanceTimeUtc <= @NowUtc;

SELECT @AttendanceNotificationEventCount = COUNT(*)
FROM dbo.AttendanceNotificationEvents AS Event
WHERE Event.WorkerId = @WorkerId
  AND Event.AttendanceTimeUtc >= @FromUtc
  AND Event.AttendanceTimeUtc <= @NowUtc;

SELECT @PublishedNotificationCount = COUNT(*)
FROM dbo.Notifications AS Notification
WHERE Notification.RelatedWorkerId = @WorkerId
  AND Notification.EventKey IN (N'WorkerCheckedIn', N'WorkerCheckedOut')
  AND Notification.CreatedAtUtc >= @FromUtc
  AND Notification.CreatedAtUtc <= @NowUtc;

SELECT
    @AttendancePolicyCount = COUNT(*),
    @EnabledAttendancePolicyCount = COALESCE(SUM(CASE WHEN IsEnabled = 1 THEN 1 ELSE 0 END), 0)
FROM dbo.NotificationPolicies
WHERE EventKey IN (N'WorkerCheckedIn', N'WorkerCheckedOut');

SELECT
    @AttendanceSyncStateCount = COUNT(*),
    @AttendanceSyncFailureCount = COALESCE(SUM(CASE WHEN LastAttemptSucceeded = 0 THEN 1 ELSE 0 END), 0),
    @LastAttendanceSyncSuccessUtc = MAX(LastSuccessfulAtUtc)
FROM dbo.AttendanceSyncStates
WHERE OperationalDate >= @FromOperationalDate;

SELECT @MissingAttendanceSyncStateDayCount = COUNT(*)
FROM
(
    SELECT CONVERT(DATE, DATEADD(SECOND, -@WorkdayBoundarySeconds, Inbox.SourceCheckTimeLocal)) AS OperationalDate
    FROM dbo.ZkAttendanceSyncInbox AS Inbox
    WHERE (Inbox.SourceUserId = @ZkUserId OR LTRIM(RTRIM(Inbox.BadgeNumber)) = @BadgeNumber)
      AND Inbox.SourceCheckTimeLocal >= @FromLocal
      AND Inbox.SourceCheckTimeLocal <= @NowLocal
      AND UPPER(LTRIM(RTRIM(Inbox.SourceCheckType))) = N'I'
    GROUP BY CONVERT(DATE, DATEADD(SECOND, -@WorkdayBoundarySeconds, Inbox.SourceCheckTimeLocal))
) AS SourceDays
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.AttendanceSyncStates AS SyncState
    WHERE SyncState.OperationalDate = SourceDays.OperationalDate
);

SELECT @ZkFailedRunCount = COUNT(*)
FROM dbo.ZkSyncRuns
WHERE StartedAtUtc >= @FromUtc AND Status = 'Failed';

SELECT TOP (1)
    @LastZkRunStatus = Status,
    @LastZkRunCompletedUtc = CompletedAtUtc
FROM dbo.ZkSyncRuns
ORDER BY StartedAtUtc DESC, RunId;

IF @LastZkAttendanceUtc IS NOT NULL AND @LastDayoubAttendanceUtc IS NOT NULL
    SET @AttendanceDelayMinutes = DATEDIFF(MINUTE, @LastDayoubAttendanceUtc, @LastZkAttendanceUtc);

PRINT N'=====================================================';
PRINT N'Worker Mapping';
PRINT N'=====================================================';

SELECT
    Worker.Id AS WorkerId,
    Worker.EmployeeCode,
    Worker.FullName,
    Worker.BadgeNumber,
    Worker.AttendanceUserId,
    Worker.AttendanceDepartmentId,
    Worker.LocalDepartmentName,
    Worker.OrganizationalDepartmentId,
    Department.NameAr AS OrganizationalDepartmentName,
    Worker.EmploymentStatus,
    CASE Worker.EmploymentStatus WHEN 1 THEN N'Active' WHEN 2 THEN N'Suspended' WHEN 3 THEN N'LeftEmployment' ELSE N'Unknown' END AS EmploymentStatusName,
    Worker.EmploymentEndDate,
    Worker.IsActive,
    Worker.LastExternalSyncAt,
    Worker.UpdatedAtUtc,
    (SELECT COUNT(*) FROM dbo.WorkerDefaultAssignments AS Assignment WHERE Assignment.WorkerId = Worker.Id AND Assignment.IsActive = 1) AS ActiveAssignmentCount
FROM dbo.Workers AS Worker
LEFT JOIN dbo.Departments AS Department ON Department.Id = Worker.OrganizationalDepartmentId
WHERE LTRIM(RTRIM(Worker.BadgeNumber)) = @BadgeNumber
   OR (@ZkUserId IS NOT NULL AND LTRIM(RTRIM(Worker.AttendanceUserId)) = CONVERT(NVARCHAR(20), @ZkUserId))
ORDER BY Worker.IsActive DESC, Worker.UpdatedAtUtc DESC, Worker.Id;

PRINT N'Worker Permanent Assignments';

SELECT
    Assignment.Id AS AssignmentId,
    Assignment.WorkerId,
    Assignment.IsActive,
    Assignment.AssignedAt,
    Assignment.UpdatedAtUtc,
    Line.Id AS ProductionLineId,
    Line.LineCode,
    Line.Name AS ProductionLineName,
    Stage.Id AS SubStageId,
    Stage.Code AS SubStageCode,
    Stage.Name AS SubStageName,
    Department.Id AS DepartmentId,
    Department.NameAr AS DepartmentName,
    Department.IsActive AS DepartmentIsActive,
    Line.IsActive AS ProductionLineIsActive,
    Stage.IsActive AS SubStageIsActive
FROM dbo.WorkerDefaultAssignments AS Assignment
INNER JOIN dbo.ProductionLines AS Line ON Line.Id = Assignment.ProductionLineId
INNER JOIN dbo.SubStages AS Stage ON Stage.Id = Assignment.SubStageId
LEFT JOIN dbo.Departments AS Department ON Department.Id = Line.DepartmentId
WHERE Assignment.WorkerId = @WorkerId
ORDER BY Assignment.IsActive DESC, Assignment.AssignedAt DESC, Assignment.Id;

PRINT N'=====================================================';
PRINT N'ZK USERINFO (Durable Staging Snapshot)';
PRINT N'=====================================================';

SELECT
    Inbox.SourceUserId AS USERID,
    Inbox.BadgeNumber AS BADGENUMBER,
    Inbox.SourceName AS [NAME],
    Inbox.SourceDefaultDepartmentId AS DEFAULTDEPTID,
    Inbox.IsCurrentWorker,
    CASE WHEN Inbox.IsCurrentWorker = 1 THEN N'Included: DEFAULTDEPTID is 1 or 4' ELSE N'Excluded: DEFAULTDEPTID is not 1 or 4' END AS DepartmentClassification,
    Inbox.FirstDiscoveredAtUtc,
    Inbox.LastSeenAtUtc,
    Inbox.ProcessingStatus,
    Inbox.AttemptCount,
    Inbox.ResolutionCode,
    Inbox.ResolutionDetails,
    Inbox.LastError,
    Inbox.ProcessedAtUtc,
    Inbox.ResolvedAtUtc
FROM dbo.ZkWorkerSyncInbox AS Inbox
WHERE LTRIM(RTRIM(Inbox.BadgeNumber)) = @BadgeNumber
   OR Inbox.SourceUserId = @ZkUserId
ORDER BY Inbox.LastSeenAtUtc DESC, Inbox.SourceUserId;

PRINT N'=====================================================';
PRINT N'ZK CHECKINOUT (Durable Staging Copy)';
PRINT N'=====================================================';

SELECT
    @ZkPunchCount AS TotalMovementCount,
    @ZkCheckInCount AS CheckInCount,
    @ZkCheckOutCount AS CheckOutCount,
    @ZkInvalidPunchCount AS InvalidCheckTypeCount,
    @ZkOperationalDayCount AS OperationalDaysWithCheckIn,
    @LastZkAttendanceLocal AS LastMovementEgyptLocal,
    @LastZkAttendanceUtc AS LastMovementUtc;

SELECT TOP (200)
    Inbox.InboxId,
    Inbox.SourceUserId AS USERID,
    Inbox.BadgeNumber AS BADGENUMBER,
    Inbox.SourceCheckTimeLocal AS CHECKTIME,
    Inbox.SourceCheckType AS CHECKTYPE,
    CONVERT(DATE, DATEADD(SECOND, -@WorkdayBoundarySeconds, Inbox.SourceCheckTimeLocal)) AS OperationalDate,
    Inbox.VerifyCode,
    Inbox.SensorId,
    Inbox.WorkCode,
    CONVERT(VARCHAR(64), Inbox.SourceKey, 2) AS SourceRawId,
    Inbox.ProcessingStatus,
    Inbox.AttemptCount,
    Inbox.ResolutionCode,
    Inbox.ResolutionDetails,
    Inbox.LastError,
    Inbox.InsertedAtUtc,
    Inbox.ProcessingStartedAtUtc,
    Inbox.ProcessedAtUtc,
    Inbox.ResolvedAtUtc,
    Inbox.UpdatedAtUtc
FROM dbo.ZkAttendanceSyncInbox AS Inbox
WHERE (Inbox.SourceUserId = @ZkUserId OR LTRIM(RTRIM(Inbox.BadgeNumber)) = @BadgeNumber)
  AND Inbox.SourceCheckTimeLocal >= @FromLocal
  AND Inbox.SourceCheckTimeLocal <= @NowLocal
ORDER BY Inbox.SourceCheckTimeLocal DESC, Inbox.InboxId DESC;

PRINT N'=====================================================';
PRINT N'Dayoub Attendance';
PRINT N'=====================================================';

SELECT TOP (200)
    Record.Id AS AttendanceRecordId,
    Record.WorkerId,
    Record.AttendanceTimeUtc,
    CONVERT(DATETIME2(7), Record.AttendanceTimeUtc AT TIME ZONE 'UTC' AT TIME ZONE 'Egypt Standard Time') AS AttendanceTimeEgyptLocal,
    CONVERT(DATE, DATEADD(SECOND, -@WorkdayBoundarySeconds,
        CONVERT(DATETIME2(7), Record.AttendanceTimeUtc AT TIME ZONE 'UTC' AT TIME ZONE 'Egypt Standard Time'))) AS OperationalDate,
    Record.AttendanceStatus,
    Record.Source,
    Record.SourceRawId,
    Record.AttendanceUserId,
    Record.BadgeNumber,
    TRY_CONVERT(DATETIME2(7), JSON_VALUE(Record.SourcePayload, N'$.FirstInUtc')) AS FirstInUtc,
    TRY_CONVERT(DATETIME2(7), JSON_VALUE(Record.SourcePayload, N'$.LastOutUtc')) AS LastOutUtc,
    Record.CreatedAtUtc,
    Record.SourcePayload
FROM dbo.AttendanceRecords AS Record
WHERE (Record.WorkerId = @WorkerId
       OR LTRIM(RTRIM(Record.BadgeNumber)) = @BadgeNumber
       OR (@ZkUserId IS NOT NULL AND LTRIM(RTRIM(Record.AttendanceUserId)) = CONVERT(NVARCHAR(20), @ZkUserId)))
  AND Record.AttendanceTimeUtc >= @FromUtc
  AND Record.AttendanceTimeUtc <= @NowUtc
ORDER BY Record.AttendanceTimeUtc DESC, Record.Id;

PRINT N'=====================================================';
PRINT N'Attendance Inbox';
PRINT N'=====================================================';

SELECT Statuses.ProcessingStatus, COALESCE(Counts.RecordCount, 0) AS RecordCount
FROM
(
    SELECT 'Pending' AS ProcessingStatus
    UNION ALL SELECT 'Processing'
    UNION ALL SELECT 'Processed'
    UNION ALL SELECT 'Skipped'
    UNION ALL SELECT 'Failed'
) AS Statuses
LEFT JOIN
(
    SELECT ProcessingStatus, COUNT_BIG(*) AS RecordCount
    FROM dbo.ZkAttendanceSyncInbox
    WHERE (SourceUserId = @ZkUserId OR LTRIM(RTRIM(BadgeNumber)) = @BadgeNumber)
      AND SourceCheckTimeLocal >= @FromLocal
    GROUP BY ProcessingStatus
) AS Counts ON Counts.ProcessingStatus = Statuses.ProcessingStatus
ORDER BY CASE Statuses.ProcessingStatus WHEN 'Failed' THEN 1 WHEN 'Pending' THEN 2 WHEN 'Processing' THEN 3 WHEN 'Skipped' THEN 4 ELSE 5 END;

SELECT TOP (200)
    InboxId,
    SourceUserId,
    BadgeNumber,
    SourceCheckTimeLocal,
    SourceCheckType,
    ProcessingStatus,
    AttemptCount AS RetryCount,
    LastError,
    ResolutionCode,
    ResolutionDetails,
    ProcessingStartedAtUtc,
    ProcessedAtUtc,
    ResolvedAtUtc,
    UpdatedAtUtc
FROM dbo.ZkAttendanceSyncInbox
WHERE (SourceUserId = @ZkUserId OR LTRIM(RTRIM(BadgeNumber)) = @BadgeNumber)
  AND SourceCheckTimeLocal >= @FromLocal
ORDER BY
    CASE ProcessingStatus WHEN 'Failed' THEN 1 WHEN 'Pending' THEN 2 WHEN 'Processing' THEN 3 WHEN 'Skipped' THEN 4 ELSE 5 END,
    SourceCheckTimeLocal DESC,
    InboxId DESC;

PRINT N'=====================================================';
PRINT N'Worker Inbox';
PRINT N'=====================================================';

SELECT Statuses.ProcessingStatus, COALESCE(Counts.RecordCount, 0) AS RecordCount
FROM
(
    SELECT 'Pending' AS ProcessingStatus
    UNION ALL SELECT 'Processing'
    UNION ALL SELECT 'Processed'
    UNION ALL SELECT 'Skipped'
    UNION ALL SELECT 'Failed'
) AS Statuses
LEFT JOIN
(
    SELECT ProcessingStatus, COUNT_BIG(*) AS RecordCount
    FROM dbo.ZkWorkerSyncInbox
    WHERE SourceUserId = @ZkUserId OR LTRIM(RTRIM(BadgeNumber)) = @BadgeNumber
    GROUP BY ProcessingStatus
) AS Counts ON Counts.ProcessingStatus = Statuses.ProcessingStatus
ORDER BY CASE Statuses.ProcessingStatus WHEN 'Failed' THEN 1 WHEN 'Pending' THEN 2 WHEN 'Processing' THEN 3 WHEN 'Skipped' THEN 4 ELSE 5 END;

SELECT
    InboxId,
    SourceUserId,
    BadgeNumber,
    SourceName,
    SourceDefaultDepartmentId,
    IsCurrentWorker,
    ProcessingStatus,
    AttemptCount AS RetryCount,
    LastError,
    ResolutionCode,
    ResolutionDetails,
    FirstDiscoveredAtUtc,
    LastSeenAtUtc,
    ProcessingStartedAtUtc,
    ProcessedAtUtc,
    ResolvedAtUtc,
    UpdatedAtUtc
FROM dbo.ZkWorkerSyncInbox
WHERE SourceUserId = @ZkUserId OR LTRIM(RTRIM(BadgeNumber)) = @BadgeNumber
ORDER BY LastSeenAtUtc DESC, InboxId DESC;

PRINT N'=====================================================';
PRINT N'Attendance Notifications';
PRINT N'=====================================================';

PRINT N'Notification policies';

SELECT
    Id AS NotificationPolicyId,
    EventKey,
    IsEnabled,
    IsInboxEnabled,
    IsToastEnabled,
    IsSoundEnabled,
    IsBrowserEnabled,
    Severity,
    UpdatedAtUtc
FROM dbo.NotificationPolicies
WHERE EventKey IN (N'WorkerCheckedIn', N'WorkerCheckedOut')
ORDER BY EventKey;

PRINT N'Attendance notification outbox';

SELECT TOP (200)
    Event.Id AS AttendanceNotificationEventId,
    Event.AttendanceRecordId,
    Event.WorkerId,
    Event.WorkerName,
    Event.EmployeeCode,
    Event.AttendanceType,
    Event.AttendanceTimeUtc,
    Event.Source,
    Event.IdempotencyKey,
    Event.AttemptCount,
    Event.LastAttemptAtUtc,
    Event.LastErrorCode,
    Event.ProcessedAtUtc,
    Event.CreatedAtUtc
FROM dbo.AttendanceNotificationEvents AS Event
WHERE Event.WorkerId = @WorkerId
  AND Event.AttendanceTimeUtc >= @FromUtc
  AND Event.AttendanceTimeUtc <= @NowUtc
ORDER BY Event.AttendanceTimeUtc DESC, Event.Id;

PRINT N'Published application notifications';

SELECT TOP (200)
    Notification.Id AS NotificationId,
    Notification.RecipientUserId,
    Notification.EventKey,
    Notification.RelatedWorkerId,
    Notification.RelatedEntityType,
    Notification.RelatedEntityId,
    Notification.CorrelationKey,
    Notification.Status,
    Notification.IsRead,
    Notification.Title,
    Notification.CreatedAtUtc,
    Notification.UpdatedAtUtc
FROM dbo.Notifications AS Notification
WHERE Notification.RelatedWorkerId = @WorkerId
  AND Notification.EventKey IN (N'WorkerCheckedIn', N'WorkerCheckedOut')
  AND Notification.CreatedAtUtc >= @FromUtc
  AND Notification.CreatedAtUtc <= @NowUtc
ORDER BY Notification.CreatedAtUtc DESC, Notification.Id;

PRINT N'=====================================================';
PRINT N'AttendanceSyncStates';
PRINT N'=====================================================';

SELECT
    Id,
    SourceName,
    OperationalDate,
    LastAttemptAtUtc,
    LastSuccessfulAtUtc,
    LastAttemptSucceeded,
    LastErrorCode,
    DATEDIFF(MINUTE, LastAttemptAtUtc, @NowUtc) AS MinutesSinceLastAttempt,
    CASE
        WHEN LastAttemptSucceeded = 0 THEN N'ERROR'
        WHEN LastSuccessfulAtUtc IS NULL THEN N'WARNING'
        WHEN DATEDIFF(MINUTE, LastSuccessfulAtUtc, @NowUtc) > @DelayWarningMinutes THEN N'WARNING'
        ELSE N'OK'
    END AS DiagnosticStatus
FROM dbo.AttendanceSyncStates
WHERE OperationalDate >= @FromOperationalDate
ORDER BY OperationalDate DESC, SourceName;

PRINT N'=====================================================';
PRINT N'ZK Sync State';
PRINT N'=====================================================';

SELECT
    State.StateId,
    State.ActiveRunId,
    State.UpdatedAtUtc,
    DATEDIFF(MINUTE, State.UpdatedAtUtc, @NowUtc) AS MinutesSinceStateChanged
FROM dbo.ZkSyncState AS State
WHERE State.StateId = 1;

SELECT TOP (30)
    RunId,
    TriggerType,
    Status,
    StartedAtUtc,
    CompletedAtUtc,
    DATEDIFF(SECOND, StartedAtUtc, COALESCE(CompletedAtUtc, @NowUtc)) AS DurationSeconds,
    WorkersDiscovered,
    WorkersInserted,
    WorkersChanged,
    PunchesDiscovered,
    PunchesInserted,
    LastError,
    UpdatedAtUtc
FROM dbo.ZkSyncRuns
WHERE StartedAtUtc >= @FromUtc
ORDER BY StartedAtUtc DESC, RunId;

PRINT N'=====================================================';
PRINT N'Summary Counters';
PRINT N'=====================================================';

;WITH ProcessedCandidates AS
(
    SELECT
        UPPER(LTRIM(RTRIM(Inbox.SourceCheckType))) AS SourceCheckType,
        CONVERT(VARCHAR(64), Inbox.SourceKey, 2) AS SourceRawId,
        CONVERT(DATETIME2(7), Inbox.SourceCheckTimeLocal AT TIME ZONE 'Egypt Standard Time' AT TIME ZONE 'UTC') AS ExpectedAttendanceTimeUtc
    FROM dbo.ZkAttendanceSyncInbox AS Inbox
    WHERE Inbox.ProcessingStatus = 'Processed'
      AND Inbox.SourceCheckTimeLocal >= @FromLocal
      AND Inbox.SourceCheckTimeLocal <= @NowLocal
      AND (Inbox.SourceUserId = @ZkUserId OR LTRIM(RTRIM(Inbox.BadgeNumber)) = @BadgeNumber)
)
SELECT @ProcessedOrphanCount = COUNT(*)
FROM ProcessedCandidates AS Candidate
WHERE @WorkerCount = 1
  AND @WorkerIsActive = 1
  AND @WorkerEmploymentStatus = 1
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.AttendanceRecords AS Record
      WHERE Record.WorkerId = @WorkerId
        AND
        (
            (Candidate.SourceCheckType = N'I'
             AND Record.SourceRawId = Candidate.SourceRawId
             AND TRY_CONVERT(DATETIME2(7), JSON_VALUE(Record.SourcePayload, N'$.FirstInUtc')) = Candidate.ExpectedAttendanceTimeUtc)
            OR
            (Candidate.SourceCheckType = N'O'
             AND TRY_CONVERT(DATETIME2(7), JSON_VALUE(Record.SourcePayload, N'$.LastOutUtc')) = Candidate.ExpectedAttendanceTimeUtc)
        )
  );

SELECT
    @ZkPunchCount AS ZkMovementCount,
    @ZkCheckInCount AS ZkCheckInCount,
    @ZkCheckOutCount AS ZkCheckOutCount,
    @ZkOperationalDayCount AS ZkOperationalDayCount,
    @DayoubAttendanceCount AS DayoubAttendanceRecordCount,
    @DayoubOperationalDayCount AS DayoubOperationalDayCount,
    @ZkOperationalDayCount - @DayoubOperationalDayCount AS Difference,
    N'Difference compares operational days containing a ZK check-in with Dayoub daily attendance summaries; raw punches are intentionally not compared one-to-one.' AS DifferenceSemantics,
    @AttendanceInboxPending AS InboxPending,
    @AttendanceInboxProcessing AS InboxProcessing,
    @AttendanceInboxProcessed AS InboxProcessed,
    @AttendanceInboxSkipped AS InboxSkipped,
    @AttendanceInboxFailed AS InboxFailed,
    @ProcessedOrphanCount AS ProcessedOrphanCount,
    @MissingAttendanceSyncStateDayCount AS ZkDaysWithoutAttendanceSyncState,
    @LastAttendanceSyncSuccessUtc AS LastAttendanceSyncSuccessUtc,
    @LastZkRunStatus AS LastZkRunStatus,
    @LastZkRunCompletedUtc AS LastZkRunCompletedUtc;

PRINT N'=====================================================';
PRINT N'Processed Attendance Orphans';
PRINT N'=====================================================';

;WITH ProcessedCandidates AS
(
    SELECT
        Inbox.InboxId,
        Inbox.SourceUserId,
        Inbox.BadgeNumber,
        Inbox.SourceCheckTimeLocal,
        UPPER(LTRIM(RTRIM(Inbox.SourceCheckType))) AS SourceCheckType,
        CONVERT(VARCHAR(64), Inbox.SourceKey, 2) AS SourceRawId,
        CONVERT(DATETIME2(7), Inbox.SourceCheckTimeLocal AT TIME ZONE 'Egypt Standard Time' AT TIME ZONE 'UTC') AS ExpectedAttendanceTimeUtc,
        Inbox.ProcessingStatus,
        Inbox.AttemptCount,
        Inbox.ProcessedAtUtc,
        Inbox.ResolutionCode,
        Inbox.ResolutionDetails
    FROM dbo.ZkAttendanceSyncInbox AS Inbox
    WHERE Inbox.ProcessingStatus = 'Processed'
      AND Inbox.SourceCheckTimeLocal >= @FromLocal
      AND Inbox.SourceCheckTimeLocal <= @NowLocal
      AND (Inbox.SourceUserId = @ZkUserId OR LTRIM(RTRIM(Inbox.BadgeNumber)) = @BadgeNumber)
),
ExactEvidence AS
(
    SELECT
        Candidate.*,
        @WorkerId AS MatchedWorkerId,
        ExactRecord.AttendanceRecordId
    FROM ProcessedCandidates AS Candidate
    OUTER APPLY
    (
        SELECT TOP (1) Record.Id AS AttendanceRecordId
        FROM dbo.AttendanceRecords AS Record
        WHERE @WorkerCount = 1
          AND @WorkerIsActive = 1
          AND @WorkerEmploymentStatus = 1
          AND Record.WorkerId = @WorkerId
          AND
          (
              (
                  Candidate.SourceCheckType = N'I'
                  AND Record.SourceRawId = Candidate.SourceRawId
                  AND TRY_CONVERT(DATETIME2(7), JSON_VALUE(Record.SourcePayload, N'$.FirstInUtc')) = Candidate.ExpectedAttendanceTimeUtc
              )
              OR
              (
                  Candidate.SourceCheckType = N'O'
                  AND TRY_CONVERT(DATETIME2(7), JSON_VALUE(Record.SourcePayload, N'$.LastOutUtc')) = Candidate.ExpectedAttendanceTimeUtc
              )
          )
        ORDER BY Record.CreatedAtUtc DESC, Record.Id
    ) AS ExactRecord
)
SELECT
    InboxId,
    SourceUserId,
    COALESCE(BadgeNumber, @BadgeNumber) AS BadgeNumber,
    SourceCheckTimeLocal,
    SourceCheckType,
    ExpectedAttendanceTimeUtc,
    MatchedWorkerId,
    AttendanceRecordId AS ExistingAttendanceRecordId,
    AttemptCount,
    ProcessedAtUtc,
    ResolutionCode,
    ResolutionDetails,
    N'ProcessedWithoutAttendance' AS ReasonCode
FROM ExactEvidence
WHERE MatchedWorkerId IS NOT NULL
  AND AttendanceRecordId IS NULL
ORDER BY SourceCheckTimeLocal, InboxId;

;WITH ProcessedCandidates AS
(
    SELECT
        Inbox.InboxId,
        UPPER(LTRIM(RTRIM(Inbox.SourceCheckType))) AS SourceCheckType,
        CONVERT(VARCHAR(64), Inbox.SourceKey, 2) AS SourceRawId,
        CONVERT(DATETIME2(7), Inbox.SourceCheckTimeLocal AT TIME ZONE 'Egypt Standard Time' AT TIME ZONE 'UTC') AS ExpectedAttendanceTimeUtc
    FROM dbo.ZkAttendanceSyncInbox AS Inbox
    WHERE Inbox.ProcessingStatus = 'Processed'
      AND Inbox.SourceCheckTimeLocal >= @FromLocal
      AND Inbox.SourceCheckTimeLocal <= @NowLocal
      AND (Inbox.SourceUserId = @ZkUserId OR LTRIM(RTRIM(Inbox.BadgeNumber)) = @BadgeNumber)
)
SELECT @ProcessedOrphanCount = COUNT(*)
FROM ProcessedCandidates AS Candidate
WHERE @WorkerCount = 1
  AND @WorkerIsActive = 1
  AND @WorkerEmploymentStatus = 1
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.AttendanceRecords AS Record
      WHERE Record.WorkerId = @WorkerId
        AND
        (
            (
                Candidate.SourceCheckType = N'I'
                AND Record.SourceRawId = Candidate.SourceRawId
                AND TRY_CONVERT(DATETIME2(7), JSON_VALUE(Record.SourcePayload, N'$.FirstInUtc')) = Candidate.ExpectedAttendanceTimeUtc
            )
            OR
            (
                Candidate.SourceCheckType = N'O'
                AND TRY_CONVERT(DATETIME2(7), JSON_VALUE(Record.SourcePayload, N'$.LastOutUtc')) = Candidate.ExpectedAttendanceTimeUtc
            )
        )
  );

SELECT
    @ProcessedOrphanCount AS ProcessedOrphanCount,
    CASE WHEN @ProcessedOrphanCount > 0 THEN N'ERROR — Processed Inbox Orphan' ELSE N'OK' END AS Diagnosis;

PRINT N'=====================================================';
PRINT N'Missing Attendance';
PRINT N'=====================================================';

;WITH SourceOperationalDays AS
(
    SELECT
        Inbox.SourceUserId,
        CONVERT(DATE, DATEADD(SECOND, -@WorkdayBoundarySeconds, Inbox.SourceCheckTimeLocal)) AS OperationalDate,
        MIN(CASE WHEN UPPER(LTRIM(RTRIM(Inbox.SourceCheckType))) = N'I' THEN Inbox.SourceCheckTimeLocal END) AS FirstCheckInLocal,
        MAX(CASE WHEN UPPER(LTRIM(RTRIM(Inbox.SourceCheckType))) = N'O' THEN Inbox.SourceCheckTimeLocal END) AS LastCheckOutLocal,
        SUM(CASE WHEN UPPER(LTRIM(RTRIM(Inbox.SourceCheckType))) = N'I' THEN 1 ELSE 0 END) AS CheckInCount,
        SUM(CASE WHEN UPPER(LTRIM(RTRIM(Inbox.SourceCheckType))) = N'O' THEN 1 ELSE 0 END) AS CheckOutCount,
        SUM(CASE WHEN Inbox.ProcessingStatus = 'Pending' THEN 1 ELSE 0 END) AS PendingCount,
        SUM(CASE WHEN Inbox.ProcessingStatus = 'Processing' THEN 1 ELSE 0 END) AS ProcessingCount,
        SUM(CASE WHEN Inbox.ProcessingStatus = 'Processed' THEN 1 ELSE 0 END) AS ProcessedCount,
        SUM(CASE WHEN Inbox.ProcessingStatus = 'Skipped' THEN 1 ELSE 0 END) AS SkippedCount,
        SUM(CASE WHEN Inbox.ProcessingStatus = 'Failed' THEN 1 ELSE 0 END) AS FailedCount,
        NULLIF(MAX(COALESCE(Inbox.LastError, N'')), N'') AS LastError,
        NULLIF(MAX(COALESCE(Inbox.ResolutionCode, N'')), N'') AS ResolutionCode
    FROM dbo.ZkAttendanceSyncInbox AS Inbox
    WHERE (Inbox.SourceUserId = @ZkUserId OR LTRIM(RTRIM(Inbox.BadgeNumber)) = @BadgeNumber)
      AND Inbox.SourceCheckTimeLocal >= @FromLocal
      AND Inbox.SourceCheckTimeLocal <= @NowLocal
    GROUP BY
        Inbox.SourceUserId,
        CONVERT(DATE, DATEADD(SECOND, -@WorkdayBoundarySeconds, Inbox.SourceCheckTimeLocal))
),
DayoubOperationalDays AS
(
    SELECT
        Record.Id AS AttendanceRecordId,
        Record.WorkerId,
        CONVERT(DATE, DATEADD(SECOND, -@WorkdayBoundarySeconds,
            CONVERT(DATETIME2(7), Record.AttendanceTimeUtc AT TIME ZONE 'UTC' AT TIME ZONE 'Egypt Standard Time'))) AS OperationalDate,
        Record.AttendanceStatus,
        Record.SourceRawId,
        Record.CreatedAtUtc
    FROM dbo.AttendanceRecords AS Record
    WHERE (Record.WorkerId = @WorkerId
           OR LTRIM(RTRIM(Record.BadgeNumber)) = @BadgeNumber
           OR (@ZkUserId IS NOT NULL AND LTRIM(RTRIM(Record.AttendanceUserId)) = CONVERT(NVARCHAR(20), @ZkUserId)))
      AND Record.AttendanceTimeUtc >= @FromUtc
      AND Record.AttendanceTimeUtc <= @NowUtc
)
SELECT
    Source.SourceUserId,
    @BadgeNumber AS BadgeNumber,
    Source.OperationalDate,
    Source.FirstCheckInLocal,
    Source.LastCheckOutLocal,
    Source.CheckInCount,
    Source.CheckOutCount,
    Source.PendingCount,
    Source.ProcessingCount,
    Source.ProcessedCount,
    Source.SkippedCount,
    Source.FailedCount,
    Source.ResolutionCode,
    Source.LastError,
    CASE
        WHEN Source.CheckInCount = 0 THEN N'No valid check-in exists for this operational day.'
        WHEN Source.FailedCount > 0 THEN N'One or more source punches failed inbox processing.'
        WHEN Source.PendingCount > 0 OR Source.ProcessingCount > 0 THEN N'Source punches have not finished inbox processing.'
        WHEN Source.SkippedCount > 0 THEN N'Source punches were skipped by a business rule.'
        WHEN @WorkerId IS NULL THEN N'No unique Dayoub worker mapping is available.'
        ELSE N'ZK check-in exists but no Dayoub daily attendance summary was found.'
    END AS MissingReason
FROM SourceOperationalDays AS Source
LEFT JOIN DayoubOperationalDays AS Dayoub ON Dayoub.OperationalDate = Source.OperationalDate
WHERE Dayoub.AttendanceRecordId IS NULL
ORDER BY Source.OperationalDate DESC, Source.SourceUserId;

SELECT @MissingAttendanceDayCount = COUNT(*)
FROM
(
    SELECT CONVERT(DATE, DATEADD(SECOND, -@WorkdayBoundarySeconds, Inbox.SourceCheckTimeLocal)) AS OperationalDate
    FROM dbo.ZkAttendanceSyncInbox AS Inbox
    WHERE (Inbox.SourceUserId = @ZkUserId OR LTRIM(RTRIM(Inbox.BadgeNumber)) = @BadgeNumber)
      AND Inbox.SourceCheckTimeLocal >= @FromLocal
      AND Inbox.SourceCheckTimeLocal <= @NowLocal
      AND UPPER(LTRIM(RTRIM(Inbox.SourceCheckType))) = N'I'
    GROUP BY CONVERT(DATE, DATEADD(SECOND, -@WorkdayBoundarySeconds, Inbox.SourceCheckTimeLocal))
) AS SourceDays
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.AttendanceRecords AS Record
    WHERE (Record.WorkerId = @WorkerId
           OR LTRIM(RTRIM(Record.BadgeNumber)) = @BadgeNumber
           OR (@ZkUserId IS NOT NULL AND LTRIM(RTRIM(Record.AttendanceUserId)) = CONVERT(NVARCHAR(20), @ZkUserId)))
      AND CONVERT(DATE, DATEADD(SECOND, -@WorkdayBoundarySeconds,
            CONVERT(DATETIME2(7), Record.AttendanceTimeUtc AT TIME ZONE 'UTC' AT TIME ZONE 'Egypt Standard Time'))) = SourceDays.OperationalDate
);

PRINT N'=====================================================';
PRINT N'Delay';
PRINT N'=====================================================';

SELECT
    @LastZkAttendanceLocal AS LastZkAttendanceEgyptLocal,
    @LastZkAttendanceUtc AS LastZkAttendanceUtc,
    @LastDayoubAttendanceUtc AS LastDayoubAttendanceEvidenceUtc,
    @LastDayoubPersistedAtUtc AS LastDayoubRecordPersistedAtUtc,
    @AttendanceDelayMinutes AS AttendanceEvidenceDifferenceMinutes,
    CASE
        WHEN @LastZkAttendanceUtc IS NULL THEN N'ERROR: No ZK attendance is available in the selected window.'
        WHEN @LastDayoubAttendanceUtc IS NULL THEN N'ERROR: No Dayoub attendance is available in the selected window.'
        WHEN @AttendanceDelayMinutes > @DelayWarningMinutes THEN N'WARNING: Dayoub attendance evidence is behind the ZK source.'
        WHEN @AttendanceDelayMinutes < -@DelayWarningMinutes THEN N'WARNING: Dayoub contains later evidence than the staged ZK window; check source window or mode.'
        ELSE N'OK: ZK and Dayoub attendance evidence are within the expected diagnostic tolerance.'
    END AS DelayDiagnosis;

PRINT N'=====================================================';
PRINT N'Final Diagnosis';
PRINT N'=====================================================';

SELECT
    CASE
        WHEN @ZkUserCount = 0 THEN N'ERROR'
        WHEN @ZkUserCount > 1 THEN N'ERROR'
        WHEN @WorkerCount = 0 THEN N'ERROR'
        WHEN @WorkerCount > 1 THEN N'ERROR'
        WHEN @ZkIsCurrentWorker = 0 THEN N'ERROR'
        WHEN ISNULL(@WorkerIsActive, 0) = 0 OR ISNULL(@WorkerEmploymentStatus, 0) <> 1 THEN N'ERROR'
        WHEN @ZkPunchCount = 0 THEN N'ERROR'
        WHEN @AttendanceInboxFailed > 0 OR @WorkerInboxFailed > 0 THEN N'ERROR'
        WHEN @ProcessedOrphanCount > 0 THEN N'ERROR'
        WHEN @MissingAttendanceDayCount > 0 THEN N'ERROR'
        WHEN @DayoubAttendanceCount = 0 THEN N'ERROR'
        WHEN @AttendanceInboxPending > 0 OR @AttendanceInboxProcessing > 0 OR @WorkerInboxPending > 0 OR @WorkerInboxProcessing > 0 THEN N'WARNING'
        WHEN @AttendanceSyncStateCount = 0 OR @MissingAttendanceSyncStateDayCount > 0 THEN N'WARNING'
        WHEN @AttendanceSyncFailureCount > 0 OR @ZkFailedRunCount > 0 OR @LastZkRunStatus = 'Failed' THEN N'WARNING'
        WHEN @AttendanceDelayMinutes > @DelayWarningMinutes THEN N'WARNING'
        WHEN @ExpectedNotificationEventCount > 0 AND @AttendanceNotificationEventCount = 0 THEN N'WARNING'
        ELSE N'OK'
    END AS DiagnosticStatus,
    CASE
        WHEN @ZkUserCount = 0 THEN N'ZK User Missing'
        WHEN @ZkUserCount > 1 THEN N'ZK Badge Ambiguous'
        WHEN @WorkerCount = 0 THEN N'Worker Mapping Missing'
        WHEN @WorkerCount > 1 THEN N'Worker Mapping Ambiguous'
        WHEN @ZkIsCurrentWorker = 0 THEN N'Department Excluded'
        WHEN ISNULL(@WorkerIsActive, 0) = 0 OR ISNULL(@WorkerEmploymentStatus, 0) <> 1 THEN N'Worker Inactive'
        WHEN @ZkPunchCount = 0 THEN N'No ZK Attendance'
        WHEN @AttendanceInboxFailed > 0 OR @WorkerInboxFailed > 0 THEN N'Inbox Failed'
        WHEN @ProcessedOrphanCount > 0 THEN N'Processed Inbox Orphan'
        WHEN @MissingAttendanceDayCount > 0 THEN N'Attendance Missing'
        WHEN @DayoubAttendanceCount = 0 THEN N'No Dayoub Attendance'
        WHEN @AttendanceInboxPending > 0 OR @AttendanceInboxProcessing > 0 OR @WorkerInboxPending > 0 OR @WorkerInboxProcessing > 0 THEN N'Inbox Pending'
        WHEN @AttendanceSyncStateCount = 0 OR @MissingAttendanceSyncStateDayCount > 0 THEN N'AttendanceSyncState Missing'
        WHEN @AttendanceSyncFailureCount > 0 OR @ZkFailedRunCount > 0 OR @LastZkRunStatus = 'Failed' THEN N'Sync Failure'
        WHEN @AttendanceDelayMinutes > @DelayWarningMinutes THEN N'Processing Delayed'
        WHEN @ExpectedNotificationEventCount > 0 AND @AttendanceNotificationEventCount = 0 THEN N'Notifications Missing'
        ELSE N'OK'
    END AS DiagnosisCode,
    CASE
        WHEN @ZkUserCount = 0 THEN N'The badge was not discovered from ZK USERINFO into the worker staging inbox.'
        WHEN @ZkUserCount > 1 THEN N'The badge maps to more than one ZK USERID; identity resolution is ambiguous.'
        WHEN @WorkerCount = 0 THEN N'No Dayoub worker matches the staged USERID or badge.'
        WHEN @WorkerCount > 1 THEN N'More than one Dayoub worker matches the staged identity.'
        WHEN @ZkIsCurrentWorker = 0 THEN N'USERINFO.DEFAULTDEPTID is outside departments 1 and 4, so the staging contract classifies this person as a non-current worker.'
        WHEN ISNULL(@WorkerIsActive, 0) = 0 OR ISNULL(@WorkerEmploymentStatus, 0) <> 1 THEN N'The mapped Dayoub worker is inactive, suspended, or has left employment.'
        WHEN @ZkPunchCount = 0 THEN N'No staged ZK CHECKINOUT movement exists for the selected badge and window.'
        WHEN @AttendanceInboxFailed > 0 OR @WorkerInboxFailed > 0 THEN N'Worker or attendance inbox rows failed; inspect LastError and ResolutionCode.'
        WHEN @ProcessedOrphanCount > 0 THEN N'One or more attendance inbox rows are Processed without exact persisted AttendanceRecord evidence.'
        WHEN @MissingAttendanceDayCount > 0 THEN N'At least one operational day has a ZK check-in but no matching Dayoub attendance summary.'
        WHEN @DayoubAttendanceCount = 0 THEN N'ZK movements exist but no Dayoub AttendanceRecord exists in the selected window.'
        WHEN @AttendanceInboxPending > 0 OR @AttendanceInboxProcessing > 0 OR @WorkerInboxPending > 0 OR @WorkerInboxProcessing > 0 THEN N'Inbox work has not completed; inspect lease age, retry count, and processor state.'
        WHEN @AttendanceSyncStateCount = 0 THEN N'No AttendanceSyncState evidence exists for the selected operational-date window.'
        WHEN @MissingAttendanceSyncStateDayCount > 0 THEN N'At least one ZK check-in operational day has no matching AttendanceSyncState evidence.'
        WHEN @AttendanceSyncFailureCount > 0 OR @ZkFailedRunCount > 0 OR @LastZkRunStatus = 'Failed' THEN N'One or more source collection or backend attendance synchronization runs failed.'
        WHEN @AttendanceDelayMinutes > @DelayWarningMinutes THEN N'The latest ZK attendance evidence is ahead of the latest Dayoub attendance evidence beyond the diagnostic tolerance.'
        WHEN @ExpectedNotificationEventCount > 0 AND @AttendanceNotificationEventCount = 0 THEN N'Attendance exists but no attendance notification outbox event was found in the selected window.'
        ELSE N'The staged source identity, source punches, inbox processing, Dayoub attendance, and synchronization evidence are consistent.'
    END AS Reason,
    @BadgeNumber AS BadgeNumber,
    @ZkUserId AS ZkUserId,
    @ZkDefaultDepartmentId AS ZkDefaultDepartmentId,
    @ZkIsCurrentWorker AS ZkIsCurrentWorker,
    @WorkerId AS WorkerId,
    @WorkerAttendanceUserId AS WorkerAttendanceUserId,
    @MissingAttendanceDayCount AS MissingAttendanceDayCount,
    @ProcessedOrphanCount AS ProcessedOrphanCount;

PRINT N'Final diagnostic checks';

SELECT CheckOrder, DiagnosticCode, DiagnosticStatus, Reason
FROM
(
    SELECT 10 AS CheckOrder, N'Worker Mapping' AS DiagnosticCode,
        CASE WHEN @WorkerCount = 1 THEN N'OK' ELSE N'ERROR' END AS DiagnosticStatus,
        CASE WHEN @WorkerCount = 0 THEN N'Worker Mapping Missing' WHEN @WorkerCount > 1 THEN N'Worker Mapping Ambiguous' ELSE N'Exactly one Dayoub worker is mapped.' END AS Reason
    UNION ALL SELECT 20, N'Department Classification', CASE WHEN @ZkUserCount = 0 THEN N'ERROR' WHEN @ZkIsCurrentWorker = 0 THEN N'ERROR' ELSE N'OK' END,
        CASE WHEN @ZkUserCount = 0 THEN N'ZK USERINFO mapping is missing.' WHEN @ZkIsCurrentWorker = 0 THEN N'Department Excluded' ELSE N'ZK department classifies the person as a current worker.' END
    UNION ALL SELECT 30, N'Worker Employment', CASE WHEN @WorkerCount <> 1 THEN N'ERROR' WHEN @WorkerIsActive = 1 AND @WorkerEmploymentStatus = 1 THEN N'OK' ELSE N'ERROR' END,
        CASE WHEN @WorkerCount <> 1 THEN N'Cannot evaluate employment without a unique worker.' WHEN @WorkerIsActive = 1 AND @WorkerEmploymentStatus = 1 THEN N'Worker is active.' ELSE N'Worker Inactive' END
    UNION ALL SELECT 40, N'ZK Attendance', CASE WHEN @ZkPunchCount > 0 THEN N'OK' ELSE N'ERROR' END,
        CASE WHEN @ZkPunchCount > 0 THEN N'ZK attendance movements were found.' ELSE N'No ZK Attendance' END
    UNION ALL SELECT 50, N'Attendance Inbox', CASE WHEN @AttendanceInboxFailed > 0 THEN N'ERROR' WHEN @AttendanceInboxPending > 0 OR @AttendanceInboxProcessing > 0 THEN N'WARNING' ELSE N'OK' END,
        CASE WHEN @AttendanceInboxFailed > 0 THEN N'Inbox Failed' WHEN @AttendanceInboxPending > 0 OR @AttendanceInboxProcessing > 0 THEN N'Inbox Pending' ELSE N'Attendance inbox has no unresolved work in the selected window.' END
    UNION ALL SELECT 60, N'Worker Inbox', CASE WHEN @WorkerInboxFailed > 0 THEN N'ERROR' WHEN @WorkerInboxPending > 0 OR @WorkerInboxProcessing > 0 THEN N'WARNING' ELSE N'OK' END,
        CASE WHEN @WorkerInboxFailed > 0 THEN N'Worker Inbox Failed' WHEN @WorkerInboxPending > 0 OR @WorkerInboxProcessing > 0 THEN N'Worker Inbox Pending' ELSE N'Worker inbox has no unresolved work for this identity.' END
    UNION ALL SELECT 65, N'Processed Attendance Orphans', CASE WHEN @ProcessedOrphanCount > 0 THEN N'ERROR' ELSE N'OK' END,
        CASE WHEN @ProcessedOrphanCount > 0 THEN N'ERROR — Processed Inbox Orphan' ELSE N'No Processed inbox row lacks exact Dayoub attendance evidence.' END
    UNION ALL SELECT 70, N'Dayoub Attendance', CASE WHEN @ProcessedOrphanCount > 0 OR @MissingAttendanceDayCount > 0 OR (@ZkPunchCount > 0 AND @DayoubAttendanceCount = 0) THEN N'ERROR' ELSE N'OK' END,
        CASE WHEN @ProcessedOrphanCount > 0 THEN N'ERROR — Processed Inbox Orphan' WHEN @MissingAttendanceDayCount > 0 THEN N'Attendance Missing' WHEN @ZkPunchCount > 0 AND @DayoubAttendanceCount = 0 THEN N'No Dayoub Attendance' ELSE N'Dayoub attendance summaries cover the staged ZK check-in days.' END
    UNION ALL SELECT 80, N'AttendanceSyncState', CASE WHEN @AttendanceSyncStateCount = 0 OR @MissingAttendanceSyncStateDayCount > 0 THEN N'WARNING' WHEN @AttendanceSyncFailureCount > 0 THEN N'ERROR' ELSE N'OK' END,
        CASE WHEN @AttendanceSyncStateCount = 0 OR @MissingAttendanceSyncStateDayCount > 0 THEN N'AttendanceSyncState Missing' WHEN @AttendanceSyncFailureCount > 0 THEN N'Attendance synchronization failure evidence exists.' ELSE N'Attendance synchronization state exists without failures in the selected window.' END
    UNION ALL SELECT 90, N'ZK Sync State', CASE WHEN @LastZkRunStatus IS NULL THEN N'WARNING' WHEN @LastZkRunStatus = 'Failed' OR @ZkFailedRunCount > 0 THEN N'ERROR' ELSE N'OK' END,
        CASE WHEN @LastZkRunStatus IS NULL THEN N'No ZK staging run evidence exists.' WHEN @LastZkRunStatus = 'Failed' OR @ZkFailedRunCount > 0 THEN N'ZK staging run failure evidence exists.' ELSE N'The latest ZK staging run completed without a recorded failure.' END
    UNION ALL SELECT 100, N'Processing Delay', CASE WHEN @LastZkAttendanceUtc IS NULL OR @LastDayoubAttendanceUtc IS NULL THEN N'ERROR' WHEN @AttendanceDelayMinutes > @DelayWarningMinutes THEN N'WARNING' ELSE N'OK' END,
        CASE WHEN @LastZkAttendanceUtc IS NULL THEN N'No ZK Attendance' WHEN @LastDayoubAttendanceUtc IS NULL THEN N'No Dayoub Attendance' WHEN @AttendanceDelayMinutes > @DelayWarningMinutes THEN N'Processing Delayed' ELSE N'Attendance evidence is within the diagnostic delay tolerance.' END
    UNION ALL SELECT 110, N'Attendance Notifications', CASE WHEN @AttendancePolicyCount < 2 THEN N'WARNING' WHEN @ExpectedNotificationEventCount > 0 AND @AttendanceNotificationEventCount = 0 THEN N'WARNING' WHEN @AttendanceNotificationEventCount > 0 AND @PublishedNotificationCount = 0 AND @EnabledAttendancePolicyCount > 0 THEN N'WARNING' ELSE N'OK' END,
        CASE WHEN @ExpectedNotificationEventCount > 0 AND @AttendanceNotificationEventCount = 0 THEN N'Notifications Missing' WHEN @AttendanceNotificationEventCount > 0 AND @PublishedNotificationCount = 0 AND @EnabledAttendancePolicyCount > 0 THEN N'Outbox exists but no published notifications were found.' WHEN @AttendancePolicyCount < 2 THEN N'One or more attendance notification policies are missing.' ELSE N'Attendance notification evidence is consistent with the configured policies.' END
) AS Checks
ORDER BY CheckOrder;
