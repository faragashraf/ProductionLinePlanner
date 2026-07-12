# 12 - Compensation

## Why

- التعويض المالي في النسخة الحالية غير مفصول كـ history.
- **Approved**: بناء تعويض من مصدرين:
  - Salary history للراتب الأساسي.
  - Production earnings حسب المرحلة/النموذج.

## Scope

- تصميم `WorkerSalaryHistory` كسجل تاريخي مع `EffectiveFrom/To`.
- تعريف نمط أجر العامل داخل `ProductModelStage` (`CompensationMode`).
- حساب payroll عبر snapshot منع انكماش القيم القديمة.

## Ownership

- Domain/Model: `WorkerSalaryHistory`, `ProductionCompensationRecord` (Planned).
- Engine: `CompensationEngine` (Planned).
- API: `/api/compensation/*`.
- UI: صفحة compensation dashboard + history viewer.

## Source of Truth

- **Planned**:
  - `WorkerSalaryHistory` داخل `AppDb`.
- **Approved**:
  - Worker's production earnings derived from `ProductionStageOutput` snapshots + approved rates.
- **Rejected**:
  - `Worker` field مباشرة باسم `Salary` للإنتاجي النهائي.

## Entities

- **Rejected** in current code: `Worker.Salary` (غير موجود حاليا).
- Planned:
  - `WorkerSalaryHistory`
  - `CompensationMode` (enum/owned value)
  - `PayrollRun` (Planned)
  - `CompensationAllocation` (Planned)
- Confirmed:
  - `AuditLog` for change trace.

## Business Rules

- `WorkerSalaryHistory`:
  - `Amount`, `CurrencyCode`, `EffectiveFrom`, `EffectiveTo`, `Notes`, `IsDeleted` flag.
- Validation:
  - لا تتداخل الفترات الزمنية.
  - لا يوجد أكثر من سجل "حالي" (`EffectiveTo = null`) لكل عامل.
  - `EffectiveFrom` و`EffectiveTo` UTC.
- `CurrencyCode` default `EGP`.
- `Base salary` مستقل عن compensation من الإنتاج.
- تعديل تاريخي:
  - يسمح بإضافة سجل تصحيحي مع effective window closed history.

## Validation

- decimal precision: `decimal(18,4)` (Planned) لكل Amount.
- مبلغ غير سالب ممنوع.
- عند تعارض الفترات يُرجع 409/422.
- عدم قبول `EffectiveFrom` قديم جدًا إذا يسبب فقدان سلسلة.

## Permissions

- `compensation.view`
- `compensation.manage`
- `compensation.import`
- `compensation.export`

## Audit

- كل تعديل في salary history => audit with:
  - `before` values (`Amount`,`EffectiveFrom`,`EffectiveTo`)
  - reason/note.
- payroll run audit (Planned) لكل job.

## API Direction

- `GET /api/workers/{id}/salary-history`
- `POST /api/workers/{id}/salary-history`
- `PUT /api/salary-history/{id}`
- `POST /api/compensation/import` (template + preview + confirm)
- `GET /api/compensation/exports`
- All endpoints require permissions above.

## UI Direction

- صفحة `workers -> compensation` تعرض history + "current effective row".
- شاشة excel import تعرض:
  - preview row-level
  - validation summary
  - commit checkpoint.

## Failure Handling

- إذا كان هناك تاريخ متداخل -> رفض مع list of conflicting records.
- إذا تغيّر السعر بعد اعتماد output قديم => recalculation ممنوع؛ استخدام snapshot.

## Deferred Work

- Payroll tax/legal deductions (`deferred`).
- multi-currency per worker (future if locale extends beyond EGP).

## Risks

- إدخال تاريخ تصحيحي قد يسبب أثر رجعي على تقارير غير مجدولة.
- بدون locking على ranges قد تظهر تعارضات في إدخال متزامن.

## Acceptance Criteria

- لا يوجد حقل `Salary` في `Worker` ضمن منطق التعويض النهائي.
- لا أكثر من سجل فعّال active salary لكل عامل.
- لا يغير تعديل salary مخرجات الإنتاج السابقة.
- permission boundary واضح لكل import/export/manage/view.

## Alternatives Considered

- **Rejected**: `Salary` مباشر داخل `Worker` مع override.
- **Rejected**: تخزين قيم payroll في `AttendanceRecord`.
