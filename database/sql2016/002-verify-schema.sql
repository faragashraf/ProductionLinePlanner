/*
  ProductionLinePlanner SQL Server 2016 schema verification.
  Read-only: this script does not alter application data or schema.
*/
SET NOCOUNT ON;

PRINT N'ProductionLinePlanner SQL Server 2016 schema verification';
SELECT
    CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(100)) AS ProductVersion,
    CAST(SERVERPROPERTY('ProductLevel') AS nvarchar(100)) AS ProductLevel,
    CAST(SERVERPROPERTY('Edition') AS nvarchar(200)) AS Edition,
    DB_NAME() AS DatabaseName,
    SUSER_SNAME() AS LoginName,
    d.compatibility_level AS CompatibilityLevel
FROM sys.databases AS d
WHERE d.name = DB_NAME();

DECLARE @ExpectedApplicationTableCount int = 27;
DECLARE @ExpectedForeignKeyCount int = 35;
DECLARE @ExpectedMigrationCount int = 17;

DECLARE @ExpectedTables table (TableName sysname NOT NULL PRIMARY KEY);
INSERT INTO @ExpectedTables (TableName) VALUES
    (N'AppRoles'), (N'AppUsers'), (N'AssignmentTimelineEntries'),
    (N'AttendanceRecords'), (N'AuditLogs'), (N'Factories'),
    (N'ImportBatches'), (N'MainStages'), (N'Notifications'),
    (N'Permissions'), (N'ProductModels'), (N'ProductModelStages'),
    (N'ProductionDayStageResolutions'), (N'ProductionLines'),
    (N'ProductionOrders'), (N'RefreshTokens'), (N'RolePermissions'),
    (N'StageProductionRecords'), (N'StageProductionWorkerAllocations'),
    (N'StageReadinessSnapshots'), (N'SubStages'),
    (N'UserPermissionOverrides'), (N'UserRoles'), (N'WorkerDefaultAssignments'),
    (N'WorkerSalaryHistories'), (N'WorkerTemporaryAssignments'), (N'Workers');

PRINT N'Expected application tables missing from dbo:';
SELECT e.TableName AS MissingTable
FROM @ExpectedTables AS e
LEFT JOIN sys.tables AS t ON t.name = e.TableName AND t.schema_id = SCHEMA_ID(N'dbo')
WHERE t.object_id IS NULL
ORDER BY e.TableName;

SELECT
    @ExpectedApplicationTableCount AS ExpectedApplicationTableCount,
    COUNT(*) AS ActualApplicationTableCount,
    CASE WHEN COUNT(*) = @ExpectedApplicationTableCount THEN N'PASS' ELSE N'FAIL' END AS Result
FROM sys.tables AS t
WHERE t.is_ms_shipped = 0 AND t.name <> N'__EFMigrationsHistory';

DECLARE @ExpectedMigrations table (MigrationId nvarchar(150) NOT NULL PRIMARY KEY);
INSERT INTO @ExpectedMigrations (MigrationId) VALUES
    (N'20260709103703_InitialCreate'),
    (N'20260709115325_AddRefreshTokens'),
    (N'20260709123947_AddAssignmentTimeline'),
    (N'20260712213444_IamPermissionsFoundation'),
    (N'20260712230006_EnableCustomRoles'),
    (N'20260713021838_AddManufacturingMasterDataFoundation'),
    (N'20260713024755_EnforceUniqueCurrentWorkerSalary'),
    (N'20260713125851_AddProductionCostRecordingV1'),
    (N'20260713154002_StrengthenProductionCostRecordingV1'),
    (N'20260714185040_AddProductionRecordingHistoricalSnapshots'),
    (N'20260714204000_AddProductionParticipantOverrideReason'),
    (N'20260714204438_SupportConcurrentTemporaryAssignmentValidation'),
    (N'20260714204459_AddTemporaryAssignmentOverlapLookupIndex'),
    (N'20260715000000_AddPilotManualProductionReadiness'),
    (N'20260715012831_AddControlledRealDataIntake'),
    (N'20260715193031_AddProductionApprovalCancellationReason'),
    (N'20260716015121_AllowWorkerMultiStageParticipation');

IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
BEGIN
    SELECT N'__EFMigrationsHistory is missing.' AS VerificationFailure;
