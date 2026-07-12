# 18 - Shared Import/Export

## Why

- استيراد/تصدير موحد يضمن جودة البيانات وtrace.
- **Approved**: إنشاء capability منفصل بدل معالجة ad-hoc في كل ميزة.

## Scope

- Payroll/Stages/Model configuration.
- Template, upload, parse, validate, preview, confirm, apply.
- Error report + audit.

## Ownership

- Shared Import/Export platform team.
- Domain owners:
  - `workers` -> HR domain
  - `compensation` -> Compensation domain
  - `stages/models` -> Production Engineering

## Source of Truth

- **Confirmed**: لا توجد pipeline موحدة حالية (لا يوجد Excel endpoints).
- **Approved**: Template definitions من Domain contracts.
- **Deferred**: direct cloud file connector.

## Entities

- `WorkerSalaryImportRow`
- `ProductModelStageImportRow`
- `StageCatalogImportRow`
- `ImportBatch`
- `ImportValidationError`
- `ImportExecutionLog`

## Business Rules

- الملفات تمر 5 خطوات:
1. Download template.
2. Upload + parse.
3. Validate (structure + rules + references).
4. Preview & confirm (show delta).
5. Apply via job.
- Max row count per batch planned (configurable).
- If any critical errors => apply blocked.
- allow dry-run preview.

## Validation

- worker salary import:
  - Amount numeric, EffectiveFrom/To valid.
  - EmployeeCode exists.
  - no overlapping history.
- stage catalog import:
  - SubStage exists or creation mode.
  - duplicate keys blocked.
- product model stage import:
  - one row per model+stage version key.

## Permissions

- `workers.import` (optional future alias)
- `workers.export`
- `compensation.import`
- `compensation.export`
- `stages.import`
- `stages.export`

## Audit

- import apply creates `ImportBatch` + `AuditLog` with:
  - file hash
  - row count
  - applied/failed counts
  - actor + timestamp.

## API Direction

- `GET /api/import/templates/{kind}`
- `POST /api/import/{kind}/preview`
- `POST /api/import/{kind}/apply`
- `GET /api/import/{batchId}`
- `GET /api/import/{batchId}/errors`

## UI Direction

- wizard UI with 4 steps and progress:
  - upload
  - validate summary
  - mapping conflict preview
  - confirm.

## Failure Handling

- invalid template => error list with line/column.
- partial apply:
  - approved policy: apply transaction per row if possible, report failures, continue or rollback per policy.
  - for now: prefer per-row failure + no silent success.

## Deferred Work

- streaming large files.
- import from integration APIs مباشرة.

## Risks

- اختلاف أعمدة بين لغات الواجهة.
- اختلاف precision currency parsing.

## Acceptance Criteria

- لا endpoint import يتجاوز engine/validation.
- 100% row-level errors مرئية قبل apply.
- export format مطابق لل template الحالي.

## Alternatives Considered

- **Rejected**: استخدام endpoint واحد generic لكل الكيانات.
- **Rejected**: تنفيذ import مباشرة داخل component دون backend validation.
