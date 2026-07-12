# 04 - Bounded Contexts

## الهدف

منع الـGod Service وGod Entity، وفصل السياقات.

## Contexts وBoundaries

### 1) Identity and Access Management
- الكيانات: `AppUser`, `AppRole` (Confirmed).
- لا توجد tables للصلاحيات المتقدمة بعد.
- Decision: إدخال `PermissionCatalog`, `RolePermission`, `UserPermissionOverride`, audit للعلاقات.

### 2) Employee Administration
- الكيانات: `Worker`, `WorkerDefaultAssignment`, `WorkerTemporaryAssignment`, `AttendanceRecord`.
- Ownership: هوية العامل الأساسية + تعييناته + نشاطاته التاريخية.

### 3) Attendance Integration
- المصادر: `AttendanceDbContext` tables (`USERINFO`, `CHECKINOUT`, `DEPARTMENTS`).
- غير المسؤولية: لا تعديل `CHECKINOUT` من Planner.

### 4) Factory Structure
- الكيانات: `Factory`, `ProductionLine`, `MainStage`, `SubStage`.
- لا تشمل Pricing.

### 5) Production Engineering
- تخطيط العمليات، قواعد capacity، readiness.
- يستهلك Stage structure + Assignments.

### 6) Production Execution
- سجل الإنتاج، المخرجات، تخصيصات العاملين.
- Confirmed: هذا السياق غير موجود كاملًا حاليًا (Deferred).

### 7) Compensation
- Confirmed: لا يوجد.
- Approved: `WorkerSalaryHistory`, `ProductModelStage`, `CompensationResult` engine.

### 8) Reporting + Audit
- `AuditLog`, notifications.
- Approved: security/audit coverage لكل مسارات write + auth.

### 9) Shared Import/Export
- لم يتم تنفيذ.
- Approved: template/parse/validate/preview/apply platform.

## Dependencies

- Attendance integration feeds Employee Administration + Production Engine.
- Factory Structure feeds Production Execution.
- Compensation depends on ProductModelStage + Production execution snapshots.

## Public contracts

- Auth contracts in `13-identity-access-and-permissions.md`.
- API contracts in `16-api-guidelines.md`.
- UI contracts in `17-frontend-guidelines.md`.