END
ELSE
BEGIN
    SELECT
        @ExpectedMigrationCount AS ExpectedMigrationCount,
        COUNT(*) AS ActualMigrationCount,
        CASE WHEN COUNT(*) = @ExpectedMigrationCount THEN N'PASS' ELSE N'FAIL' END AS Result
    FROM dbo.__EFMigrationsHistory;

    PRINT N'Expected migrations missing from history:';
    SELECT e.MigrationId AS MissingMigration
    FROM @ExpectedMigrations AS e
    LEFT JOIN dbo.__EFMigrationsHistory AS h ON h.MigrationId = e.MigrationId
    WHERE h.MigrationId IS NULL
    ORDER BY e.MigrationId;

    PRINT N'Unexpected migrations in history:';
    SELECT h.MigrationId AS UnexpectedMigration
    FROM dbo.__EFMigrationsHistory AS h
    LEFT JOIN @ExpectedMigrations AS e ON e.MigrationId = h.MigrationId
    WHERE e.MigrationId IS NULL
    ORDER BY h.MigrationId;
END;

SELECT
    @ExpectedForeignKeyCount AS ExpectedForeignKeyCount,
    COUNT(*) AS ActualForeignKeyCount,
    CASE WHEN COUNT(*) = @ExpectedForeignKeyCount THEN N'PASS' ELSE N'FAIL' END AS Result
FROM sys.foreign_keys
WHERE is_ms_shipped = 0;

DECLARE @ExpectedIndexes table (IndexName sysname NOT NULL, TableName sysname NOT NULL, PRIMARY KEY (IndexName, TableName));
INSERT INTO @ExpectedIndexes (IndexName, TableName) VALUES
    (N'IX_AppRoles_Name', N'AppRoles'),
    (N'IX_AppRoles_Role', N'AppRoles'),
    (N'IX_AppUsers_Email', N'AppUsers'),
    (N'IX_Factories_Code', N'Factories'),
    (N'IX_ImportBatches_IdempotencyKey', N'ImportBatches'),
    (N'IX_ProductModels_Code', N'ProductModels'),
    (N'IX_ProductModelStages_ProductModelId_StageOrder', N'ProductModelStages'),
    (N'IX_ProductModelStages_ProductModelId_SubStageId', N'ProductModelStages'),
    (N'IX_ProductionDayStageResolutions_ProductionOrderId_ProductModelStageId', N'ProductionDayStageResolutions'),
    (N'IX_ProductionLines_FactoryId_LineCode', N'ProductionLines'),
    (N'IX_ProductionOrders_OrderNumber', N'ProductionOrders'),
    (N'IX_ProductionOrders_ProductionDate_ProductionLineId_ProductModelId', N'ProductionOrders'),
    (N'IX_RefreshTokens_TokenHash', N'RefreshTokens'),
    (N'IX_StageProductionRecords_ProductionOrderId_ClientRequestId', N'StageProductionRecords'),
    (N'IX_StageProductionWorkerAllocations_StageProductionRecordId_WorkerId', N'StageProductionWorkerAllocations'),
    (N'IX_SubStages_Code', N'SubStages'),
    (N'IX_SubStages_MainStageId_SequenceOrder', N'SubStages'),
    (N'IX_WorkerDefaultAssignments_WorkerId_SubStageId', N'WorkerDefaultAssignments'),
    (N'IX_WorkerSalaryHistories_Current', N'WorkerSalaryHistories'),
    (N'IX_Workers_EmployeeCode', N'Workers'),
    (N'IX_WorkerTemporaryAssignments_WorkerId_Status_StartAtUtc_EndAtUtc', N'WorkerTemporaryAssignments');

PRINT N'Expected integrity and lookup indexes missing:';
SELECT e.TableName, e.IndexName AS MissingIndex
FROM @ExpectedIndexes AS e
LEFT JOIN sys.tables AS t ON t.name = e.TableName AND t.schema_id = SCHEMA_ID(N'dbo')
LEFT JOIN sys.indexes AS i ON i.object_id = t.object_id AND i.name = e.IndexName
WHERE i.index_id IS NULL
ORDER BY e.TableName, e.IndexName;

