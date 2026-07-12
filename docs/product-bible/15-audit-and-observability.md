# 15 - Audit and Observability

## Why

- أي نظام تشغيل + أدوار يحتاج trace واضح لكل تغيير حساس.
- **Approved**: Audit ليس logging فقط، بل سياق أعمالي (before/after + actor).

## Scope

- توحيد سياسة التتبع (changes, auth events, permission changes, compensation edits, production recording).
- dashboards للمراجعة.

## Ownership

- Backend: `AuditEngine`.
- Data: `AuditLog` table.
- Frontend: صفحة تقرير audit + filters.

## Source of Truth

- **Confirmed**: `AuditLog` موجود في `ProductionLinePlanner.Domain/Entities/AuditLog.cs`.
- **Confirmed**: معظم write endpoints الحالية تستدعي `AssignmentHelpers.AddAuditLog`.
- **Planned**: توسيع audit scope ليشمل:
  - permission grants/denies
  - salary history changes
  - production output & allocations
  - failed authorization checks.

## Entities

- `AuditLog` (Confirmed)
- `PermissionsAudit` (Planned)
- `SecurityEvent` (Planned)

## Business Rules

- actor required (except anonymous system actions).
- event type must be deterministic.
- keep source metadata مثل endpoint، correlation, reason.

## Validation

- PII filtering in audit payload.
- قبل/بعد سجل فقط للمجالات المسموحة.
- لا يحفظ كلمات السر أو التوكنات.

## Permissions

- `audit.view`
- `permissions.assign`
- `roles.manage`

## Audit

- **Confirmed**: `AuditEngine.RecordAsync` يحفظ actor/action/entity + JSON sanitized.
- **Approved**:
  - policy: كل تعديل على IAM وcompensation + production execution يجب أن يكون auditable.
  - إضافة `requestMeta` موحد (batchId, route, correlationId).

## API Direction

- `GET /api/audit` مع filters:
  - date range
  - entityType
  - actorId
  - actionType
- حماية endpoint بـ `audit.view`.

## UI Direction

- شاشة audit:
  - filters + timeline.
  - تفصيل diff before/after.
  - export CSV/JSON.

## Failure Handling

- إذا failed audit لا يجب إيقاف العمليات الحساسة بعد اعتماد design.
- إذا فشل persist audit critical operations يُستثنى من success?
  - Approved policy: failure to write audit يُعامل `warning` مع fallback path للمراقبة (configurable).

## Deferred Work

- retention policy وarchival pipeline.
- alerting على anomalous event bursts.

## Risks

- زيادة حجم JSON payload إذا قبل/بعد fields كثيرة.
- اختلاف نمط event types بين engines القديمة والجديدة.

## Acceptance Criteria

- 100% من writes الحساسة مسجلة في `AuditLog`.
- audit قابل للتصفية على صلاحية المستخدم.
- أي تغييرات في IAM تظهر مصدرها (role/grant/deny) بشكل واضح.

## Alternatives Considered

- **Rejected**: الاعتماد على DB trigger بدلاً من app-layer audit.
- **Rejected**: عدم تسجيل before/after وإرجاع فقط action string.
