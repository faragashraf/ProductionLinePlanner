# Phase 2 production-data bootstrap manifest — 2026-07-11

This is a read-only manifest. No data, photos, backups, JSON exports, or credentials are included. Source counts were collected from the existing application database on 2026-07-16. Timestamp predicates use the required half-open range `>= 2026-07-11T00:00:00` and `< 2026-07-12T00:00:00`; `date` columns use the equivalent half-open date range.

## Transfer order and source inventory

| Order | Entity/table | Purpose and source rows | Key / destination handling | Required | Validation / scope |
|---:|---|---|---|---|---|
| 1 | `Factories` | Global factory reference: **1** row. | GUID, preserve IDs. | Yes | Code unique; global reference. |
| 2 | `ProductionLines` | Global line reference: **1** row. | GUID, preserve IDs; depends on Factory. | Yes | Factory FK and line code index; global reference. |
| 3 | `MainStages` | Global hierarchy: **1** row. | GUID, preserve IDs; depends on line. | Yes | Line FK and sequence unique; global reference. |
| 4 | `SubStages` | Global hierarchy: **67** rows. | GUID, preserve IDs; depends on main stage. | Yes | Code and sequence uniqueness; global reference. |
| 5 | `ProductModels` | Product/model reference: **1** row. | GUID, preserve IDs. | Yes | Code unique; global reference. |
| 6 | `ProductModelStages` | Model-to-stage and cost settings: **67** rows. | GUID, preserve IDs; depends on model/substage. | Yes | Mapping/order uniqueness, piece price and standard seconds precision; global reference. |
| 7 | `Workers` | Exact union of workers referenced by active default staffing or 2026-07-11 attendance/allocation: **282** distinct workers (source table total 2,091). | GUID, preserve IDs. Do not transfer `PhotoReference` payloads or source photos. | Yes | Worker code unique; global/reference subset. |
| 8 | Worker department metadata | No standalone application `Departments` table exists. `Workers.AttendanceDepartmentId`/`LocalDepartmentName` contain required department metadata; 2,091 source workers have department metadata. | Columns travel only with selected Worker rows. | Yes | Do not create a new department table; global metadata. |
| 9 | `WorkerSalaryHistories` | Salary/entitlement history for required workers: **209** rows (source total 213). | GUID, preserve IDs; depends on Worker. | Optional unless Phase 2 needs entitlement history. | Current-salary filtered unique index; global/reference subset. |
| 10 | `WorkerDefaultAssignments` | Permanent/home participation: **75** active rows for required workers (source total 90). | GUID, preserve IDs; depends on Worker/SubStage. | Yes | Preserve `IsActive`; filtered unique participation rule; global staffing state. |
| 11 | `WorkerTemporaryAssignments` | Temporary assignment and `ParticipationMode`: **0** rows. | GUID, preserve IDs if rows appear during final export. | Optional | Depends on Worker/SubStages; global/date-overlap state. |
| 12 | Daily staffing state | No separate daily-staffing table is persisted; state is derived from default/temporary assignments and their effective dates. | No direct insert. | Yes, derived | Recompute from orders 10–11 at import validation. |
| 13 | `AttendanceRecords` | Attendance application records on 2026-07-11: **282** rows (source total 3,435). | GUID, preserve IDs; depends on Worker. | Yes | Half-open `AttendanceTimeUtc` range; date-specific. No attendance-source raw data/photo import. |
| 14 | `ProductionOrders` | Production plan/record headers on 2026-07-11: **1** row (source total 2). | GUID, preserve IDs; depends on ProductModel/ProductionLine. | Yes | `ProductionDate` half-open range; date-specific. |
| 15 | `StageProductionRecords` | Stage production records on 2026-07-11: **66** rows (source total 67). | GUID, preserve IDs; depends on order/model-stage. | Yes | Snapshot/quantity/approval fields retained; date-specific. |
| 16 | `StageProductionWorkerAllocations` | Worker allocations for date records: **75** rows (source total 76). | GUID, preserve IDs; depends on stage record/Worker. | Yes | Unique record/worker mapping; date-specific. |
| 17 | Approval state | 2026-07-11 orders with approval: **0**; stage records with cancellation reason: **0**. | Existing approval/cancellation columns in rows 14–15. | Conditional | Do not fabricate approval records; date-specific. |
| 18 | `ProductionDayStageResolutions` | Persisted stage-resolution/override rows for date: **0** (source total 0). | GUID, preserve IDs if present. | Optional | Depends on order/model-stage; date-specific. |
| 19 | `ImportBatches` | Import provenance rows linked to date orders: **0** (source total 0). | GUID, preserve IDs if a selected order references one. | Conditional | Required only to satisfy `SourceImportBatchId`; do not create artificial batches. |
| 20 | Persisted previews/drafts | No standalone preview/draft table exists. Preview is transient; draft/approval status is stored on ProductionOrder/StageProductionRecord. | No direct insert. | Derived | Validate via rows 14–17. |
| 21 | `AssignmentTimelineEntries` | Historical assignment audit for required workers: **104** rows. | GUID, preserve IDs only if audit history is explicitly in Phase 2 scope. | Optional | Requires referenced `AppUsers`; not needed for current production-row FK integrity. |
| 22 | `AuditLogs` | Source total **260** rows. | Do not transfer by default. | Optional | Include only after a separate audit-retention decision; not required by production FKs. |

## Explicit exclusions

- Worker photos and attendance-source photo/blob payloads.
- Credentials, connection strings with passwords, backups, and raw production/attendance exports.
- Authentication users, refresh tokens, roles, permissions, and user overrides unless a later authorized scope requires historical-assignment auditing. Production creation/approval GUID fields do not have FK constraints to AppUsers.
- Any production data outside the selected 2026-07-11 half-open date range.

## Phase 2 import validation order

1. Verify target schema with `database/sql2016/002-verify-schema.sql` and keep operational tables empty before import.
2. Load global hierarchy and model/cost references (orders 1–6), then selected workers/department metadata and staffing state (orders 7–12).
3. Load date attendance, order header, stage records, allocations, then conditional resolutions/import provenance (orders 13–19).
4. Validate all FK/orphan checks, unique keys, `date` values, decimal precision, row counts, and zero photo payloads.
5. Do not import audit history unless explicitly approved; if approved, include its AppUser dependencies first.
