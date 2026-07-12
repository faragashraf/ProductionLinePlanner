# 14 - Security and Threat Boundaries

## Why

- الأمان ليس طبقة UI فقط؛ يجب أن تكون backend مصدر القرار.
- **Approved**: كل endpoint حساس يحتاج authorization مستقل مبني على capability.

## Scope

- Threat boundaries بين public auth, business APIs, audit, attendance integration.
- إدارة tokens + session revocation + rate limiting.
- hardening endpoints ضد IDOR وprivilege escalation.

## Ownership

- Backend security: API + middleware + policies.
- Domain invariants: service/engine layer.
- DevOps: logging + monitoring.

## Source of Truth

- **Confirmed**:
  - `UseAuthentication/UseAuthorization` مفعّل.
  - Role policies موجودة `SuperAdmin` و`Admin`.
- **Approved**:
  - Endpoint-level policies جديدة قائمة على permissions.
- **Rejected**:
  - الاعتماد على sidebar visibility كحماية أمنية.

## Entities/Boundaries

- `AuditLog`, `RefreshToken`, `AppUser` (Confirmed).
- `CurrentUserService` claims-based context.
- `AttendanceRecord` (internal projection, immutable from Planner writes).

## Business Rules

1. Backend هو boundary authority.
2. Authorization check في كل endpoint تعديل.
3. Permission denied returns 403 (authenticated but unauthorized).
4. Unauthenticated returns 401.
5. كل واجهة sensitive تستخدم anti-enumeration (error code موحد قدر الإمكان).

## Validation

- Endpoint guard:
  - `RequirePermission("workers.manage")` أو equivalent policy.
- Rate limiting موجود حاليا على API عام (Confirmed).
- CORS وsecurity headers مفعلة (Confirmed).
- **Planned**: dynamic policy provider + endpoint metadata.

## Permissions

- `audit.view`
- `permissions.assign`
- `users.manage`
- `workers.manage`
- `compensation.manage`
- `stages.manage`
- `production.approve`

## Audit

- كل فشل/نجاح حساس يسجل:
  - actor، action، entity, request id، source IP.
- refresh/logout/rotate already writes `AuditLog` partially (Confirmed).
- **Planned**: audit for denied access and role/permission changes.

## API Direction

- `MapGroup` حاليا role-based:
  - `RequireAuthorization("Admin")`, `RequireAuthorization("SuperAdmin")`.
- **Approved**:
  - انتقال إلى policy/attribute-based:
    - `RequirePermission("workers.view")`
    - `RequirePermission("workers.manage")` … وهكذا.
- `token_version` ثابت حاليا في token claims، لكن لا يوجد invalidation framework.
  - **Approved**: version/permissions stamp claim للinvalidate سريع.

## UI Direction

- لا تعتمد UI على أدوار string.
- Route guard + directive تتحقق فقط لخبرة UX مع بقاء backend المصدر.
- 403 page يجب أن يكون واضحًا.

## Failure Handling

- إذا session invalid => تسجيل خروج.
- إذا صلاحية endpoint تغيرت:
  - إما فورًا عبر revocation/version bump.
  - أو بعد refresh التالي.

## Deferred Work

- Scope-level authorization (scope_id checks) لمهام IDOR.
- SIEM integration + anomaly detection.

## Risks

- token_version ثابت حاليا (=1) يحد من إمكانيات revoke سريع.
- مخرجات أخطاء `Refresh` قد تكشف تفاصيل أكثر من اللازم.

## Acceptance Criteria

- لا endpoint حساس يعتمد على Role مباشرة ضمن endpoint body logic.
- 401/403 مفصولان بوضوح.
- logs تتضمن deny reasons summary بدون كشف PII إضافي.

## Alternatives Considered

- **Rejected**: "Admin-only" لكل المكونات حتى انتهاء بناء IAM.
- **Rejected**: السماح بإدارة permissions داخل localStorage.
