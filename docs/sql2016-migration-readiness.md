# SQL Server 2016 migration readiness

## Scope and design-time ownership

- `AppDbContext` is in `ProductionLinePlanner.Infrastructure`; it registers 21 DbSets and applies entity configurations from the Infrastructure assembly.
- The API project owns `UserSecretsId` (`ced688bb-0592-47a4-90af-b8606083e1db`) and the design-time `AppDbContextFactory` loads `ConnectionStrings:AppDatabase` only for EF tooling.
- Runtime registration uses `UseSqlServer` from Infrastructure dependency injection. Application startup contains no production `Database.Migrate`, `MigrateAsync`, or `EnsureCreated` call.
- The migration package was generated with EF Core CLI 9.0.0 using `AppDbContext`, Infrastructure as project, and API as startup project.

## Final model characteristics

- 27 application tables, 35 foreign keys, and 17 migrations; all application primary keys are GUIDs/composite GUID keys, not SQL `IDENTITY` keys.
- `DateOnly` maps to SQL Server `date` for `ProductionOrders.ProductionDate` and `StageProductionRecords.ProductionDate`; timestamps use `datetime2`.
- Important decimal mappings include `ProductModelStages.PiecePrice decimal(18,2)`, production quantities `decimal(18,3)`, worker allocations/earnings `decimal(18,4)` (percentage `decimal(9,4)`), and readiness percent `decimal(5,2)`.
- The model uses check constraints and SQL Server filtered unique indexes. It has no `HasData` seed data, default SQL, computed columns, temporal tables, sequences, or provider online/resumable index options.

## Ordered migration inventory

| Migration ID | Schema changes / integrity | Data or manual SQL | SQL Server 2016 assessment |
|---|---|---|---|
| `20260709103703_InitialCreate` | Base IAM, factory/line/stage, worker, attendance, notification, readiness, and staffing tables; base FKs, checks, unique and filtered indexes. | None. | Compatible: `uniqueidentifier`, `datetime2`, checks and filtered indexes are supported. |
| `20260709115325_AddRefreshTokens` | `RefreshTokens`, FK to `AppUsers`, token indexes. | None. | Compatible. |
| `20260709123947_AddAssignmentTimeline` | `AssignmentTimelineEntries`, four FKs and assignment lookup indexes. | None. | Compatible. |
| `20260712213444_IamPermissionsFoundation` | Adds `AppRoles.IsActive`; creates `Permissions`, `RolePermissions`, `UserPermissionOverrides` and their FKs/indexes. | None. | Compatible. |
| `20260712230006_EnableCustomRoles` | Makes legacy role nullable; replaces role uniqueness with role/name unique indexes. | Guards duplicate role names using `IF EXISTS` and `THROW`; updates system-role flag. | Compatible: `THROW` is SQL Server 2012+. |
| `20260713021838_AddManufacturingMasterDataFoundation` | Adds worker master fields; adds `SubStages.Code`; creates `ProductModels`, `WorkerSalaryHistories`, `ProductModelStages`, checks/FKs/unique indexes. | CTE remediation for stage codes/orders using `CONCAT`, `ROW_NUMBER`, `THROW`. | Compatible: all constructs are supported by SQL Server 2016. |
| `20260713024755_EnforceUniqueCurrentWorkerSalary` | Recreates filtered current-salary unique index. | None. | Compatible. |
| `20260713125851_AddProductionCostRecordingV1` | Creates `ProductionOrders`, `StageProductionRecords`, `StageProductionWorkerAllocations`, FKs and production indexes. | None. | Compatible; `date` and decimal precision are supported. |
| `20260713154002_StrengthenProductionCostRecordingV1` | Adds request/concurrency/snapshot columns and filtered idempotency index. | `NEWID()` snapshot backfill joins. | Compatible. |
| `20260714185040_AddProductionRecordingHistoricalSnapshots` | Adds worker/factory/line/main-stage snapshot columns. | `UPDATE ... FROM` backfills. | Compatible. |
| `20260714204000_AddProductionParticipantOverrideReason` | Adds allocation override reason. | None. | Compatible. |
| `20260714204438_SupportConcurrentTemporaryAssignmentValidation` | Empty marker migration. | None. | Compatible. |
| `20260714204459_AddTemporaryAssignmentOverlapLookupIndex` | Adds temporary-assignment overlap lookup index. | None. | Compatible. |
| `20260715000000_AddPilotManualProductionReadiness` | Adds worker department field, production provenance/approval fields, input quantity, and filtered line/day/model uniqueness. | Timestamp backfill plus duplicate guard using `IF EXISTS`/`THROW`. | Compatible. |
| `20260715012831_AddControlledRealDataIntake` | Creates `ImportBatches`, `ProductionDayStageResolutions`; adds import FK and lookup/uniqueness indexes. | None. | Compatible. |
| `20260715193031_AddProductionApprovalCancellationReason` | Adds approval-cancellation reason. | None. | Compatible. |
| `20260716015121_AllowWorkerMultiStageParticipation` | Makes temporary origin nullable; adds `ParticipationMode`; replaces default-assignment filtered unique index. | Default `TemporaryMove` for existing rows. | Compatible. |

## SQL Server 2016 compatibility scan

The generated `database/sql2016/001-create-schema.sql` is idempotent, contains no `CREATE DATABASE`, and creates/updates `__EFMigrationsHistory` in migration order. Static scanning found **no** use of:

- `CREATE OR ALTER`, `DROP ... IF EXISTS`, `DATETRUNC`, `STRING_AGG`, `JSON_ARRAY`, `JSON_OBJECT`, `GREATEST`, `LEAST`;
- `OPTIMIZE_FOR_SEQUENTIAL_KEY`, online/resumable index options, temporal system-versioning, or newer JSON/sequence syntax;
- computed columns or default SQL expressions that depend on a newer server version.

The package does use idempotent guards (`OBJECT_ID`, `IF NOT EXISTS`), `THROW`, CTEs, `ROW_NUMBER`, `UPDATE ... FROM`, `NEWID`, and SQL Server filtered indexes. All are supported by SQL Server 2016. A target probe must still confirm product major version `13.x`, permissions, and compatibility level before application.

## Compatibility risks that remain operational, not syntactic

1. Migrations include historical-data remediation guards. They are harmless on an empty target but correctly fail on a partially initialized database with inconsistent data.
2. The EF Core SQL Server provider is current while the target is older. Static SQL is compatible, but the real target probe and post-apply verification remain mandatory.
3. The idempotent script is rerunnable only when the target has the matching EF migration history. It must not be used to repair unknown/manual schema drift.