DECLARE @ExpectedColumns table
(
    TableName sysname NOT NULL,
    ColumnName sysname NOT NULL,
    TypeName sysname NOT NULL,
    NumericPrecision tinyint NULL,
    NumericScale int NULL,
    PRIMARY KEY (TableName, ColumnName)
);
INSERT INTO @ExpectedColumns (TableName, ColumnName, TypeName, NumericPrecision, NumericScale) VALUES
    (N'ProductionOrders', N'ProductionDate', N'date', NULL, NULL),
    (N'ProductionOrders', N'PlannedQuantity', N'decimal', 18, 3),
    (N'ProductModelStages', N'PiecePrice', N'decimal', 18, 2),
    (N'ProductModelStages', N'StandardSeconds', N'decimal', 18, 4),
    (N'StageProductionRecords', N'ProductionDate', N'date', NULL, NULL),
    (N'StageProductionRecords', N'ProducedQuantity', N'decimal', 18, 3),
    (N'StageProductionRecords', N'AcceptedQuantity', N'decimal', 18, 3),
    (N'StageProductionRecords', N'RejectedQuantity', N'decimal', 18, 3),
    (N'StageProductionRecords', N'SnapshotPiecePrice', N'decimal', 18, 4),
    (N'StageProductionRecords', N'TotalWorkerEarnings', N'decimal', 18, 4),
    (N'StageProductionWorkerAllocations', N'InputQuantity', N'decimal', 18, 3),
    (N'StageProductionWorkerAllocations', N'Percentage', N'decimal', 9, 4),
    (N'StageProductionWorkerAllocations', N'CalculatedEarning', N'decimal', 18, 4),
    (N'StageReadinessSnapshots', N'ReadinessPercent', N'decimal', 5, 2);

PRINT N'Critical column type/precision mismatches:';
SELECT
    e.TableName,
    e.ColumnName,
    e.TypeName AS ExpectedType,
    e.NumericPrecision AS ExpectedPrecision,
    e.NumericScale AS ExpectedScale,
    ty.name AS ActualType,
    c.precision AS ActualPrecision,
    c.scale AS ActualScale
FROM @ExpectedColumns AS e
LEFT JOIN sys.tables AS t ON t.name = e.TableName AND t.schema_id = SCHEMA_ID(N'dbo')
LEFT JOIN sys.columns AS c ON c.object_id = t.object_id AND c.name = e.ColumnName
LEFT JOIN sys.types AS ty ON ty.user_type_id = c.user_type_id
WHERE c.column_id IS NULL
   OR ty.name <> e.TypeName
   OR (e.NumericPrecision IS NOT NULL AND c.precision <> e.NumericPrecision)
   OR (e.NumericScale IS NOT NULL AND c.scale <> e.NumericScale)
ORDER BY e.TableName, e.ColumnName;

PRINT N'Orphan checks for major production and staffing relationships (all counts must be zero):';
SELECT N'ProductionLines -> Factories' AS CheckName, COUNT_BIG(*) AS OrphanCount
FROM dbo.ProductionLines AS c LEFT JOIN dbo.Factories AS p ON p.Id = c.FactoryId WHERE p.Id IS NULL
UNION ALL SELECT N'MainStages -> ProductionLines', COUNT_BIG(*)
FROM dbo.MainStages AS c LEFT JOIN dbo.ProductionLines AS p ON p.Id = c.ProductionLineId WHERE p.Id IS NULL
UNION ALL SELECT N'SubStages -> MainStages', COUNT_BIG(*)
FROM dbo.SubStages AS c LEFT JOIN dbo.MainStages AS p ON p.Id = c.MainStageId WHERE p.Id IS NULL
UNION ALL SELECT N'ProductModelStages -> ProductModels', COUNT_BIG(*)
FROM dbo.ProductModelStages AS c LEFT JOIN dbo.ProductModels AS p ON p.Id = c.ProductModelId WHERE p.Id IS NULL
UNION ALL SELECT N'ProductModelStages -> SubStages', COUNT_BIG(*)
FROM dbo.ProductModelStages AS c LEFT JOIN dbo.SubStages AS p ON p.Id = c.SubStageId WHERE p.Id IS NULL
UNION ALL SELECT N'WorkerDefaultAssignments -> Workers', COUNT_BIG(*)
FROM dbo.WorkerDefaultAssignments AS c LEFT JOIN dbo.Workers AS p ON p.Id = c.WorkerId WHERE p.Id IS NULL
UNION ALL SELECT N'WorkerDefaultAssignments -> SubStages', COUNT_BIG(*)
FROM dbo.WorkerDefaultAssignments AS c LEFT JOIN dbo.SubStages AS p ON p.Id = c.SubStageId WHERE p.Id IS NULL
UNION ALL SELECT N'WorkerTemporaryAssignments -> Workers', COUNT_BIG(*)
FROM dbo.WorkerTemporaryAssignments AS c LEFT JOIN dbo.Workers AS p ON p.Id = c.WorkerId WHERE p.Id IS NULL
UNION ALL SELECT N'AttendanceRecords -> Workers', COUNT_BIG(*)
FROM dbo.AttendanceRecords AS c LEFT JOIN dbo.Workers AS p ON p.Id = c.WorkerId WHERE p.Id IS NULL
UNION ALL SELECT N'ProductionOrders -> ProductModels', COUNT_BIG(*)
FROM dbo.ProductionOrders AS c LEFT JOIN dbo.ProductModels AS p ON p.Id = c.ProductModelId WHERE p.Id IS NULL
UNION ALL SELECT N'ProductionOrders -> ProductionLines when set', COUNT_BIG(*)
FROM dbo.ProductionOrders AS c LEFT JOIN dbo.ProductionLines AS p ON p.Id = c.ProductionLineId WHERE c.ProductionLineId IS NOT NULL AND p.Id IS NULL
UNION ALL SELECT N'StageProductionRecords -> ProductionOrders', COUNT_BIG(*)
FROM dbo.StageProductionRecords AS c LEFT JOIN dbo.ProductionOrders AS p ON p.Id = c.ProductionOrderId WHERE p.Id IS NULL
UNION ALL SELECT N'StageProductionRecords -> ProductModelStages', COUNT_BIG(*)
FROM dbo.StageProductionRecords AS c LEFT JOIN dbo.ProductModelStages AS p ON p.Id = c.ProductModelStageId WHERE p.Id IS NULL
UNION ALL SELECT N'StageProductionWorkerAllocations -> StageProductionRecords', COUNT_BIG(*)
FROM dbo.StageProductionWorkerAllocations AS c LEFT JOIN dbo.StageProductionRecords AS p ON p.Id = c.StageProductionRecordId WHERE p.Id IS NULL
UNION ALL SELECT N'StageProductionWorkerAllocations -> Workers', COUNT_BIG(*)
FROM dbo.StageProductionWorkerAllocations AS c LEFT JOIN dbo.Workers AS p ON p.Id = c.WorkerId WHERE p.Id IS NULL
UNION ALL SELECT N'ProductionDayStageResolutions -> ProductionOrders', COUNT_BIG(*)
FROM dbo.ProductionDayStageResolutions AS c LEFT JOIN dbo.ProductionOrders AS p ON p.Id = c.ProductionOrderId WHERE p.Id IS NULL
UNION ALL SELECT N'ProductionDayStageResolutions -> ProductModelStages', COUNT_BIG(*)
FROM dbo.ProductionDayStageResolutions AS c LEFT JOIN dbo.ProductModelStages AS p ON p.Id = c.ProductModelStageId WHERE p.Id IS NULL
UNION ALL SELECT N'ProductionOrders -> ImportBatches when set', COUNT_BIG(*)
FROM dbo.ProductionOrders AS c LEFT JOIN dbo.ImportBatches AS p ON p.Id = c.SourceImportBatchId WHERE c.SourceImportBatchId IS NOT NULL AND p.Id IS NULL;

PRINT N'Operational data counts. A Phase 1 schema-only initialization should report zero in every row:';
SELECT N'AttendanceRecords' AS TableName, COUNT_BIG(*) AS [RowCount] FROM dbo.AttendanceRecords
UNION ALL SELECT N'ProductionOrders', COUNT_BIG(*) FROM dbo.ProductionOrders
UNION ALL SELECT N'StageProductionRecords', COUNT_BIG(*) FROM dbo.StageProductionRecords
UNION ALL SELECT N'StageProductionWorkerAllocations', COUNT_BIG(*) FROM dbo.StageProductionWorkerAllocations
UNION ALL SELECT N'ProductionDayStageResolutions', COUNT_BIG(*) FROM dbo.ProductionDayStageResolutions
UNION ALL SELECT N'ImportBatches', COUNT_BIG(*) FROM dbo.ImportBatches
ORDER BY TableName;
